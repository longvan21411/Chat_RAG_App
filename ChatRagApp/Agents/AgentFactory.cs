using ChatRagApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using ChatRagAppConfiguration;

namespace ChatRagApp.Agents;

public class AgentFactory
{
    private readonly Dictionary<string, IAgent> _agents = [];
    private readonly ILogger _logger;

    public AgentFactory(
        IConfiguration configuration,
        IImageService imageService,
        IChatHistoryService historyService,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AgentFactory>();

        var githubConfig = ChatRagAppConfigurationInfo.GetChatRagAppConfigurationInfo();
        var modelName = string.IsNullOrWhiteSpace(githubConfig.GithubModelName) ? "gpt-4o" : githubConfig.GithubModelName;
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
                    LlmModel = modelName,
                    MaxTokens = 2048,
                    SystemPrompt = "You are a helpful, knowledgeable assistant. Provide clear and concise answers.",
                    Instructions = "Answer user queries to the best of your ability. If you don't know the answer, say you don't know. Always be polite and helpful.",
                    Endpoint = githubConfig.GithubEndpoint,
                    Token = githubConfig.GithubToken
                },
                new AgentConfig
                {
                    Id = "image-analyst",
                    Name = "Image Analyst",
                    LlmModel = modelName,
                    MaxTokens = 1024,
                    SystemPrompt = "You specialize in analyzing and retrieving images from the database. Help users find relevant images and describe what they might be looking for.",
                    Instructions = "Analyze images and provide detailed descriptions. Assist users in finding the images they need.",
                    Endpoint = githubConfig.GithubEndpoint,
                    Token = githubConfig.GithubToken
                }
            ];
        }

        foreach (var cfg in configs.Select(cfg => ApplySharedConfiguration(cfg, githubConfig, modelName)))
        {
            var openAIClient = CreateOpenAIClient(cfg);

            IAgent agent = cfg.Id switch
            {
                "image-analyst" => new ImageAnalystAgent(cfg, openAIClient, imageService, historyService,
                    loggerFactory.CreateLogger<ImageAnalystAgent>()),
                _ => new GeneralAssistantAgent(cfg, openAIClient, imageService, historyService,
                    loggerFactory.CreateLogger<GeneralAssistantAgent>())
            };
            _agents[cfg.Id] = agent;
        }
    }

    private AgentConfig ApplySharedConfiguration(AgentConfig config, ConfigurationInfo githubConfig, string defaultModelName)
    {
        config.LlmModel = string.IsNullOrWhiteSpace(config.LlmModel) ? defaultModelName : config.LlmModel;
        config.Token = string.IsNullOrWhiteSpace(config.Token) ? githubConfig.GithubToken : config.Token;
        config.Endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? githubConfig.GithubEndpoint : config.Endpoint;
        return config;
    }

    private OpenAIClient? CreateOpenAIClient(AgentConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Token) || string.IsNullOrWhiteSpace(config.Endpoint))
        {
            _logger.LogWarning(
                "Agent {AgentId} is missing token or endpoint configuration. LLM features will be disabled.",
                config.Id);
            return null;
        }

        if (!Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpointUri))
        {
            _logger.LogWarning(
                "Agent {AgentId} has an invalid endpoint configuration: {Endpoint}",
                config.Id,
                config.Endpoint);
            return null;
        }

        try
        {
            return new OpenAIClient(
                new ApiKeyCredential(config.Token),
                new OpenAIClientOptions
                {
                    Endpoint = endpointUri
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create OpenAI client for agent {AgentId}", config.Id);
            return null;
        }
    }

    public IAgent GetAgent(string agentId)
        => _agents.TryGetValue(agentId, out var a) ? a : _agents.Values.First();

    public IReadOnlyList<IAgent> GetAllAgents()
        => [.. _agents.Values];
}
