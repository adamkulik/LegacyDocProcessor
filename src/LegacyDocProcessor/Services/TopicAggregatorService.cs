using System.Text;
using System.Text.RegularExpressions;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for aggregating document content by topics
/// </summary>
public interface ITopicAggregatorService
{
    Task<AggregationResult> AggregateAsync(List<ProcessedKnowledge> processedDocuments, CancellationToken cancellationToken = default);
    string MergeTopicContent(List<TopicSource> sources);
    Task<TopicMappingDictionary?> GetLastMappingAsync();
}

/// <summary>
/// Aggregates processed documents into unified topic-based content
/// </summary>
public class TopicAggregatorService : ITopicAggregatorService
{
    private readonly ILogger _logger;
    private readonly ITopicMappingService? _mappingService;
    private readonly TopicDeduplicationConfig? _dedupConfig;
    
    // Minimum relevance threshold to include content in merged topic
    private const double MinRelevanceThreshold = 0.2;
    
    // Maximum content length per source before truncation
    private const int MaxSourceContentLength = 8000;
    
    // Cache the last mapping for downstream use
    private TopicMappingDictionary? _lastMapping;
    
    public TopicAggregatorService(
        ILogger logger, 
        ITopicMappingService? mappingService = null,
        TopicDeduplicationConfig? dedupConfig = null)
    {
        _logger = logger;
        _mappingService = mappingService;
        _dedupConfig = dedupConfig;
    }
    
    /// <summary>
    /// Aggregate processed documents into unified topics (with deduplication)
    /// </summary>
    public async Task<AggregationResult> AggregateAsync(
        List<ProcessedKnowledge> processedDocuments, 
        CancellationToken cancellationToken = default)
    {
        _logger.Information("Starting topic aggregation for {Count} documents", processedDocuments.Count);
        
        var result = new AggregationResult
        {
            TotalSourceFiles = processedDocuments.Count
        };
        
        // Collect all topics with their sources
        var topicSources = new Dictionary<string, List<TopicSource>>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var doc in processedDocuments)
        {
            foreach (var topicInfo in doc.Topics)
            {
                if (topicInfo.Relevance < MinRelevanceThreshold)
                {
                    _logger.Debug("Skipping topic '{Topic}' from {File} due to low relevance ({Relevance})",
                        topicInfo.Topic, doc.FilePath, topicInfo.Relevance);
                    continue;
                }
                
                if (!topicSources.TryGetValue(topicInfo.Topic, out var sources))
                {
                    sources = new List<TopicSource>();
                    topicSources[topicInfo.Topic] = sources;
                }
                
                sources.Add(new TopicSource
                {
                    FilePath = doc.FilePath,
                    FileName = Path.GetFileName(doc.FilePath),
                    Content = TruncateContent(topicInfo.Content, MaxSourceContentLength),
                    Relevance = topicInfo.Relevance,
                    Summary = topicInfo.Summary
                });
            }
        }
        
        // Get topic name mappings from mapping service
        var topicMapping = await GetTopicMappingsAsync(topicSources.Keys.ToList(), cancellationToken);
        
