namespace ChatRagApp.Models;

public class ImageUploadResult
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? Error { get; set; }
    public Guid PointId { get; set; }
}
