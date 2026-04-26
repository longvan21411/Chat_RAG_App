using ChatRagApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRagApp.Controllers;

[ApiController]
[Route("api/report")]
[Authorize]
public class ReportApiController : ControllerBase
{
    private readonly IQdrantService _qdrant;

    public ReportApiController(IQdrantService qdrant)
    {
        _qdrant = qdrant;
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyReport([FromQuery] string? date = null, CancellationToken ct = default)
    {
        var reportDate = date is not null && DateTime.TryParse(date, out var d) ? d : DateTime.UtcNow;
        var report = await _qdrant.GetDailyReportAsync(reportDate, ct);
        return Ok(report);
    }
}
