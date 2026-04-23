using ChatRagApp.Services;
using Microsoft.Extensions.Logging;

namespace ChatRagApp.Agents;

public class GeneralAssistantAgent : BaseAgent
{
    public GeneralAssistantAgent(
        AgentConfig config,
        string apiKey,
        IImageService imageService,
        IChatHistoryService historyService,
        ILogger<GeneralAssistantAgent> logger)
        : base(config, apiKey, imageService, historyService, logger) { }
}