        // Group content by canonical topic name
        var canonicalGroups = new Dictionary<string, List<TopicSource>>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var (originalTopic, sources) in topicSources)
        {
            var canonicalTopic = topicMapping.GetCanonicalTopic(originalTopic);
            
            if (!canonicalGroups.TryGetValue(canonicalTopic, out var groupSources))
            {
                groupSources = new List<TopicSource>();
                canonicalGroups[canonicalTopic] = groupSources;
            }
            
            groupSources.AddRange(sources);
        }
        
        // Build unified topics from groups
        foreach (var (topicName, sources) in canonicalGroups)
        {
            // Sort by relevance (highest first)
            var sortedSources = sources.OrderByDescending(s => s.Relevance).ToList();
            
            var mergedContent = MergeTopicContent(sortedSources);
            
            var unifiedTopic = new UnifiedTopic
            {
                Name = topicName,
                MergedContent = mergedContent,
                SourceFiles = sortedSources.Select(s => s.FileName).Distinct().ToList(),
                Sources = sortedSources,
                SectionCount = sortedSources.Count,
                AverageRelevance = sortedSources.Average(s => s.Relevance)
            };
            
            result.Topics.Add(unifiedTopic);
            result.TopicFileCounts[topicName] = sortedSources.Count;
            
            _logger.Information("Aggregated topic '{Topic}' from {Count} sources", 
                topicName, sortedSources.Count);
        }
        
        // Sort topics by number of sources (most covered first)
        result.Topics = result.Topics
            .OrderByDescending(t => t.SourceFiles.Count)
            .ThenBy(t => t.Name)
            .ToList();
        
        _logger.Information("Aggregation complete: {TopicCount} topics from {FileCount} documents",
            result.Topics.Count, processedDocuments.Count);
        
        return result;
    }
    
    /// <summary>
    /// Get topic name mappings from mapping service
    /// </summary>
    private async Task<TopicMappingDictionary> GetTopicMappingsAsync(
        List<string> topicNames, 
        CancellationToken cancellationToken)
    {
        if (_mappingService == null || topicNames.Count < 3)
        {
            _logger.Debug("Skipping topic deduplication (service not available or too few topics)");
            return new TopicMappingDictionary { Source = "None" };
        }
        
        try
        {
            var mapping = await _mappingService.BuildFromTopicsAsync(topicNames, cancellationToken);
            _lastMapping = mapping; // Cache for downstream use
            
            _logger.Information("Topic mapping: {Original} topics → {Canonical} canonical topics (merge ratio: {Ratio:F2})",
                mapping.Statistics.TotalMappings, 
                mapping.Statistics.UniqueCanonicalTopics,
                mapping.Statistics.MergeRatio);
            
            // Log merge groups for debugging
            foreach (var canonical in mapping.GetAllCanonicalTopics())
            {
                var originals = mapping.GetOriginalTopics(canonical);
                if (originals.Count > 1)
                {
                    _logger.Debug("Merged topics into '{Canonical}': {Members}",
                        canonical, string.Join(", ", originals));
                }
            }
            
            // Save merge log if configured
            if (_dedupConfig?.LogMerges == true)
            {
                try
                {
                    var mergeLog = _mappingService.BuildMergeLog(mapping, _dedupConfig.Aggressiveness.ToString());
                    await _mappingService.SaveMergeLogAsync(mergeLog, _dedupConfig.MergeLogPath, cancellationToken);
                    _logger.Information("Topic merge log saved to {Path}", _dedupConfig.MergeLogPath);
                }
                catch (Exception logEx)
                {
                    _logger.Warning(logEx, "Failed to save topic merge log to {Path}", _dedupConfig.MergeLogPath);
                }
            }
            
            return mapping;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Topic mapping failed, proceeding without deduplication");
            return new TopicMappingDictionary { Source = "Error" };
        }
    }
    
    /// <summary>
    /// Get the last mapping dictionary for downstream use
    /// </summary>
    public Task<TopicMappingDictionary?> GetLastMappingAsync()
    {
        return Task.FromResult(_lastMapping);
    }
    
    /// <summary>
    /// Merge content from multiple sources into a unified topic page
    /// </summary>
    public string MergeTopicContent(List<TopicSource> sources)
    {
        if (sources.Count == 0)
            return string.Empty;
            
        if (sources.Count == 1)
        {
            var s = sources[0];
            return BuildSourceSection(s.FileName, s.Content, s.Relevance, s.Summary);
        }
        
        var sb = new StringBuilder();
        
        // Add header with source count
        sb.AppendLine($"*This topic is covered by {sources.Count} source documents.*");
        sb.AppendLine();
        
        // Process each source, deduplicating as we go
        var seenParagraphs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedAnyContent = false;
        
        foreach (var source in sources)
        {
            // Skip if no meaningful content
            if (string.IsNullOrWhiteSpace(source.Content))
                continue;
                
            // Extract and deduplicate paragraphs
            var paragraphs = source.Content
                .Split(new[] { "\r\n\r\n", "\n\n", "\r\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 20) // Skip very short paragraphs
                .ToList();
            
            var newParagraphs = new List<string>();
            foreach (var paragraph in paragraphs)
            {
                // Normalize for comparison
                var normalized = NormalizeForComparison(paragraph);
                if (!seenParagraphs.Contains(normalized))
                {
                    seenParagraphs.Add(normalized);
                    newParagraphs.Add(paragraph);
                }
            }
            
            if (newParagraphs.Count > 0)
            {
                if (addedAnyContent)
                {
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
                
                sb.AppendLine(BuildSourceSection(
                    source.FileName, 
                    string.Join("\n\n", newParagraphs),
                    source.Relevance,
                    source.Summary));
                
                addedAnyContent = true;
            }
        }
        
        // Add sources reference section at the bottom
        if (sources.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("*Sources:*");
            foreach (var source in sources.DistinctBy(s => s.FileName))
            {
                sb.AppendLine($"  * {source.FileName}");
            }
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Build a single source section with header and content
    /// </summary>
    private string BuildSourceSection(string fileName, string content, double relevance, string summary)
    {
        var sb = new StringBuilder();
        
        // Section header with relevance indicator
        var relevanceStars = GetRelevanceStars(relevance);
        sb.AppendLine($"h2. {relevanceStars} {SanitizeTitle(fileName)}");
        sb.AppendLine();
        
        // Summary if available
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine($"_{summary}_");
            sb.AppendLine();
        }
        
        // Content
        sb.AppendLine(content);
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Normalize text for paragraph comparison (deduplication)
    /// </summary>
    private string NormalizeForComparison(string text)
    {
        // Lowercase, remove extra whitespace, remove punctuation
        var normalized = text.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = Regex.Replace(normalized, @"[^\w\s]", "");
        return normalized.Trim();
    }
    
    /// <summary>
    /// Truncate content to maximum length
    /// </summary>
    private string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
            
        // Try to truncate at a sentence boundary
        var truncated = content.Substring(0, maxLength);
        var lastPeriod = truncated.LastIndexOf('.');
        var lastNewline = truncated.LastIndexOf('\n');
        var cutoff = Math.Max(lastPeriod, lastNewline);
        
        if (cutoff > maxLength / 2) // Only cut at sentence if it's reasonably close
        {
            return truncated.Substring(0, cutoff + 1);
        }
        
        return truncated + "...";
    }
    
    /// <summary>
    /// Get relevance indicator stars
    /// </summary>
    private string GetRelevanceStars(double relevance)
    {
        return relevance switch
        {
            >= 0.9 => "★★★★★",
            >= 0.7 => "★★★★",
            >= 0.5 => "★★★",
            >= 0.3 => "★★",
            _ => "★"
        };
    }
    
    /// <summary>
    /// SanitizeConfluence title (remove special characters)
    /// </summary>
    private string SanitizeTitle(string title)
    {
        // Remove or replace characters that cause issues in Confluence
        return Regex.Replace(title, @"[\[\]\{\}\\|^`]", "_");
    }
}
