using ChatRagApp.Models;

namespace ChatRagApp.Services;

public class ChatHistoryService : IChatHistoryService
{
    private readonly IQdrantService _qdrant;
    private readonly IEmbeddingService _embedding;

    public ChatHistoryService(IQdrantService qdrant, IEmbeddingService embedding)
    {
        _qdrant = qdrant;
        _embedding = embedding;
    }

    public async Task SaveMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        var embedding = await _embedding.GenerateEmbeddingAsync(message.Content, ct);
        await _qdrant.SaveChatMessageAsync(message, embedding, ct);
    }

    public async Task<List<ChatMessage>> GetRecentMessagesAsync(string userId, string agentId, int limit = 5, CancellationToken ct = default)
        => await _qdrant.GetRecentChatMessagesAsync(userId, agentId, limit, ct);

    public async Task<List<ChatMessage>> GetHistoryBySessionIdAsync(string sessionId, CancellationToken ct = default)
    {
        // Assuming _qdrant has a method to get messages by sessionId, otherwise filter manually
        var allMessages = await _qdrant.GetAllChatMessagesBySessionIdAsync(sessionId, ct);
        return allMessages.OrderBy(m => m.Timestamp).ToList();
    }

    public async Task<List<ChatMessage>> GetAllHistoryAsync(CancellationToken ct = default)
    {
        return await _qdrant.GetAllChatMessagesAsync(ct);
    }
}
