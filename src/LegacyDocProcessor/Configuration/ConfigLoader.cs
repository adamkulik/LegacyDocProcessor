using Newtonsoft.Json;

namespace LegacyDocProcessor.Configuration;

public static class ConfigLoader
{
    public static AppConfig Load(string? configPath = null)
    {
        configPath ??= FindConfigFile();
        
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            Console.WriteLine($"No config file found. Using defaults.");
            return new AppConfig();
        }
        
        var json = File.ReadAllText(configPath);
        var config = JsonConvert.DeserializeObject<AppConfig>(json);
        
        return config ?? new AppConfig();
    }
    
    private static string? FindConfigFile()
    {
        // Look for config.json in common locations
        var searchPaths = new[]
        {
            "config.json",
            "config.json",
            Path.Combine(Environment.CurrentDirectory, "config.json"),
            Path.Combine(AppContext.BaseDirectory, "config.json")
        };
        
        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        return null;
    }
    
    public static void Save(AppConfig config, string path = "config.json")
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(path, json);
    }
    
    public static string GenerateSampleConfig()
    {
        var sample = new AppConfig
        {
            Source = new SourceConfig
            {
                BasePath = @"D:\LegacyDocs",
                SupportedExtensions = new List<string> { "msg", "doc", "docx", "pdf", "txt" },
                ExcludePatterns = new List<string> { "**/node_modules/**", "**/.git/**" },
                MaxFileSizeMb = 50
            },
            Llm = new LlmConfig
            {
                BaseUrl = "https://api.openai.com/v1",
                ApiKey = "YOUR_API_KEY_HERE",
                Model = "gpt-4",
                MaxTokens = 4096,
                Temperature = 0.3
            },
            Confluence = new ConfluenceConfig
            {
                BaseUrl = "https://confluence.yourcompany.com",
                SpaceKey = "TECH",
                AuthType = "Saml" // Awaiting clarification
            },
            Processing = new ProcessingConfig
            {
                SkipProcessedFiles = true
            }
        };
        
        return JsonConvert.SerializeObject(sample, Formatting.Indented);
    }
}
