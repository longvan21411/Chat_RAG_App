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
        int countofImages = 0;
        int skippedImages = 0;
        _logger.LogInformation("Recursively upserting all images from all subfolders in TrainedImg...");
        if (Directory.Exists(_trainedImgPath))
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
            var files = Directory.GetFiles(_trainedImgPath, "*.*", SearchOption.AllDirectories)
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var dirName = Path.GetDirectoryName(file);
                var category = dirName != null ? Path.GetFileName(dirName) : "Unknown";
                try
                {
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
                    _logger.LogInformation("[Seeded] {FileName} in category {Category} from {Path}", fileName, category, file);
                    countofImages++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Skipped] {FileName} in category {Category} from {Path} due to error", fileName, category, file);
                    skippedImages++;
                }
            }
        }
        _logger.LogInformation("Total images seeded: {Count}", countofImages);
        _logger.LogInformation("Total images skipped: {Count}", skippedImages);
        _logger.LogInformation("Image seeding complete.");
    }
}
