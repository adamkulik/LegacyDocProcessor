using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;
using UglyToad.PdfPig;

namespace LegacyDocProcessor.Services.Extractors;

/// <summary>
/// Service for detecting if a PDF is scanned/image-based rather than text-based.
/// Uses PdfPig to analyze PDF content and determine if OCR is needed.
/// </summary>
public class ImageBasedPdfDetector : IImageBasedPdfDetector
{
    private readonly ILogger _logger;
    private readonly int _minTextLength;
    
    /// <summary>
    /// Creates a new ImageBasedPdfDetector.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="ocrConfig">OCR configuration (uses MinTextLength threshold)</param>
    public ImageBasedPdfDetector(ILogger logger, OcrConfig? ocrConfig = null)
    {
        _logger = logger;
        _minTextLength = ocrConfig?.MinTextLength ?? 100;
    }
    
    /// <inheritdoc />
    public int MinTextLength => _minTextLength;
    
    /// <inheritdoc />
    public async Task<PdfDetectionResult> DetectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new PdfDetectionResult
            {
                FilePath = filePath,
                IsSuccess = false
            };
            
            try
            {
                using var document = PdfDocument.Open(filePath);
                
                result.PageCount = document.NumberOfPages;
                var allText = new System.Text.StringBuilder();
                var textPageCount = 0;
                var imagePageCount = 0;
                
                // Analyze each page
                foreach (var page in document.GetPages())
                {
                    var pageText = page.Text;
                    var cleanedText = pageText?.Replace("\n", "").Replace("\r", "").Replace(" ", "") ?? "";
                    
                    if (cleanedText.Length > 0)
                    {
                        allText.AppendLine(pageText);
                        
                        // Consider a page as "text page" if it has meaningful content
                        // (more than just a few characters of artifacts)
                        if (cleanedText.Length >= 10)
                        {
                            textPageCount++;
                        }
                        else
                        {
                            imagePageCount++;
                        }
                    }
                    else
                    {
                        imagePageCount++;
                    }
                }
                
                result.DirectTextLength = allText.ToString().Replace("\n", "").Replace("\r", "").Replace(" ", "").Length;
                result.TextPageCount = textPageCount;
                result.ImagePageCount = imagePageCount;
                
                // Determine if scanned based on text length threshold
                result.IsScanned = result.DirectTextLength < _minTextLength;
                
                // Calculate confidence based on how clear the determination is
                if (result.DirectTextLength == 0)
                {
                    // No text at all - definitely scanned
                    result.Confidence = 1.0f;
                    result.Description = "PDF contains no extractable text - definitely scanned/image-based";
                }
                else if (result.DirectTextLength < _minTextLength / 2)
                {
                    // Very little text - highly likely scanned
                    result.Confidence = 0.9f;
                    result.Description = $"PDF has very little text ({result.DirectTextLength} chars) - likely scanned";
                }
                else if (result.DirectTextLength < _minTextLength)
                {
                    // Below threshold - probably scanned
                    result.Confidence = 0.7f;
                    result.Description = $"PDF has minimal text ({result.DirectTextLength} chars) - probably scanned";
                }
                else if (result.DirectTextLength < _minTextLength * 2)
                {
                    // Just above threshold - might be hybrid
                    result.Confidence = 0.5f;
                    result.Description = $"PDF has some text ({result.DirectTextLength} chars) - possibly hybrid";
                }
                else
                {
                    // Well above threshold - definitely text-based
                    result.Confidence = 0.9f;
                    result.Description = $"PDF has substantial text ({result.DirectTextLength} chars) - text-based";
                }
                
                // Adjust for hybrid documents
                if (imagePageCount > 0 && textPageCount > 0)
                {
                    result.Description += $" (hybrid: {textPageCount} text pages, {imagePageCount} image pages)";
                }
                
                result.IsSuccess = true;
                
                _logger.Debug("PDF detection for {FilePath}: IsScanned={IsScanned}, Confidence={Confidence}, TextLength={TextLength}",
                    filePath, result.IsScanned, result.Confidence, result.DirectTextLength);
                
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.Description = $"Failed to analyze PDF: {ex.Message}";
                
                _logger.Error(ex, "Failed to detect PDF type for {FilePath}", filePath);
                
                return result;
            }
        }, cancellationToken);
    }
}
