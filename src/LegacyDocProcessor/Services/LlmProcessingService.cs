using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using LegacyDocProcessor.Configuration;
using LegacyDocProcessor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace LegacyDocProcessor.Services;

/// <summary>
/// Service for processing document content through an LLM
/// </summary>
public interface ILlmProcessingService
{
    Task<ProcessedKnowledge> ProcessContentAsync(ExtractedContent content, CancellationToken cancellationToken = default);
    Task<List<ProcessedKnowledge>> ProcessBatchAsync(List<ExtractedContent> contents, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Processes document content using an LLM API (OpenAI-compatible)
/// </summary>
public class LlmProcessingService : ILlmProcessingService
{
    private readonly HttpClient _httpClient;
    private readonly LlmConfig _config;
    private readonly ILogger _logger;
    private readonly int _maxRetries = 3;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);

    public LlmProcessingService(LlmConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(2)
        };
        
        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }
        
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Process a single document through the LLM
    /// </summary>
    public async Task<ProcessedKnowledge> ProcessContentAsync(ExtractedContent content, CancellationToken cancellationToken = default)
    {
        _logger.Information("Processing content from {FileName} with LLM", content.FileName);
        
        var prompt = BuildPrompt(content);
        
        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var response = await SendLlmRequestAsync(prompt, cancellationToken);
                var processed = ParseLlmResponse(content, response);
                
                _logger.Information("Successfully processed {FileName}, extracted {TopicCount} topics",
                    content.FileName, processed.Topics.Count);
                
                return processed;
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries)
            {
                _logger.Warning("LLM request failed (attempt {Attempt}/{Max}): {Error}. Retrying...",
                    attempt, _maxRetries, ex.Message);
                await Task.Delay(_retryDelay * attempt, cancellationToken);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process {FileName} after {Attempts} attempts", 
                    content.FileName, attempt);
                    
                if (attempt == _maxRetries)
                {
                    return CreateErrorResult(content, ex.Message);
                }
                
