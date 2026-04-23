namespace ChatRagApp.Agents;

public class AgentConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LlmModel { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 2048;
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
}
