namespace LegacyDocProcessor.Models;

/// <summary>
/// Represents a file found during scanning
/// </summary>
public class DocumentFile
{
    public string FullPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string? Checksum { get; set; }
    
    public string RelativePath { get; set; } = string.Empty;
}

/// <summary>
/// Represents extracted text content from a document
/// </summary>
public class ExtractedContent
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public List<string> Metadata { get; set; } = new();
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents the LLM-processed knowledge from a document
/// </summary>
public class ProcessedKnowledge
{
    public string FilePath { get; set; } = string.Empty;
    public string SuggestedTitle { get; set; } = string.Empty;
    public List<TopicInfo> Topics { get; set; } = new();
    public List<string> TechnicalDetails { get; set; } = new();
    public List<string> ActionItems { get; set; } = new();
    public List<string> Questions { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public string RawLlmResponse { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a Confluence page to be created/updated
/// </summary>
public class ConfluencePage
{
    public string Title { get; set; } = string.Empty;
    public string SpaceKey { get; set; } = string.Empty;
    public string? ParentPageId { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public string? SourceFilePath { get; set; }
}

/// <summary>
/// Status of a processed file
/// </summary>
public enum ProcessingStatus
{
    Pending,
    Extracting,
    Extracted,
    Processing,
    Processed,
    Publishing,
    Published,
    Failed
}

/// <summary>
/// Tracks the processing state of a document
/// </summary>
public class ProcessingState
{
    public string FilePath { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    public DateTime? ExtractedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ConfluencePageId { get; set; }
    public string? ErrorMessage { get; set; }
}
