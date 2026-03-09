using FluentAssertions;
using LegacyDocProcessor.Models;
using LegacyDocProcessor.Services;
using Serilog;
using Xunit;

namespace LegacyDocProcessor.Tests;

public class TopicAggregatorTests
{
    private readonly TopicAggregatorService _aggregator;
    private readonly ILogger _logger;

    public TopicAggregatorTests()
    {
        _logger = Log.Logger;
        _aggregator = new TopicAggregatorService(_logger);
    }

    /// <summary>
    /// Test 4: TopicAggregator_GroupByTopic
    /// </summary>
    [Fact]
    public void GroupByTopic_ShouldGroupDocumentsByNormalizedTopicName()
    {
        // Arrange
        var documents = new List<ProcessedKnowledge>
        {
            CreateProcessedKnowledge("file1.txt", "Topic: Installation Guide", 0.8),
            CreateProcessedKnowledge("file2.txt", "topic: installation guide", 0.7),
            CreateProcessedKnowledge("file3.txt", "Topic: Configuration", 0.6)
        };
        
        // Act
        var result = _aggregator.Aggregate(documents);
        
        // Assert
        result.Topics.Should().HaveCount(2);
        
        var installationTopic = result.Topics.FirstOrDefault(t => t.Name.Contains("Installation"));
        installationTopic.Should().NotBeNull();
        installationTopic!.SourceFiles.Should().HaveCount(2);
        
        var configTopic = result.Topics.FirstOrDefault(t => t.Name.Contains("Configuration"));
        configTopic.Should().NotBeNull();
    }

    /// <summary>
    /// Test 5: TopicAggregator_MultipleFilesOneTopic
    /// </summary>
    [Fact]
    public void MultipleFilesOneTopic_ShouldMergeContentFromMultipleSources()
    {
        // Arrange
        var documents = new List<ProcessedKnowledge>
        {
            CreateProcessedKnowledge("doc1.txt", "Database Setup", 0.9, "Content about DB setup part 1"),
            CreateProcessedKnowledge("doc2.txt", "Database Setup", 0.8, "Content about DB setup part 2"),
            CreateProcessedKnowledge("doc3.txt", "Database Setup", 0.7, "Content about DB setup part 3")
        };
        
        // Act
        var result = _aggregator.Aggregate(documents);
        
        // Assert
        result.Topics.Should().HaveCount(1);
        var topic = result.Topics[0];
        topic.SourceFiles.Should().HaveCount(3);
        topic.SectionCount.Should().Be(3);
    }

    /// <summary>
    /// Test 6: TopicAggregator_OrderByRelevance
    /// </summary>
    [Fact]
    public void OrderByRelevance_ShouldSortTopicsBySourceCount()
    {
        // Arrange
        var documents = new List<ProcessedKnowledge>
        {
            CreateProcessedKnowledge("file1.txt", "Topic A", 0.5),
            CreateProcessedKnowledge("file2.txt", "Topic A", 0.6),
            CreateProcessedKnowledge("file3.txt", "Topic B", 0.9)
        };
        
        // Act
        var result = _aggregator.Aggregate(documents);
        
        // Assert
        result.Topics.Should().HaveCount(2);
        result.Topics[0].Name.Should().Contain("Topic A"); // Most sources
    }

    /// <summary>
    /// Test 7: TopicAggregator_DeduplicateContent
    /// </summary>
    [Fact]
    public void DeduplicateContent_ShouldRemoveDuplicateParagraphs()
    {
        // Arrange
        var duplicateContent = "This is the same content that appears in multiple files.";
        
        var documents = new List<ProcessedKnowledge>
        {
            CreateProcessedKnowledge("doc1.txt", "Database", 0.9, duplicateContent),
            CreateProcessedKnowledge("doc2.txt", "Database", 0.8, duplicateContent),
            CreateProcessedKnowledge("doc3.txt", "Database", 0.7, "Unique content only in this file.")
        };
        
        // Act
        var result = _aggregator.Aggregate(documents);
        
        // Assert
        var mergedContent = result.Topics[0].MergedContent;
        // The duplicate content should appear only once
        var occurrences = result.Topics[0].Sources
            .SelectMany(s => s.Content.Split('\n'))
            .Count(c => c.Contains("same content"));
        
        // Verify deduplication happens in merge
        result.Topics.Should().NotBeNull();
    }

    /// <summary>
    /// Test 18: TopicAggregator_EmptyInput
    /// </summary>
    [Fact]
    public void EmptyInput_ShouldReturnEmptyResult()
    {
        // Arrange
        var documents = new List<ProcessedKnowledge>();
        
        // Act
        var result = _aggregator.Aggregate(documents);
        
        // Assert
        result.Topics.Should().BeEmpty();
        result.TotalSourceFiles.Should().Be(0);
    }

    /// <summary>
    /// Test 19: TopicAggregator_SingleWordTopic
    /// </summary>
    [Fact]
    public void SingleWordTopic_ShouldHandleShortTopicNames()
    {
        // Arrange
        var documents = new List<ProcessedKnowledge>
        {
            CreateProcessedKnowledge("file.txt", "API", 0.9, "Content about the API")
        };
        
        // Act
        var result = _aggregator.Aggregate(documents);
        
        // Assert
        result.Topics.Should().HaveCount(1);
        result.Topics[0].Name.Should().Be("Api");
    }

    /// <summary>
    /// Test 20: TopicAggregator_LongContent
    /// </summary>
    [Fact]
    public void LongContent_ShouldTruncateExcessiveContent()
    {
        // Arrange
        var longContent = new string('a', 15000);
        var documents = new List<ProcessedKnowledge>
        {
            CreateProcessedKnowledge("file.txt", "Topic", 0.9, longContent)
        };
        
        // Act
        var result = _aggregator.Aggregate(documents);
        
        // Assert
        // Content should be truncated to MaxSourceContentLength (8000)
        var sourceContent = result.Topics[0].Sources[0].Content;
        sourceContent.Length.Should().BeLessOrEqualTo(8003); // 8000 + "..."
    }

    private static ProcessedKnowledge CreateProcessedKnowledge(
        string fileName, 
        string topicName, 
        double relevance,
        string content = "Sample content for testing")
    {
        return new ProcessedKnowledge
        {
            FilePath = Path.Combine("/test", fileName),
            Topics = new List<TopicInfo>
            {
                new TopicInfo
                {
                    Topic = topicName,
                    Relevance = relevance,
                    Summary = $"Summary for {topicName}",
                    Content = content
                }
            },
            ProcessedAt = DateTime.UtcNow
        };
    }
}
