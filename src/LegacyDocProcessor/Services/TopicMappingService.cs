using System.Text.Json;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for building and managing topic mapping dictionaries
/// </summary>
public interface ITopicMappingService
{
    /// <summary>
    /// Build a topic mapping dictionary from a deduplication result
    /// </summary>
    TopicMappingDictionary BuildFromDeduplicationResult(TopicDeduplicationResult result);
    
    /// <summary>
    /// Build a topic mapping dictionary from a list of topics using the deduplication service
    /// </summary>
    Task<TopicMappingDictionary> BuildFromTopicsAsync(List<string> topics, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Load a topic mapping dictionary from a JSON file
    /// </summary>
    Task<TopicMappingDictionary> LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Save a topic mapping dictionary to a JSON file
    /// </summary>
    Task SaveToFileAsync(TopicMappingDictionary mapping, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Apply a topic mapping dictionary to a list of processed documents
    /// </summary>
    List<ProcessedKnowledge> ApplyToDocuments(List<ProcessedKnowledge> documents, TopicMappingDictionary mapping);
    
    /// <summary>
    /// Apply a topic mapping dictionary to a single document's topics
    /// </summary>
    ProcessedKnowledge ApplyToDocument(ProcessedKnowledge document, TopicMappingDictionary mapping);
    
    /// <summary>
    /// Get or create a cached mapping for a given set of topics
    /// </summary>
    Task<TopicMappingDictionary> GetOrCreateMappingAsync(
        List<string> topics, 
        string? cacheKey = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Build a merge log from a topic mapping dictionary
    /// </summary>
    TopicMergeLog BuildMergeLog(TopicMappingDictionary mapping, string aggressivenessLevel = "Medium");
    
    /// <summary>
    /// Save a merge log to a JSON file
    /// </summary>
    Task SaveMergeLogAsync(TopicMergeLog log, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the last merge log that was created
    /// </summary>
    TopicMergeLog? GetLastMergeLog();
}

/// <summary>
/// Manages topic mapping dictionaries for downstream use
/// </summary>
public class TopicMappingService : ITopicMappingService
{
    private readonly ILogger _logger;
    private readonly ITopicDeduplicationService? _deduplicationService;
    private readonly ITopicNormalizer? _normalizer;
    private readonly TopicDeduplicationConfig? _config;
    private readonly Dictionary<string, TopicMappingDictionary> _cache = new();
    private TopicMergeLog? _lastMergeLog;
    
    public TopicMappingService(
        ILogger logger, 
        ITopicDeduplicationService? deduplicationService = null,
        ITopicNormalizer? normalizer = null,
        TopicDeduplicationConfig? config = null)
    {
        _logger = logger;
        _deduplicationService = deduplicationService;
        _normalizer = normalizer;
        _config = config;
    }
    
    /// <summary>
    /// Build a topic mapping dictionary from a deduplication result
    /// </summary>
    public TopicMappingDictionary BuildFromDeduplicationResult(TopicDeduplicationResult result)
    {
        var mapping = new TopicMappingDictionary
        {
            Source = "LLM Deduplication",
            CreatedAt = DateTime.UtcNow
        };
        
        // Add all mappings from the result
        mapping.AddMappings(result.TopicMappings);
        
        // Copy statistics if available
        if (result.Statistics != null)
        {
            mapping.Statistics.TotalMappings = result.OriginalTopicCount;
            mapping.Statistics.UniqueCanonicalTopics = result.DeduplicatedTopicCount;
            mapping.Statistics.MergedTopics = result.MergeGroups.Sum(g => g.OriginalNames.Count - 1);
            mapping.Statistics.UnchangedTopics = result.UnmergedTopics.Count;
        }
        
        _logger.Information("Built topic mapping dictionary: {Original} original → {Canonical} canonical topics",
            mapping.Statistics.TotalMappings, mapping.Statistics.UniqueCanonicalTopics);
        
        return mapping;
    }
    
    /// <summary>
    /// Build a topic mapping dictionary from a list of topics using the deduplication service
    /// </summary>
    public async Task<TopicMappingDictionary> BuildFromTopicsAsync(
        List<string> topics, 
        CancellationToken cancellationToken = default)
    {
        // Declare at method level so it's accessible in both normalizer blocks
        Dictionary<string, List<string>>? normalizedGroups = null;
        
        // First apply normalizer if available
        if (_normalizer != null)
        {
            normalizedGroups = _normalizer.NormalizeAndGroup(topics);
            var normalizedMapping = new TopicMappingDictionary
            {
                Source = "TopicNormalizer",
                CreatedAt = DateTime.UtcNow
            };
            
            // Build initial mapping from normalizer
            foreach (var (normalized, originals) in normalizedGroups)
            {
                foreach (var original in originals)
                {
                    normalizedMapping.AddMapping(original, normalized);
                }
            }
            
            // Get unique normalized topics for LLM deduplication
            topics = normalizedGroups.Keys.ToList();
        }
        
        // Then apply LLM deduplication if available
        if (_deduplicationService != null && topics.Count >= 3)
        {
            try
            {
                var deduplicationResult = await _deduplicationService.DeduplicateTopicsAsync(topics, cancellationToken);
                var llmMapping = BuildFromDeduplicationResult(deduplicationResult);
                
                // If we had a normalizer, compose the mappings
                if (_normalizer != null)
                {
                    var composedMapping = new TopicMappingDictionary
                    {
                        Source = "Normalizer + LLM",
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    // For each original topic, get its normalized form, then the LLM canonical form
                    foreach (var original in normalizedGroups?.SelectMany(g => g.Value) ?? topics)
                    {
                        var normalized = _normalizer.Normalize(original);
                        var canonical = llmMapping.GetCanonicalTopic(normalized);
                        composedMapping.AddMapping(original, canonical);
                    }
                    
                    return composedMapping;
                }
                
                return llmMapping;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build topic mapping from LLM deduplication");
            }
        }
        
        // Fallback: build from normalizer only or identity mapping
        if (_normalizer != null)
        {
            return BuildFromNormalizer(topics);
        }
        
        // Final fallback: identity mapping
        return BuildIdentityMapping(topics);
    }
    
    /// <summary>
    /// Build mapping from normalizer only
    /// </summary>
    private TopicMappingDictionary BuildFromNormalizer(List<string> topics)
    {
        var mapping = new TopicMappingDictionary
        {
            Source = "TopicNormalizer",
            CreatedAt = DateTime.UtcNow
        };
        
        foreach (var topic in topics)
        {
            var normalized = _normalizer!.Normalize(topic);
            mapping.AddMapping(topic, normalized);
        }
        
        return mapping;
    }
    
    /// <summary>
    /// Build identity mapping (each topic maps to itself)
    /// </summary>
    private TopicMappingDictionary BuildIdentityMapping(List<string> topics)
    {
        var mapping = new TopicMappingDictionary
        {
            Source = "Identity",
            CreatedAt = DateTime.UtcNow
        };
        
        foreach (var topic in topics)
        {
            mapping.AddMapping(topic, topic);
        }
        
        return mapping;
    }
    
    /// <summary>
    /// Load a topic mapping dictionary from a JSON file
    /// </summary>
    public async Task<TopicMappingDictionary> LoadFromFileAsync(
        string filePath, 
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Topic mapping file not found: {filePath}");
        }
        
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var mapping = JsonSerializer.Deserialize<TopicMappingDictionary>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (mapping == null)
        {
            throw new InvalidOperationException($"Failed to deserialize topic mapping from: {filePath}");
        }
        
        // Rebuild reverse mappings
        var rebuilt = new TopicMappingDictionary
        {
            Source = mapping.Source + " (Loaded)",
            CreatedAt = mapping.CreatedAt
        };
        rebuilt.AddMappings(mapping.OriginalToCanonical);
        
        _logger.Information("Loaded topic mapping dictionary from {Path}: {Count} mappings",
            filePath, rebuilt.Statistics.TotalMappings);
        
        return rebuilt;
    }
    
    /// <summary>
    /// Save a topic mapping dictionary to a JSON file
    /// </summary>
    public async Task SaveToFileAsync(
        TopicMappingDictionary mapping, 
        string filePath, 
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var json = JsonSerializer.Serialize(mapping, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        
        _logger.Information("Saved topic mapping dictionary to {Path}: {Count} mappings",
            filePath, mapping.Statistics.TotalMappings);
    }
    
    /// <summary>
    /// Apply a topic mapping dictionary to a list of processed documents
    /// </summary>
    public List<ProcessedKnowledge> ApplyToDocuments(List<ProcessedKnowledge> documents, TopicMappingDictionary mapping)
    {
        _logger.Information("Applying topic mapping to {Count} documents", documents.Count);
        
        var results = new List<ProcessedKnowledge>();
        foreach (var doc in documents)
        {
            results.Add(ApplyToDocument(doc, mapping));
        }
        
        return results;
    }
    
    /// <summary>
    /// Apply a topic mapping dictionary to a single document's topics
    /// </summary>
    public ProcessedKnowledge ApplyToDocument(ProcessedKnowledge document, TopicMappingDictionary mapping)
    {
        // Create a copy to avoid modifying the original
        var result = new ProcessedKnowledge
        {
            FilePath = document.FilePath,
            SuggestedTitle = document.SuggestedTitle,
            Summary = document.Summary,
            TechnicalDetails = document.TechnicalDetails.ToList(),
            ActionItems = document.ActionItems.ToList(),
            Questions = document.Questions.ToList(),
            ProcessedAt = document.ProcessedAt,
            RawLlmResponse = document.RawLlmResponse
        };
        
        // Apply mapping to each topic
        foreach (var topicInfo in document.Topics)
        {
            var canonicalTopic = mapping.GetCanonicalTopic(topicInfo.Topic);
            
            result.Topics.Add(new TopicInfo
            {
                Topic = canonicalTopic,
                Relevance = topicInfo.Relevance,
                Summary = topicInfo.Summary,
                Content = topicInfo.Content
            });
        }
        
        return result;
    }
    
    /// <summary>
    /// Get or create a cached mapping for a given set of topics
    /// </summary>
    public async Task<TopicMappingDictionary> GetOrCreateMappingAsync(
        List<string> topics, 
        string? cacheKey = null, 
        CancellationToken cancellationToken = default)
    {
        // Generate cache key if not provided
        cacheKey ??= GenerateCacheKey(topics);
        
        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            _logger.Debug("Using cached topic mapping for key {Key}", cacheKey);
            return cached;
        }
        
        // Build new mapping
        var mapping = await BuildFromTopicsAsync(topics, cancellationToken);
        
        // Cache it
        _cache[cacheKey] = mapping;
        
        return mapping;
    }
    
    /// <summary>
    /// Clear the mapping cache
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _logger.Debug("Topic mapping cache cleared");
    }
    
    /// <summary>
    /// Generate a cache key from a list of topics
    /// </summary>
    private static string GenerateCacheKey(List<string> topics)
    {
        // Use hash of sorted topics for cache key
        var sorted = topics.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        var combined = string.Join("|", sorted);
        return $"topics_{combined.GetHashCode():X}";
    }
    
    /// <summary>
    /// Build a merge log from a topic mapping dictionary
    /// </summary>
    public TopicMergeLog BuildMergeLog(TopicMappingDictionary mapping, string aggressivenessLevel = "Medium")
    {
        var log = new TopicMergeLog
        {
            Source = mapping.Source,
            TotalOriginalTopics = mapping.Statistics.TotalMappings,
            TotalCanonicalTopics = mapping.Statistics.UniqueCanonicalTopics,
            MergeRatio = mapping.Statistics.MergeRatio,
            AggressivenessLevel = aggressivenessLevel
        };
        
        // Build merge entries for topics that were actually merged
        foreach (var canonical in mapping.GetAllCanonicalTopics())
        {
            var originals = mapping.GetOriginalTopics(canonical);
            
            // Only log if there was an actual merge (more than one topic or topic changed)
            if (originals.Count > 1 || (originals.Count == 1 && !string.Equals(originals[0], canonical, StringComparison.OrdinalIgnoreCase)))
            {
                var mergeEntry = new TopicMergeEntry
                {
                    CanonicalTopic = canonical,
                    OriginalTopics = originals.ToList(),
                    MergeSource = mapping.Source,
                    Confidence = 0.85 // Default confidence for rule-based merges
                };
                
                log.Merges.Add(mergeEntry);
            }
        }
        
        // Calculate statistics
        log.Statistics.MergeGroupsCreated = log.Merges.Count;
        log.Statistics.TopicsMerged = mapping.Statistics.MergedTopics;
        log.Statistics.TopicsKeptSeparate = mapping.Statistics.UnchangedTopics;
        log.Statistics.AverageConfidence = log.Merges.Count > 0 
            ? log.Merges.Average(m => m.Confidence) 
            : 0;
        log.Statistics.HighConfidenceMerges = log.Merges.Count(m => m.Confidence >= 0.90);
        log.Statistics.MediumConfidenceMerges = log.Merges.Count(m => m.Confidence >= 0.70 && m.Confidence < 0.90);
        log.Statistics.LowConfidenceMerges = log.Merges.Count(m => m.Confidence < 0.70);
        
        _lastMergeLog = log;
        
        _logger.Information("Built merge log: {MergeGroups} merge groups, {TopicsMerged} topics merged, {KeptSeparate} kept separate",
            log.Statistics.MergeGroupsCreated, log.Statistics.TopicsMerged, log.Statistics.TopicsKeptSeparate);
        
        return log;
    }
    
    /// <summary>
    /// Build a merge log from a topic mapping dictionary with deduplication result details
    /// </summary>
    public TopicMergeLog BuildMergeLog(TopicMappingDictionary mapping, TopicDeduplicationResult deduplicationResult, string aggressivenessLevel = "Medium")
    {
        var log = BuildMergeLog(mapping, aggressivenessLevel);
        
        // Enrich with details from deduplication result
        log.Merges.Clear(); // Clear and rebuild with more detail
        
        foreach (var mergeGroup in deduplicationResult.MergeGroups)
        {
            var mergeEntry = new TopicMergeEntry
            {
                CanonicalTopic = mergeGroup.CanonicalName,
                OriginalTopics = mergeGroup.OriginalNames.ToList(),
                Confidence = mergeGroup.Confidence,
                Rationale = mergeGroup.MergeReason,
                MergeSource = "LLM"
            };
            
            log.Merges.Add(mergeEntry);
        }
        
        // Add kept-separate entries
        foreach (var unmerged in deduplicationResult.UnmergedTopics)
        {
            log.KeptSeparate.Add(new TopicKeptSeparateEntry
            {
                Topic = unmerged.Topic,
                Reason = unmerged.Reason ?? "No similar topics found"
            });
        }
        
        // Recalculate statistics with confidence data
        log.Statistics.AverageConfidence = log.Merges.Count > 0 
            ? log.Merges.Average(m => m.Confidence) 
            : 0;
        log.Statistics.HighConfidenceMerges = log.Merges.Count(m => m.Confidence >= 0.90);
        log.Statistics.MediumConfidenceMerges = log.Merges.Count(m => m.Confidence >= 0.70 && m.Confidence < 0.90);
        log.Statistics.LowConfidenceMerges = log.Merges.Count(m => m.Confidence < 0.70);
        
        
        _lastMergeLog = log;
        
        return log;
    }
    
    /// <summary>
    /// Save a merge log to a JSON file
    /// </summary>
    public async Task SaveMergeLogAsync(TopicMergeLog log, string filePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        
        _logger.Information("Saved topic merge log to {Path}: {MergeGroups} merge groups, {TotalTopics} total topics",
            filePath, log.Merges.Count, log.TotalOriginalTopics);
    }
    
    /// <summary>
    /// Get the last merge log that was created
    /// </summary>
    public TopicMergeLog? GetLastMergeLog()
    {
        return _lastMergeLog;
    }
    
    /// <summary>
    /// Save merge log if logging is enabled in configuration
    /// </summary>
    public async Task<bool> SaveMergeLogIfEnabledAsync(TopicMappingDictionary mapping, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        if (_config == null || !_config.LogMerges)
        {
            return false;
        }
        
        var log = _lastMergeLog ?? BuildMergeLog(mapping, _config.Aggressiveness.ToString());
        var filePath = outputPath ?? _config.MergeLogPath;
        
        await SaveMergeLogAsync(log, filePath, cancellationToken);
        return true;
    }
}
