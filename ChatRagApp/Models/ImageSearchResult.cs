namespace ChatRagApp.Models;

public class ImageSearchResult
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float Score { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
}
