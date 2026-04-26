using ChatRagApp.Models;
using ChatRagApp.Services;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace ChatRagApp.Agents;

/// <summary>
/// Image Analyst agent — augments chat responses with relevant image search results.
/// </summary>
public class ImageAnalystAgent : BaseAgent
{
    public ImageAnalystAgent(
        AgentConfig config,
        OpenAIClient? openAIClient,
        IImageService imageService,
        IChatHistoryService historyService,
        ILogger<ImageAnalystAgent> logger)
        : base(config, openAIClient, imageService, historyService, logger) { }

    public override async Task<AgentResponse> ChatAsync(string userMessage, string sessionId, string userId, CancellationToken ct = default)
    {
        // First, perform a semantic image search using the user message
        var imageResults = await ImageService.SearchByTextAsync(userMessage, 3, ct);

        // Enrich the user message with image context if results found
        var enriched = userMessage;
        if (imageResults.Count > 0)
        {
            var imageContext = string.Join("\n", imageResults.Select(r =>
                $"- [{r.Title}] ({r.Category}): {r.Description} (score: {r.Score:F2})"));
            enriched = $"{userMessage}\n\n[Relevant images found in database:\n{imageContext}]";
        }

        var response = await base.ChatAsync(enriched, sessionId, userId, ct);

        // Attach image results to the response for display
        if (imageResults.Count > 0)
        {
            response.Content += $"\n\n**Related images found:** {imageResults.Count}";
        }

        return response;
    }
}
