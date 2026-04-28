# Chat RAG App — Implementation Instruction

## Project Overview

A .NET 8 Web Application implementing the Retrieval-Augmented Generation (RAG) chat pattern. The application integrates Qdrant as a vector database, supports SSO via Google, manages multiple AI agents, and provides image semantic search capabilities.

---

## 1. Project Setup

### 1.1 Create .NET 8 Web Application

```bash
dotnet new webapp -n ChatRagApp --framework net8.0
cd ChatRagApp
```

### 1.2 Required NuGet Packages

```bash
# Authentication
dotnet add package Microsoft.AspNetCore.Authentication.Google
dotnet add package Microsoft.AspNetCore.Authentication.Cookies

# Qdrant
dotnet add package Qdrant.Client

# AI / LLM
dotnet add package Microsoft.SemanticKernel
dotnet add package Microsoft.SemanticKernel.Connectors.Qdrant

# Image processing
dotnet add package SixLabors.ImageSharp

# MCP (Model Context Protocol)
dotnet add package ModelContextProtocol

# Token counting
dotnet add package Microsoft.ML.Tokenizers
```

---

## 2. Authentication & Authorization

### 2.1 Authentication Requirements

- All routes require authenticated users. Unauthenticated requests redirect to the login page.
- SSO via Google OAuth2 (supports `google.com` and `gmail.com` accounts).
- Session auto-expires after **5 minutes of inactivity**.

### 2.2 Configure Authentication in `Program.cs`

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Google:ClientId"];
    options.ClientSecret = builder.Configuration["Google:ClientSecret"];
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

### 2.3 Inactivity Auto-Logout

Implement a JavaScript heartbeat on each page interaction and a server-side sliding cookie expiration (already set above). Add a client-side idle timer:

```javascript
// wwwroot/js/idle-timer.js
let idleTime = 0;
const IDLE_LIMIT_MS = 5 * 60 * 1000;

document.addEventListener('mousemove', resetTimer);
document.addEventListener('keydown', resetTimer);
document.addEventListener('click', resetTimer);

function resetTimer() {
    idleTime = Date.now();
}

setInterval(() => {
    if (Date.now() - idleTime > IDLE_LIMIT_MS) {
        window.location.href = '/Account/Logout';
    }
}, 30000);

idleTime = Date.now();
```

### 2.4 User Storage in Qdrant

Store logged-in user records in a dedicated Qdrant collection `users`:

| Field          | Type     | Notes                          |
|----------------|----------|--------------------------------|
| `id`           | UUID     | Qdrant point ID                |
| `email`        | string   | Google email                   |
| `display_name` | string   | Full name from Google profile  |
| `provider`     | string   | `"google"`                     |
| `last_login`   | datetime | ISO 8601                       |
| `is_active`    | bool     | Account enabled flag           |

On each Google login callback, upsert the user point into Qdrant using email as the deduplication key.

---

## 3. Qdrant Vector Database

### 3.1 Collections

| Collection       | Purpose                                    |
|------------------|--------------------------------------------|
| `users`          | Logged-in user records (no vectors needed) |
| `chat_history`   | Chat messages with text embeddings         |
| `images`         | Image metadata + dual-vector points        |

### 3.2 Qdrant Configuration in `appsettings.json`

```json
{
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "ApiKey": ""
  },
  "Google": {
    "ClientId": "",
    "ClientSecret": ""
  },
  "LLM": {
    "Models": [
      {
        "Name": "gpt-4o",
        "MaxTokens": 4096,
        "Endpoint": "",
        "ApiKey": ""
      }
    ]
  }
}
```

---

## 4. Image Management

### 4.1 Upload Folder

Define a base upload path in configuration:

```json
"ImageUpload": {
  "BasePath": "wwwroot/uploads/images"
}
```

### 4.2 Image Point Structure (Qdrant)

Each image is stored as a point in the `images` collection with **two named vectors**:

| Vector Name         | Source                                | Dimension |
|---------------------|---------------------------------------|-----------|
| `text_description`  | Embedding of the image's text fields  | 1536      |
| `image_embedding`   | CLIP / vision model embedding of image| 512       |

#### Payload Fields

| Field          | Type     | Description                         |
|----------------|----------|-------------------------------------|
| `file_name`    | string   | Original file name with extension   |
| `title`        | string   | Human-readable image title          |
| `category`     | string   | Classification category             |
| `description`  | string   | Free-text description of the image  |
| `created_date` | datetime | ISO 8601 upload timestamp           |
| `is_active`    | bool     | Soft-delete / visibility flag       |

