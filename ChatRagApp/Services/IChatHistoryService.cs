using ChatRagApp.Models;

namespace ChatRagApp.Services;

public interface IChatHistoryService
{
    Task SaveMessageAsync(ChatMessage message, CancellationToken ct = default);
    Task<List<ChatMessage>> GetRecentMessagesAsync(string userId, string agentId, int limit = 5, CancellationToken ct = default);
    Task<List<ChatMessage>> GetHistoryBySessionIdAsync(string sessionId, CancellationToken ct = default); // Added for chat history by session
    Task<List<ChatMessage>> GetAllHistoryAsync(CancellationToken ct = default); // Fetch all chat history
}
