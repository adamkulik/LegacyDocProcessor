using LegacyDocProcessor.Models;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Interface for extracting text content from documents
/// </summary>
public interface ITextExtractor
{
    string FileType { get; }
    Task<ExtractedContent> ExtractAsync(string filePath);
    bool CanExtract(string extension);
}

/// <summary>
/// Base class for text extractors with common functionality
/// </summary>
public abstract class BaseTextExtractor : ITextExtractor
{
    public abstract string FileType { get; }
    
    public abstract Task<ExtractedContent> ExtractAsync(string filePath);
    
    public virtual bool CanExtract(string extension)
    {
        return extension.Equals(FileType, StringComparison.OrdinalIgnoreCase);
    }
    
    protected ExtractedContent CreateSuccessResponse(string filePath, string text)
    {
        return new ExtractedContent
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = FileType,
            TextContent = text,
            IsSuccess = true,
            ExtractedAt = DateTime.UtcNow
        };
    }
    
    protected ExtractedContent CreateErrorResponse(string filePath, string error)
    {
        return new ExtractedContent
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = FileType,
            IsSuccess = false,
            ErrorMessage = error,
            ExtractedAt = DateTime.UtcNow
        };
    }
}
