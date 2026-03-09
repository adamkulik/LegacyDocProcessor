using Aspose.Email;
using LegacyDocProcessor.Models;
using MsgReader.Outlook;

namespace LegacyDocProcessor.Services.Extractors.Implementations;

/// <summary>
/// Extracts text and metadata from .msg Outlook message files
/// </summary>
public class MsgExtractor : BaseTextExtractor
{
    public MsgExtractor()
    {
        // No logger needed - BaseTextExtractor handles responses
    }
    
    public override string FileType => "msg";
    
    public override async Task<ExtractedContent> ExtractAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var msg = new Storage.Message(filePath);
                
                var metadata = new List<string>();
                var content = new List<string>();
                
                // Extract email metadata - Sender is a Sender object, get display name
                if (msg.Sender != null)
                    metadata.Add($"From: {msg.Sender.DisplayName}");
                if (msg.Sender != null && !string.IsNullOrEmpty(msg.Sender.Email))
                    metadata.Add($"Sender: {msg.Sender.Email}");
                if (msg.SentOn.HasValue)
                    metadata.Add($"Sent: {msg.SentOn.Value:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrEmpty(msg.Subject))
                {
                    metadata.Add($"Subject: {msg.Subject}");
                    content.Add($"Subject: {msg.Subject}");
                }
                if (!string.IsNullOrEmpty(msg.BodyText))
                    content.Add(msg.BodyText);
                else if (!string.IsNullOrEmpty(msg.BodyHtml))
                    content.Add(msg.BodyHtml); // Include HTML as fallback
                
                // Recipients
                if (msg.Recipients != null && msg.Recipients.Count > 0)
                {
                    var recipients = string.Join(", ", msg.Recipients.Select(r => r.DisplayName ?? r.Email ?? "Unknown"));
                    metadata.Add($"To: {recipients}");
                }
                
                // Attachments - cast to Attachment type to access FileName
                if (msg.Attachments != null && msg.Attachments.Count > 0)
                {
                    var attachments = string.Join(", ", msg.Attachments.OfType<Attachment>().Select(a => a.Name ?? "Unknown"));
                    metadata.Add($"Attachments: {attachments}");
                }
                
                var fullContent = string.Join("\n\n", metadata) + "\n\n" + string.Join("\n\n", content);
                
                return CreateSuccessResponse(filePath, fullContent);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(filePath, $"Failed to extract MSG: {ex.Message}");
            }
        });
    }
}
