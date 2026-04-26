using ChatRagApp.Models;

namespace ChatRagApp.Agents;

public interface IAgent
{
    string Id { get; }
    string Name { get; }
    string LlmModel { get; }

    Task<AgentResponse> ChatAsync(string userMessage, string sessionId, string userId, CancellationToken ct = default);
    Task<TokenUsage> MeasureTokensAsync(string text, CancellationToken ct = default);
    Task<List<ImageSearchResult>> SearchImagesAsync(string query, int topK = 5, CancellationToken ct = default);
    Task<List<string>> PredictNextQuestionsAsync(string context, CancellationToken ct = default);
    Task<string> GenerateInstructionAsync(string featureDescription, CancellationToken ct = default);
}
