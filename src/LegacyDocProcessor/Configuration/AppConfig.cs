namespace LegacyDocProcessor.Configuration;

public class AppConfig
{
    public SourceConfig Source { get; set; } = new();
    public LlmConfig Llm { get; set; } = new();
    public ConfluenceConfig Confluence { get; set; } = new();
    public ProcessingConfig Processing { get; set; } = new();
}

public class SourceConfig
{
    /// <summary>
    /// Path to the network share (e.g., "D:\" or "Z:\")
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
    
    /// <summary>
    /// File extensions to process (without dot)
    /// </summary>
    public List<string> SupportedExtensions { get; set; } = new() 
    { 
        "msg", "doc", "docx", "pdf", "txt" 
    };
    
    /// <summary>
    /// Glob patterns to exclude (e.g., "**/node_modules/**")
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new()
    {
        "**/node_modules/**",
        "**/.git/**",
        "**/bin/**",
        "**/obj/**"
    };
    
    /// <summary>
    /// Maximum file size in MB to process
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 50;
}

public class LlmConfig
{
    /// <summary>
    /// Base URL for OpenAI-compatible API (e.g., "https://api.openai.com/v1" or your local LLM)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// API key for authentication
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Model name (e.g., "gpt-4", "gpt-3.5-turbo", "llama2", etc.)
    /// </summary>
    public string Model { get; set; } = "gpt-4";
    
    /// <summary>
    /// Maximum tokens in LLM response
    /// </summary>
    public int MaxTokens { get; set; } = 4096;
    
    /// <summary>
    /// Temperature for LLM generation
    /// </summary>
    public double Temperature { get; set; } = 0.3;
    
    /// <summary>
    /// Target language for knowledge base output (default: English)
    /// The LLM will translate all output fields to this language
    /// </summary>
    public string OutputLanguage { get; set; } = "English";
    /// <summary>
    /// Delay in milliseconds between LLM requests (rate limiting)
    /// </summary>
    public int LlmDelayMs { get; set; } = 1000;
    /// <summary>
    /// Number of concurrent file processing tasks
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// System prompt for the LLM
    /// </summary>
    public string SystemPrompt { get; set; } = @"You are a documentation analyst. Analyze the provided document content and extract key information including:
- Main topics and themes
- Technical details, specifications, or requirements
- Action items, decisions, or next steps
- Any questions or uncertainties
- Relevant context for understanding this document

IMPORTANT: Translate ALL output fields (topics, summaries, content, technicalDetails, actionItems, questions) to the target language specified below. The knowledge base must be in a single consistent language.

Provide structured output that can be used to create a knowledge base article.";
}

public class ConfluenceConfig
{
    /// <summary>
    /// Confluence base URL (e.g., "https://confluence.yourcompany.com")
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Space key where pages will be created (e.g., "TECH", "PROJ")
    /// </summary>
    public string SpaceKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Parent page ID for organizing created pages (optional)
    /// </summary>
    public string? ParentPageId { get; set; }
    
    /// <summary>
    /// Authentication type: "Basic", "ApiToken", "OAuth", "Saml"
    /// </summary>
    public string AuthType { get; set; } = "Saml";
    
    /// <summary>
    /// Username for Basic/ApiToken auth
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Password or API token for Basic/ApiToken auth
    /// </summary>
    public string? PasswordOrToken { get; set; }
    
    /// <summary>
    /// OAuth/SAML session token (if using browser-based auth)
    /// </summary>
    public string? SessionToken { get; set; }
}

public class ProcessingConfig
{
    /// <summary>
    /// License file name for Aspose products (e.g., "Aspose.Total.NET.lic")
    /// The license file should be located in the application base directory
    /// </summary>
    public string? AsposeLicenseFileName { get; set; } = "Aspose.Total.NET.lic";
    
    /// <summary>
    /// Whether to skip files already processed (based on checksum)
    /// </summary>
    public bool SkipProcessedFiles { get; set; } = true;
    
    /// <summary>
    /// Path to store processed file checksums
    /// </summary>
    public string ProcessedFilesDb { get; set; } = "processed_files.json";
    
    /// <summary>
    /// Batch size for LLM processing
    /// </summary>
    public int LlmBatchSize { get; set; } = 10;
    
    /// <summary>
    /// OCR configuration for scanned PDF documents
    /// </summary>
    public OcrConfig Ocr { get; set; } = new();
    
    /// <summary>
    /// Topic deduplication and normalization configuration
    /// </summary>
    public TopicDeduplicationConfig TopicDeduplication { get; set; } = new();
}

/// <summary>
/// OCR (Optical Character Recognition) configuration for scanned PDF documents.
/// Uses Aspose.OCR (part of Aspose.Total license) for text recognition.
/// </summary>
public class OcrConfig
{
    /// <summary>
    /// Enable or disable OCR processing for scanned PDFs
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Minimum text length extracted by PdfPig before falling back to OCR.
    /// PDFs with less text than this threshold will be processed with OCR.
    /// </summary>
    public int MinTextLength { get; set; } = 100;
    
    /// <summary>
    /// OCR language for text recognition.
    /// Supported: English, German, French, Spanish, Italian, Portuguese, etc.
    /// </summary>
    public string Language { get; set; } = "English";
    
    /// <summary>
    /// DPI (dots per inch) for PDF rendering during OCR.
    /// Higher values give better OCR accuracy but slower processing.
    /// Recommended: 300 for good quality, 150 for faster processing.
    /// </summary>
    public int Dpi { get; set; } = 300;
    
    /// <summary>
    /// Enable automatic text area detection.
    /// Improves accuracy for documents with complex layouts.
    /// </summary>
    public bool DetectAreas { get; set; } = true;
    
