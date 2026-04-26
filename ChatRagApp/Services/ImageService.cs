using ChatRagApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChatRagApp.Services;

public class ImageService : IImageService
{
    private static readonly HashSet<string> AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IQdrantService _qdrant;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<ImageService> _logger;
    private readonly string _basePath;
    private readonly string _wwwrootPath;

    public ImageService(IQdrantService qdrant, IEmbeddingService embedding,
        IConfiguration config, ILogger<ImageService> logger, IWebHostEnvironment env)
    {
        _qdrant = qdrant;
        _embedding = embedding;
        _logger = logger;
        _wwwrootPath = env.WebRootPath;
        _basePath = config["ImageUpload:BasePath"] ?? "wwwroot/uploads/images";
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

            // Generate text embedding from image metadata
            var textContent = $"{imagePoint.Title} {imagePoint.Category} {imagePoint.Description}";
            var textEmbedding = await _embedding.GenerateEmbeddingAsync(textContent, ct);

            // For image embedding: use same text-based embedding as proxy (CLIP would be used in production)
            var imageEmbedding = await _embedding.GenerateEmbeddingAsync(textContent + " [image]", ct);

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
        var queryEmbedding = await _embedding.GenerateEmbeddingAsync(query, ct);
        return await _qdrant.SearchImagesByTextAsync(queryEmbedding, topK, ct);
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

    private static string SanitizePath(string input)
    {
        var safe = Path.GetInvalidFileNameChars()
            .Aggregate(input, (s, c) => s.Replace(c, '_'))
            .Replace("..", "_")
            .Trim();
        return string.IsNullOrEmpty(safe) ? "general" : safe;
    }
}
