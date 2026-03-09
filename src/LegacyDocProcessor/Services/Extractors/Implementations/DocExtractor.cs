using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using LegacyDocProcessor.Services;
using Serilog;
using Asp =Aspose.Words;

namespace LegacyDocProcessor.Services.Extractors.Implementations;

/// <summary>
/// Extracts text from .doc files using Aspose.Words
/// </summary>
public class DocExtractor : BaseTextExtractor
{
    private readonly ILogger _logger;
    
    public DocExtractor(ILogger logger)
    {
        _logger = logger;
    }
    
    public override string FileType => "doc";
    
    public override async Task<ExtractedContent> ExtractAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Load the document using Aspose.Words
                var doc = new Asp.Document(filePath);
                
                // Extract text using the simplest method
                var text = doc.GetText();
                
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Clean up the extracted text
                    var cleaned = CleanExtractedText(text);
                    return CreateSuccessResponse(filePath, cleaned);
                }
                
                return CreateErrorResponse(filePath, "No text content found in document.");
            }
            catch (Asp.UnsupportedFileFormatException ex)
            {
                _logger.Warning("Unsupported DOC format: {FilePath} - {Error}", filePath, ex.Message);
                return CreateErrorResponse(filePath, $"Unsupported DOC format: {ex.Message}");
            }
            catch (Asp.FileCorruptedException ex)
            {
                _logger.Warning("Aspose.Words failed to read DOC (IO error): {FilePath} - {Error}", filePath, ex.Message);
                return CreateErrorResponse(filePath, $"Failed to read DOC: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract DOC: {FilePath}", filePath);
                return CreateErrorResponse(filePath, $"Failed to extract DOC: {ex.Message}");
            }
        });
    }
    
    private string CleanExtractedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
            
        // Split into lines and filter
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var cleanedLines = new List<string>();
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
                
            // Skip very short lines (likely artifacts)
            if (trimmed.Length < 2)
                continue;
            
            // Skip lines that are mostly special characters
            var letterCount = trimmed.Count(c => char.IsLetter(c));
            var digitCount = trimmed.Count(c => char.IsDigit(c));
            var total = trimmed.Length;
            
            if (letterCount + digitCount < total * 0.5)
                continue;
            
            cleanedLines.Add(trimmed);
        }
        
        return string.Join("\n", cleanedLines);
    }
}