    /// <summary>
    /// Enable automatic skew correction.
    /// Corrects rotated or tilted scanned documents.
    /// </summary>
    public bool AutoSkew { get; set; } = true;
    
    /// <summary>
    /// Timeout in milliseconds for OCR processing per document.
    /// </summary>
    public int TimeoutMs { get; set; } = 60000;
    
    /// <summary>
    /// Maximum number of pages to process with OCR.
    /// Pages beyond this limit will be skipped with a warning.
    /// Set to 0 for unlimited.
    /// </summary>
    public int MaxPages { get; set; } = 0;
    
    /// <summary>
    /// Enable parallel processing of PDF pages for OCR.
    /// Can significantly speed up processing of multi-page documents.
    /// </summary>
    public bool ParallelProcessing { get; set; } = true;
    
    /// <summary>
    /// Maximum degree of parallelism when ParallelProcessing is enabled.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;
    
    /// <summary>
    /// Path to Tesseract tessdata directory containing language data files.
    /// Can be absolute or relative to application directory.
    /// Default: "tessdata" (relative to application)
    /// </summary>
    public string TessDataPath { get; set; } = "tessdata";
    
    /// <summary>
    /// Tesseract page segmentation mode.
    /// Options: OsdOnly, AutoOsd, AutoOnly, Auto, SingleColumn, SingleBlockVertText,
    /// SingleBlock, SingleLine, SingleWord, CircleWord, SingleChar, SparseText, 
    /// SparseTextOsd, RawLine
    /// Default: Auto
    /// </summary>
    public string PageSegMode { get; set; } = "Auto";
}

/// <summary>
/// Aggressiveness level for topic merging.
/// </summary>
public enum AggressivenessLevel
{
    /// <summary>
    /// Conservative: Only merge topics that are nearly identical.
    /// Minimizes false positives but may leave some duplicates.
    /// </summary>
    Conservative,
    
    /// <summary>
    /// Medium: Balance between catching duplicates and avoiding false positives.
    /// Recommended for most use cases.
    /// </summary>
    Medium,
    
    /// <summary>
    /// Aggressive: Merge topics that share the same core concept.
    /// Maximizes consolidation but may occasionally merge distinct topics.
    /// </summary>
    Aggressive
}

/// <summary>
/// Topic deduplication and normalization configuration.
/// Reduces topic fragmentation by normalizing and merging similar topics.
/// </summary>
public class TopicDeduplicationConfig
{
    /// <summary>
    /// Enable or disable topic deduplication processing
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Custom merge rules for topic normalization.
    /// These rules are applied before LLM-based canonicalization.
    /// </summary>
    public List<CustomMergeRule> CustomMergeRules { get; set; } = new();
    
    /// <summary>
    /// Similarity threshold (0.0-1.0) for fuzzy topic matching.
    /// Topics with similarity above this threshold may be merged.
    /// Higher values = more strict matching (fewer merges).
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.85;
    
    /// <summary>
    /// Maximum number of topics to send to LLM in a single canonicalization request.
    /// </summary>
    public int LlmBatchSize { get; set; } = 50;
    
    /// <summary>
    /// Minimum number of similar topics required to trigger LLM canonicalization.
    /// Groups smaller than this are handled by rule-based normalization only.
    /// </summary>
    public int MinTopicsForLlmCanonicalization { get; set; } = 3;
    
    /// <summary>
    /// Aggressiveness level for topic merging.
    /// Controls how aggressively the LLM should merge similar topics.
    /// </summary>
    public AggressivenessLevel Aggressiveness { get; set; } = AggressivenessLevel.Medium;
    
    /// <summary>
    /// When true, preserve distinct technical terms and acronyms.
    /// Prevents merging of topics that contain different technical identifiers
    /// (e.g., "API-123" and "API-456" will not be merged).
    /// </summary>
    public bool PreserveTechnicalTerms { get; set; } = true;
    
    /// <summary>
    /// Technical term patterns to preserve (regex patterns).
    /// Topics matching these patterns will not be merged with each other.
    /// </summary>
    public List<string> TechnicalTermPatterns { get; set; } = new()
    {
        @"[A-Z]{2,}-\d+",           // JIRA-style: PROJ-123, API-456
        @"\b[A-Z]{3,}\d{3,}\b",     // Codes: ABC123, XYZ789
        @"v\d+\.\d+",               // Version numbers: v1.0, v2.5
        @"\b[A-Z]{2,}\d+[A-Z]*\b"   // Mixed codes: AB12C, XY99Z
    };
    
    /// <summary>
    /// When true, log all topic merges to a JSON file for auditing and debugging.
    /// </summary>
    public bool LogMerges { get; set; } = false;
    
    /// <summary>
    /// Path to the merge log file (relative to output directory or absolute path).
    /// Default: "topic-merges.json"
    /// </summary>
    public string MergeLogPath { get; set; } = "topic-merges.json";
}

/// <summary>
/// Custom merge rule for normalizing topic variations.
/// </summary>
public class CustomMergeRule
{
    /// <summary>
    /// Human-readable name for this rule (e.g., "Project Code Variations")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Regex pattern to match topic variations.
    /// Use capture groups to extract the canonical form.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;
    
    /// <summary>
    /// Replacement pattern for the canonical form.
    /// Use $1, $2, etc. to reference capture groups.
    /// </summary>
    public string CanonicalReplacement { get; set; } = string.Empty;
    
    /// <summary>
    /// Example variations this rule handles (for documentation/debugging)
    /// </summary>
    public List<string> ExampleVariations { get; set; } = new();
}
