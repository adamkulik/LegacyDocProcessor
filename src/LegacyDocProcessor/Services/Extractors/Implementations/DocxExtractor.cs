using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LegacyDocProcessor.Models;

namespace LegacyDocProcessor.Services.Extractors.Implementations;

/// <summary>
/// Extracts text from .docx files using OpenXML (free, no license required)
/// </summary>
public class DocxExtractor : BaseTextExtractor
{
    public override string FileType => "docx";
    
    public override async Task<ExtractedContent> ExtractAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var document = WordprocessingDocument.Open(filePath, false);
                var body = document.MainDocumentPart?.Document.Body;
                
                if (body == null)
                {
                    return CreateErrorResponse(filePath, "Document body is empty");
                }
                
                var paragraphs = body.Elements<Paragraph>();
                var textContent = new List<string>();
                
                foreach (var paragraph in paragraphs)
                {
                    var text = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textContent.Add(text.Trim());
                    }
                }
                
                var fullContent = string.Join("\n\n", textContent);
                
                if (string.IsNullOrWhiteSpace(fullContent))
                {
                    return CreateErrorResponse(filePath, "No text content found in document");
                }
                
                return CreateSuccessResponse(filePath, fullContent);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(filePath, $"Failed to extract DOCX: {ex.Message}");
            }
        });
    }
}
