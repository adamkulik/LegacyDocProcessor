extern alias SystemDrawing;
using SystemDrawing::System.Drawing;
using SystemDrawing::System.Drawing.Imaging;
using Serilog;
using Aspose.Pdf;
using Aspose.Pdf.Devices;

namespace LegacyDocProcessor.Services.Extractors;

/// <summary>
/// Converts PDF pages to images for OCR processing.
/// Uses Aspose.PDF for rendering PDF pages.
/// </summary>
public class PdfToImageConverter : IDisposable
{
    private readonly ILogger _logger;
    private readonly int _dpi;
    private bool _disposed;
    
    public PdfToImageConverter(ILogger logger, int dpi = 300)
    {
        _logger = logger;
        _dpi = dpi;
    }
    
    /// <summary>
    /// Converts a single PDF page to an image.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <returns>Bitmap of the rendered page, or null if conversion failed</returns>
    public System.Drawing.Bitmap ConvertPageToImage(string pdfPath, int pageNumber)
    {
        try
        {
            // Aspose.PDF uses 1-based page indices
            var pageIndex = pageNumber;
            
            using var pdfDoc = new Document(pdfPath);
            
            if (pageIndex < 1 || pageIndex > pdfDoc.Pages.Count)
            {
                _logger.Warning("Page {PageNumber} is out of range for PDF {PdfPath}. PDF has {PageCount} pages.", 
                    pageNumber, pdfPath, pdfDoc.Pages.Count);
                return null;
            }
            
            var page = pdfDoc.Pages[pageIndex];
            
            // Create resolution based on DPI
            var resolution = new Resolution(_dpi);
            
            // Create PngDevice for rendering
            var pngDevice = new PngDevice(resolution);
            
            // Convert page to stream
            using var ms = new MemoryStream();
            pngDevice.Process(page, ms);
            ms.Position = 0;
            
            // Load as bitmap
            var bitmap = new System.Drawing.Bitmap(ms);
            return bitmap;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to convert page {PageNumber} of PDF {PdfPath} to image", pageNumber, pdfPath);
            return null;
        }
    }
    
    /// <summary>
    /// Converts all pages of a PDF to images.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <param name="maxPages">Maximum number of pages to convert (0 for all)</param>
    /// <returns>List of bitmaps, one per page</returns>
    public List<System.Drawing.Bitmap> ConvertAllPages(string pdfPath, int maxPages = 0)
    {
        var images = new List<System.Drawing.Bitmap>();
        
        try
        {
            using var pdfDoc = new Document(pdfPath);
            var pageCount = pdfDoc.Pages.Count;
            
            if (maxPages > 0 && pageCount > maxPages)
            {
                _logger.Warning("PDF {PdfPath} has {PageCount} pages, but max pages limit is {MaxPages}. Processing first {MaxPages} pages only.",
                    pdfPath, pageCount, maxPages, maxPages);
                pageCount = maxPages;
            }
            
            for (int i = 1; i <= pageCount; i++)
            {
                var bitmap = ConvertPageToImage(pdfPath, i);
                if (bitmap != null)
                {
                    images.Add(bitmap);
                }
            }
            
            _logger.Debug("Converted {ImageCount} pages from PDF {PdfPath}", images.Count, pdfPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to convert PDF {PdfPath} to images", pdfPath);
        }
        
        return images;
    }
    
    /// <summary>
    /// Gets the number of pages in a PDF document.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <returns>Number of pages, or 0 if the file couldn't be read</returns>
    public int GetPageCount(string pdfPath)
    {
        try
        {
            using var pdfDoc = new Document(pdfPath);
            return pdfDoc.Pages.Count;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get page count for PDF {PdfPath}", pdfPath);
            return 0;
        }
    }
    
    /// <summary>
    /// Converts a bitmap to a byte array in the specified format.
    /// </summary>
    /// <param name="bitmap">The bitmap to convert</param>
    /// <param name="format">Image format (default: PNG)</param>
    /// <returns>Byte array of the image data</returns>
    public byte[] BitmapToBytes(System.Drawing.Bitmap bitmap, System.Drawing.Imaging.ImageFormat format)
    {
        format = System.Drawing.Imaging.ImageFormat.Png;
        using var ms = new MemoryStream();
        bitmap.Save(ms, format);
        return ms.ToArray();
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
