using System.Diagnostics;
using Aspose.OCR;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;

namespace LegacyDocProcessor.Services.Extractors;

/// <summary>
/// Aspose.OCR service implementation for extracting text from images and scanned PDFs.
/// Uses Aspose.Total license for OCR processing.
/// Note: This is a simplified implementation. Adjust based on your Aspose.OCR version.
/// </summary>
public class AsposeOcrService : IOcrService, IDisposable
{
    private readonly ILogger _logger;
    private readonly OcrConfig _config;
    private AsposeOcr? _ocrEngine;
    private bool _disposed;
    private bool? _isAvailable;

    /// <summary>
    /// Creates a new AsposeOcrService instance.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="config">OCR configuration</param>
    public AsposeOcrService(ILogger logger, OcrConfig config)
    {
        _logger = logger;
        _config = config;
        InitializeEngine();
    }

    /// <summary>
    /// Initializes the Aspose.OCR engine.
    /// </summary>
    private void InitializeEngine()
    {
        try
        {
            _ocrEngine = new AsposeOcr();
            
            // Verify engine is functional
            // Aspose.OCR will work in trial mode without license but with limitations
            _isAvailable = true;
            
            _logger.Information("Aspose.OCR initialized successfully. Language: {Language}, DetectAreas: {DetectAreas}, AutoSkew: {AutoSkew}",
                _config.Language, _config.DetectAreas, _config.AutoSkew);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize Aspose.OCR");
            _ocrEngine = null;
            _isAvailable = false;
        }
    }

    /// <inheritdoc />
    public bool IsAvailable()
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        InitializeEngine();
        return _isAvailable ?? false;
    }

    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new OcrResult();

        try
        {
            if (!IsAvailable() || _ocrEngine == null)
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Aspose.OCR is not available. Check license and installation."
                };
            }

            // Perform OCR using the available API
            using var ms = new MemoryStream(imageData);
            
            // Use the basic Recognize method with OcrInput
            var recognitionResults = await Task.Run(() => 
            {
                using var input = new OcrInput(InputType.SingleImage);
                input.Add(ms);
                return _ocrEngine.Recognize(input);
            }, cancellationToken);

            if (recognitionResults != null && recognitionResults.Count > 0)
            {
                result.Text = recognitionResults[0]?.RecognitionText ?? string.Empty;
                result.Confidence = EstimateTextQuality(result.Text);
            }
            
            result.PageCount = 1;
            result.IsSuccess = true;

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.Debug("OCR completed for image data. Text length: {TextLength}, Confidence: {Confidence:P}, Time: {Time}ms",
                result.Text.Length, result.Confidence, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = "OCR operation was cancelled";
            result.ProcessingTime = stopwatch.Elapsed;
            _logger.Warning("OCR cancelled for image data");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.ProcessingTime = stopwatch.Elapsed;
            _logger.Error(ex, "OCR failed for image data");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new OcrResult();

        try
        {
            if (!File.Exists(imagePath))
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Image file not found: {imagePath}"
                };
            }

            if (!IsAvailable() || _ocrEngine == null)
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Aspose.OCR is not available. Check license and installation."
                };
            }

            // Perform OCR using the available API
            var recognitionResults = await Task.Run(() => 
            {
                using var input = new OcrInput(InputType.SingleImage);
                input.Add(imagePath);
                return _ocrEngine.Recognize(input);
            }, cancellationToken);

            if (recognitionResults != null && recognitionResults.Count > 0)
            {
                result.Text = recognitionResults[0]?.RecognitionText ?? string.Empty;
                result.Confidence = EstimateTextQuality(result.Text);
            }
            
            result.PageCount = 1;
            result.IsSuccess = true;

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.Debug("OCR completed for {ImagePath}. Text length: {TextLength}, Confidence: {Confidence:P}, Time: {Time}ms",
                imagePath, result.Text.Length, result.Confidence, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = "OCR operation was cancelled";
            result.ProcessingTime = stopwatch.Elapsed;
            _logger.Warning("OCR cancelled for image: {ImagePath}", imagePath);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.ProcessingTime = stopwatch.Elapsed;
            _logger.Error(ex, "OCR failed for image file: {ImagePath}", imagePath);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OcrResult> RecognizePdfAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new OcrResult();

        try
        {
            if (!File.Exists(pdfPath))
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"PDF file not found: {pdfPath}"
                };
            }

            if (!IsAvailable() || _ocrEngine == null)
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Aspose.OCR is not available. Check license and installation."
                };
            }

            // Perform OCR on PDF using the available API
            var recognitionResults = await Task.Run(() => 
            {
                using var input = new OcrInput(InputType.PDF);
                input.Add(pdfPath);
                return _ocrEngine.Recognize(input);
            }, cancellationToken);

            if (recognitionResults == null || recognitionResults.Count == 0)
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "No text could be extracted from PDF"
                };
            }

            // Limit pages if configured
            var resultsToProcess = _config.MaxPages > 0 
                ? recognitionResults.Take(_config.MaxPages).ToList()
                : recognitionResults.ToList();

            // Process each page result
            var pageResults = new List<OcrPageResult>();
            var allText = new List<string>();
            var totalConfidence = 0f;

            for (int i = 0; i < resultsToProcess.Count; i++)
            {
                var pageRecognition = resultsToProcess[i];
                var pageText = pageRecognition?.RecognitionText ?? string.Empty;
                var pageConfidence = EstimateTextQuality(pageText);

                pageResults.Add(new OcrPageResult
                {
                    PageNumber = i + 1,
                    Text = pageText,
                    Confidence = pageConfidence
                });

                allText.Add(pageText);
                totalConfidence += pageConfidence;
            }

            result.Text = string.Join("\n\n", allText);
            result.PageCount = pageResults.Count;
            result.Pages = pageResults;
            result.Confidence = pageResults.Count > 0 ? totalConfidence / pageResults.Count : 0;
            result.IsSuccess = true;

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.Information("OCR completed for PDF {PdfPath}. Pages: {PageCount}, Text length: {TextLength}, Confidence: {Confidence:P}, Time: {Time}ms",
                pdfPath, result.PageCount, result.Text.Length, result.Confidence, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = "OCR operation was cancelled";
            result.ProcessingTime = stopwatch.Elapsed;
            _logger.Warning("OCR cancelled for PDF: {PdfPath}", pdfPath);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.ProcessingTime = stopwatch.Elapsed;
            _logger.Error(ex, "OCR failed for PDF: {PdfPath}", pdfPath);
        }

        return result;
    }

    /// <summary>
    /// Estimates text quality based on character distribution and patterns.
    /// </summary>
    private float EstimateTextQuality(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        // Simple heuristic: count printable characters vs total
        var printableCount = text.Count(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c));
        var ratio = (float)printableCount / text.Length;

        // Boost score if text has reasonable word structure
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var avgWordLength = words.Length > 0 ? words.Average(w => w.Length) : 0;

        // Ideal word length is around 4-6 characters
        var wordLengthScore = avgWordLength >= 2 && avgWordLength <= 10 ? 0.1f : 0f;

        return Math.Min(1.0f, ratio + wordLengthScore);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // AsposeOcr doesn't implement IDisposable in newer versions
            _ocrEngine = null;
            _disposed = true;
        }
    }
}
    