### 4.3 Bulk Upload Flow

1. Accept one or more image files via multipart form.
2. Save each file to `BasePath/{category}/{file_name}`.
3. Generate `image_embedding` using a vision/CLIP model.
4. Generate `text_description` embedding from `title + category + description`.
5. Upsert point into `images` collection with both vectors and full payload.

### 4.4 Image Service Interface

```csharp
public interface IImageService
{
    Task<List<ImageUploadResult>> UploadBulkAsync(
        IEnumerable<IFormFile> files,
        ImageMetadata metadata,
        CancellationToken ct = default);

    Task<List<ImageSearchResult>> SearchByTextAsync(
        string query,
        int topK = 10,
        CancellationToken ct = default);
}

public record ImageMetadata(
    string Title,
    string Category,
    string Description,
    bool IsActive = true);
```

### 4.5 Image Collection Seeding (TrainedImg)

On application startup, the app will automatically seed the `images` collection in Qdrant from the `TrainedImg` folder if the collection is empty. This is handled by the `ImageSeeder` class.

- The `TrainedImg` folder must contain subfolders: `cats`, `dogs`, and `wild_animals`.
- Images in each subfolder are inserted with the following categories:
  - `cats` → `Cats`
  - `dogs` → `Dogs`
  - `wild_animals` → `Animals`
- Each image is inserted as a point with:
  - Numeric, incrementing `Id`
  - Payload: file_name, title, category, description, created_date, is_active
  - Vectors: text_description (embedding), image_embedding (embedding)
- Seeding only occurs if the `images` collection is empty.
- This process runs automatically on app startup.

---

## 5. Chat History

### 5.1 Storage in Qdrant

Each message is stored as a point in `chat_history`:

| Field        | Type     | Description                        |
|--------------|----------|------------------------------------|
| `session_id` | string   | Groups messages per chat session   |
| `user_id`    | string   | Reference to user point ID         |
| `role`       | string   | `"user"` or `"assistant"`          |
| `content`    | string   | Message text                       |
| `timestamp`  | datetime | ISO 8601                           |
| `agent_id`   | string   | Which agent handled the message    |

### 5.2 Load Latest History

On session start, retrieve the **5 most recent** messages for the current user and agent, ordered by `timestamp` descending, then reverse for display:

```csharp
var history = await qdrantClient.ScrollAsync(
    collectionName: "chat_history",
    filter: MatchFilter("user_id", userId),
    limit: 5,
    orderBy: new OrderBy { Key = "timestamp", Direction = Direction.Desc }
);
```

---

## 6. Multiple Agents

### 6.1 Agent Definition

Each agent is defined via configuration and registered as a named service:

```json
"Agents": [
  {
    "Id": "general-assistant",
    "Name": "General Assistant",
    "LlmModel": "gpt-4o",
    "MaxTokens": 2048,
    "SystemPrompt": "You are a helpful assistant."
  },
  {
    "Id": "image-analyst",
    "Name": "Image Analyst",
    "LlmModel": "gpt-4o",
    "MaxTokens": 1024,
    "SystemPrompt": "You specialize in analyzing and retrieving images."
  }
]
```

### 6.2 Agent Capabilities

Each agent must implement:

```csharp
public interface IAgent
{
    string Id { get; }
    string Name { get; }

    // Core chat
    Task<AgentResponse> ChatAsync(string userMessage, string sessionId, CancellationToken ct = default);

    // Token measurement
    Task<TokenUsage> MeasureTokensAsync(string text, CancellationToken ct = default);

    // Semantic image search
    Task<List<ImageSearchResult>> SearchImagesAsync(string query, int topK = 5, CancellationToken ct = default);

    // Next question prediction
    Task<List<string>> PredictNextQuestionsAsync(string context, CancellationToken ct = default);

    // Instruction generation
    Task<string> GenerateInstructionAsync(string featureDescription, CancellationToken ct = default);
}
```

### 6.3 Token Measurement

Track and persist per-message token usage:

```csharp
public record TokenUsage(
    int InputTokens,
    int OutputTokens,
    int CachedTokens);
```

Store these values in the `chat_history` point payload.

---

## 7. MCP Server

### 7.1 Setup

Register a Model Context Protocol (MCP) server that exposes the agents' tools to external LLM clients:

```csharp
builder.Services.AddMcpServer(options =>
{
    options.ServerName = "ChatRagApp";
    options.ServerVersion = "1.0.0";
})
.WithHttpTransport()
.WithTools<ImageSearchTool>()
.WithTools<ChatHistoryTool>()
.WithTools<ReportTool>();
```

