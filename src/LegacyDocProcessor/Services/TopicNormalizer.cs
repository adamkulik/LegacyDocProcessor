using System.Text.RegularExpressions;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Interface for topic normalization service
/// </summary>
public interface ITopicNormalizer
{
    /// <summary>
    /// Normalize a single topic name using rule-based transformations
    /// </summary>
    /// <param name="topic">The original topic name</param>
    /// <returns>The normalized topic name</returns>
    string Normalize(string topic);
    
    /// <summary>
    /// Normalize a list of topic names and group them by their normalized form
    /// </summary>
    /// <param name="topics">List of original topic names</param>
    /// <returns>Dictionary mapping normalized topics to lists of original topics that normalize to them</returns>
    Dictionary<string, List<string>> NormalizeAndGroup(IEnumerable<string> topics);
    
    /// <summary>
    /// Get the normalization statistics from the last normalization operation
    /// </summary>
    NormalizationStatistics GetStatistics();
}

/// <summary>
/// Statistics about topic normalization
/// </summary>
public class NormalizationStatistics
{
    /// <summary>
    /// Number of topics processed
    /// </summary>
    public int TopicsProcessed { get; set; }
    
    /// <summary>
    /// Number of topics that were changed by normalization
    /// </summary>
    public int TopicsChanged { get; set; }
    
    /// <summary>
    /// Number of groups created (unique normalized topics)
    /// </summary>
    public int UniqueGroups { get; set; }
    
    /// <summary>
    /// Number of custom rules applied
    /// </summary>
    public int CustomRulesApplied { get; set; }
    
    /// <summary>
    /// Number of suffix removals performed
    /// </summary>
    public int SuffixRemovals { get; set; }
    
    /// <summary>
    /// Number of punctuation normalizations performed
    /// </summary>
    public int PunctuationNormalizations { get; set; }
    
    /// <summary>
    /// Processing time
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }
}

/// <summary>
/// Service for normalizing topic names using rule-based transformations.
/// This is Phase 1 of the topic deduplication process.
/// 
/// Normalization steps:
/// 1. Lowercase all topics
/// 2. Remove common suffixes (Capabilities, Functionality, Features, etc.)
/// 3. Normalize punctuation (hyphens → spaces, underscores → spaces)
/// 4. Trim whitespace and collapse multiple spaces
/// 5. Apply custom regex-based merge rules
/// </summary>
public class TopicNormalizer : ITopicNormalizer
{
    private readonly TopicDeduplicationConfig _config;
    private readonly ILogger _logger;
    private NormalizationStatistics _lastStatistics = new();
    
    /// <summary>
    /// Default suffixes to remove from topic names (case-insensitive)
    /// </summary>
    private static readonly string[] DefaultSuffixesToRemove = 
    {
        "Capabilities",
        "Functionality",
        "Features",
        "Overview",
        "Summary",
        "Details",
        "Information",
        "Info",
        "Description",
        "Documentation",
        "Docs"
    };
    
    /// <summary>
    /// Characters to normalize to spaces
    /// </summary>
    private static readonly char[] CharsToNormalize = { '-', '_', '.' };
    
