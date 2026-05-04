using ChatRagApp.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ChatRagApp.Services;

public class ImageSeeder
{
    private const int EmbeddingDimension = 1536;
    private const int ImageGridColumns = 24;
    private const int ImageGridRows = 16;
    private const int ImageWidth = 192;
    private const int ImageHeight = 128;

    private readonly IQdrantService _qdrant;
    private readonly ILogger<ImageSeeder> _logger;
    private readonly string _trainedImgPath;

    public ImageSeeder(IQdrantService qdrant, ILogger<ImageSeeder> logger, string trainedImgPath)
    {
        _qdrant = qdrant;
        _logger = logger;
        _trainedImgPath = trainedImgPath;
        _logger.LogInformation("ImageSeeder initialized with trained image path: {TrainedImgPath}. Deterministic local embeddings will be used for seed data.", _trainedImgPath);
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
                    // Seed images into the same deterministic vector space used by the upload/search flow.
                    var textContent = $"{imagePoint.Title} {imagePoint.Category} {imagePoint.Description}";
                    var textEmbedding = ComputeTextEmbeddingApprox(textContent);
                    var imageEmbedding = ComputeImageEmbedding(file);
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

    private static float[] ComputeTextEmbeddingApprox(string text)
    {
        var embedding = new float[EmbeddingDimension];
        if (string.IsNullOrWhiteSpace(text))
        {
            return embedding;
        }

        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), "\\p{L}+|\\p{N}+"))
        {
            var token = match.Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var index = HashToInt(token) % EmbeddingDimension;
            embedding[index] += 1f;
        }

        NormalizeInPlace(embedding);
        return embedding;
    }

    private static float[] ComputeImageEmbedding(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(ImageWidth, ImageHeight),
            Mode = ResizeMode.Crop
        }));

        var embedding = new float[EmbeddingDimension];
        var blockWidth = ImageWidth / ImageGridColumns;
        var blockHeight = ImageHeight / ImageGridRows;
        var offset = 0;

        for (var row = 0; row < ImageGridRows; row++)
        {
            for (var column = 0; column < ImageGridColumns; column++)
            {
                double red = 0;
                double green = 0;
                double blue = 0;
                double alpha = 0;
                var count = 0;

                for (var y = row * blockHeight; y < (row + 1) * blockHeight; y++)
                {
                    for (var x = column * blockWidth; x < (column + 1) * blockWidth; x++)
                    {
                        var pixel = image[x, y];
                        red += pixel.R;
                        green += pixel.G;
                        blue += pixel.B;
                        alpha += pixel.A;
                        count++;
                    }
                }

                if (count == 0)
                {
                    count = 1;
                }

                embedding[offset++] = (float)(red / count / 255d);
                embedding[offset++] = (float)(green / count / 255d);
                embedding[offset++] = (float)(blue / count / 255d);
                embedding[offset++] = (float)(alpha / count / 255d);
            }
        }

        NormalizeInPlace(embedding);
        return embedding;
    }

    private static int HashToInt(string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
    }

    private static void NormalizeInPlace(float[] vector)
    {
        double sum = 0;
        for (var index = 0; index < vector.Length; index++)
        {
            sum += vector[index] * vector[index];
        }

        if (sum <= 0)
        {
            return;
        }

        var norm = Math.Sqrt(sum);
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / norm);
        }
    }
}
