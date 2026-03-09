using LegacyDocProcessor.Models;
using UglyToad.PdfPig;

namespace LegacyDocProcessor.Services.Extractors.Implementations;

/// <summary>
/// Extracts text from PDF files using PdfPig
/// </summary>
public class PdfExtractor : BaseTextExtractor
{
    public PdfExtractor(Serilog.ILogger logger)
    {
        // BaseTextExtractor doesn't use logger, but keeping signature consistent
    }
    
    public override string FileType => "pdf";
    
    public override async Task<ExtractedContent> ExtractAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var document = PdfDocument.Open(filePath);
                
                var metadata = new List<string>();
                var contentParts = new List<string>();
                
                // Extract PDF metadata - these are already strings or DateTime (not nullable)
                if (!string.IsNullOrEmpty(document.Information.Title))
                    metadata.Add($"Title: {document.Information.Title}");
                if (!string.IsNullOrEmpty(document.Information.Author))
                    metadata.Add($"Author: {document.Information.Author}");
                if (!string.IsNullOrEmpty(document.Information.Subject))
                    metadata.Add($"Subject: {document.Information.Subject}");
                if (!string.IsNullOrEmpty(document.Information.Keywords))
                    metadata.Add($"Keywords: {document.Information.Keywords}");
                // CreationDate is already a DateTime, not nullable
                if (document.Information.CreationDate != default)
                    metadata.Add($"Created: {document.Information.CreationDate:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrEmpty(document.Information.Creator))
                    metadata.Add($"Creator: {document.Information.Creator}");
                
                // Extract text from each page
                foreach (var page in document.GetPages())
                {
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        contentParts.Add($"--- Page {page.Number} ---\n{pageText}");
                    }
                }
                
                var fullContent = string.Join("\n\n", metadata) + "\n\n" + string.Join("\n\n", contentParts);
                
                return CreateSuccessResponse(filePath, fullContent);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(filePath, $"Failed to extract PDF: {ex.Message}");
            }
        });
    }
}
