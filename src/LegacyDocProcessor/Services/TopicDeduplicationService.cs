using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for deduplicating similar topic names using LLM
/// </summary>
public interface ITopicDeduplicationService
{
    /// <summary>
    /// Deduplicate a list of topic names using LLM-based semantic similarity
    /// </summary>
    /// <param name="topicNames">List of unique topic names to deduplicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing topic mappings and canonical names</returns>
    Task<TopicDeduplicationResult> DeduplicateTopicsAsync(
        List<string> topicNames, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Deduplicates similar topic names using an LLM API (OpenAI-compatible)
/// </summary>
public class TopicDeduplicationService : ITopicDeduplicationService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LlmConfig _config;
    private readonly TopicDeduplicationConfig _dedupConfig;
    private readonly ILogger _logger;
    private readonly int _maxRetries = 3;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
    
    public TopicDeduplicationService(LlmConfig config, TopicDeduplicationConfig dedupConfig, ILogger logger)
    {
        _config = config;
        _dedupConfig = dedupConfig ?? new TopicDeduplicationConfig();
        _logger = logger;
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(3)
        };
        
        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }
        
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
    
    /// <summary>
    /// Deduplicate topics using LLM-based semantic analysis
    /// </summary>
    public async Task<TopicDeduplicationResult> DeduplicateTopicsAsync(
        List<string> topicNames, 
        CancellationToken cancellationToken = default)
    {
        var result = new TopicDeduplicationResult
        {
            OriginalTopicCount = topicNames.Count,
            WasPerformed = true
        };
        
        if (topicNames.Count == 0)
        {
            result.WasPerformed = false;
            return result;
        }
        
        // If only a few topics, no need to deduplicate
        if (topicNames.Count < 3)
        {
            _logger.Information("Only {Count} topics found, skipping deduplication", topicNames.Count);
            result.WasPerformed = false;
            result.CanonicalTopics = topicNames.ToList();
            foreach (var topic in topicNames)
            {
                result.TopicMappings[topic] = topic;
            }
            return result;
        }
        
        _logger.Information("Starting topic deduplication for {Count} topics", topicNames.Count);
        
        // Get unique topics (case-insensitive)
        var uniqueTopics = topicNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();
        
        // If few unique topics, process in one batch
        if (uniqueTopics.Count <= _dedupConfig.LlmBatchSize)
        {
            var batchResult = await ProcessBatchAsync(uniqueTopics, cancellationToken);
            MergeBatchResult(result, batchResult);
        }
        else
        {
            // Process in multiple batches and merge results
            var batches = SplitIntoBatches(uniqueTopics, _dedupConfig.LlmBatchSize);
            _logger.Information("Processing {Count} topics in {Batches} batches (batch size: {BatchSize})", 
                uniqueTopics.Count, batches.Count, _dedupConfig.LlmBatchSize);
            
            foreach (var (batch, index) in batches.Select((b, i) => (b, i)))
            {
                _logger.Information("Processing deduplication batch {Index}/{Total}", 
                    index + 1, batches.Count);
                
                var batchResult = await ProcessBatchAsync(batch, cancellationToken);
                MergeBatchResult(result, batchResult);
                
                // Small delay between batches to avoid rate limiting
                if (index < batches.Count - 1)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            
            // Post-process: merge any canonical topics that are similar across batches
            result = await CrossBatchMergeAsync(result, cancellationToken);
        }
        
        result.DeduplicatedTopicCount = result.CanonicalTopics.Count;
        
        // Calculate final statistics
        CalculateStatistics(result);
        
        _logger.Information("Deduplication complete: {Original} → {Deduplicated} topics " +
            "(Merge ratio: {MergeRatio:F2}, Avg confidence: {AvgConfidence:P0})",
            result.OriginalTopicCount, result.DeduplicatedTopicCount,
            result.Statistics.MergeRatio, result.Statistics.AverageConfidence);
        
        return result;
    }
    
    /// <summary>
    /// Process a single batch of topics through the LLM
    /// </summary>
    private async Task<TopicDeduplicationResult> ProcessBatchAsync(
        List<string> topics, 
        CancellationToken cancellationToken)
    {
        var result = new TopicDeduplicationResult();
        
        var prompt = BuildDeduplicationPrompt(topics);
        
        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var response = await SendLlmRequestAsync(prompt, cancellationToken);
                ParseDeduplicationResponse(result, topics, response);
                return result;
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries)
            {
                _logger.Warning("LLM deduplication request failed (attempt {Attempt}/{Max}): {Error}",
                    attempt, _maxRetries, ex.Message);
                await Task.Delay(_retryDelay * attempt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deduplicate topics after {Attempts} attempts", attempt);
                
                if (attempt == _maxRetries)
                {
                    // Fall back to no deduplication
                    _logger.Warning("Using fallback: no deduplication applied");
                    foreach (var topic in topics)
                    {
                        result.TopicMappings[topic] = topic;
                        result.CanonicalTopics.Add(topic);
                    }
                    return result;
                }
                
                await Task.Delay(_retryDelay * attempt, cancellationToken);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Build the prompt for topic deduplication
    /// </summary>
    private string BuildDeduplicationPrompt(List<string> topics)
    {
        var topicsList = string.Join("\n", topics.Select((t, i) => $"{i + 1}. {t}"));
        
        // Build aggressiveness-specific guidance
        var (mergeGuidance, thresholdDescription) = GetAggressivenessGuidance();
        
        // Build technical term preservation section
        var technicalTermsSection = BuildTechnicalTermsSection();
        
        return $@"You are a topic deduplication assistant. Your task is to analyze a list of topic names and identify which ones should be merged because they represent the same or very similar concepts.

TOPIC LIST:
{topicsList}

MERGE AGGRESSIVENESS: {_dedupConfig.Aggressiveness}
{thresholdDescription}

{mergeGuidance}

{technicalTermsSection}

SIMILARITY RULES:
1. Ignore differences in capitalization, hyphens, underscores, and spacing
2. Topics sharing the same core concept with different qualifiers are often the same topic
3. Suffixes like ""Capabilities"", ""Functionality"", ""Features"", ""Overview"" can often be ignored

EXAMPLES OF TOPICS THAT SHOULD BE MERGED:

Example Group 1 - Formatting variations:
  • ""Key Features and Functionality"" → ""Key Features""
  • ""Key Features and Functional Capabilities"" → ""Key Features""
  • ""Key-Features"" → ""Key Features""
  Rationale: All refer to the same concept; suffixes and formatting differ only.

Example Group 2 - Semantic equivalents:
  • ""Image Processing"" → ""Image Processing""
  • ""Image-Processing"" → ""Image Processing""
  • ""Image Processing Capabilities"" → ""Image Processing""
  • ""Working with Images"" → ""Image Processing""
  Rationale: All describe the same functional area with different wording.

Example Group 3 - Scope variations:
  • ""User Authentication"" → ""User Authentication""
  • ""User Authentication Overview"" → ""User Authentication""
  • ""Authentication for Users"" → ""User Authentication""
  Rationale: Same core topic, different scope descriptors.

Example Group 4 - Abbreviated forms:
  • ""Application Programming Interface"" → ""API""
  • ""API"" → ""API""
  • ""APIs"" → ""API""
  Rationale: Standard abbreviation, same concept.

EXAMPLES OF TOPICS THAT SHOULD NOT BE MERGED:

Example A - Distinct technical identifiers:
  • ""API-123 Integration"" ≠ ""API-456 Integration""
  • ""PROJ-100 Setup"" ≠ ""PROJ-200 Setup""
  Rationale: Different ticket/issue references indicate distinct topics.

Example B - Different scope levels:
  • ""Database Configuration"" ≠ ""Database Optimization""
  • ""User Management"" ≠ ""User Permissions""
  Rationale: Related but distinct technical concepts.

Example C - Different products/components:
  • ""Login Service"" ≠ ""Logout Service""
  • ""Export to PDF"" ≠ ""Export to Excel""
  Rationale: Different functionality despite similar naming pattern.

RESPONSE FORMAT:
Respond in JSON format with the following structure:
```json
{{
  ""mappings"": {{
    ""Original Topic Name 1"": ""Canonical Topic Name"",
    ""Original Topic Name 2"": ""Canonical Topic Name"",
    ...
  }},
  ""groups"": [
    {{
      ""canonical"": ""Canonical Topic Name"",
      ""members"": [""Topic 1"", ""Topic 2""],
      ""confidence"": 0.95,
      ""rationale"": ""Brief explanation of why these were merged (e.g., 'Formatting variations of same concept')""
    }}
  ],
  ""unmerged"": [
    {{
      ""topic"": ""Unique Topic Name"",
      ""reason"": ""Why this topic was kept separate (e.g., 'Distinct technical concept')""
    }}
  ]
}}
```

CONFIDENCE SCORES:
- 0.95-1.0: Near-identical topics (formatting/capitalization differences only)
- 0.85-0.94: Clear semantic equivalence (same concept, different wording)
- 0.70-0.84: Likely equivalent (related concepts that probably should be merged)
- Below 0.70: Do not merge (insufficient similarity)

IMPORTANT REQUIREMENTS:
1. Every topic in the input list MUST appear in the ""mappings"" object
2. If a topic has no similar topics, map it to itself (same name)
3. Each merge group MUST include a confidence score and rationale
4. Be thorough - do not skip any topics
5. Prefer concise canonical names (remove unnecessary suffixes)
6. Include brief, specific rationales that explain WHY topics were merged or kept separate";
    }
    
    /// <summary>
    /// Get aggressiveness-specific guidance based on configuration
    /// </summary>
    private (string guidance, string description) GetAggressivenessGuidance()
    {
        return _dedupConfig.Aggressiveness switch
        {
            AggressivenessLevel.Conservative => (
                @"CONSERVATIVE MERGE STRATEGY:
- Only merge topics that are nearly identical (confidence ≥ 0.90)
- Require exact semantic match - when in doubt, keep separate
- Only merge topics that differ in formatting, capitalization, or minor suffixes
- Prefer more specific canonical names over generic ones
- Example: ""API Overview"" and ""API Overview"" → merge; ""API Overview"" and ""API Details"" → keep separate",
                "This mode minimizes false positives but may leave some duplicates unmerged."
            ),
            
            AggressivenessLevel.Aggressive => (
                @"AGGRESSIVE MERGE STRATEGY:
- Merge topics that share the same core concept (confidence ≥ 0.70)
- Interpret related topics broadly - when in doubt, merge
- Consolidate topics that cover the same functional area
- Prefer shorter, more generic canonical names
- Example: ""API Overview"", ""API Details"", ""Using the API"", ""API Reference"" → all merge to ""API""",
                "This mode maximizes consolidation but may occasionally merge topics that are technically distinct."
            ),
            
            _ => ( // Medium (default)
                @"BALANCED MERGE STRATEGY:
- Merge topics with clear semantic equivalence (confidence ≥ 0.80)
- Balance between catching duplicates and avoiding false positives
- Consider context and intent, not just wording
- Choose canonical names that are clear and descriptive
- Example: ""API Overview"" and ""API Introduction"" → merge; ""API Overview"" and ""API Error Codes"" → keep separate",
                "This mode provides a good balance for most use cases."
            )
        };
    }
    
    /// <summary>
    /// Build the technical terms preservation section if enabled
    /// </summary>
    private string BuildTechnicalTermsSection()
    {
        if (!_dedupConfig.PreserveTechnicalTerms)
        {
            return "TECHNICAL TERMS: No special handling - treat all topics equally.";
        }
        
        var patterns = _dedupConfig.TechnicalTermPatterns.Any() 
            ? string.Join("\n  ", _dedupConfig.TechnicalTermPatterns.Select(p => $"- Pattern: {p}"))
            : "- [A-Z]{{2,}}-\\d+ (e.g., PROJ-123, API-456)\n  - v\\d+\\.\\d+ (e.g., v1.0, v2.5)";
        
        return $@"TECHNICAL TERM PRESERVATION:
Topics containing distinct technical identifiers should NOT be merged with each other.
Technical term patterns to preserve:
  {patterns}

Examples of preserved technical terms:
  • ""PROJ-123 Integration"" and ""PROJ-456 Setup"" → DO NOT MERGE (different project tickets)
  • ""API v1.0 Features"" and ""API v2.0 Features"" → DO NOT MERGE (different versions)
  • ""ABC123 Configuration"" and ""XYZ789 Configuration"" → DO NOT MERGE (different codes)

However, variations of the SAME technical term CAN be merged:
  • ""PROJ-123"" and ""proj-123"" and ""PROJ-123 Overview"" → MERGE (same ticket, formatting variations)";
    }
    
    /// <summary>
    /// Send request to the LLM API
    /// </summary>
    private async Task<string> SendLlmRequestAsync(string prompt, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _config.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 4000,
            temperature = 0.1, // Low temperature for more deterministic results
            response_format = new { type = "json_object" }
        };
        
        var json = JsonConvert.SerializeObject(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync("chat/completions", httpContent, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LLM API returned {response.StatusCode}: {errorContent}");
        }
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObj = JObject.Parse(responseJson);
        
        // Handle both OpenAI and Ollama response formats
        var choices = responseObj["choices"];
        if (choices != null && choices.HasValues)
        {
            var firstChoice = choices[0];
            var message = firstChoice["message"];
            if (message != null)
            {
                return message["content"]?.ToString() ?? string.Empty;
            }
            
            var text = firstChoice["text"];
            if (text != null)
            {
                return text.ToString() ?? string.Empty;
            }
        }
        
        throw new InvalidOperationException("Unexpected LLM response format");
    }
    
    /// <summary>
    /// Parse the LLM response and populate the result
    /// </summary>
    private void ParseDeduplicationResponse(
        TopicDeduplicationResult result, 
        List<string> originalTopics,
        string llmResponse)
    {
        try
        {
            // Extract JSON from response
            var jsonStart = llmResponse.IndexOf('{');
            var jsonEnd = llmResponse.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = llmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var jsonDoc = JObject.Parse(jsonStr);
                
                var mappings = jsonDoc["mappings"] as JObject;
                if (mappings != null)
                {
                    foreach (var prop in mappings.Properties())
                    {
                        var originalTopic = prop.Name;
                        var canonicalTopic = prop.Value?.ToString() ?? originalTopic;
                        
                        result.TopicMappings[originalTopic] = canonicalTopic;
                        
                        if (!result.CanonicalTopics.Contains(canonicalTopic, StringComparer.OrdinalIgnoreCase))
                        {
                            result.CanonicalTopics.Add(canonicalTopic);
                        }
                    }
                }
                
                var groups = jsonDoc["groups"] as JArray;
                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        var mergeGroup = new TopicMergeGroup
                        {
                            CanonicalName = group["canonical"]?.ToString() ?? "",
                            // Support both "rationale" (new) and "reason" (legacy) for backward compatibility
                            MergeReason = group["rationale"]?.ToString() ?? group["reason"]?.ToString() ?? "",
                            Confidence = (double?)group["confidence"] ?? 1.0
                        };
                        
                        var members = group["members"] as JArray;
                        if (members != null)
                        {
                            mergeGroup.OriginalNames = members
                                .Select(m => m.ToString())
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();
                        }
                        
                        if (!string.IsNullOrEmpty(mergeGroup.CanonicalName) && 
                            mergeGroup.OriginalNames.Count > 1)
                        {
                            result.MergeGroups.Add(mergeGroup);
                        }
                    }
                }
                
                // Parse unmerged topics
                var unmerged = jsonDoc["unmerged"] as JArray;
                if (unmerged != null)
                {
                    foreach (var item in unmerged)
                    {
                        var topic = item["topic"]?.ToString();
                        var reason = item["reason"]?.ToString() ?? "No similar topics found";
                        
                        if (!string.IsNullOrEmpty(topic))
                        {
                            result.UnmergedTopics.Add(new UnmergedTopic
                            {
                                Topic = topic,
                                Reason = reason
                            });
                        }
                    }
                }
                
                // Calculate statistics
                CalculateStatistics(result);
            }
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse deduplication LLM response");
        }
        
        // Ensure all original topics have a mapping (fallback to self)
        foreach (var topic in originalTopics)
        {
            if (!result.TopicMappings.ContainsKey(topic))
            {
                result.TopicMappings[topic] = topic;
                if (!result.CanonicalTopics.Contains(topic, StringComparer.OrdinalIgnoreCase))
                {
                    result.CanonicalTopics.Add(topic);
                }
            }
        }
    }
    
    /// <summary>
    /// Calculate statistics from the deduplication result
    /// </summary>
    private void CalculateStatistics(TopicDeduplicationResult result)
    {
        var stats = result.Statistics;
        
        stats.MergeGroupsCreated = result.MergeGroups.Count;
        stats.TopicsKeptSeparate = result.UnmergedTopics.Count;
        
        if (result.MergeGroups.Any())
        {
            stats.AverageConfidence = result.MergeGroups.Average(g => g.Confidence);
            stats.HighConfidenceMerges = result.MergeGroups.Count(g => g.Confidence >= 0.90);
            stats.MediumConfidenceMerges = result.MergeGroups.Count(g => g.Confidence >= 0.70 && g.Confidence < 0.90);
            stats.LowConfidenceMerges = result.MergeGroups.Count(g => g.Confidence < 0.70);
        }
        
        if (result.DeduplicatedTopicCount > 0)
        {
            stats.MergeRatio = (double)result.OriginalTopicCount / result.DeduplicatedTopicCount;
        }
    }
    
    /// <summary>
    /// Merge batch result into the main result
    /// </summary>
    private void MergeBatchResult(TopicDeduplicationResult main, TopicDeduplicationResult batch)
    {
        foreach (var mapping in batch.TopicMappings)
        {
            main.TopicMappings[mapping.Key] = mapping.Value;
        }
        
        foreach (var canonical in batch.CanonicalTopics)
        {
            if (!main.CanonicalTopics.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                main.CanonicalTopics.Add(canonical);
            }
        }
        
        main.MergeGroups.AddRange(batch.MergeGroups);
        main.UnmergedTopics.AddRange(batch.UnmergedTopics);
        
        // Merge statistics
        main.Statistics.MergeGroupsCreated += batch.Statistics.MergeGroupsCreated;
        main.Statistics.TopicsKeptSeparate += batch.Statistics.TopicsKeptSeparate;
        main.Statistics.HighConfidenceMerges += batch.Statistics.HighConfidenceMerges;
        main.Statistics.MediumConfidenceMerges += batch.Statistics.MediumConfidenceMerges;
        main.Statistics.LowConfidenceMerges += batch.Statistics.LowConfidenceMerges;
        
        // Recalculate average confidence weighted by number of groups
        var totalGroups = main.Statistics.MergeGroupsCreated;
        if (totalGroups > 0)
        {
            main.Statistics.AverageConfidence = 
                (main.Statistics.AverageConfidence * (totalGroups - batch.Statistics.MergeGroupsCreated) + 
                 batch.Statistics.AverageConfidence * batch.Statistics.MergeGroupsCreated) / totalGroups;
        }
    }
    
    /// <summary>
    /// Split topics into batches
    /// </summary>
    private List<List<T>> SplitIntoBatches<T>(List<T> items, int batchSize)
    {
        var batches = new List<List<T>>();
        for (int i = 0; i < items.Count; i += batchSize)
        {
            batches.Add(items.Skip(i).Take(batchSize).ToList());
        }
        return batches;
    }
    
    /// <summary>
    /// Perform cross-batch merging to handle similar topics that ended up in different batches
    /// </summary>
    private async Task<TopicDeduplicationResult> CrossBatchMergeAsync(
        TopicDeduplicationResult result, 
        CancellationToken cancellationToken)
    {
        // Get all canonical topics
        var canonicalTopics = result.CanonicalTopics.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        
        // If few enough, do a final pass
        if (canonicalTopics.Count <= _dedupConfig.LlmBatchSize && canonicalTopics.Count > 10)
        {
            _logger.Information("Performing cross-batch merge on {Count} canonical topics", canonicalTopics.Count);
            
            var crossBatchResult = await ProcessBatchAsync(canonicalTopics, cancellationToken);
            
            // Update mappings based on cross-batch result
            var updatedMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in result.TopicMappings)
            {
                var currentCanonical = mapping.Value;
                if (crossBatchResult.TopicMappings.TryGetValue(currentCanonical, out var newCanonical))
                {
                    updatedMappings[mapping.Key] = newCanonical;
                }
                else
                {
                    updatedMappings[mapping.Key] = currentCanonical;
                }
            }
            
            result.TopicMappings = updatedMappings;
            result.CanonicalTopics = crossBatchResult.CanonicalTopics;
            result.MergeGroups.AddRange(crossBatchResult.MergeGroups);
        }
        
        return result;
    }
    
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
