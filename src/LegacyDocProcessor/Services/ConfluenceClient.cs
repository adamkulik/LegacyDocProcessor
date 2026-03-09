using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Client for interacting with Confluence REST API
/// </summary>
public class ConfluenceClient
{
    private readonly HttpClient _httpClient;
    private readonly ConfluenceConfig _config;
    private readonly ILogger _logger;
    
    public ConfluenceClient(ConfluenceConfig config)
    {
        _config = config;
        _logger = Log.ForContext<ConfluenceClient>();
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/wiki/rest/api/")
        };
        
        ConfigureAuthentication();
    }
    
    private void ConfigureAuthentication()
    {
        var authType = _config.AuthType.ToLowerInvariant();
        
        switch (authType)
        {
            case "basicauth":
            case "apitoken":
                if (!string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.PasswordOrToken))
                {
                    var credentials = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{_config.Username}:{_config.PasswordOrToken}")
                    );
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new AuthenticationHeaderValue("Basic", credentials);
                }
                break;
                
            case "oauth":
            case "saml":
                if (!string.IsNullOrEmpty(_config.SessionToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new AuthenticationHeaderValue("Bearer", _config.SessionToken);
                }
                break;
                
            default:
                _logger.Warning("Unknown Confluence auth type: {AuthType}, trying session token", _config.AuthType);
                if (!string.IsNullOrEmpty(_config.SessionToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new AuthenticationHeaderValue("Bearer", _config.SessionToken);
                }
                break;
        }
        
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }
    
    /// <summary>
    /// Test connection to Confluence
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("space");
            if (response.IsSuccessStatusCode)
            {
                _logger.Information("Successfully connected to Confluence at {BaseUrl}", _config.BaseUrl);
                return true;
            }
            
            _logger.Warning("Confluence connection test failed: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to connect to Confluence at {BaseUrl}", _config.BaseUrl);
            return false;
        }
    }
    
    /// <summary>
    /// Get the Confluence space information
    /// </summary>
    public async Task<string?> GetSpaceAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"space/{_config.SpaceKey}?expand=description");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("name").GetString();
            }
            
            _logger.Warning("Failed to get space {SpaceKey}: {StatusCode}", _config.SpaceKey, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting space {SpaceKey}", _config.SpaceKey);
            return null;
        }
    }
    
    /// <summary>
    /// Find a page by title in the target space
    /// </summary>
    public async Task<ConfluencePageInfo?> FindPageByTitleAsync(string title)
    {
        try
        {
            var encodedTitle = Uri.EscapeDataString(title);
            var response = await _httpClient.GetAsync(
                $"content?spaceKey={_config.SpaceKey}&title={encodedTitle}&limit=1"
            );
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                var results = doc.RootElement.GetProperty("results");
                if (results.GetArrayLength() > 0)
                {
                    var page = results[0];
                    return new ConfluencePageInfo
                    {
                        Id = page.GetProperty("id").GetString() ?? "",
                        Title = page.GetProperty("title").GetString() ?? "",
                        Version = page.GetProperty("version").GetProperty("number").GetInt32()
                    };
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error finding page with title: {Title}", title);
            return null;
        }
    }
    
    /// <summary>
    /// Create a new Confluence page
    /// </summary>
    public async Task<string?> CreatePageAsync(ConfluencePage page)
    {
        try
        {
            var payload = new
            {
                type = "page",
                title = page.Title,
                space = new { key = page.SpaceKey },
                body = new
                {
                    storage = new
                    {
                        value = ConvertMarkdownToStorageFormat(page.Content),
                        representation = "storage"
                    }
                },
                metadata = new
                {
                    properties = new
                    {
                        content = new { value = page.Labels }
                    }
                }
            };
            
            // Add parent if specified
            if (!string.IsNullOrEmpty(page.ParentPageId))
            {
                var parentPayload = JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(payload)
                );
                
                payload = new
                {
                    type = "page",
                    title = page.Title,
                    space = new { key = page.SpaceKey },
                    body = new
                    {
                        storage = new
                        {
                            value = ConvertMarkdownToStorageFormat(page.Content),
                            representation = "storage"
                        }
                    },
                    metadata = new
                    {
                        properties = new
                        {
                            content = new { value = page.Labels }
                        }
                    }
                };
            }
            
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("content", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                var pageId = doc.RootElement.GetProperty("id").GetString();
                _logger.Information("Created Confluence page: {Title} (ID: {PageId})", page.Title, pageId);
                return pageId;
            }
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.Error("Failed to create page {Title}: {StatusCode} - {Error}", 
                page.Title, response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error creating page: {Title}", page.Title);
            return null;
        }
    }
    
    /// <summary>
    /// Update an existing Confluence page
    /// </summary>
    public async Task<bool> UpdatePageAsync(string pageId, ConfluencePage page, int currentVersion)
    {
        try
        {
            var payload = new
            {
                id = pageId,
                type = "page",
                title = page.Title,
                version = new { number = currentVersion + 1, message = $"Updated by LegacyDocProcessor" },
                body = new
                {
                    storage = new
                    {
                        value = ConvertMarkdownToStorageFormat(page.Content),
                        representation = "storage"
                    }
                }
            };
            
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"content/{pageId}", content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Information("Updated Confluence page: {Title} (ID: {PageId})", page.Title, pageId);
                return true;
            }
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.Error("Failed to update page {Title}: {StatusCode} - {Error}", 
                page.Title, response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error updating page: {Title} (ID: {PageId})", page.Title, pageId);
            return false;
        }
    }
    
    /// <summary>
    /// Publish a page (create or update if exists)
    /// </summary>
    public async Task<string?> PublishPageAsync(ConfluencePage page)
    {
        page.SpaceKey = string.IsNullOrEmpty(page.SpaceKey) ? _config.SpaceKey : page.SpaceKey;
        
        var existingPage = await FindPageByTitleAsync(page.Title);
        
        if (existingPage != null)
        {
            _logger.Information("Page '{Title}' exists (ID: {Id}, Version: {Version}), updating...", 
                existingPage.Title, existingPage.Id, existingPage.Version);
            
            var success = await UpdatePageAsync(existingPage.Id, page, existingPage.Version);
            return success ? existingPage.Id : null;
        }
        
        _logger.Information("Creating new page: {Title}", page.Title);
        return await CreatePageAsync(page);
    }
    
    /// <summary>
    /// Convert Markdown to Confluence storage format
    /// </summary>
    private string ConvertMarkdownToStorageFormat(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;
            
        var result = markdown;
        
        // Escape XML special characters first
        result = result.Replace("&", "&amp;")
                      .Replace("<", "&lt;")
                      .Replace(">", "&gt;");
        
        // Convert Markdown headings to Confluence format
        result = Regex.Replace(result, @"^######\s+(.+)$", "<h6>$1</h6>", RegexOptions.Multiline);
        result = Regex.Replace(result, @"^#####\s+(.+)$", "<h5>$1</h5>", RegexOptions.Multiline);
        result = Regex.Replace(result, @"^####\s+(.+)$", "<h4>$1</h4>", RegexOptions.Multiline);
        result = Regex.Replace(result, @"^###\s+(.+)$", "<h3>$1</h3>", RegexOptions.Multiline);
        result = Regex.Replace(result, @"^##\s+(.+)$", "<h2>$1</h2>", RegexOptions.Multiline);
        result = Regex.Replace(result, @"^#\s+(.+)$", "<h1>$1</h1>", RegexOptions.Multiline);
        
        // Convert bold and italic
        result = Regex.Replace(result, @"\*\*\*(.+?)\*\*\*", "<strong><em>$1</em></strong>");
        result = Regex.Replace(result, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        result = Regex.Replace(result, @"\*(.+?)\*", "<em>$1</em>");
        result = Regex.Replace(result, @"__(.+?)__", "<strong>$1</strong>");
        result = Regex.Replace(result, @"_(.+?)_", "<em>$1</em>");
        
        // Convert code blocks (```language ... ```)
        result = Regex.Replace(result, @"```(\w*)\n([\s\S]*?)```", 
            "<ac:code-block language=\"$1\">$2</ac:code-block>");
        
        // Convert inline code
        result = Regex.Replace(result, @"`([^`]+)`", "<code>$1</code>");
        
        // Convert horizontal rules
        result = Regex.Replace(result, @"^---+$", "<hr/>", RegexOptions.Multiline);
        result = Regex.Replace(result, @"^\*\*\*+$", "<hr/>", RegexOptions.Multiline);
        
        // Convert lists (unordered)
        result = Regex.Replace(result, @"^[\*\-\+]\s+(.+)$", "<li>$1</li>", RegexOptions.Multiline);
        
        // Convert numbered lists
        result = Regex.Replace(result, @"^\d+\.\s+(.+)$", "<li>$1</li>", RegexOptions.Multiline);
        
        // Wrap consecutive <li> elements in <ul>
        result = Regex.Replace(result, @"(<li>.*?</li>\n?)+", match =>
        {
            var content = match.Value;
            if (content.Contains("<li>"))
            {
                return "<ul>" + content + "</ul>";
            }
            return content;
        });
        
        // Convert blockquotes
        result = Regex.Replace(result, @"^>\s+(.+)$", "<blockquote>$1</blockquote>", RegexOptions.Multiline);
        
        // Convert links [text](url)
        result = Regex.Replace(result, @"\[([^\]]+)\]\(([^)]+)\)", 
            "<a href=\"$2\">$1</a>");
        
        // Convert images ![alt](url)
        result = Regex.Replace(result, @"!\[([^\]]*)\]\(([^)]+)\)", 
            "<ac:image><ri:url ri:value=\"$2\"/><ac:alt>$1</ac:alt></ac:image>");
        
        // Convert tables (basic support)
        result = ConvertTables(result);
        
        // Convert paragraphs (double newlines)
        result = Regex.Replace(result, @"\n\n+", "</p><p>");
        result = "<p>" + result + "</p>";
        
        // Clean up empty paragraphs
        result = result.Replace("<p></p>", "");
        result = result.Replace("<p><br></p>", "");
        
        return result;
    }
    
    /// <summary>
    /// Convert Markdown tables to Confluence format
    /// </summary>
    private string ConvertTables(string markdown)
    {
        var result = new StringBuilder();
        var lines = markdown.Split('\n');
        var inTable = false;
        var headerProcessed = false;
        
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("|") && line.TrimEnd().EndsWith("|"))
            {
                if (!inTable)
                {
                    inTable = true;
                    result.Append("<table><tbody>");
                }
                
                // Check if it's a separator line (|---|)
                if (line.Contains("---"))
                {
                    if (!headerProcessed)
                    {
                        headerProcessed = true;
                    }
                    continue;
                }
                
                var cells = line.Split('|', StringSplitOptions.TrimEntries)
                               .Where(c => c.Length > 0)
                               .ToList();
                
                if (cells.Count > 0)
                {
                    var tag = headerProcessed ? "td" : "th";
                    result.Append("<tr>");
                    foreach (var cell in cells)
                    {
                        result.Append($"<{tag}>{cell}</{tag}>");
                    }
                    result.Append("</tr>");
                    
                    if (!headerProcessed)
                    {
                        headerProcessed = true;
                    }
                }
            }
            else
            {
                if (inTable)
                {
                    result.Append("</tbody></table>");
                    inTable = false;
                    headerProcessed = false;
                }
                result.Append(line);
            }
        }
        
        if (inTable)
        {
            result.Append("</tbody></table>");
        }
        
        return result.ToString();
    }
    
    /// <summary>
    /// Get page content in storage format
    /// </summary>
    public async Task<string?> GetPageContentAsync(string pageId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"content/{pageId}?expand=body.storage");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement
                    .GetProperty("body")
                    .GetProperty("storage")
                    .GetProperty("value")
                    .GetString();
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting page content for ID: {PageId}", pageId);
            return null;
        }
    }
    
    /// <summary>
    /// Add a label to a page
    /// </summary>
    public async Task<bool> AddLabelAsync(string pageId, string label)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"content/{pageId}/label",
                new StringContent($"\"{label}\"", Encoding.UTF8, "application/json")
            );
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error adding label {Label} to page {PageId}", label, pageId);
            return false;
        }
    }
}

/// <summary>
/// Information about an existing Confluence page
/// </summary>
public class ConfluencePageInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
}
