using LegacyDocProcessor.Models;
using Newtonsoft.Json;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for managing processing state and checkpoint-based resume functionality
/// </summary>
public class ProcessingStateService
{
    private readonly string _stateFilePath;
    private ProcessingCheckpoint _checkpoint;
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public ProcessingStateService(string scanFilePath, ILogger logger)
    {
        _logger = logger;
        
        // Generate state file name based on scan file
        var scanFileName = Path.GetFileNameWithoutExtension(scanFilePath);
        var stateDir = Path.Combine(Environment.CurrentDirectory, ".processing-state");
        Directory.CreateDirectory(stateDir);
        _stateFilePath = Path.Combine(stateDir, $"{scanFileName}-state.json");
        
        _checkpoint = new ProcessingCheckpoint
        {
            ScanFile = scanFilePath,
            OutputFile = GetOutputFileName(scanFilePath),
            StartedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
    }

    private string GetOutputFileName(string scanFilePath)
    {
        var dir = Path.GetDirectoryName(scanFilePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(scanFilePath);
        return Path.Combine(dir, $"{name}-processed.json");
    }

    /// <summary>
    /// Check if a state file exists for resuming
    /// </summary>
    public bool StateFileExists => File.Exists(_stateFilePath);

    /// <summary>
    /// Load existing checkpoint from disk
    /// </summary>
    public void Load()
    {
        if (!StateFileExists)
            return;

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var loaded = JsonConvert.DeserializeObject<ProcessingCheckpoint>(json);
            if (loaded != null)
            {
                _checkpoint = loaded;
                _logger.Information("Loaded checkpoint: {Completed} completed, {Failed} failed, {Total} total",
                    _checkpoint.CompletedFiles.Count, _checkpoint.FailedFiles.Count, _checkpoint.TotalFiles);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load checkpoint file, starting fresh");
            _checkpoint = new ProcessingCheckpoint
            {
                ScanFile = _checkpoint.ScanFile,
                OutputFile = _checkpoint.OutputFile,
                StartedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Save current checkpoint to disk
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            _checkpoint.LastUpdated = DateTime.UtcNow;
            var json = JsonConvert.SerializeObject(_checkpoint, Formatting.Indented);
            File.WriteAllText(_stateFilePath, json);
        }
    }

    /// <summary>
    /// Initialize checkpoint with total file count
    /// </summary>
    public void Initialize(int totalFiles)
    {
        _checkpoint.TotalFiles = totalFiles;
        _checkpoint.LastUpdated = DateTime.UtcNow;
        Save();
    }

    /// <summary>
    /// Mark a file as successfully completed
    /// </summary>
    public void MarkCompleted(string filePath, ProcessedKnowledge? processedKnowledge = null)
    {
        lock (_lock)
        {
            if (!_checkpoint.CompletedFiles.Contains(filePath))
            {
                _checkpoint.CompletedFiles.Add(filePath);
            }
            
            // Remove from failed files if it was there
            _checkpoint.FailedFiles.RemoveAll(f => f.FilePath == filePath);
            
            _checkpoint.LastProcessedFile = filePath;
            _checkpoint.LastProcessedIndex = _checkpoint.CompletedFiles.Count;
            
            // Store processed knowledge for later use
            if (processedKnowledge != null)
            {
                _checkpoint.ProcessedDocuments ??= new List<ProcessedKnowledge>();
                _checkpoint.ProcessedDocuments.Add(processedKnowledge);
            }
            
            Save();
        }
    }

    /// <summary>
    /// Mark a file as failed
    /// </summary>
    public void MarkFailed(string filePath, string errorMessage)
    {
        lock (_lock)
        {
            // Remove from completed if it was there
            _checkpoint.CompletedFiles.Remove(filePath);
            
            // Add or update failed file
            var existing = _checkpoint.FailedFiles.FirstOrDefault(f => f.FilePath == filePath);
            if (existing != null)
            {
                _checkpoint.FailedFiles.Remove(existing);
            }
            
            _checkpoint.FailedFiles.Add(new FailedFileInfo
            {
                FilePath = filePath,
                ErrorMessage = errorMessage,
                FailedAt = DateTime.UtcNow,
                AttemptCount = (existing?.AttemptCount ?? 0) + 1
            });
            
            _checkpoint.LastProcessedFile = filePath;
            _checkpoint.LastUpdated = DateTime.UtcNow;
            
            Save();
        }
    }

    /// <summary>
    /// Get list of files that still need processing
    /// </summary>
    public List<string> GetFilesToProcess(List<string> allFiles, bool retryFailed = false)
    {
        if (retryFailed)
        {
            // Only process previously failed files
            return _checkpoint.FailedFiles.Select(f => f.FilePath).ToList();
        }
        
        // Return files not yet completed
        return allFiles
            .Where(f => !_checkpoint.CompletedFiles.Contains(f))
            .ToList();
    }

    /// <summary>
    /// Get the checkpoint summary
    /// </summary>
    public ProcessingCheckpoint GetCheckpoint() => _checkpoint;

    /// <summary>
    /// Clear the checkpoint (for force start)
    /// </summary>
    public void Clear()
    {
        if (StateFileExists)
        {
            File.Delete(_stateFilePath);
        }
        _checkpoint = new ProcessingCheckpoint
        {
            ScanFile = _checkpoint.ScanFile,
            OutputFile = _checkpoint.OutputFile,
            StartedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get progress percentage
    /// </summary>
    public double GetProgressPercent()
    {
        if (_checkpoint.TotalFiles == 0) return 0;
        return (double)_checkpoint.CompletedFiles.Count / _checkpoint.TotalFiles * 100;
    }

    /// <summary>
    /// Check if processing is complete
    /// </summary>
    public bool IsComplete() => _checkpoint.CompletedFiles.Count >= _checkpoint.TotalFiles;

    /// <summary>
    /// Get output file path
    /// </summary>
    public string OutputFile => _checkpoint.OutputFile;
}

/// <summary>
/// Represents the checkpoint state for resuming processing
/// </summary>
public class ProcessingCheckpoint
{
    public string ScanFile { get; set; } = string.Empty;
    public string OutputFile { get; set; } = string.Empty;
    public int LastProcessedIndex { get; set; }
    public string? LastProcessedFile { get; set; }
    public int TotalFiles { get; set; }
    
    public List<string> CompletedFiles { get; set; } = new();
    public List<FailedFileInfo> FailedFiles { get; set; } = new();
    
    public List<ProcessedKnowledge> ProcessedDocuments { get; set; } = new();
    
    public DateTime StartedAt { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Information about a failed file
/// </summary>
public class FailedFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime FailedAt { get; set; }
    public int AttemptCount { get; set; }
}
