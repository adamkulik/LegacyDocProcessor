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
    
    /// <summary>
    /// Indicates if OCR was used to extract text (for PDFs)
    /// </summary>
    public bool UsedOcr { get; set; }
    
    /// <summary>
    /// OCR confidence score (0.0-1.0), only set when OCR was used
    /// </summary>
    public float? OcrConfidence { get; set; }
}

/// <summary>
/// Represents the result of OCR processing
/// </summary>
public class OcrResult
{
    /// <summary>
    /// The extracted text content
    /// </summary>
    public string Text { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of pages processed
    /// </summary>
    public int PageCount { get; set; }
    
    /// <summary>
    /// Overall confidence score (0.0-1.0)
    /// </summary>
    public float Confidence { get; set; }
    
    /// <summary>
    /// Time taken to process
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }
    
    /// <summary>
    /// Any warnings encountered during OCR
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// Text content per page (optional, for detailed processing)
    /// </summary>
    public List<OcrPageResult> Pages { get; set; } = new();
    
    /// <summary>
    /// Indicates if OCR was successful
    /// </summary>
    public bool IsSuccess { get; set; } = true;
    
    /// <summary>
    /// Error message if OCR failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents OCR result for a single page
/// </summary>
public class OcrPageResult
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
}

/// <summary>
/// Represents the result of detecting whether a PDF is scanned/image-based
/// </summary>
public class PdfDetectionResult
{
    /// <summary>
    /// Path to the analyzed PDF file
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates whether the PDF appears to be scanned/image-based
    /// </summary>
    public bool IsScanned { get; set; }
    
    /// <summary>
    /// Number of characters extracted directly from the PDF (before OCR)
    /// </summary>
    public int DirectTextLength { get; set; }
    
    /// <summary>
    /// Total number of pages in the PDF
    /// </summary>
    public int PageCount { get; set; }
    
    /// <summary>
    /// Number of pages that contain extractable text
    /// </summary>
    public int TextPageCount { get; set; }
    
    /// <summary>
    /// Number of pages that appear to be image-only
    /// </summary>
    public int ImagePageCount { get; set; }
    
    /// <summary>
    /// Detection confidence (0.0-1.0)
    /// </summary>
    public float Confidence { get; set; }
    
    /// <summary>
    /// Human-readable description of the detection result
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates if the detection was successful
    /// </summary>
    public bool IsSuccess { get; set; } = true;
    
    /// <summary>
    /// Error message if detection failed
    /// </summary>
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
