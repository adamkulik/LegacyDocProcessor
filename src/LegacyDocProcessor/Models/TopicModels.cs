using System.Text.Json.Serialization;

namespace LegacyDocProcessor.Models;

/// <summary>
/// Detailed topic information extracted by LLM from a single document
/// </summary>
public class TopicInfo
{
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;
    
    [JsonPropertyName("relevance")]
    public double Relevance { get; set; }
    
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Represents a unified topic after aggregation from multiple files
/// </summary>
public class UnifiedTopic
{
    public string Name { get; set; } = string.Empty;
    public string MergedContent { get; set; } = string.Empty;
    public List<string> SourceFiles { get; set; } = new();
    public List<TopicSource> Sources { get; set; } = new();
    public int SectionCount { get; set; }
    public double AverageRelevance { get; set; }
    public DateTime AggregatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks the source of topic content
/// </summary>
public class TopicSource
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Relevance { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Result of aggregating multiple documents into topics
/// </summary>
public class AggregationResult
{
    public List<UnifiedTopic> Topics { get; set; } = new();
    public int TotalSourceFiles { get; set; }
    public Dictionary<string, int> TopicFileCounts { get; set; } = new();
    public DateTime AggregatedAt { get; set; } = DateTime.UtcNow;
}
