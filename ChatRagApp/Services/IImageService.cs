using ChatRagApp.Models;

namespace ChatRagApp.Services;

public interface IImageService
{
    Task<List<ImageUploadResult>> UploadBulkAsync(
        IEnumerable<Microsoft.AspNetCore.Http.IFormFile> files,
        ImagePoint metadata,
        CancellationToken ct = default);

    Task<List<ImageSearchResult>> SearchByTextAsync(string query, int topK = 10, CancellationToken ct = default);
    Task<List<ImagePoint>> GetAllActiveImagesAsync(CancellationToken ct = default);
}
