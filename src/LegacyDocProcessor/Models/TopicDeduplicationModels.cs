namespace LegacyDocProcessor.Models;

/// <summary>
/// Result of topic deduplication process
/// </summary>
public class TopicDeduplicationResult
{
    /// <summary>
    /// Mapping from original topic name to canonical (deduplicated) topic name
    /// </summary>
    public Dictionary<string, string> TopicMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// List of all canonical topic names after deduplication
    /// </summary>
    public List<string> CanonicalTopics { get; set; } = new();
    
    /// <summary>
    /// Groups of topics that were merged together
    /// </summary>
    public List<TopicMergeGroup> MergeGroups { get; set; } = new();
    
    /// <summary>
    /// Topics that were kept separate (not merged) with reasons
    /// </summary>
    public List<UnmergedTopic> UnmergedTopics { get; set; } = new();
    
    /// <summary>
    /// Number of original topics before deduplication
    /// </summary>
    public int OriginalTopicCount { get; set; }
    
    /// <summary>
    /// Number of topics after deduplication
    /// </summary>
    public int DeduplicatedTopicCount { get; set; }
    
    /// <summary>
    /// Whether deduplication was actually performed (false if skipped)
    /// </summary>
    public bool WasPerformed { get; set; }
    
    /// <summary>
    /// Statistics about the deduplication process
    /// </summary>
    public DeduplicationStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Represents a group of topics that were merged into a canonical topic
/// </summary>
public class TopicMergeGroup
{
    /// <summary>
    /// The canonical topic name chosen for this group
    /// </summary>
    public string CanonicalName { get; set; } = string.Empty;
    
    /// <summary>
    /// All original topic names that were merged into this canonical name
    /// </summary>
    public List<string> OriginalNames { get; set; } = new();
    
    /// <summary>
    /// Reason for merging (from LLM)
    /// </summary>
    public string MergeReason { get; set; } = string.Empty;
    
    /// <summary>
    /// Confidence score for this merge (0.0-1.0)
    /// Higher values indicate more certain merges
    /// </summary>
    public double Confidence { get; set; } = 1.0;
}

/// <summary>
/// Represents a topic that was kept separate (not merged)
/// </summary>
public class UnmergedTopic
{
    /// <summary>
    /// The topic name that was kept separate
    /// </summary>
    public string Topic { get; set; } = string.Empty;
    
    /// <summary>
    /// Reason why this topic was not merged with others
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Statistics about the deduplication process
/// </summary>
public class DeduplicationStatistics
{
    /// <summary>
    /// Number of merge groups created
    /// </summary>
    public int MergeGroupsCreated { get; set; }
    
    /// <summary>
    /// Number of topics that were kept separate
    /// </summary>
    public int TopicsKeptSeparate { get; set; }
    
    /// <summary>
    /// Average confidence score across all merges
    /// </summary>
    public double AverageConfidence { get; set; }
    
    /// <summary>
    /// Number of high-confidence merges (≥ 0.90)
    /// </summary>
    public int HighConfidenceMerges { get; set; }
    
    /// <summary>
    /// Number of medium-confidence merges (0.70-0.89)
    /// </summary>
    public int MediumConfidenceMerges { get; set; }
    
    /// <summary>
    /// Number of low-confidence merges (< 0.70)
    /// </summary>
    public int LowConfidenceMerges { get; set; }
    
    /// <summary>
    /// Merge ratio (original count / canonical count)
    /// </summary>
    public double MergeRatio { get; set; }
}
