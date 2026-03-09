using LegacyDocProcessor.Models;

namespace LegacyDocProcessor.Services.Extractors.Implementations;

/// <summary>
/// Extracts text from .txt and other plain text files
/// </summary>
public class TextExtractor : BaseTextExtractor
{
    public override string FileType => "txt";
    
    public override async Task<ExtractedContent> ExtractAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            return CreateSuccessResponse(filePath, content);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(filePath, $"Failed to read text file: {ex.Message}");
        }
    }
}
