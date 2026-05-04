using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ChatRagAppConfiguration;

#pragma warning disable SKEXP0010

namespace ChatRagApp.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _generator;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly int _dimension;

    public bool IsConfigured => _generator is not null;

    public EmbeddingService(ILogger<EmbeddingService> logger)
    {
        _logger = logger;
       
        var config = ChatRagAppConfigurationInfo.GetChatRagAppConfigurationInfo();
        var tokenKey = config.GithubToken;
        var model = "text-embedding-3-small";        
         _dimension = 1536;

        _logger.LogInformation("Initializing embedding service with model {Model}", model);
        _logger.LogInformation("GitHub Token configured: {TokenKey}", !string.IsNullOrWhiteSpace(tokenKey));

        if (!string.IsNullOrWhiteSpace(tokenKey))
        {
            try
            {
                var kernel = Kernel.CreateBuilder()
                    .AddOpenAIEmbeddingGenerator(model, tokenKey)
                    .Build();
                _generator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
                _logger.LogInformation("Embedding service initialized with model {Model}", model);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize embedding service using deterministic fallback");
            }
        }
        else
        {
            _logger.LogWarning("GitHub Token key not configured using deterministic embeddings");
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (_generator is null)
            return GenerateDeterministicEmbedding(text, _dimension);

        try
        {
            var results = await _generator.GenerateAsync([text], cancellationToken: ct);
            return results[0].Vector.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding generation failed, using deterministic fallback");
            return GenerateDeterministicEmbedding(text, _dimension);
        }
    }

    private static float[] GenerateDeterministicEmbedding(string text, int dimension)
    {
        var seed = text.Aggregate(0, (acc, c) => HashCode.Combine(acc, c));
        var rng = new Random(seed);
        var values = new float[dimension];
        double magnitude = 0;
        for (int i = 0; i < dimension; i++)
        {
            values[i] = (float)(rng.NextDouble() * 2 - 1);
            magnitude += values[i] * (double)values[i];
        }
        magnitude = Math.Sqrt(magnitude);
        if (magnitude > 0)
            for (int i = 0; i < dimension; i++)
                values[i] /= (float)magnitude;
        return values;
    }
}
