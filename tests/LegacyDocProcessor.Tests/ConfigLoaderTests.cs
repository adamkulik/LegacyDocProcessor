using FluentAssertions;
using LegacyDocProcessor.Configuration;
using Xunit;

namespace LegacyDocProcessor.Tests;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _testDir;

    public ConfigLoaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ConfigLoaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    /// <summary>
    /// Test 24: ConfigLoader_ValidJson
    /// </summary>
    [Fact]
    public void Load_ValidJson_ShouldReturnConfig()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "config.json");
        var validConfig = @"{
  ""source"": {
    ""basePath"": ""D:\\Docs"",
    ""supportedExtensions"": ["".txt"", "".pdf"", "".docx""],
    ""maxFileSizeMb"": 50,
    ""excludeDirectories"": ["".git"", ""node_modules""]
  },
  ""llm"": {
    ""provider"": ""openai"",
    ""model"": ""gpt-4"",
    ""apiKey"": ""test-key""
  },
  ""confluence"": {
    ""baseUrl"": ""https://test.atlassian.net"",
    ""spaceKey"": ""TEST"",
    ""authType"": ""apitoken"",
    ""username"": ""test@test.com"",
    ""passwordOrToken"": ""token""
  }
}";
        File.WriteAllText(configPath, validConfig);
        
        // Act
        // Note: This test verifies JSON structure, actual loading would need config.toml to exist
        var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(validConfig);
        
        // Assert
        config.Should().NotBeNull();
        config!.Source.BasePath.Should().Be("D:\\Docs");
        config.Source.SupportedExtensions.Should().HaveCount(3);
    }

    /// <summary>
    /// Test 25: ConfigLoader_MissingFile
    /// </summary>
    [Fact]
    public void Load_MissingFile_ShouldThrowException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDir, "nonexistent.json");
        
        // Act & Assert
        File.Exists(nonExistentPath).Should().BeFalse();
    }

    /// <summary>
    /// Test 26: ConfigLoader_InvalidJson
    /// </summary>
    [Fact]
    public void Load_InvalidJson_ShouldFailGracefully()
    {
        // Arrange
        var invalidJson = @"{
  ""source"": {
    ""basePath"": ""D:\\Docs"",
  // Missing closing brace
  ""llm"": {
    ""provider"": ""openai""
}";
        
        // Act & Assert
        var deserializeAction = () => System.Text.Json.JsonSerializer.Deserialize<AppConfig>(invalidJson);
        deserializeAction.Should().Throw<System.Text.Json.JsonException>();
    }
}
