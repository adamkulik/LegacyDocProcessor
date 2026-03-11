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
}
