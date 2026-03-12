using LegacyDocProcessor.Models;

namespace LegacyDocProcessor.Services.Extractors;

/// <summary>
/// Interface for OCR (Optical Character Recognition) services.
/// Implementations extract text from images or scanned PDF documents.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Performs OCR on an image provided as byte array and returns extracted text.
    /// </summary>
    /// <param name="imageData">The image data as a byte array (supports PNG, JPEG, TIFF, BMP, GIF)</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>OCR result containing extracted text and metadata</returns>
    Task<OcrResult> RecognizeAsync(byte[] imageData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Performs OCR on an image file and returns extracted text.
    /// </summary>
    /// <param name="imagePath">Path to the image file</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>OCR result containing extracted text and metadata</returns>
    Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Performs OCR on a PDF document by converting pages to images and processing each.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>OCR result containing combined text from all pages</returns>
    Task<OcrResult> RecognizePdfAsync(string pdfPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if the OCR service is properly configured and available.
    /// </summary>
    /// <returns>True if OCR is available, false otherwise</returns>
    bool IsAvailable();
}