### 7.2 Exposed MCP Tools

| Tool Name            | Description                                 |
|----------------------|---------------------------------------------|
| `search_images`      | Search images by text query via embeddings  |
| `get_chat_history`   | Retrieve recent chat messages for a session |
| `generate_report`    | Generate daily usage report                 |

---

## 8. Dashboard & Reporting

### 8.1 Report Agent

The report agent queries Qdrant `chat_history` and `images` collections to produce daily aggregates:

| Metric                  | Description                                          |
|-------------------------|------------------------------------------------------|
| `images_uploaded_today` | Count of images with `created_date` = today          |
| `text_searches_today`   | Count of search queries by day                       |
| `active_users_today`    | Distinct user IDs active today                       |
| `token_usage_summary`   | Total input / output / cached tokens per agent today |

### 8.2 Dashboard Page

- Route: `/Dashboard`
- Renders charts using Chart.js or ApexCharts.
- Data is fetched via a JSON API endpoint `/api/report/daily`.
- Refreshes every 60 seconds.

---

## 9. Project Structure

```
ChatRagApp/
├── Agents/
│   ├── IAgent.cs
│   ├── BaseAgent.cs
│   ├── GeneralAssistantAgent.cs
│   └── ImageAnalystAgent.cs
├── Controllers/
│   ├── AccountController.cs
│   ├── ChatController.cs
│   ├── ImageController.cs
│   └── ReportController.cs
├── Models/
│   ├── AppUser.cs
│   ├── ChatMessage.cs
│   ├── ImageMetadata.cs
│   ├── ImagePoint.cs
│   └── TokenUsage.cs
├── Services/
│   ├── IImageService.cs
│   ├── ImageService.cs
│   ├── IChatHistoryService.cs
│   ├── ChatHistoryService.cs
│   ├── IQdrantService.cs
│   └── QdrantService.cs
├── Mcp/
│   ├── ImageSearchTool.cs
│   ├── ChatHistoryTool.cs
│   └── ReportTool.cs
├── Pages/ (or Views/)
│   ├── Chat/
│   ├── Images/
│   └── Dashboard/
├── wwwroot/
│   ├── uploads/images/
│   └── js/idle-timer.js
├── appsettings.json
└── Program.cs
```

---

## 10. Configuration Reference (`appsettings.json`)

```json
{
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "ApiKey": ""
  },
  "Google": {
    "ClientId": "<your-client-id>",
    "ClientSecret": "<your-client-secret>"
  },
  "ImageUpload": {
    "BasePath": "wwwroot/uploads/images"
  },
  "Session": {
    "IdleTimeoutMinutes": 5
  },
  "Agents": [
    {
      "Id": "general-assistant",
      "Name": "General Assistant",
      "LlmModel": "gpt-4o",
      "MaxTokens": 2048,
      "SystemPrompt": "You are a helpful assistant."
    }
  ],
  "LLM": {
    "Models": [
      {
        "Name": "gpt-4o",
        "MaxTokens": 4096,
        "Endpoint": "https://api.openai.com/v1",
        "ApiKey": "<your-api-key>"
      }
    ]
  }
}
```

---

## 11. Security Checklist

- [ ] Store all secrets in environment variables or Azure Key Vault, never in source code.
- [ ] Validate uploaded file types (whitelist: `.jpg`, `.jpeg`, `.png`, `.webp`).
- [ ] Enforce max file size limit on uploads.
- [ ] Sanitize all user inputs before storing or embedding.
- [ ] Require HTTPS in production.
- [ ] Apply rate limiting to `/api/` and `/Chat/` endpoints.
- [ ] Rotate Google OAuth2 client secrets regularly.

---

## 12. Development Checklist

- [ ] Project scaffold with .NET 8
- [ ] Google SSO authentication configured
- [ ] Cookie-based session with 5-minute idle timeout
- [ ] Qdrant `users` collection and upsert on login
- [ ] Qdrant `chat_history` collection and 5-message history load
- [ ] Qdrant `images` collection with dual-vector point schema
- [ ] Bulk image upload endpoint
- [ ] Text-to-image semantic search via image embedding vector
- [ ] Multiple agent framework with pluggable LLM models
- [ ] Per-agent token measurement (input / output / cached)
- [ ] Next question prediction agent capability
- [ ] `instruction.md` generation agent capability
- [ ] MCP server exposing tools
- [ ] Dashboard page with daily image and search report
- [ ] Chart.js / ApexCharts integration on dashboard
