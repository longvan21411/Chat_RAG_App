namespace ChatRagApp.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SessionId { get; set; } = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;   // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string AgentId { get; set; } = string.Empty;
    public TokenUsage TokenUsage { get; set; } = TokenUsage.Empty;
}
