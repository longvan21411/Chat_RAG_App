using ChatRagApp.Models;

namespace ChatRagApp.Services;

public interface IChatHistoryService
{
    Task SaveMessageAsync(ChatMessage message, CancellationToken ct = default);
    Task<List<ChatMessage>> GetRecentMessagesAsync(string userId, string agentId, int limit = 5, CancellationToken ct = default);
}
