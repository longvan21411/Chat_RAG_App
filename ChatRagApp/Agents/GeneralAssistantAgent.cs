using ChatRagApp.Services;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace ChatRagApp.Agents;

public class GeneralAssistantAgent : BaseAgent
{
    public GeneralAssistantAgent(
        AgentConfig config,
    OpenAIClient? openAIClient,
        IImageService imageService,
        IChatHistoryService historyService,
        ILogger<GeneralAssistantAgent> logger)
    : base(config, openAIClient, imageService, historyService, logger) { }
}
