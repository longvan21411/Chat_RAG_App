namespace ChatRagApp.Models;

public class DailyReport
{
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public int ImagesUploadedToday { get; set; }
    public int TextSearchesToday { get; set; }
    public int ActiveUsersToday { get; set; }
    public Dictionary<string, TokenUsageSummary> TokenUsageByAgent { get; set; } = [];
}

public class TokenUsageSummary
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public int TotalInput { get; set; }
    public int TotalOutput { get; set; }
    public int TotalCached { get; set; }
    public int TotalMessages { get; set; }
}
