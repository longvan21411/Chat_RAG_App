# Agents Folder — Implementation Instruction

## Purpose

This folder contains the application agent layer for Chat RAG App.

Agents are the domain-level abstraction used by the chat UI and services to:

- generate assistant responses
- read recent chat history
- measure token usage
- predict follow-up questions
- search related images
- generate feature instructions

The folder is structured around one shared base implementation, a common interface, concrete agent types, and a factory that creates agents from configuration.

---

## Files In Scope

### `IAgent.cs`

Defines the contract all agents must implement.

Current responsibilities:

- expose `Id`, `Name`, and `LlmModel`
- handle chat requests through `ChatAsync`
- estimate or measure tokens through `MeasureTokensAsync`
- support semantic image search through `SearchImagesAsync`
- predict suggested next questions through `PredictNextQuestionsAsync`
- generate Markdown implementation guidance through `GenerateInstructionAsync`

### `AgentConfig.cs`

Defines the configuration model used to create agents from `appsettings.json`.

Current fields:

- `Id`
- `Name`
- `LlmModel`
- `MaxTokens`
- `SystemPrompt`

### `BaseAgent.cs`

Implements the common agent workflow and the default behavior for most agent capabilities.

Current responsibilities:

- initialize Semantic Kernel chat completion when `OpenAI:ApiKey` is configured
- load the last 5 messages for the current user and agent from chat history
- prepend the configured system prompt
- call the LLM with max token and temperature settings
- fall back gracefully when the LLM is not configured
- persist both user and assistant turns through `IChatHistoryService`
- derive token usage from Semantic Kernel metadata when available
- generate default follow-up questions when the LLM is unavailable

### `GeneralAssistantAgent.cs`

Represents the default text-only assistant.

Current behavior:

- inherits `BaseAgent` behavior without additional specialization

### `ImageAnalystAgent.cs`

Represents the image-aware assistant.

Current behavior:

- performs semantic image search before the main LLM call
- enriches the user message with relevant image context
- appends a short related-image summary to the final response

### `AgentFactory.cs`

Creates and stores concrete agents for runtime use.

Current responsibilities:

- read `Agents` configuration from application settings
- create default agents when no explicit configuration exists
- construct the correct concrete agent based on agent id
- provide `GetAgent(agentId)` and `GetAllAgents()` accessors

---

## Runtime Flow

1. The application loads agent definitions from configuration.
2. `AgentFactory` creates one concrete agent instance per configuration entry.
3. A caller asks the factory for a specific agent by id.
4. The selected agent receives a user message through `ChatAsync`.
5. `BaseAgent` loads recent history for the same user and agent.
6. The agent builds a Semantic Kernel `ChatHistory` with the system prompt and prior turns.
7. If an LLM is configured, the agent calls the model and extracts token usage.
8. If no LLM is configured, the agent returns a safe fallback response.
9. The user turn and assistant turn are both saved to history.
10. The response returns content, token usage, and suggested follow-up questions.

For the `image-analyst` agent, image search occurs before the base chat call so the model receives enriched context.

---

## Configuration Requirements

Agents depend on these configuration sections:

```json
"OpenAI": {
  "ApiKey": "",
  "EmbeddingModel": "text-embedding-3-small",
  "EmbeddingDimension": 1536
},
"Agents": [
  {
    "Id": "general-assistant",
    "Name": "General Assistant",
    "LlmModel": "gpt-4o",
    "MaxTokens": 2048,
    "SystemPrompt": "You are a helpful assistant."
  }
]
```

Behavior:

- If `OpenAI:ApiKey` is missing, agent construction still succeeds.
- In that case, chat and instruction generation return safe fallback content instead of failing startup.
- If the `Agents` section is missing, `AgentFactory` creates built-in defaults.

---

## Extension Rules

When adding a new agent type:

1. Create a new concrete class in this folder.
2. Inherit from `BaseAgent` unless there is a clear reason not to.
3. Override only the behavior that is genuinely different.
4. Add a mapping in `AgentFactory` so the new agent can be constructed from config.
5. Keep `Id` values stable because chat history and UI selection depend on them.

Recommended pattern:

- put shared orchestration in `BaseAgent`
- keep specialization focused in the derived agent
- avoid duplicating history persistence and token extraction logic

---

## Design Constraints

- Keep this folder focused on agent orchestration, not UI rendering.
- Do not move storage logic into agents when it belongs in services.
- Prefer configuration-driven agent registration through `AgentFactory`.
- Keep startup resilient when LLM configuration is incomplete.
- Preserve compatibility with the current `IAgent` interface because controllers and pages rely on it.

---

## Related Files

- `ChatRagApp/Services/IChatHistoryService.cs` — conversation persistence abstraction
- `ChatRagApp/Services/ChatHistoryService.cs` — history storage implementation
- `ChatRagApp/Services/IImageService.cs` — image search abstraction used by agents
- `ChatRagApp/Models/AgentResponse.cs` — returned response shape for agent chat operations
- `ChatRagApp/Models/TokenUsage.cs` — token accounting model
- `ChatRagApp/appsettings.json` — agent and OpenAI configuration source
