using ChatRagApp.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChatRagApp.Services;

public class ImageSeeder
{
    private readonly IQdrantService _qdrant;
    private readonly IEmbeddingService _embedding;
    private readonly IImageService _imageService;
    private readonly ILogger<ImageSeeder> _logger;
    private readonly string _trainedImgPath;

    public ImageSeeder(IQdrantService qdrant, IEmbeddingService embedding, IImageService imageService, ILogger<ImageSeeder> logger, string trainedImgPath)
    {
        _qdrant = qdrant;
        _embedding = embedding;
        _imageService = imageService;
        _logger = logger;
        _trainedImgPath = trainedImgPath;
    }

    public async Task SeedImagesIfEmptyAsync(CancellationToken ct = default)
    {
        // Ensure 'images' collection exists, create if not
        if (_qdrant is QdrantService qdrantService)
        {
            var exists = await qdrantService.CollectionExistsAsync("images", ct);
            if (!exists)
            {
                await qdrantService.EnsureNamedVectorCollectionIfNotExistsAsync("images", ct);
                _logger.LogInformation("Created 'images' collection in Qdrant.");
            }
        }

        _logger.LogInformation("Upserting all images from TrainedImg...");
        var categories = new[]
        {
            ("cats", "Cats"),
            ("dogs", "Dogs"),
            ("wild_animals", "Animals")
        };
        foreach (var (folder, category) in categories)
        {
            var dir = Path.Combine(_trainedImgPath, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir))
            {
                var fileName = Path.GetFileName(file);
                var imagePoint = new ImagePoint
                {
                    Id = Guid.NewGuid(),
                    FileName = fileName,
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    Category = category,
                    Description = $"Seeded image for {category}",
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true
                };
                var textContent = $"{imagePoint.Title} {imagePoint.Category} {imagePoint.Description}";
                var textEmbedding = await _embedding.GenerateEmbeddingAsync(textContent, ct);
                var imageEmbedding = await _embedding.GenerateEmbeddingAsync(textContent + " [image]", ct);
                await _qdrant.UpsertImageAsync(imagePoint, textEmbedding, imageEmbedding, ct);
                _logger.LogInformation("Seeded image: {FileName}", fileName);
            }
        }
        _logger.LogInformation("Image seeding complete.");
    }
}
