using ChatRagApp.Models;
using ChatRagApp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ChatRagApp.Agents;

public abstract class BaseAgent : IAgent
{
    protected readonly AgentConfig Config;
    private readonly IChatCompletionService? _chatService;
    protected readonly IImageService ImageService;
    private readonly IChatHistoryService _historyService;
    private readonly ILogger _logger;

    public string Id => Config.Id;
    public string Name => Config.Name;
    public string LlmModel => Config.LlmModel;

    protected bool IsLlmConfigured => _chatService is not null;

    protected BaseAgent(
        AgentConfig config,
        string apiKey,
        IImageService imageService,
        IChatHistoryService historyService,
        ILogger logger)
    {
        Config = config;
        ImageService = imageService;
        _historyService = historyService;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var kernel = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(config.LlmModel, apiKey)
                    .Build();
                _chatService = kernel.GetRequiredService<IChatCompletionService>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize LLM for agent {AgentId}", config.Id);
            }
        }
    }

    public virtual async Task<AgentResponse> ChatAsync(string userMessage, string sessionId, string userId, CancellationToken ct = default)
    {
        // Load last 5 messages from history
        var history = await _historyService.GetRecentMessagesAsync(userId, Config.Id, 5, ct);

        // Build Semantic Kernel chat history
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(Config.SystemPrompt);
        foreach (var msg in history)
        {
            if (msg.Role == "user") chatHistory.AddUserMessage(msg.Content);
            else chatHistory.AddAssistantMessage(msg.Content);
        }
        chatHistory.AddUserMessage(userMessage);

        string responseContent;
        TokenUsage tokenUsage;

        if (_chatService is null)
        {
            responseContent = "⚠️ LLM not configured. Please set **OpenAI:ApiKey** in appsettings.json.";
            tokenUsage = TokenUsage.Empty;
        }
        else
        {
            try
            {
#pragma warning disable SKEXP0010
                var settings = new OpenAIPromptExecutionSettings
                {
                    MaxTokens = Config.MaxTokens,
                    Temperature = 0.7
                };
#pragma warning restore SKEXP0010

                var result = await _chatService.GetChatMessageContentAsync(chatHistory, settings, cancellationToken: ct);
                responseContent = result.Content ?? string.Empty;
                tokenUsage = ExtractTokenUsage(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM call failed for agent {AgentId}", Config.Id);
                responseContent = $"Error communicating with LLM: {ex.Message}";
                tokenUsage = TokenUsage.Empty;
            }
        }

        // Save both turns to history
        var userMsg = new Models.ChatMessage
        {
            SessionId = sessionId, UserId = userId, Role = "user",
            Content = userMessage, AgentId = Config.Id
        };
        var assistantMsg = new Models.ChatMessage
        {
            SessionId = sessionId, UserId = userId, Role = "assistant",
            Content = responseContent, AgentId = Config.Id, TokenUsage = tokenUsage
        };

        await _historyService.SaveMessageAsync(userMsg, ct);
        await _historyService.SaveMessageAsync(assistantMsg, ct);

        var suggestedQuestions = await PredictNextQuestionsAsync(userMessage + "\n" + responseContent, ct);

        return new AgentResponse
        {
            Content = responseContent,
            TokenUsage = tokenUsage,
            SuggestedQuestions = suggestedQuestions
        };
    }

    public Task<TokenUsage> MeasureTokensAsync(string text, CancellationToken ct = default)
    {
        // Approximate: 1 token ≈ 4 characters
        var estimated = (int)Math.Ceiling(text.Length / 4.0);
        return Task.FromResult(new TokenUsage(estimated, 0, 0));
    }

    public async Task<List<ImageSearchResult>> SearchImagesAsync(string query, int topK = 5, CancellationToken ct = default)
        => await ImageService.SearchByTextAsync(query, topK, ct);

    public async Task<List<string>> PredictNextQuestionsAsync(string context, CancellationToken ct = default)
    {
        if (_chatService is null)
            return ["Tell me more.", "Can you explain?", "What else should I know?"];

        try
        {
            var prompt = new ChatHistory();
            prompt.AddSystemMessage("You are a question predictor. Based on the conversation, suggest 3 short follow-up questions the user might ask. Return only the questions, one per line, no numbering.");
            prompt.AddUserMessage(context.Length > 800 ? context[^800..] : context);

#pragma warning disable SKEXP0010
            var settings = new OpenAIPromptExecutionSettings { MaxTokens = 150, Temperature = 0.8 };
#pragma warning restore SKEXP0010

            var result = await _chatService.GetChatMessageContentAsync(prompt, settings, cancellationToken: ct);
            return (result.Content ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(3)
                .ToList();
        }
        catch
        {
            return ["Tell me more.", "Can you explain that?", "What are the next steps?"];
        }
    }

    public async Task<string> GenerateInstructionAsync(string featureDescription, CancellationToken ct = default)
    {
        if (_chatService is null)
            return $"# Feature: {featureDescription}\n\n*LLM not configured.*";

        try
        {
            var prompt = new ChatHistory();
            prompt.AddSystemMessage("You are a software architect. Generate a concise implementation instruction in Markdown for the following feature. Include: overview, steps, code snippets, and any caveats.");
            prompt.AddUserMessage(featureDescription);

#pragma warning disable SKEXP0010
            var settings = new OpenAIPromptExecutionSettings { MaxTokens = 1000, Temperature = 0.4 };
#pragma warning restore SKEXP0010

            var result = await _chatService.GetChatMessageContentAsync(prompt, settings, cancellationToken: ct);
            return result.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"# Error generating instruction\n\n{ex.Message}";
        }
    }

    private static TokenUsage ExtractTokenUsage(ChatMessageContent result)
    {
        try
        {
            if (result.Metadata is not null &&
                result.Metadata.TryGetValue("Usage", out var usageObj) &&
                usageObj is not null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(usageObj);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                int input = TryGetInt(root, "InputTokenCount", "PromptTokens", "prompt_tokens");
                int output = TryGetInt(root, "OutputTokenCount", "CompletionTokens", "completion_tokens");
                int cached = TryGetInt(root, "CachedTokenCount");
                return new TokenUsage(input, output, cached);
            }
        }
        catch { /* ignore */ }

        // Fallback estimate
        var estimated = (int)Math.Ceiling(result.Content?.Length / 4.0 ?? 0);
        return new TokenUsage(0, estimated, 0);
    }

    private static int TryGetInt(System.Text.Json.JsonElement el, params string[] keys)
    {
        foreach (var key in keys)
            if (el.TryGetProperty(key, out var prop) && prop.TryGetInt32(out var val))
                return val;
        return 0;
    }
}