    /// <summary>
    /// Regex to collapse multiple spaces into one
    /// </summary>
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);
    
    /// <summary>
    /// Regex to match common suffixes (case-insensitive)
    /// </summary>
    private readonly Regex _suffixRemovalRegex;
    
    public TopicNormalizer(TopicDeduplicationConfig config, ILogger logger)
    {
        _config = config ?? new TopicDeduplicationConfig();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Build suffix removal regex from default suffixes
        var suffixPattern = string.Join("|", DefaultSuffixesToRemove.Select(Regex.Escape));
        _suffixRemovalRegex = new Regex(
            $@"\s+({suffixPattern})(?:\s|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
    
    /// <summary>
    /// Normalize a single topic name
    /// </summary>
    public string Normalize(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return string.Empty;
        }
        
        var original = topic;
        
        // Step 1: Lowercase
        topic = topic.ToLowerInvariant();
        
        // Step 2: Normalize punctuation (hyphens, underscores, dots → spaces)
        var charArray = topic.ToCharArray();
        for (int i = 0; i < charArray.Length; i++)
        {
            if (CharsToNormalize.Contains(charArray[i]))
            {
                charArray[i] = ' ';
            }
        }
        topic = new string(charArray);
        
        // Step 3: Remove common suffixes
        topic = RemoveSuffixes(topic);
        
        // Step 4: Trim and collapse multiple spaces
        topic = MultipleSpacesRegex.Replace(topic.Trim(), " ");
        
        // Step 5: Apply custom merge rules
        topic = ApplyCustomRules(topic);
        
        // Final trim
        topic = topic.Trim();
        
        if (!string.Equals(original, topic, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug("Normalized topic: '{Original}' → '{Normalized}'", original, topic);
        }
        
        return topic;
    }
    
    /// <summary>
    /// Normalize a list of topics and group them by their normalized form
    /// </summary>
    public Dictionary<string, List<string>> NormalizeAndGroup(IEnumerable<string> topics)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var statistics = new NormalizationStatistics();
        
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var topicList = topics.ToList();
        
        foreach (var originalTopic in topicList)
        {
            if (string.IsNullOrWhiteSpace(originalTopic))
            {
                continue;
            }
            
            statistics.TopicsProcessed++;
            
            var normalizedTopic = NormalizeWithStats(originalTopic, statistics);
            
            if (!string.Equals(originalTopic, normalizedTopic, StringComparison.OrdinalIgnoreCase))
            {
                statistics.TopicsChanged++;
            }
            
            if (!groups.TryGetValue(normalizedTopic, out var group))
            {
                group = new List<string>();
                groups[normalizedTopic] = group;
            }
            
            // Only add if not already in the group (avoid duplicates)
            if (!group.Contains(originalTopic, StringComparer.OrdinalIgnoreCase))
            {
                group.Add(originalTopic);
            }
        }
        
        statistics.UniqueGroups = groups.Count;
        statistics.ProcessingTime = stopwatch.Elapsed;
        _lastStatistics = statistics;
        
        _logger.Information(
            "Topic normalization complete: {Processed} topics → {Unique} unique groups " +
            "({Changed} changed, {Rules} custom rules applied, {Suffixes} suffixes removed)",
            statistics.TopicsProcessed,
            statistics.UniqueGroups,
            statistics.TopicsChanged,
            statistics.CustomRulesApplied,
            statistics.SuffixRemovals);
        
        return groups;
    }
    
    /// <summary>
    /// Get statistics from the last normalization operation
    /// </summary>
    public NormalizationStatistics GetStatistics() => _lastStatistics;
    
    /// <summary>
    /// Normalize a topic and track statistics
    /// </summary>
    private string NormalizeWithStats(string topic, NormalizationStatistics stats)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return string.Empty;
        }
        
        // Step 1: Lowercase
        topic = topic.ToLowerInvariant();
        
        // Step 2: Normalize punctuation
        var beforePunctuation = topic;
        var charArray = topic.ToCharArray();
        int punctuationChanges = 0;
        for (int i = 0; i < charArray.Length; i++)
        {
            if (CharsToNormalize.Contains(charArray[i]))
            {
                charArray[i] = ' ';
                punctuationChanges++;
            }
        }
        topic = new string(charArray);
        if (punctuationChanges > 0)
        {
            stats.PunctuationNormalizations++;
        }
        
        // Step 3: Remove suffixes
        var beforeSuffix = topic;
        topic = RemoveSuffixes(topic);
        if (!string.Equals(beforeSuffix, topic, StringComparison.Ordinal))
        {
            stats.SuffixRemovals++;
        }
        
        // Step 4: Trim and collapse spaces
        topic = MultipleSpacesRegex.Replace(topic.Trim(), " ");
        
        // Step 5: Apply custom rules
        var beforeCustom = topic;
        topic = ApplyCustomRulesWithStats(topic, stats);
        
        return topic.Trim();
    }
    
    /// <summary>
    /// Remove common suffixes from a topic name
    /// </summary>
    private string RemoveSuffixes(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return topic;
        }
        
        // Match and remove suffixes like "Capabilities", "Functionality", etc.
        var result = _suffixRemovalRegex.Replace(topic, " ");
        
        // Also check for suffixes at the very end of the string
        foreach (var suffix in DefaultSuffixesToRemove)
        {
            if (result.EndsWith($" {suffix}", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - suffix.Length - 1);
            }
            else if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && 
                     result.Length > suffix.Length)
            {
                result = result.Substring(0, result.Length - suffix.Length);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Apply custom merge rules from configuration
    /// </summary>
    private string ApplyCustomRules(string topic)
    {
        if (_config.CustomMergeRules == null || _config.CustomMergeRules.Count == 0)
        {
            return topic;
        }
        
        foreach (var rule in _config.CustomMergeRules)
        {
            if (string.IsNullOrEmpty(rule.Pattern))
            {
                continue;
            }
            
            try
            {
                var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
                if (regex.IsMatch(topic))
                {
                    var result = regex.Replace(topic, rule.CanonicalReplacement);
                    if (!string.Equals(result, topic, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Debug(
                            "Custom rule '{RuleName}' applied: '{Topic}' → '{Result}'",
                            rule.Name ?? rule.Pattern,
                            topic,
                            result);
                        topic = result;
                    }
                }
            }
            catch (RegexParseException ex)
            {
                _logger.Warning(
                    ex,
                    "Invalid regex pattern in custom merge rule '{RuleName}': {Pattern}",
                    rule.Name ?? "unnamed",
                    rule.Pattern);
            }
        }
        
        return topic;
    }
    
    /// <summary>
    /// Apply custom merge rules and track statistics
    /// </summary>
    private string ApplyCustomRulesWithStats(string topic, NormalizationStatistics stats)
    {
        if (_config.CustomMergeRules == null || _config.CustomMergeRules.Count == 0)
        {
            return topic;
        }
        
        foreach (var rule in _config.CustomMergeRules)
        {
            if (string.IsNullOrEmpty(rule.Pattern))
            {
                continue;
            }
            
            try
            {
                var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
                if (regex.IsMatch(topic))
                {
                    var result = regex.Replace(topic, rule.CanonicalReplacement);
                    if (!string.Equals(result, topic, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Debug(
                            "Custom rule '{RuleName}' applied: '{Topic}' → '{Result}'",
                            rule.Name ?? rule.Pattern,
                            topic,
                            result);
                        topic = result;
                        stats.CustomRulesApplied++;
                    }
                }
            }
            catch (RegexParseException ex)
            {
                _logger.Warning(
                    ex,
                    "Invalid regex pattern in custom merge rule '{RuleName}': {Pattern}",
                    rule.Name ?? "unnamed",
                    rule.Pattern);
            }
        }
        
        return topic;
    }
    
    /// <summary>
    /// Build merge results from normalization groups
    /// </summary>
    /// <param name="groups">Groups from NormalizeAndGroup</param>
    /// <returns>List of merge results for groups with multiple topics</returns>
    public List<TopicMergeResult> BuildMergeResults(Dictionary<string, List<string>> groups)
    {
        var results = new List<TopicMergeResult>();
        
        foreach (var group in groups)
        {
            if (group.Value.Count > 1)
            {
                // Multiple topics map to the same normalized form
                var mergeResult = new TopicMergeResult
                {
                    CanonicalName = ToTitleCase(group.Key),
                    MergedTopics = group.Value.ToList(),
                    Confidence = 1.0f, // Rule-based matches have high confidence
                    Rationale = "Topics normalized to the same form via rule-based processing",
                    Source = "Rule"
                };
                results.Add(mergeResult);
            }
        }
        
        return results;
    }
    
    /// <summary>
    /// Convert a normalized (lowercase) topic back to title case for display
    /// </summary>
    private static string ToTitleCase(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return topic;
        }
        
        var words = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new System.Text.StringBuilder();
        
        foreach (var word in words)
        {
            if (result.Length > 0)
            {
                result.Append(' ');
            }
            
            if (word.Length > 0)
            {
                result.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                {
                    result.Append(word.Substring(1));
                }
            }
        }
        
        return result.ToString();
    }
}