                await Task.Delay(_retryDelay * attempt, cancellationToken);
            }
        }
        
        return CreateErrorResult(content, "Max retries exceeded");
    }

    /// <summary>
    /// Process multiple documents in batch
    /// </summary>
    public async Task<List<ProcessedKnowledge>> ProcessBatchAsync(
        List<ExtractedContent> contents, 
        IProgress<string>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        _logger.Information("Starting batch processing of {Count} documents", contents.Count);
        
        var results = new List<ProcessedKnowledge>();
        var semaphore = new SemaphoreSlim(_config.MaxConcurrency > 0 ? _config.MaxConcurrency : 4);
        
        var tasks = contents.Select(async content =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                progress?.Report($"Processing: {content.FileName}");
                
                // Apply rate limiting delay
                if (_config.LlmDelayMs > 0)
                {
                    await Task.Delay(_config.LlmDelayMs, cancellationToken);
                }
                
                var result = await ProcessContentAsync(content, cancellationToken);
                
                lock (results)
                {
                    results.Add(result);
                }
                
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        
        var successCount = results.Count(r => !string.IsNullOrEmpty(r.Summary));
        _logger.Information("Batch processing complete: {Success}/{Total} successful",
            successCount, contents.Count);
        
        return results;
    }

    /// <summary>
    /// Build the prompt for the LLM, including language translation instruction
    /// </summary>
    private string BuildPrompt(ExtractedContent content)
    {
        var truncatedContent = content.TextContent.Length > 150000
            ? content.TextContent.Substring(0, 150000) + "\n\n[Content truncated...]"
            : content.TextContent;
            
        var outputLanguage = _config.OutputLanguage ?? "English";
        
        return $@"{_config.SystemPrompt}

TARGET LANGUAGE: {outputLanguage}

Document: {content.FileName}
File Type: {content.FileType}

Content:
{truncatedContent}

Please analyze the above document and translate ALL output fields to {outputLanguage}. Provide your response in the following JSON format:
```json
{{
  ""suggestedTitle"": ""A clear, descriptive title for this document in {outputLanguage}"",
  ""topics"": [
    {{
      ""topic"": ""Main topic or theme in {outputLanguage}"",
      ""relevance"": 0.0-1.0,
      ""summary"": ""Brief 1-2 sentence summary of this topic in {outputLanguage}"",
      ""content"": ""The relevant content related to this topic (4-6 paragraphs) in {outputLanguage}""
    }}
  ],
  ""summary"": ""A comprehensive 2-3 sentence summary of the entire document in {outputLanguage}"",
  ""technicalDetails"": [""Key technical details or specifications in {outputLanguage}""],
  ""actionItems"": [""Any action items, decisions, or next steps mentioned in {outputLanguage}""],
  ""questions"": [""Any questions or uncertainties raised in the document in {outputLanguage}""]
}}
```

IMPORTANT: Every field in your response must be in {outputLanguage}. This includes topic names, summaries, content paragraphs, technical details, action items, and questions.";
    }

    /// <summary>
    /// Send request to the LLM API
    /// </summary>
    private async Task<string> SendLlmRequestAsync(string prompt, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _config.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = _config.MaxTokens,
            temperature = _config.Temperature,
            response_format = new { type = "json_object" }
        };
        
        var json = JsonConvert.SerializeObject(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync("chat/completions", httpContent, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LLM API returned {response.StatusCode}: {errorContent}");
        }
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObj = JObject.Parse(responseJson);
        
        // Handle both OpenAI and Ollama response formats
        var choices = responseObj["choices"];
        if (choices != null && choices.HasValues)
        {
            var firstChoice = choices[0];
            var message = firstChoice["message"];
            if (message != null)
            {
                return message["content"]?.ToString() ?? string.Empty;
            }
            
            var text = firstChoice["text"];
            if (text != null)
            {
                return text.ToString() ?? string.Empty;
            }
        }
        
        throw new InvalidOperationException("Unexpected LLM response format");
    }

    /// <summary>
    /// Parse the LLM JSON response into ProcessedKnowledge
    /// </summary>
    private ProcessedKnowledge ParseLlmResponse(ExtractedContent content, string llmResponse)
    {
        var result = new ProcessedKnowledge
        {
            FilePath = content.FilePath,
            ProcessedAt = DateTime.UtcNow,
            RawLlmResponse = llmResponse
        };
        
        try
        {
            // Try to extract JSON from response (in case there's extra text)
            var jsonStart = llmResponse.IndexOf('{');
            var jsonEnd = llmResponse.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = llmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var jsonDoc = JObject.Parse(jsonStr);
                
                var title = jsonDoc["suggestedTitle"]?.ToString();
                if (!string.IsNullOrEmpty(title))
                {
                    result.SuggestedTitle = title;
                }
                
                var summary = jsonDoc["summary"]?.ToString();
                if (!string.IsNullOrEmpty(summary))
                {
                    result.Summary = summary;
                }
                
                var topics = jsonDoc["topics"];
                if (topics != null)
                {
                    foreach (var topic in topics)
                    {
                        var topicInfo = new TopicInfo
                        {
                            Topic = topic["topic"]?.ToString() ?? "",
                            Relevance = topic["relevance"]?.Value<double>() ?? 0.5,
                            Summary = topic["summary"]?.ToString() ?? "",
                            Content = topic["content"]?.ToString() ?? ""
                        };
                        
                        if (!string.IsNullOrEmpty(topicInfo.Topic))
                        {
                            result.Topics.Add(topicInfo);
                        }
                    }
                }
                
                var techDetails = jsonDoc["technicalDetails"];
                if (techDetails != null)
                {
                    result.TechnicalDetails = techDetails.Values<string>()
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }
                
                var actionItems = jsonDoc["actionItems"];
                if (actionItems != null)
                {
                    result.ActionItems = actionItems.Values<string>()
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }
                
                var questions = jsonDoc["questions"];
                if (questions != null)
                {
                    result.Questions = questions.Values<string>()
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }
            }
            else
            {
                _logger.Warning("No JSON found in LLM response for {File}", content.FileName);
                result.SuggestedTitle = content.FileName;
                result.Summary = "Failed to parse LLM response";
            }
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse LLM JSON response for {File}", content.FileName);
            result.SuggestedTitle = content.FileName;
            result.Summary = $"JSON parsing error: {ex.Message}";
        }
        
        // Ensure we have a title
        if (string.IsNullOrEmpty(result.SuggestedTitle))
        {
            result.SuggestedTitle = content.FileName;
        }
        
        return result;
    }

    /// <summary>
    /// Create an error result when processing fails
    /// </summary>
    private ProcessedKnowledge CreateErrorResult(ExtractedContent content, string errorMessage)
    {
        return new ProcessedKnowledge
        {
            FilePath = content.FilePath,
            SuggestedTitle = content.FileName,
            Summary = $"Processing failed: {errorMessage}",
            RawLlmResponse = string.Empty,
            ProcessedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
