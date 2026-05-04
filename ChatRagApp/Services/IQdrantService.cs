using ChatRagApp.Models;

namespace ChatRagApp.Services;

public interface IQdrantService
{
    Task InitializeCollectionsAsync(CancellationToken ct = default);
    Task UpsertUserAsync(AppUser user, CancellationToken ct = default);
    Task<AppUser?> GetUserByEmailAsync(string email, CancellationToken ct = default);
    Task<AppUser?> GetUserByUserNameAsync(string userName, CancellationToken ct = default);

    Task SaveChatMessageAsync(ChatMessage message, float[] embedding, CancellationToken ct = default);
    Task<List<ChatMessage>> GetRecentChatMessagesAsync(string userId, string agentId, int limit = 5, CancellationToken ct = default);
    Task<List<ChatMessage>> GetAllChatMessagesBySessionIdAsync(string sessionId, CancellationToken ct = default); // For chat history by session
    Task<List<ChatMessage>> GetAllChatMessagesAsync(CancellationToken ct = default); // Fetch all chat messages

    Task UpsertImageAsync(ImagePoint image, float[] textEmbedding, float[] imageEmbedding, CancellationToken ct = default);
    Task<List<ImageSearchResult>> SearchImagesByTextAsync(float[] queryEmbedding, int topK = 10, CancellationToken ct = default);
    Task<List<ImageSearchResult>> SearchImagesByImageAsync(float[] queryEmbedding, int topK = 10, CancellationToken ct = default);
    Task<List<ImageSearchResult>> GetImagesByCategoryAsync(string category, int topK = 24, CancellationToken ct = default);

    Task<DailyReport> GetDailyReportAsync(DateTime date, CancellationToken ct = default);
}
