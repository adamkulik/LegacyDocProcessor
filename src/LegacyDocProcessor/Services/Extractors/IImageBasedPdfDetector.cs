using LegacyDocProcessor.Models;

namespace LegacyDocProcessor.Services.Extractors;

/// <summary>
/// Service for detecting if a PDF is scanned/image-based rather than text-based.
/// </summary>
public interface IImageBasedPdfDetector
{
    /// <summary>
    /// Analyzes a PDF file to determine if it contains scanned images rather than embedded text.
    /// </summary>
    /// <param name="filePath">Path to the PDF file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detection result indicating whether the PDF is scanned</returns>
    Task<PdfDetectionResult> DetectAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the minimum text length threshold used to determine if a PDF is scanned.
    /// </summary>
    int MinTextLength { get; }
}
