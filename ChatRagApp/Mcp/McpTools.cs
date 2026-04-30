using ChatRagApp.Agents;
using ChatRagApp.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ChatRagApp.Mcp;

[McpServerToolType]
public class McpTools
{
    private readonly IImageService _imageService;
    private readonly IQdrantService _qdrant;
    private readonly IServiceProvider _sp;

    public McpTools(IImageService imageService, IQdrantService qdrant, IServiceProvider sp)
    {
        _imageService = imageService;
        _qdrant = qdrant;
        _sp = sp;
    }

    [McpServerTool(Name = "search_images")]
    [Description("Search for images in the database by text query using semantic similarity.")]
    public async Task<string> SearchImages(
        [Description("The text query to search for images")] string query,
        [Description("Maximum number of results to return (default 5)")] int topK = 5)
    {
        var results = await _imageService.SearchByTextAsync(query, topK);
        if (results.Count == 0)
            return "No images found matching the query.";

        return string.Join("\n", results.Select(r =>
            $"[{r.Score:F3}] {r.Title} ({r.Category}): {r.Description} — {r.FileName}"));
    }

    [McpServerTool(Name = "get_daily_report")]
    [Description("Generate a daily usage report showing images uploaded, searches performed, and token usage.")]
    public async Task<string> GetDailyReport(
        [Description("Date in YYYY-MM-DD format (default: today)")] string? date = null)
    {
        var reportDate = date is not null && DateTime.TryParse(date, out var d) ? d : DateTime.UtcNow;
        var report = await _qdrant.GetDailyReportAsync(reportDate);

        var lines = new List<string>
        {
            $"# Daily Report — {report.Date:yyyy-MM-dd}",
            $"- Images uploaded today: {report.ImagesUploadedToday}",
            $"- Text searches today: {report.TextSearchesToday}",
            $"- Active users today: {report.ActiveUsersToday}",
            "",
            "## Token Usage by Agent"
        };

        foreach (var (agentId, summary) in report.TokenUsageByAgent)
            lines.Add($"- {summary.AgentName}: input={summary.TotalInput}, output={summary.TotalOutput}, cached={summary.TotalCached}, messages={summary.TotalMessages}");

        return string.Join("\n", lines);
    }

    [McpServerTool(Name = "list_agents")]
    [Description("List all available agents and their configurations.")]
    public string ListAgents()
    {
        using var scope = _sp.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<AgentFactory>();
        var agents = factory.GetAllAgents();
        return string.Join("\n", agents.Select(a =>
            $"- [{a.Id}] {a.Name} (model: {a.LlmModel})"));
    }
}
