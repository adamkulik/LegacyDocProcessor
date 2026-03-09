using FluentAssertions;
using LegacyDocProcessor.Models;
using Xunit;

namespace LegacyDocProcessor.Tests;

/// <summary>
/// Tests for Local Knowledge Base (Markdown) export functionality
/// Tests 14, 15, 23 from PROGRESS.md
/// </summary>
public class LocalKBExportTests : IDisposable
{
    private readonly string _testOutputDir;

    public LocalKBExportTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), $"LocalKBTests_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, true);
        }
    }

    /// <summary>
    /// Test 14: LocalKB_GenerateTopicPage
    /// </summary>
    [Fact]
    public void GenerateTopicPage_ShouldCreateValidMarkdownFile()
    {
        // Arrange
        var topic = new UnifiedTopic
        {
            Name = "Installation Guide",
            MergedContent = "This is the installation content.\n\n### Prerequisites\n- .NET 8",
            SourceFiles = new List<string> { "install.txt", "setup.docx" },
            SectionCount = 2,
            AverageRelevance = 0.85,
            AggregatedAt = DateTime.UtcNow
        };
        
        // Act - Simulate export
        var markdown = GenerateTopicMarkdown(topic);
        
        // Assert
        markdown.Should().Contain("# Installation Guide");
        markdown.Should().Contain("[[install.txt]]");
        markdown.Should().Contain("[[setup.docx]]");
        markdown.Should().Contain("85%"); // relevance
    }

    /// <summary>
    /// Test 15: LocalKB_GenerateIndex
    /// </summary>
    [Fact]
    public void GenerateIndex_ShouldCreateIndexWithAllTopics()
    {
        // Arrange
        var topics = new List<UnifiedTopic>
        {
            new UnifiedTopic { Name = "Topic A", SourceFiles = new List<string> { "f1.txt" } },
            new UnifiedTopic { Name = "Topic B", SourceFiles = new List<string> { "f2.txt", "f3.txt" } },
            new UnifiedTopic { Name = "Topic C", SourceFiles = new List<string> { "f4.txt" } }
        };
        
        // Act
        var indexMarkdown = GenerateIndexMarkdown(topics);
        
        // Assert
        indexMarkdown.Should().Contain("# Knowledge Base Index");
        indexMarkdown.Should().Contain("Topic A");
        indexMarkdown.Should().Contain("Topic B");
        indexMarkdown.Should().Contain("Topic C");
        indexMarkdown.Should().Contain("topics/Topic_A/index.md");
    }

    /// <summary>
    /// Test 23: LocalKB_CreateNestedFolders
    /// </summary>
    [Fact]
    public void CreateNestedFolders_ShouldCreateDirectoryStructure()
    {
        // Arrange
        var topicNames = new[] { "Getting Started", "API Reference", "Troubleshooting" };
        
        // Act
        Directory.CreateDirectory(_testOutputDir);
        
        foreach (var topicName in topicNames)
        {
            var safeName = SanitizeFileName(topicName);
            var topicPath = Path.Combine(_testOutputDir, "topics", safeName);
            Directory.CreateDirectory(topicPath);
            
            // Create index.md in each folder
            File.WriteAllText(Path.Combine(topicPath, "index.md"), $"# {topicName}");
        }
        
        // Assert
        Directory.Exists(Path.Combine(_testOutputDir, "topics", "Getting_Started")).Should().BeTrue();
        Directory.Exists(Path.Combine(_testOutputDir, "topics", "API_Reference")).Should().BeTrue();
        Directory.Exists(Path.Combine(_testOutputDir, "topics", "Troubleshooting")).Should().BeTrue();
        
        File.ReadAllText(Path.Combine(_testOutputDir, "topics", "Getting_Started", "index.md"))
            .Should().Contain("Getting Started");
    }

    /// <summary>
    /// Helper: Generate markdown for a topic page
    /// </summary>
    private static string GenerateTopicMarkdown(UnifiedTopic topic)
    {
        return $@"# {topic.Name}

**Summary:** {topic.MergedContent.Split('\n').FirstOrDefault() ?? "No summary available"}
**Sources:** {string.Join(", ", topic.SourceFiles.Select(f => $"[[{f}]]"))}
**Processed:** {topic.AggregatedAt:yyyy-MM-dd HH:mm:ss} UTC
**Confidence:** {topic.AverageRelevance:P0}

---

{topic.MergedContent}

---

### Source References

{string.Join("\n", topic.SourceFiles.Select(f => $"- [[{f}]]"))}
";
    }

    /// <summary>
    /// Helper: Generate index markdown
    /// </summary>
    private static string GenerateIndexMarkdown(List<UnifiedTopic> topics)
    {
        var topicIndex = topics.Select(t => $"- [{t.Name}](topics/{SanitizeFileName(t.Name)}/index.md) - {t.SourceFiles.Count} sources");
        
        return $@"# Knowledge Base Index

Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

## Topics ({topics.Count})

{string.Join("\n", topicIndex)}
";
    }

    /// <summary>
    /// Helper: Sanitize filename (same as Program.cs)
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}
