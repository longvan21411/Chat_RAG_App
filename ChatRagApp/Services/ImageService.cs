using ChatRagApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace ChatRagApp.Services;

public class ImageService : IImageService
{
    private static readonly HashSet<string> AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int EmbeddingDimension = 1536;
    private const int ImageGridColumns = 24;
    private const int ImageGridRows = 16;
    private const int ImageWidth = 192;
    private const int ImageHeight = 128;

    private readonly IQdrantService _qdrant;
    private readonly ILogger<ImageService> _logger;
    private readonly string _wwwrootPath;

    public ImageService(IQdrantService qdrant, ILogger<ImageService> logger, IWebHostEnvironment env)
    {
        _qdrant = qdrant;
        _logger = logger;
        _wwwrootPath = env.WebRootPath;
        _logger.LogInformation("ImageService initialized with deterministic local text and image embeddings.");
    }

    public async Task<List<ImageUploadResult>> UploadBulkAsync(
        IEnumerable<IFormFile> files, ImagePoint metadata, CancellationToken ct = default)
    {
        var results = new List<ImageUploadResult>();

        foreach (var file in files)
        {
            var result = await UploadSingleAsync(file, metadata, ct);
            results.Add(result);
        }

        return results;
    }

    private async Task<ImageUploadResult> UploadSingleAsync(IFormFile file, ImagePoint metadata, CancellationToken ct)
    {
        try
        {
            // Validate extension
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return new ImageUploadResult { Success = false, FileName = file.FileName, Error = $"Extension {ext} not allowed." };

            // Validate size
            if (file.Length > MaxFileSizeBytes)
                return new ImageUploadResult { Success = false, FileName = file.FileName, Error = "File exceeds 10 MB limit." };

            // Sanitize filename
            var safeFileName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ext;
            var safeCategory = SanitizePath(metadata.Category);

            var uploadDir = Path.Combine(_wwwrootPath, "uploads", "images", safeCategory);
            Directory.CreateDirectory(uploadDir);
            var destPath = Path.Combine(uploadDir, safeFileName);

            await using (var stream = new FileStream(destPath, FileMode.Create))
                await file.CopyToAsync(stream, ct);

            var imagePoint = new ImagePoint
            {
                Id = Guid.NewGuid(),
                FileName = safeFileName,
                Title = metadata.Title,
                Category = safeCategory,
                Description = metadata.Description,
                CreatedDate = DateTime.UtcNow,
                IsActive = true
            };

            // Use deterministic local embeddings so image uploads do not depend on external model services.
            var textContent = $"{imagePoint.Title} {imagePoint.Category} {imagePoint.Description}";
            var textEmbedding = ComputeTextEmbeddingApprox(textContent);

            var imageEmbedding = ComputeImageEmbedding(destPath);

            await _qdrant.UpsertImageAsync(imagePoint, textEmbedding, imageEmbedding, ct);

            return new ImageUploadResult { Success = true, FileName = safeFileName, PointId = imagePoint.Id };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FileName}", file.FileName);
            return new ImageUploadResult { Success = false, FileName = file.FileName, Error = ex.Message };
        }
    }

    public async Task<List<ImageSearchResult>> SearchByTextAsync(string query, int topK = 10, CancellationToken ct = default)
    {
        // Text queries are projected into the same deterministic vector space used during image upserts.
        var queryEmbedding = ComputeTextEmbeddingApprox(query);
        return await _qdrant.SearchImagesByTextAsync(queryEmbedding, topK, ct);
    }

    public async Task<List<ImageSearchResult>> GetImagesByCategoryAsync(string category, int topK = 24, CancellationToken ct = default)
    {
        try
        {
            return await _qdrant.GetImagesByCategoryAsync(category, topK, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetImagesByCategoryAsync failed for {Category}", category);
            return new List<ImageSearchResult>();
        }
    }

    public async Task<List<ImageSearchResult>> SearchByImageAsync(Guid imageId, int topK = 10, CancellationToken ct = default)
    {
        try
        {
            var image = await GetImagePointByIdAsync(imageId, ct);
            if (image == null) return new List<ImageSearchResult>();
            var path = Path.Combine(_wwwrootPath, "uploads", "images", image.Category, image.FileName);
            if (!File.Exists(path)) return new List<ImageSearchResult>();
            var emb = ComputeImageEmbedding(path);
            return await _qdrant.SearchImagesByImageAsync(emb, topK, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SearchByImageAsync failed for {ImageId}", imageId);
            return new List<ImageSearchResult>();
        }
    }


    public async Task<List<ImagePoint>> GetAllActiveImagesAsync(CancellationToken ct = default)
    {
        // Returns images via search with a generic query
        var results = await SearchByTextAsync("image", 100, ct);
        return results.Select(r => new ImagePoint
        {
            Id = r.Id,
            FileName = r.FileName,
            Title = r.Title,
            Category = r.Category,
            Description = r.Description
        }).ToList();
    }

    public async Task<ImagePoint?> GetImagePointByIdAsync(Guid id, CancellationToken ct = default)
    {
        var all = await GetAllActiveImagesAsync(ct);
        return all.FirstOrDefault(i => i.Id == id);
    }

    private static string SanitizePath(string input)
    {
        var safe = Path.GetInvalidFileNameChars()
            .Aggregate(input, (s, c) => s.Replace(c, '_'))
            .Replace("..", "_")
            .Trim();
        return string.IsNullOrEmpty(safe) ? "general" : safe;
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
