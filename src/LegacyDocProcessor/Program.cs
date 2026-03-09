using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using LegacyDocProcessor.Services;
using LegacyDocProcessor.Services.Extractors;
using LegacyDocProcessor.Services.Extractors.Implementations;
using Newtonsoft.Json;
using Serilog;
using Spectre.Console;

namespace LegacyDocProcessor;

/// <summary>
/// Main entry point for LegacyDocProcessor CLI
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Set up console encoding for Windows
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        try
        {
            return await RunAsync(args);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            Log.Error(ex, "Application error");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        // Load configuration
        var config = ConfigLoader.Load();
        
        // Set up Serilog
        var logPath = Path.Combine(Environment.CurrentDirectory, "logs", "legacydocprocessor-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        // Handle no arguments - show help
        if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
        {
            ShowHelp();
            return 0;
        }
        
        var command = args[0].ToLowerInvariant();
        
        switch (command)
        {
            case "scan":
                return await ScanAsync(config, args);
                
            case "process":
                return await ProcessAsync(config, args);
                
            case "aggregate":
                return await AggregateAsync(config, args);
                
            case "publish":
                return await PublishAsync(config, args);
                
            case "export":
                return await ExportAsync(config, args);
                
            case "init":
                return InitConfig();
                
            case "version":
                ShowVersion();
                return 0;
                
            default:
                AnsiConsole.MarkupLine($"[red]Unknown command: {command}[/]");
                ShowHelp();
                return 1;
        }
    }

    private static void ShowHelp()
    {
        var panel = new Panel(@"
[bold]LegacyDocProcessor[/] - Process legacy documentation using LLM

[bold]Commands:[/]
  scan        Scan directory for supported documents
  process     Extract text and process with LLM
  aggregate   Merge processed documents by topic
  publish     Publish to Confluence
  export      Export to local Markdown knowledge base
  init        Create sample config file
  version     Show version info

[bold]Usage:[/]
  LegacyDocProcessor scan --path ""D:\Docs""
  LegacyDocProcessor process --input scan-results.json
  LegacyDocProcessor aggregate --input processed.json
  LegacyDocProcessor publish --input aggregated.json
  LegacyDocProcessor export --input aggregated.json --output ./

[bold]Options:[/]
  --path, -p       Source path
  --input, -i       Input file
  --output, -o      Output file/directory
  --config, -c      Config file path
  --resume          Resume from previous run (default if state exists)
  --force           Start fresh, ignore state file
  --retry-failed    Only process previously failed files
  --help, -h        Show this help")
            .Header("[bold]LegacyDocProcessor CLI[/]")
            .BorderColor(Color.Cyan1);
        
        AnsiConsole.Write(panel);
    }

    private static void ShowVersion()
    {
        AnsiConsole.MarkupLine("[cyan]LegacyDocProcessor v1.0.0[/]");
        AnsiConsole.MarkupLine("Built with .NET 8 + Spectre.Console + Serilog");
    }

    private static int InitConfig()
    {
        var configPath = "config.json";
        
        if (File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Config file already exists: {configPath}[/]");
            if (!AnsiConsole.Confirm("Overwrite?"))
                return 0;
        }
        
        var sampleConfig = ConfigLoader.GenerateSampleConfig();
        File.WriteAllText(configPath, sampleConfig);
        AnsiConsole.MarkupLine($"[green]Created sample config: {configPath}[/]");
        
        return 0;
    }

    private static async Task<int> ScanAsync(AppConfig config, string[] args)
    {
        var path = GetArgValue(args, "--path", "-p") ?? config.Source.BasePath;
        
        if (string.IsNullOrEmpty(path))
        {
            AnsiConsole.MarkupLine("[red]Error: No path specified. Use --path or set Source.BasePath in config.[/]");
            return 1;
        }
        
        if (!Directory.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]Error: Directory not found: {path}[/]");
            return 1;
        }
        
        AnsiConsole.MarkupLine($"[cyan]Scanning:[/] {path}");
        
        var scanner = new FileScannerService(Log.Logger);
        
        // Create progress handler for scanning
        var progressMessage = "";
        IProgress<string> progressHandler = new Progress<string>(msg => progressMessage = msg);
        
        var progress = AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new SpinnerColumn(), new PercentageColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask("[cyan]Scanning files...[/]");
                
                var files = scanner.ScanWithFilterAsync(path, config.Source, progressHandler).GetAwaiter().GetResult();
                
                task.Description = $"[green]Found {files.Count} files[/]";
                return files;
            });
        
        var outputPath = GetArgValue(args, "--output", "-o") ?? "scan-results.json";
        
        var output = new
        {
            ScanPath = path,
            ScannedAt = DateTime.UtcNow,
            TotalFiles = progress.Count,
            Files = progress.Select(f => new
            {
                f.FullPath,
                f.FileName,
                f.Extension,
                f.SizeBytes,
                f.LastModified,
                f.RelativePath
            }).ToList()
        };
        
        await File.WriteAllTextAsync(outputPath, JsonConvert.SerializeObject(output, Formatting.Indented));
        AnsiConsole.MarkupLine($"[green]Scan complete: {progress.Count} files saved to {outputPath}[/]");
        
        return 0;
    }

    private static async Task<int> ProcessAsync(AppConfig config, string[] args)
    {
        var inputPath = GetArgValue(args, "--input", "-i") ?? "scan-results.json";
        
        // Parse options
        var forceStart = HasArg(args, "--force");
        var retryFailed = HasArg(args, "--retry-failed");
        var resume = HasArg(args, "--resume") || (!forceStart && !retryFailed); // Default: resume if state exists
        
        if (!File.Exists(inputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {inputPath}[/]");
            AnsiConsole.MarkupLine("Run 'scan' first to generate scan-results.json");
            return 1;
        }
        
        // Initialize state service
        var stateService = new ProcessingStateService(inputPath, Log.Logger);
        
        // Handle resume/force options
        if (forceStart)
        {
            AnsiConsole.MarkupLine("[yellow]Force start - clearing previous state[/]");
            stateService.Clear();
        }
        else if (retryFailed)
        {
            AnsiConsole.MarkupLine("[yellow]Retry failed files only[/]");
        }
        else if (stateService.StateFileExists)
        {
            var savedCheckpoint = stateService.GetCheckpoint();
            AnsiConsole.MarkupLine($"[cyan]Resuming from checkpoint:[/] {savedCheckpoint.CompletedFiles.Count}/{savedCheckpoint.TotalFiles} completed, {savedCheckpoint.FailedFiles.Count} failed");
        }
        
        // Load state if resuming
        if (!forceStart && !retryFailed && stateService.StateFileExists)
        {
            stateService.Load();
        }
        
        var json = await File.ReadAllTextAsync(inputPath);
        var scanData = JsonConvert.DeserializeObject<dynamic>(json);
        
        var files = ((IEnumerable<dynamic>)scanData!.files)
            .Select(f => new Models.DocumentFile
            {
                FullPath = f.FullPath,
                FileName = f.FileName,
                Extension = f.Extension,
                SizeBytes = f.SizeBytes,
                LastModified = f.LastModified,
                RelativePath = f.RelativePath
            })
            .ToList();
        
        // Determine which files to process
        var allFilePaths = files.Select(f => f.FullPath).ToList();
        var filesToProcess = stateService.GetFilesToProcess(allFilePaths, retryFailed);
        
        AnsiConsole.MarkupLine($"[cyan]Processing {files.Count} files with LLM...[/]");
        
        if (retryFailed)
        {
            AnsiConsole.MarkupLine($"[yellow]Retrying {filesToProcess.Count} previously failed files[/]");
        }
        else if (stateService.StateFileExists && filesToProcess.Count < files.Count)
        {
            AnsiConsole.MarkupLine($"[cyan]Skipping {files.Count - filesToProcess.Count} already completed files[/]");
        }
        
        // Initialize state with total files if not resuming
        if (!stateService.StateFileExists || forceStart)
        {
            stateService.Initialize(files.Count);
        }
        
        // Set up extractors
        var extractors = new List<ITextExtractor>
        {
            new MsgExtractor(),
            new PdfExtractor(Log.Logger),
            new DocxExtractor(),
            new DocExtractor(Log.Logger),
            new TextExtractor()
        };
        
        var extractorFactory = new ExtractorFactory(extractors);
        var textExtractor = new TextExtractorService(extractorFactory, Log.Logger);
        
        // Phase 1: Extract text from files (only for files that need processing)
        AnsiConsole.MarkupLine("[cyan]Phase 1: Extracting text...[/]");
        
        var extractedContents = new List<Models.ExtractedContent>();
        var filesToExtract = files.Where(f => filesToProcess.Contains(f.FullPath)).ToList();
        
        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new SpinnerColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Extracting text...[/]");
                task.MaxValue = filesToExtract.Count;
                
                foreach (var file in filesToExtract)
                {
                    try
                    {
                        var content = await textExtractor.ExtractAsync(file.FullPath);
                        extractedContents.Add(content);
                        
                        if (content.IsSuccess)
                        {
                            stateService.MarkCompleted(file.FullPath);
                        }
                        else
                        {
                            stateService.MarkFailed(file.FullPath, content.ErrorMessage ?? "Unknown extraction error");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to extract {File}: {Error}", file.FileName, ex.Message);
                        stateService.MarkFailed(file.FullPath, ex.Message);
                    }
                    
                    task.Increment(1);
                    task.Description = $"Extracted: {file.FileName}";
                }
            });
        
        var successCount = extractedContents.Count(c => c.IsSuccess);
        var failedCount = extractedContents.Count(c => !c.IsSuccess);
        AnsiConsole.MarkupLine($"[green]Extracted {successCount}/{filesToExtract.Count} files successfully[/]");
        
        if (failedCount > 0)
        {
            AnsiConsole.MarkupLine($"[red]Failed to extract {failedCount} files[/]");
        }
        
        // Phase 2: Process with LLM (only successful extractions)
        AnsiConsole.MarkupLine("[cyan]Phase 2: Processing with LLM...[/]");
        
        var llmService = new LlmProcessingService(config.Llm, Log.Logger);
        var processedKnowledge = new List<Models.ProcessedKnowledge>();
        
        var successfulContents = extractedContents.Where(c => c.IsSuccess).ToList();
        
        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new SpinnerColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Processing with LLM...[/]");
                task.MaxValue = successfulContents.Count;
                
                foreach (var content in successfulContents)
                {
                    try
                    {
                        var result = await llmService.ProcessContentAsync(content);
                        processedKnowledge.Add(result);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to process {File}: {Error}", content.FileName, ex.Message);
                        // Mark as failed in state
                        stateService.MarkFailed(content.FilePath, ex.Message);
                    }
                    
                    task.Increment(1);
                    task.Description = $"LLM: {content.FileName}";
                }
            });
        
        // Load any previously processed documents when resuming
        var checkpoint = stateService.GetCheckpoint();
        if (checkpoint.ProcessedDocuments.Any())
        {
            // Merge with newly processed documents, avoiding duplicates
            var newFilePaths = processedKnowledge.Select(p => p.FilePath).ToHashSet();
            var existingDocs = checkpoint.ProcessedDocuments.Where(p => !newFilePaths.Contains(p.FilePath)).ToList();
            processedKnowledge.AddRange(existingDocs);
            AnsiConsole.MarkupLine($"[cyan]Loaded {existingDocs.Count} previously processed documents[/]");
        }
        
        var outputPath = GetArgValue(args, "--output", "-o") ?? stateService.OutputFile;
        
        var output = new
        {
            ProcessedAt = DateTime.UtcNow,
            TotalFiles = files.Count,
            ExtractedCount = successCount,
            ProcessedCount = processedKnowledge.Count,
            Documents = processedKnowledge.Select(d => new
            {
                d.FilePath,
                d.SuggestedTitle,
                d.Summary,
                Topics = d.Topics.Select(t => new TopicInfo
                {
                    Topic = t.Topic,
                    Relevance = 0.0,
                    Summary = "",
                    Content = ""
                }).ToList(),
                d.TechnicalDetails,
                d.ActionItems,
                d.Questions,
                d.ProcessedAt
            }).ToList()
        };

        await File.WriteAllTextAsync(outputPath, JsonConvert.SerializeObject(output, Formatting.Indented));
        
        // Show summary
        var finalCheckpoint = stateService.GetCheckpoint();
        AnsiConsole.MarkupLine($"[green]Processed {processedKnowledge.Count} documents saved to {outputPath}[/]");
        AnsiConsole.MarkupLine($"[cyan]Summary:[/] {finalCheckpoint.CompletedFiles.Count} completed, {finalCheckpoint.FailedFiles.Count} failed, {files.Count - finalCheckpoint.CompletedFiles.Count - finalCheckpoint.FailedFiles.Count} remaining");
        
        if (finalCheckpoint.FailedFiles.Any())
        {
            AnsiConsole.MarkupLine($"[yellow]Run with --retry-failed to retry failed files[/]");
        }
        
        return 0;
    }

    private static async Task<int> AggregateAsync(AppConfig config, string[] args)
    {
        var inputPath = GetArgValue(args, "--input", "-i") ?? "processed.json";
        
        if (!File.Exists(inputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {inputPath}[/]");
            AnsiConsole.MarkupLine("Run 'process' first to generate processed.json");
            return 1;
        }
        
        var json = await File.ReadAllTextAsync(inputPath);
        var processData = JsonConvert.DeserializeObject<dynamic>(json);
        
        var documents = new List<Models.ProcessedKnowledge>();
        
        foreach (var doc in processData!.documents)
        {
            var knowledge = new Models.ProcessedKnowledge
            {
                FilePath = doc.FilePath,
                SuggestedTitle = doc.SuggestedTitle,
                Summary = doc.Summary,
                Topics = new List<TopicInfo>()
            };
            
            // Topics is List<string>, so we extract just the topic names
            foreach (var topic in doc.Topics)
            {
                // Each topic could be a string OR an object with Topic property
                if (topic is TopicInfo topicName)
                {
                    knowledge.Topics.Add(topicName);
                }
            }
            
            documents.Add(knowledge);
        }
        
        AnsiConsole.MarkupLine($"[cyan]Aggregating {documents.Count} documents by topic...[/]");
        
        var aggregator = new TopicAggregatorService(Log.Logger);
        var result = aggregator.Aggregate(documents);
        
        AnsiConsole.MarkupLine($"[green]Aggregated into {result.Topics.Count} topics[/]");
        
        foreach (var topic in result.Topics.Take(10))
        {
            AnsiConsole.MarkupLine($"  • {topic.Name} ({topic.SourceFiles.Count} sources)");
        }
        
        if (result.Topics.Count > 10)
        {
            AnsiConsole.MarkupLine($"  ... and {result.Topics.Count - 10} more topics");
        }
        
        var outputPath = GetArgValue(args, "--output", "-o") ?? "aggregated.json";
        
        var output = new
        {
            AggregatedAt = DateTime.UtcNow,
            TotalSourceFiles = result.TotalSourceFiles,
            TopicCount = result.Topics.Count,
            Topics = result.Topics.Select(t => new
            {
                t.Name,
                t.MergedContent,
                t.SourceFiles,
                t.SectionCount,
                t.AverageRelevance,
                t.AggregatedAt
            }).ToList()
        };
        
        await File.WriteAllTextAsync(outputPath, JsonConvert.SerializeObject(output, Formatting.Indented));
        AnsiConsole.MarkupLine($"[green]Aggregated topics saved to {outputPath}[/]");
        
        return 0;
    }

    private static async Task<int> PublishAsync(AppConfig config, string[] args)
    {
        var inputPath = GetArgValue(args, "--input", "-i") ?? "aggregated.json";
        
        if (!File.Exists(inputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {inputPath}[/]");
            AnsiConsole.MarkupLine("Run 'aggregate' first to generate aggregated.json");
            return 1;
        }
        
        if (string.IsNullOrEmpty(config.Confluence.BaseUrl))
        {
            AnsiConsole.MarkupLine("[red]Error: Confluence not configured. Set Confluence.BaseUrl in config.[/]");
            return 1;
        }
        
        AnsiConsole.MarkupLine($"[cyan]Connecting to Confluence:[/] {config.Confluence.BaseUrl}");
        AnsiConsole.MarkupLine($"[cyan]Target Space:[/] {config.Confluence.SpaceKey}");
        
        // Load aggregated topics
        var json = await File.ReadAllTextAsync(inputPath);
        var aggData = JsonConvert.DeserializeObject<dynamic>(json);
        
        var topics = new List<Models.UnifiedTopic>();
        
        foreach (var topic in aggData!.topics)
        {
            topics.Add(new Models.UnifiedTopic
            {
                Name = topic.Name,
                MergedContent = topic.MergedContent,
                SourceFiles = ((IEnumerable<dynamic>)topic.SourceFiles).Select(s => (string)s).ToList(),
                SectionCount = topic.SectionCount,
                AverageRelevance = topic.AverageRelevance
            });
        }
        
        AnsiConsole.MarkupLine($"[cyan]Publishing {topics.Count} topics to Confluence...[/]");
        
        // Create Confluence client
        var confluenceClient = new ConfluenceClient(config.Confluence);
        
        // Test connection
        AnsiConsole.MarkupLine("[cyan]Testing connection...[/]");
        var connected = await confluenceClient.TestConnectionAsync();
        
        if (!connected)
        {
            AnsiConsole.MarkupLine("[red]Failed to connect to Confluence. Check your configuration and try again.[/]");
            return 1;
        }
        
        // Get space info
        var spaceName = await confluenceClient.GetSpaceAsync();
        AnsiConsole.MarkupLine($"[green]Connected to space:[/] {spaceName ?? config.Confluence.SpaceKey}");
        
        // Publish topics
        var successCount = 0;
        var failedCount = 0;
        
        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new SpinnerColumn(), new PercentageColumn(), new RemainingTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Publishing to Confluence...[/]");
                task.MaxValue = topics.Count;
                
                foreach (var topic in topics)
                {
                    try
                    {
                        // Build Confluence page content
                        var content = BuildConfluencePageContent(topic);
                        
                        var page = new Models.ConfluencePage
                        {
                            Title = topic.Name,
                            SpaceKey = config.Confluence.SpaceKey,
                            ParentPageId = config.Confluence.ParentPageId,
                            Content = content,
                            Labels = new List<string> { "auto-generated", "legacy-doc" }
                        };
                        
                        var pageId = await confluenceClient.PublishPageAsync(page);
                        
                        if (!string.IsNullOrEmpty(pageId))
                        {
                            successCount++;
                            task.Description = $"[green]Published: {topic.Name}[/]";
                        }
                        else
                        {
                            failedCount++;
                            task.Description = $"[red]Failed: {topic.Name}[/]";
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to publish topic: {Topic}", topic.Name);
                        failedCount++;
                    }
                    
                    task.Increment(1);
                }
            });
        
        AnsiConsole.MarkupLine($"[green]Published {successCount}/{topics.Count} topics successfully[/]");
        
        if (failedCount > 0)
        {
            AnsiConsole.MarkupLine($"[red]Failed to publish {failedCount} topics. Check logs for details.[/]");
        }
        
        return failedCount > 0 ? 1 : 0;
    }
    
    private static string BuildConfluencePageContent(Models.UnifiedTopic topic)
    {
        var summary = topic.MergedContent.Split('\n').FirstOrDefault() ?? "No summary available";
        
        var content = $@"{topic.MergedContent}

---

h3. Source Files

{string.Join("\n", topic.SourceFiles.Select(f => $"* [[{f}]]"))}

---

*Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*
*Confidence: {topic.AverageRelevance:P0}*
*Sources: {topic.SourceFiles.Count} files*";
        
        return content;
    }

    private static async Task<int> ExportAsync(AppConfig config, string[] args)
    {
        var inputPath = GetArgValue(args, "--input", "-i") ?? "aggregated.json";
        var outputDir = GetArgValue(args, "--output", "-o") ?? "./knowledge-base";
        
        if (!File.Exists(inputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {inputPath}[/]");
            AnsiConsole.MarkupLine("Run 'aggregate' first to generate aggregated.json");
            return 1;
        }
        
        var json = await File.ReadAllTextAsync(inputPath);
        var aggData = JsonConvert.DeserializeObject<dynamic>(json);
        
        var topics = new List<Models.UnifiedTopic>();
        
        foreach (var topic in aggData!.topics)
        {
            topics.Add(new Models.UnifiedTopic
            {
                Name = topic.Name,
                MergedContent = topic.MergedContent,
                SourceFiles = ((IEnumerable<dynamic>)topic.SourceFiles).Select(s => (string)s).ToList(),
                SectionCount = topic.SectionCount,
                AverageRelevance = topic.AverageRelevance
            });
        }
        
        AnsiConsole.MarkupLine($"[cyan]Exporting {topics.Count} topics to Markdown...[/]");
        
        // Create output directory
        Directory.CreateDirectory(outputDir);
        
        // Export each topic as Markdown
        var topicIndex = new List<string>();
        
        foreach (var topic in topics)
        {
            var safeName = SanitizeFileName(topic.Name);
            var topicPath = Path.Combine(outputDir, "topics", safeName);
            Directory.CreateDirectory(topicPath);
            
            var md = $@"# {topic.Name}

**Summary:** {topic.MergedContent.Split('\n').FirstOrDefault() ?? "No summary available"}
**Sources:** {string.Join(", ", topic.SourceFiles.Select(f => $"[[{f}]]"))}
**Processed:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
**Confidence:** {topic.AverageRelevance:P0}

---

{topic.MergedContent}

---

### Source References

{string.Join("\n", topic.SourceFiles.Select(f => $"- [[{f}]]"))}
";
            
            await File.WriteAllTextAsync(Path.Combine(topicPath, "index.md"), md);
            topicIndex.Add($"- [{topic.Name}](topics/{safeName}/index.md) - {topic.SourceFiles.Count} sources");
        }
        
        // Create index
        var readme = $@"# Knowledge Base Index

Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

## Topics ({topics.Count})

{string.Join("\n", topicIndex)}
";
        
        await File.WriteAllTextAsync(Path.Combine(outputDir, "README.md"), readme);
        
        AnsiConsole.MarkupLine($"[green]Exported {topics.Count} topics to {outputDir}[/]");
        
        return 0;
    }

    private static string? GetArgValue(string[] args, string name, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == name || args[i] == shortName)
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    return args[i + 1];
                }
            }
        }
        return null;
    }
    
    private static bool HasArg(string[] args, string name)
    {
        return args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// Factory for getting the appropriate text extractor
/// </summary>
public class ExtractorFactory
{
    private readonly List<ITextExtractor> _extractors;
    
    public ExtractorFactory(List<ITextExtractor> extractors)
    {
        _extractors = extractors;
    }
    
    public ITextExtractor? GetExtractor(string extension)
    {
        return _extractors.FirstOrDefault(e => e.CanExtract(extension));
    }
}

/// <summary>
/// Service that orchestrates text extraction using available extractors
/// </summary>
public class TextExtractorService
{
    private readonly ExtractorFactory _factory;
    private readonly ILogger _logger;
    
    public TextExtractorService(ExtractorFactory factory, ILogger logger)
    {
        _factory = factory;
        _logger = logger;
    }
    
    public async Task<Models.ExtractedContent> ExtractAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var extractor = _factory.GetExtractor(extension);
        
        if (extractor == null)
        {
            _logger.Warning("No extractor found for {Extension}: {File}", extension, filePath);
            return new Models.ExtractedContent
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FileType = extension,
                IsSuccess = false,
                ErrorMessage = $"No extractor available for .{extension} files",
                ExtractedAt = DateTime.UtcNow
            };
        }
        
        return await extractor.ExtractAsync(filePath);
    }
}
