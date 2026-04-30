namespace ChatRagApp.Models;

public class AgentResponse
{
    public string Content { get; set; } = string.Empty;
    public TokenUsage TokenUsage { get; set; } = TokenUsage.Empty;
    public List<string> SuggestedQuestions { get; set; } = [];
}
