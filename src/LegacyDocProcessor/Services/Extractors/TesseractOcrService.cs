extern alias SystemDrawing;
using System.Diagnostics;
using SystemDrawing::System.Drawing;
using SystemDrawing::System.Drawing.Imaging;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;
using Tesseract;

namespace LegacyDocProcessor.Services.Extractors;

/// <summary>
/// Tesseract OCR service implementation for extracting text from images and scanned PDFs.
/// </summary>
public class TesseractOcrService : IOcrService, IDisposable
{
    private readonly ILogger _logger;
    private readonly OcrConfig _config;
    private readonly PdfToImageConverter _pdfConverter;
    private readonly string _resolvedTessDataPath;
    private bool _disposed;
    private bool? _isAvailable;
    
    public TesseractOcrService(ILogger logger, OcrConfig config)
    {
        _logger = logger;
        _config = config;
        _pdfConverter = new PdfToImageConverter(logger, config.Dpi);
        _resolvedTessDataPath = ResolveTessDataPath(config.TessDataPath);
    }
    
    /// <summary>
    /// Resolves the tessdata path, checking both relative and absolute paths.
    /// </summary>
    private string ResolveTessDataPath(string configuredPath)
    {
        // If absolute path exists, use it
        if (Path.IsPathRooted(configuredPath) && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }
        
        // Try relative to application base directory
        var basePath = AppContext.BaseDirectory;
        var appRelativePath = Path.Combine(basePath, configuredPath);
        if (Directory.Exists(appRelativePath))
        {
            return appRelativePath;
        }
        
        // Try relative to current working directory
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
        if (Directory.Exists(cwdPath))
        {
            return cwdPath;
        }
        
        // Return the configured path anyway (will fail with clear error)
        _logger.Warning("Tessdata directory not found at {ConfiguredPath}. Searched: {AppPath}, {CwdPath}", 
            configuredPath, appRelativePath, cwdPath);
        return configuredPath;
    }
    
    /// <inheritdoc />
    public bool IsAvailable()
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;
        
        try
        {
            // Check if tessdata directory exists
            if (!Directory.Exists(_resolvedTessDataPath))
            {
                _logger.Warning("Tessdata directory not found: {TessDataPath}", _resolvedTessDataPath);
                _isAvailable = false;
                return false;
            }
            
            // Check if language data file exists
            var languages = _config.Language.Split('+');
            foreach (var lang in languages)
            {
                var langFile = Path.Combine(_resolvedTessDataPath, $"{lang}.traineddata");
                if (!File.Exists(langFile))
                {
                    _logger.Warning("Language data file not found: {LangFile}", langFile);
                    _isAvailable = false;
                    return false;
                }
            }
            
            // Try to create a Tesseract engine to verify it works
            using var engine = CreateEngine();
            _isAvailable = true;
            _logger.Information("Tesseract OCR initialized successfully. Language: {Language}, TessData: {TessDataPath}", 
                _config.Language, _resolvedTessDataPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Tesseract OCR is not available");
            _isAvailable = false;
            return false;
        }
    }
    
    /// <summary>
    /// Creates a new Tesseract engine instance.
    /// </summary>
    private TesseractEngine CreateEngine()
    {
        var pageSegMode = ParsePageSegMode(_config.PageSegMode);
        var engine = new TesseractEngine(_resolvedTessDataPath, _config.Language, EngineMode.Default);
        engine.DefaultPageSegMode = pageSegMode;
        return engine;
    }
    
