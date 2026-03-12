using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;
using UglyToad.PdfPig;

namespace LegacyDocProcessor.Services.Extractors.Implementations;

/// <summary>
/// Extracts text from PDF files using PdfPig with OCR fallback for scanned documents.
/// </summary>
public class PdfExtractor : BaseTextExtractor
{
    private readonly ILogger _logger;
    private readonly IOcrService? _ocrService;
    private readonly OcrConfig _ocrConfig;
    
    /// <summary>
    /// Creates a new PdfExtractor with OCR support.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="ocrService">OCR service for scanned PDFs (optional)</param>
    /// <param name="ocrConfig">OCR configuration</param>
    public PdfExtractor(ILogger logger, IOcrService? ocrService = null, OcrConfig? ocrConfig = null)
    {
        _logger = logger;
        _ocrService = ocrService;
        _ocrConfig = ocrConfig ?? new OcrConfig();
    }
    
    public override string FileType => "pdf";
    
    public override async Task<ExtractedContent> ExtractAsync(string filePath)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var document = PdfDocument.Open(filePath);
                
                var metadata = new List<string>();
                var contentParts = new List<string>();
                string? directText = null;
                
                // Extract PDF metadata
                if (!string.IsNullOrEmpty(document.Information.Title))
                    metadata.Add($"Title: {document.Information.Title}");
                if (!string.IsNullOrEmpty(document.Information.Author))
                    metadata.Add($"Author: {document.Information.Author}");
                if (!string.IsNullOrEmpty(document.Information.Subject))
                    metadata.Add($"Subject: {document.Information.Subject}");
                if (!string.IsNullOrEmpty(document.Information.Keywords))
                    metadata.Add($"Keywords: {document.Information.Keywords}");
                if (document.Information.CreationDate != default)
                    metadata.Add($"Created: {document.Information.CreationDate:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrEmpty(document.Information.Creator))
                    metadata.Add($"Creator: {document.Information.Creator}");
                
                // Extract text from each page using PdfPig
                foreach (var page in document.GetPages())
                {
                    var pageText = page.Text;
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        contentParts.Add($"--- Page {page.Number} ---\n{pageText}");
                    }
                }
                
                directText = string.Join("\n\n", contentParts);
                
                // Check if OCR is needed
                var needsOcr = ShouldUseOcr(directText);
                
                if (needsOcr && _ocrConfig.Enabled && _ocrService != null && _ocrService.IsAvailable())
                {
                    _logger.Information("PDF {FilePath} appears to be scanned (only {CharCount} chars extracted directly), using OCR",
                        filePath, directText.Length);
                    
                    var ocrResult = await _ocrService.RecognizePdfAsync(filePath);
                    
                    if (ocrResult.IsSuccess && !string.IsNullOrWhiteSpace(ocrResult.Text))
                    {
                        // Combine direct text with OCR text
                        var combinedText = CombineTexts(directText, ocrResult.Text);
                        
                        // Add OCR info to metadata
                        metadata.Add($"OCR: Applied (confidence: {ocrResult.Confidence:P})");
                        metadata.Add($"OCR Pages: {ocrResult.PageCount}");
                        
                        if (ocrResult.Warnings.Count > 0)
                        {
                            metadata.Add($"OCR Warnings: {string.Join("; ", ocrResult.Warnings)}");
                        }
                        
                        var fullContent = string.Join("\n", metadata) + "\n\n" + combinedText;
                        
                        var response = CreateSuccessResponse(filePath, fullContent);
                        response.UsedOcr = true;
                        response.OcrConfidence = ocrResult.Confidence;
                        return response;
                    }
                    else
                    {
                        _logger.Warning("OCR failed for PDF {FilePath}: {Error}. Falling back to direct extraction.",
                            filePath, ocrResult.ErrorMessage ?? "No text extracted");
                        
                        // Fall back to direct text
                        metadata.Add("OCR: Failed - using direct text extraction");
                        var fullContent = string.Join("\n", metadata) + "\n\n" + directText;
                        return CreateSuccessResponse(filePath, fullContent);
                    }
                }
                else if (needsOcr)
                {
                    // OCR needed but not available
                    if (!_ocrConfig.Enabled)
                    {
                        _logger.Debug("OCR is disabled for PDF {FilePath}", filePath);
                        metadata.Add("OCR: Disabled in configuration");
                    }
                    else if (_ocrService == null || !_ocrService.IsAvailable())
                    {
                        _logger.Warning("OCR is not available for PDF {FilePath}. Consider installing Tesseract and tessdata.", filePath);
                        metadata.Add("OCR: Not available (Tesseract/tessdata may not be installed)");
                    }
                }
                
                // Return direct text (text-based PDF or OCR not available)
                var metadataContent = string.Join("\n", metadata);
                var finalContent = string.IsNullOrEmpty(directText) 
                    ? metadataContent 
                    : metadataContent + "\n\n" + directText;
                
                return CreateSuccessResponse(filePath, finalContent);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(filePath, $"Failed to extract PDF: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Determines if OCR should be used based on the extracted text length.
    /// </summary>
    private bool ShouldUseOcr(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        
        // Remove whitespace for accurate character count
        var charCount = text.Replace("\n", "").Replace("\r", "").Replace(" ", "").Length;
        return charCount < _ocrConfig.MinTextLength;
    }
    
    /// <summary>
    /// Combines direct text extraction with OCR text, avoiding duplication.
    /// </summary>
    private string CombineTexts(string? directText, string ocrText)
    {
        if (string.IsNullOrWhiteSpace(directText))
            return ocrText;
        
        if (string.IsNullOrWhiteSpace(ocrText))
            return directText;
        
        // If direct text is very short (likely just artifacts), prefer OCR
        if (directText.Length < 50)
            return ocrText;
        
        // If OCR text is much longer, it likely has more content
        if (ocrText.Length > directText.Length * 2)
            return ocrText;
        
        // Otherwise, combine both with a note
        return $"--- Direct Text Extraction ---\n{directText}\n\n--- OCR Text ---\n{ocrText}";
    }
}


