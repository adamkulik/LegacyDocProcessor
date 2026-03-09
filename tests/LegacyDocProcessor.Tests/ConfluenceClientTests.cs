using FluentAssertions;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Services;
using Xunit;

namespace LegacyDocProcessor.Tests;

public class ConfluenceClientTests
{
    private readonly ConfluenceClient _client;

    public ConfluenceClientTests()
    {
        var config = new ConfluenceConfig
        {
            BaseUrl = "https://test.atlassian.net",
            SpaceKey = "TEST",
            AuthType = "apitoken",
            Username = "test@test.com",
            PasswordOrToken = "test-token"
        };
        _client = new ConfluenceClient(config);
    }

    /// <summary>
    /// Test 8: ConfluenceClient_ConvertMarkdown_Bold
    /// </summary>
    [Fact]
    public void ConvertMarkdown_BoldText_ShouldConvertToStrongTags()
    {
        // Arrange
        var markdown = "This is **bold text** and __also bold__";
        
        // Act - Use reflection to access private method
        var result = ConvertMarkdown(markdown);
        
        // Assert
        result.Should().Contain("<strong>bold text</strong>");
    }

    /// <summary>
    /// Test 9: ConfluenceClient_ConvertMarkdown_Italic
    /// </summary>
    [Fact]
    public void ConvertMarkdown_ItalicText_ShouldConvertToEmTags()
    {
        // Arrange
        var markdown = "This is *italic text* and _also italic_";
        
        // Act
        var result = ConvertMarkdown(markdown);
        
        // Assert
        result.Should().Contain("<em>italic text</em>");
    }

    /// <summary>
    /// Test 10: ConfluenceClient_ConvertMarkdown_Code
    /// </summary>
    [Fact]
    public void ConvertMarkdown_Code_ShouldConvertToCodeTags()
    {
        // Arrange
        var markdown = "Inline `code` and code block:\n```csharp\nvar x = 1;\n```";
        
        // Act
        var result = ConvertMarkdown(markdown);
        
        // Assert
        result.Should().Contain("<code>code</code>");
        result.Should().Contain("<ac:code-block");
    }

    /// <summary>
    /// Test 11: ConfluenceClient_ConvertMarkdown_Headers
    /// </summary>
    [Fact]
    public void ConvertMarkdown_Headers_ShouldConvertToHTags()
    {
        // Arrange
        var markdown = @"# Heading 1
## Heading 2
### Heading 3
#### Heading 4
##### Heading 5
###### Heading 6";
        
        // Act
        var result = ConvertMarkdown(markdown);
        
        // Assert
        result.Should().Contain("<h1>Heading 1</h1>");
        result.Should().Contain("<h2>Heading 2</h2>");
        result.Should().Contain("<h3>Heading 3</h3>");
        result.Should().Contain("<h4>Heading 4</h4>");
        result.Should().Contain("<h5>Heading 5</h5>");
        result.Should().Contain("<h6>Heading 6</h6>");
    }

    /// <summary>
    /// Test 12: ConfluenceClient_ConvertMarkdown_Lists
    /// </summary>
    [Fact]
    public void ConvertMarkdown_Lists_ShouldConvertToUlLiTags()
    {
        // Arrange
        var markdown = @"* Item 1
* Item 2
* Item 3

1. Numbered 1
2. Numbered 2";
        
        // Act
        var result = ConvertMarkdown(markdown);
        
        // Assert
        result.Should().Contain("<ul>");
        result.Should().Contain("<li>Item 1</li>");
        result.Should().Contain("<li>Numbered 1</li>");
    }

    /// <summary>
    /// Test 13: ConfluenceClient_ConvertMarkdown_Tables
    /// </summary>
    [Fact]
    public void ConvertMarkdown_Tables_ShouldConvertToTableTags()
    {
        // Arrange
        var markdown = @"| Header 1 | Header 2 |
|----------|----------|
| Cell 1   | Cell 2   |";
        
        // Act
        var result = ConvertMarkdown(markdown);
        
        // Assert
        result.Should().Contain("<table>");
        result.Should().Contain("<th>Header 1</th>");
        result.Should().Contain("<td>Cell 1</td>");
    }

    /// <summary>
    /// Test 21: ConfluenceClient_EmptyMarkdown
    /// </summary>
    [Fact]
    public void ConvertMarkdown_EmptyInput_ShouldReturnEmptyString()
    {
        // Arrange
        var markdown = "";
        
        // Act
        var result = ConvertMarkdown(markdown);
        
        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Test 22: ConfluenceClient_SpecialCharacters
    /// </summary>
    [Fact]
    public void ConvertMarkdown_SpecialCharacters_ShouldEscapeProperly()
    {
        // Arrange
        var markdown = "Test <script>alert('xss')</script> and &amp; entity";
        
        // Act
        var result = ConvertMarkdown(markdown);
        
        // Assert - XML special chars should be escaped
        result.Should().Contain("&lt;script&gt;");
        result.Should().Contain("&amp;amp;");
    }

    /// <summary>
    /// Helper to access private ConvertMarkdownToStorageFormat method via reflection
    /// </summary>
    private string ConvertMarkdown(string markdown)
    {
        var method = typeof(ConfluenceClient).GetMethod(
            "ConvertMarkdownToStorageFormat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        return (string)method!.Invoke(_client, new object[] { markdown })!;
    }
}
