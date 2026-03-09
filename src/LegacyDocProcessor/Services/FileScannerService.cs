using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for scanning directories and discovering supported files
/// </summary>
public interface IFileScannerService
{
    Task<List<DocumentFile>> ScanAsync(string basePath, IProgress<string>? progress = null);
    Task<List<DocumentFile>> ScanWithFilterAsync(string basePath, SourceConfig config, IProgress<string>? progress = null);
}

/// <summary>
/// File scanner that discovers documents in the legacy share
/// </summary>
public class FileScannerService : IFileScannerService
{
    private readonly ILogger _logger;
    
    public FileScannerService(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Scan a directory for all files (no filtering)
    /// </summary>
    public async Task<List<DocumentFile>> ScanAsync(string basePath, IProgress<string>? progress = null)
    {
        if (!Directory.Exists(basePath))
        {
            _logger.Error($"Directory does not exist: {basePath}");
            return new List<DocumentFile>();
        }
        
        var files = new List<DocumentFile>();
        var allFiles = Directory.EnumerateFiles(basePath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System
        });
        
        foreach (var filePath in allFiles)
        {
            progress?.Report(filePath);
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                files.Add(new DocumentFile
                {
                    FullPath = filePath,
                    FileName = fileInfo.Name,
                    Extension = fileInfo.Extension.TrimStart('.').ToLowerInvariant(),
                    SizeBytes = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    RelativePath = Path.GetRelativePath(basePath, filePath)
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to read file info: {filePath} - {ex.Message}");
            }
        }
        
        _logger.Information($"Scanned {files.Count} files from {basePath}");
        return files;
    }
    
    /// <summary>
    /// Scan with filtering based on config
    /// </summary>
    public async Task<List<DocumentFile>> ScanWithFilterAsync(string basePath, SourceConfig config, IProgress<string>? progress = null)
    {
        var allFiles = await ScanAsync(basePath, progress);
        
        // Filter by extension
        var supportedExtensions = config.SupportedExtensions
            .Select(e => e.TrimStart('.').ToLowerInvariant())
            .ToHashSet();
        
        var filtered = allFiles
            .Where(f => supportedExtensions.Contains(f.Extension))
            .ToList();
        
        _logger.Information($"Filtered to {filtered.Count} supported files (from {allFiles.Count} total)");
        
        // Filter by max file size
        var maxBytes = config.MaxFileSizeMb * 1024 * 1024;
        var bySize = filtered.Where(f => f.SizeBytes <= maxBytes).ToList();
        
        if (bySize.Count < filtered.Count)
        {
            _logger.Warning($"Skipped {filtered.Count - bySize.Count} files exceeding max size of {config.MaxFileSizeMb}MB");
        }
        
        return bySize;
    }
}
