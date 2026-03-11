using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for managing Aspose license files
/// </summary>
public class AsposeLicenseService
{
    private readonly ILogger _logger;
    private bool _isInitialized;
    
    public AsposeLicenseService(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Initialize the Aspose license using the specified license file name
    /// </summary>
    /// <param name="licenseFileName">Name of the license file (e.g., "Aspose.Total.NET.lic")</param>
    /// <returns>True if license was initialized successfully, false otherwise</returns>
    public bool InitializeLicense(string? licenseFileName)
    {
        if (_isInitialized)
        {
            _logger.Debug("Aspose license already initialized");
            return true;
        }
        
        if (string.IsNullOrWhiteSpace(licenseFileName))
        {
            _logger.Warning("Aspose license file name not configured - running in evaluation mode");
            return false;
        }
        
        try
        {
            // Try multiple locations for the license file
            var possiblePaths = new[]
            {
                // Application base directory
                Path.Combine(AppContext.BaseDirectory, licenseFileName),
                Path.Combine(AppContext.BaseDirectory, "licenses", licenseFileName),
                // Current working directory
                Path.Combine(Environment.CurrentDirectory, licenseFileName),
                Path.Combine(Environment.CurrentDirectory, "licenses", licenseFileName)
            };
            
            string? foundLicensePath = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    foundLicensePath = path;
                    break;
                }
            }
            
            if (foundLicensePath == null)
            {
                _logger.Warning("Aspose license file '{LicenseFile}' not found in any of these locations: {Paths}", 
                    licenseFileName, 
                    string.Join(", ", possiblePaths.Select(p => $"'{p}'")));
                _logger.Warning("Running in evaluation mode (watermark may appear)");
                return false;
            }
            
            // Try to set license for each product we use
            var success = false;
            
            //Aspose.Words
            try
            {
                var wordsLicense = new global::Aspose.Words.License();
                wordsLicense.SetLicense(foundLicensePath);
                _logger.Information("Aspose.Words license initialized successfully from {Path}", foundLicensePath);
                success = true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to initializeAspose.Words license");
            }
            
            //Aspose.Pdf (if used)
            try
            {
                var pdfLicense = new global::Aspose.Pdf.License();
                pdfLicense.SetLicense(foundLicensePath);
                _logger.Information("Aspose.Pdf license initialized successfully from {Path}", foundLicensePath);
                success = true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to initializeAspose.Pdf license");
            }
            
            //Aspose.Cells (if used)
            try
            {
                var cellsLicense = new global::Aspose.Cells.License();
                cellsLicense.SetLicense(foundLicensePath);
                _logger.Information("Aspose.Cells license initialized successfully from {Path}", foundLicensePath);
                success = true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to initializeAspose.Cells license");
            }
            
            if (success)
            {
                _isInitialized = true;
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initializeAspose license from file '{File}': {Error}", licenseFileName, ex.Message);
            _logger.Warning("Running in evaluation mode (watermark may appear)");
            return false;
        }
    }
    
    /// <summary>
    /// Check if license has been initialized
    /// </summary>
    public bool IsInitialized => _isInitialized;
}
