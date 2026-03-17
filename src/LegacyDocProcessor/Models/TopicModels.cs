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
/// Comprehensive topic mapping dictionary for downstream use.
/// Maps original topic names to canonical (deduplicated) names.
/// </summary>
public class TopicMappingDictionary
{
    /// <summary>
    /// Mapping from original topic name to canonical topic name
    /// </summary>
    public Dictionary<string, string> OriginalToCanonical { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Reverse mapping: canonical topic name to list of original names
    /// </summary>
    public Dictionary<string, List<string>> CanonicalToOriginals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Metadata about when this mapping was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Source of this mapping (e.g., "LLM", "Manual", "Normalizer")
    /// </summary>
    public string Source { get; set; } = "Unknown";
    
    /// <summary>
    /// Statistics about the mapping
    /// </summary>
    public TopicMappingStatistics Statistics { get; set; } = new();
    
    /// <summary>
    /// Get the canonical topic for an original topic name
    /// </summary>
    public string GetCanonicalTopic(string originalTopic)
    {
        if (OriginalToCanonical.TryGetValue(originalTopic, out var canonical))
        {
            return canonical;
        }
        return originalTopic; // Return original if no mapping exists
    }
    
    /// <summary>
    /// Get all original topic names that map to a canonical topic
    /// </summary>
    public List<string> GetOriginalTopics(string canonicalTopic)
    {
        if (CanonicalToOriginals.TryGetValue(canonicalTopic, out var originals))
        {
            return originals.ToList();
        }
        return new List<string> { canonicalTopic };
    }
    
    /// <summary>
    /// Add a mapping from original to canonical topic
    /// </summary>
    public void AddMapping(string originalTopic, string canonicalTopic)
    {
        if (string.IsNullOrWhiteSpace(originalTopic) || string.IsNullOrWhiteSpace(canonicalTopic))
            return;
            
        // Add to forward mapping
        OriginalToCanonical[originalTopic] = canonicalTopic;
        
        // Add to reverse mapping
        if (!CanonicalToOriginals.TryGetValue(canonicalTopic, out var originals))
        {
            originals = new List<string>();
            CanonicalToOriginals[canonicalTopic] = originals;
        }
        
        if (!originals.Contains(originalTopic, StringComparer.OrdinalIgnoreCase))
        {
            originals.Add(originalTopic);
        }
        
        // Update statistics
        UpdateStatistics();
    }
    
    /// <summary>
    /// Add multiple mappings at once
    /// </summary>
    public void AddMappings(Dictionary<string, string> mappings)
    {
        foreach (var (original, canonical) in mappings)
        {
            AddMapping(original, canonical);
        }
    }
    
    /// <summary>
    /// Check if a topic has a mapping (different from itself)
    /// </summary>
    public bool HasMapping(string topic)
    {
        if (OriginalToCanonical.TryGetValue(topic, out var canonical))
        {
            return !string.Equals(topic, canonical, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    
    /// <summary>
    /// Get all canonical topic names
    /// </summary>
    public IEnumerable<string> GetAllCanonicalTopics()
    {
        return CanonicalToOriginals.Keys;
    }
    
    /// <summary>
    /// Get all original topic names
    /// </summary>
    public IEnumerable<string> GetAllOriginalTopics()
    {
        return OriginalToCanonical.Keys;
    }
    
    /// <summary>
    /// Apply mappings to a list of topics, returning canonical names
    /// </summary>
    public List<string> ApplyMappings(IEnumerable<string> topics)
    {
        return topics.Select(t => GetCanonicalTopic(t)).ToList();
    }
    
    /// <summary>
    /// Merge another mapping dictionary into this one
    /// </summary>
    public void Merge(TopicMappingDictionary other, bool overwrite = false)
    {
        foreach (var (original, canonical) in other.OriginalToCanonical)
        {
            if (overwrite || !OriginalToCanonical.ContainsKey(original))
            {
                AddMapping(original, canonical);
            }
        }
    }
    
    /// <summary>
    /// Export to a simple dictionary format
    /// </summary>
    public Dictionary<string, string> ToDictionary()
    {
        return new Dictionary<string, string>(OriginalToCanonical, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Create from a simple dictionary
    /// </summary>
    public static TopicMappingDictionary FromDictionary(Dictionary<string, string> mappings, string source = "Imported")
    {
        var dict = new TopicMappingDictionary { Source = source };
        dict.AddMappings(mappings);
        return dict;
    }
    
    /// <summary>
    /// Update statistics after changes
    /// </summary>
    private void UpdateStatistics()
    {
        Statistics.TotalMappings = OriginalToCanonical.Count;
        Statistics.UniqueCanonicalTopics = CanonicalToOriginals.Count;
        Statistics.MergedTopics = OriginalToCanonical.Count(kv => 
            !string.Equals(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase));
        Statistics.UnchangedTopics = OriginalToCanonical.Count(kv => 
            string.Equals(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Statistics about a topic mapping dictionary
/// </summary>
public class TopicMappingStatistics
{
    /// <summary>
    /// Total number of mappings (original topics)
    /// </summary>
    public int TotalMappings { get; set; }
    
    /// <summary>
    /// Number of unique canonical topics
    /// </summary>
    public int UniqueCanonicalTopics { get; set; }
    
    /// <summary>
    /// Number of topics that were merged (mapped to a different canonical name)
    /// </summary>
    public int MergedTopics { get; set; }
    
    /// <summary>
    /// Number of topics that remained unchanged (mapped to themselves)
    /// </summary>
    public int UnchangedTopics { get; set; }
    
    /// <summary>
    /// Merge ratio (original count / canonical count)
    /// </summary>
    public double MergeRatio => UniqueCanonicalTopics > 0 
        ? (double)TotalMappings / UniqueCanonicalTopics 
        : 1.0;
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

/// <summary>
/// Log of all topic merges performed during deduplication.
/// Saved to JSON file when LogMerges is enabled.
/// </summary>
public class TopicMergeLog
{
    /// <summary>
    /// When this log was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Source of the deduplication (e.g., "LLM", "Normalizer")
    /// </summary>
    public string Source { get; set; } = "Unknown";
    
    /// <summary>
    /// Total number of original topics before deduplication
    /// </summary>
    public int TotalOriginalTopics { get; set; }
    
    /// <summary>
    /// Total number of canonical topics after deduplication
    /// </summary>
    public int TotalCanonicalTopics { get; set; }
    
    /// <summary>
    /// Overall merge ratio (original / canonical)
    /// </summary>
    public double MergeRatio { get; set; }
    
    /// <summary>
    /// Aggressiveness level used for merging
    /// </summary>
    public string AggressivenessLevel { get; set; } = "Medium";
    
    /// <summary>
    /// Individual merge entries (only topics that were actually merged)
    /// </summary>
    public List<TopicMergeEntry> Merges { get; set; } = new();
    
    /// <summary>
    /// Topics that were kept separate (not merged)
    /// </summary>
    public List<TopicKeptSeparateEntry> KeptSeparate { get; set; } = new();
    
    /// <summary>
    /// Summary statistics
    /// </summary>
    public TopicMergeLogStatistics Statistics { get; set; } = new();
}

/// <summary>
/// A single topic merge entry in the log
/// </summary>
public class TopicMergeEntry
{
    /// <summary>
    /// The canonical (merged-to) topic name
    /// </summary>
    public string CanonicalTopic { get; set; } = string.Empty;
    
    /// <summary>
    /// Original topic names that were merged into the canonical topic
    /// </summary>
    public List<string> OriginalTopics { get; set; } = new();
    
    /// <summary>
    /// Confidence score for this merge (0.0-1.0)
    /// </summary>
    public double Confidence { get; set; }
    
    /// <summary>
    /// Rationale/explanation for why these topics were merged
    /// </summary>
    public string Rationale { get; set; } = string.Empty;
    
    /// <summary>
    /// Source of this merge decision (e.g., "LLM", "Normalizer", "CustomRule")
    /// </summary>
    public string MergeSource { get; set; } = "Unknown";
    
    /// <summary>
    /// Number of documents referencing this topic group
    /// </summary>
    public int DocumentCount { get; set; }
}

/// <summary>
/// A topic that was considered for merging but kept separate
/// </summary>
public class TopicKeptSeparateEntry
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
/// Statistics for the merge log
/// </summary>
public class TopicMergeLogStatistics
{
    /// <summary>
    /// Number of merge groups created
    /// </summary>
    public int MergeGroupsCreated { get; set; }
    
    /// <summary>
    /// Number of topics that were merged (changed from original)
    /// </summary>
    public int TopicsMerged { get; set; }
    
    /// <summary>
    /// Number of topics kept separate (unchanged)
    /// </summary>
    public int TopicsKeptSeparate { get; set; }
    
    /// <summary>
    /// Average confidence score for merges
    /// </summary>
    public double AverageConfidence { get; set; }
    
    /// <summary>
    /// Number of high confidence merges (≥ 0.90)
    /// </summary>
    public int HighConfidenceMerges { get; set; }
    
    /// <summary>
    /// Number of medium confidence merges (0.70-0.89)
    /// </summary>
    public int MediumConfidenceMerges { get; set; }
    
    /// <summary>
    /// Number of low confidence merges (&lt; 0.70)
    /// </summary>
    public int LowConfidenceMerges { get; set; }
    
    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}
