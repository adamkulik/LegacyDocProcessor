using System.Text;
using System.Text.RegularExpressions;
using LegacyDocProcessor.Models;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for aggregating document content by topics
/// </summary>
public interface ITopicAggregatorService
{
    AggregationResult Aggregate(List<ProcessedKnowledge> processedDocuments);
    string MergeTopicContent(List<TopicSource> sources);
}

/// <summary>
/// Aggregates processed documents into unified topic-based content
/// </summary>
public class TopicAggregatorService : ITopicAggregatorService
{
    private readonly ILogger _logger;
    
    // Minimum relevance threshold to include content in merged topic
    private const double MinRelevanceThreshold = 0.2;
    
    // Maximum content length per source before truncation
    private const int MaxSourceContentLength = 8000;
    
    public TopicAggregatorService(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Aggregate processed documents into unified topics
    /// </summary>
    public AggregationResult Aggregate(List<ProcessedKnowledge> processedDocuments)
    {
        _logger.Information("Starting topic aggregation for {Count} documents", processedDocuments.Count);
        
        var result = new AggregationResult
        {
            TotalSourceFiles = processedDocuments.Count
        };
        
        // Group content by normalized topic name
        var topicGroups = new Dictionary<string, List<TopicSource>>(StringComparer.OrdinalIgnoreCase);
        
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
                
                // Normalize topic name for grouping
                var normalizedTopic = NormalizeTopicName(topicInfo.Topic);
                
                if (!topicGroups.TryGetValue(normalizedTopic, out var sources))
                {
                    sources = new List<TopicSource>();
                    topicGroups[normalizedTopic] = sources;
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
        
        // Build unified topics from groups
        foreach (var (topicName, sources) in topicGroups)
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
    /// Normalize topic name for grouping
    /// </summary>
    private string NormalizeTopicName(string topic)
    {
        // Trim, remove extra whitespace, capitalize first letter of each word
        var normalized = topic.Trim();
        
        // Remove common prefixes/suffixes that don't add meaning
        normalized = Regex.Replace(normalized, @"^(the\s+|a\s+|an\s+)", "", RegexOptions.IgnoreCase);
        
        // Normalize whitespace
        normalized = Regex.Replace(normalized, @"\s+", " ");
        
        // Title case
        normalized = System.Globalization.CultureInfo.CurrentCulture
            .TextInfo.ToTitleCase(normalized.ToLower());
        
        return normalized;
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
