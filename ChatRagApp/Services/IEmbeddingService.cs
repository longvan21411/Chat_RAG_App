namespace ChatRagApp.Services;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    bool IsConfigured { get; }
}
