namespace ChatRagApp.Models;

public record TokenUsage(int InputTokens, int OutputTokens, int CachedTokens)
{
    public static readonly TokenUsage Empty = new(0, 0, 0);
    public int Total => InputTokens + OutputTokens + CachedTokens;
}