    /// <summary>
    /// Parses the page segmentation mode string to enum.
    /// </summary>
    private PageSegMode ParsePageSegMode(string mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "osdonly" => PageSegMode.OsdOnly,
            "autoosd" => PageSegMode.AutoOsd,
            "autoonly" => PageSegMode.AutoOnly,
            "auto" => PageSegMode.Auto,
            "singlecolumn" => PageSegMode.SingleColumn,
            "singleblockverttext" => PageSegMode.SingleBlockVertText,
            "singleblock" => PageSegMode.SingleBlock,
            "singleline" => PageSegMode.SingleLine,
            "singleword" => PageSegMode.SingleWord,
            "circleword" => PageSegMode.CircleWord,
            "singlechar" => PageSegMode.SingleChar,
            "sparsetext" => PageSegMode.SparseText,
            "sparsetextosd" => PageSegMode.SparseTextOsd,
            "rawline" => PageSegMode.RawLine,
            _ => PageSegMode.Auto
        };
    }
    
    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new OcrResult();
        
        try
        {
            if (!IsAvailable())
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Tesseract OCR is not available. Check tessdata installation."
                };
            }
            
            using var engine = CreateEngine();
            using var img = Pix.LoadFromMemory(imageData);
            
            using var page = engine.Process(img);
            
            result.Text = page.GetText() ?? string.Empty;
            result.Confidence = page.GetMeanConfidence();
            result.IsSuccess = true;
            
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;
            
            _logger.Debug("OCR completed for image. Text length: {TextLength}, Confidence: {Confidence:P}, Time: {Time}ms",
                result.Text.Length, result.Confidence, stopwatch.ElapsedMilliseconds);
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
            
            if (!IsAvailable())
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Tesseract OCR is not available. Check tessdata installation."
                };
            }
            
            using var engine = CreateEngine();
            using var img = Pix.LoadFromFile(imagePath);
            
            using var page = engine.Process(img);
            
            result.Text = page.GetText() ?? string.Empty;
            result.Confidence = page.GetMeanConfidence();
            result.IsSuccess = true;
            
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;
            
            _logger.Debug("OCR completed for {ImagePath}. Text length: {TextLength}, Confidence: {Confidence:P}, Time: {Time}ms",
                imagePath, result.Text.Length, result.Confidence, stopwatch.ElapsedMilliseconds);
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
            
            if (!IsAvailable())
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Tesseract OCR is not available. Check tessdata installation."
                };
            }
            
            // Convert PDF pages to images
            var maxPages = _config.MaxPages > 0 ? _config.MaxPages : int.MaxValue;
            var images = _pdfConverter.ConvertAllPages(pdfPath, maxPages);
            
            if (images.Count == 0)
            {
                return new OcrResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Failed to convert PDF pages to images"
                };
            }
            
            result.PageCount = images.Count;
            
            // Process each page
            var pageResults = new List<OcrPageResult>();
            var totalConfidence = 0f;
            
            if (_config.ParallelProcessing)
            {
                // Parallel processing
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _config.MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                };
                
                var lockObj = new object();
                var pageTexts = new string[images.Count];
                var pageConfidences = new float[images.Count];
                
                Parallel.For(0, images.Count, parallelOptions, i =>
                {
                    try
                    {
                        var pageResult = ProcessImage(images[i], i + 1);
                        lock (lockObj)
                        {
                            pageTexts[i] = pageResult.Text;
                            pageConfidences[i] = pageResult.Confidence;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "OCR failed for page {PageNumber}", i + 1);
                        lock (lockObj)
                        {
                            pageTexts[i] = string.Empty;
                            pageConfidences[i] = 0;
                            result.Warnings.Add($"Page {i + 1}: OCR failed - {ex.Message}");
                        }
                    }
                });
                
                for (int i = 0; i < images.Count; i++)
                {
                    pageResults.Add(new OcrPageResult
                    {
                        PageNumber = i + 1,
                        Text = pageTexts[i],
                        Confidence = pageConfidences[i]
                    });
                    totalConfidence += pageConfidences[i];
                }
            }
            else
            {
                // Sequential processing
                for (int i = 0; i < images.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        var pageResult = ProcessImage(images[i], i + 1);
                        pageResults.Add(pageResult);
                        totalConfidence += pageResult.Confidence;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "OCR failed for page {PageNumber}", i + 1);
                        result.Warnings.Add($"Page {i + 1}: OCR failed - {ex.Message}");
                        pageResults.Add(new OcrPageResult
                        {
                            PageNumber = i + 1,
                            Text = string.Empty,
                            Confidence = 0
                        });
                    }
                }
            }
            
            // Dispose images
            foreach (var img in images)
            {
                img.Dispose();
            }
            
            // Combine results
            result.Text = string.Join("\n\n", pageResults.Select(p => p.Text));
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
    /// Processes a single image with Tesseract OCR.
    /// </summary>
    private OcrPageResult ProcessImage(System.Drawing.Bitmap bitmap, int pageNumber)
    {
        using var engine = CreateEngine();
        
        // Convert bitmap to Pix format
        using var pix = BitmapToPix(bitmap);
        using var page = engine.Process(pix);
        
        var text = page.GetText() ?? string.Empty;
        var confidence = page.GetMeanConfidence();
        
        _logger.Debug("Page {PageNumber}: OCR completed. Text length: {TextLength}, Confidence: {Confidence:P}",
            pageNumber, text.Length, confidence);
        
        return new OcrPageResult
        {
            PageNumber = pageNumber,
            Text = text,
            Confidence = confidence
        };
    }
    
    /// <summary>
    /// Converts a System.Drawing.Bitmap to a Tesseract Pix format.
    /// </summary>
    private Pix BitmapToPix(System.Drawing.Bitmap bitmap)
    {
        // Convert to byte array and load via Pix
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        return Pix.LoadFromMemory(ms.ToArray());
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _pdfConverter.Dispose();
            _disposed = true;
        }
    }
}
