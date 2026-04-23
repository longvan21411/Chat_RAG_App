using ChatRagApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChatRagApp.Agents;

public class AgentFactory
{
    private readonly Dictionary<string, IAgent> _agents = [];

    public AgentFactory(
        IConfiguration configuration,
        IImageService imageService,
        IChatHistoryService historyService,
        ILoggerFactory loggerFactory)
    {
        var apiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
        var configs = configuration.GetSection("Agents").Get<List<AgentConfig>>() ?? [];

        // Register default agents if none configured
        if (configs.Count == 0)
        {
            configs =
            [
                new AgentConfig
                {
                    Id = "general-assistant",
                    Name = "General Assistant",
                    LlmModel = "gpt-4o",
                    MaxTokens = 2048,
                    SystemPrompt = "You are a helpful, knowledgeable assistant. Provide clear and concise answers."
                },
                new AgentConfig
                {
                    Id = "image-analyst",
                    Name = "Image Analyst",
                    LlmModel = "gpt-4o",
                    MaxTokens = 1024,
                    SystemPrompt = "You specialize in analyzing and retrieving images from the database. Help users find relevant images and describe what they might be looking for."
                }
            ];
        }

        foreach (var cfg in configs)
        {
            IAgent agent = cfg.Id switch
            {
                "image-analyst" => new ImageAnalystAgent(cfg, apiKey, imageService, historyService,
                    loggerFactory.CreateLogger<ImageAnalystAgent>()),
                _ => new GeneralAssistantAgent(cfg, apiKey, imageService, historyService,
                    loggerFactory.CreateLogger<GeneralAssistantAgent>())
            };
            _agents[cfg.Id] = agent;
        }
    }

    public IAgent GetAgent(string agentId)
        => _agents.TryGetValue(agentId, out var a) ? a : _agents.Values.First();

    public IReadOnlyList<IAgent> GetAllAgents()
        => [.. _agents.Values];
}
