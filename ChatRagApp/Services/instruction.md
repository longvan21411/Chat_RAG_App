# Services Folder — Implementation Instruction

## Purpose

This folder contains the application service layer for Chat RAG App.

Services in this folder provide reusable business and integration logic for:

- chat history persistence
- embedding generation
- Qdrant vector database access
- image upload and semantic search

These services are consumed by agents, controllers, and pages. They should remain focused on orchestration and data access rather than UI behavior.

---

## Files In Scope

### `IChatHistoryService.cs`

Defines the abstraction for storing and retrieving conversation history.

Current responsibilities:

- save a single chat message
- load recent messages for a given user and agent
- load all chat messages for a session through the 

### `ChatHistoryService.cs`

Implements chat history persistence.

Current responsibilities:

- generate an embedding for each message through `IEmbeddingService`
- persist chat messages through `IQdrantService`
- retrieve recent chat history through `IQdrantService`
- retrieve all chat messages for a session through `IQdrantService`
- always fetching 5 latest chat messages history sessions through `IQdrantService`

### `IEmbeddingService.cs`

Defines the abstraction for text embedding generation.

Current responsibilities:

- generate an embedding vector from text
- expose whether the embedding provider is configured

### `EmbeddingService.cs`

Implements embedding generation using Semantic Kernel and OpenAI when configured.

Current responsibilities:

- initialize an OpenAI embedding generator when an API key is available
- generate embeddings for text content
- fall back to deterministic local embeddings when the external provider is unavailable
- log initialization and fallback conditions

### `IQdrantService.cs`

Defines the abstraction for vector database operations.

Current responsibilities:

- initialize collections
- store and query users
- store and query chat messages
- store and search image points
- build daily reporting data
- retrieve all chat messages for a session

### `QdrantService.cs`

Implements all Qdrant persistence and query operations.

Current responsibilities:

- ensure required collections exist on startup
- upsert and retrieve users by email and username
- save embedded chat messages and retrieve recent history
- upsert image points with named vectors
- perform semantic image search
- aggregate daily report metrics from stored chat and image data
- retrieve all chat messages for a session
- expose methods to check for collection existence and create named-vector collections as needed
Implements image seeding logic for the 'images' collection in Qdrant.

Current responsibilities:

- On application startup, ensures the 'images' collection exists in Qdrant (creates it if missing)
- Always upserts all images from every subfolder in the TrainedImg directory into the 'images' collection, regardless of whether the collection is empty
- Uses each subfolder name as the image category
- Uses embedding service to generate text and image embeddings for each image
- Logs seeding progress and completion

Behavioral change:
- The seeder no longer skips seeding if the collection is non-empty; it always upserts all images, ensuring the Qdrant collection is up to date with all folders and images in TrainedImg

### `IImageService.cs`

Defines the abstraction for image upload and semantic search.

Current responsibilities:

- upload one or more images with metadata
- search images by text query
- retrieve active images for display

### `ImageService.cs`

Implements the image pipeline.

Current responsibilities:

- validate file extensions and size
- sanitize category and file paths
- save uploaded files under `wwwroot/uploads/images`
- generate text and proxy image embeddings
- upsert image records into Qdrant
- search images by semantic text query

---

## Runtime Service Flow

1. The application registers service implementations in `Program.cs`.
2. `EmbeddingService` is created with OpenAI configuration and fallback behavior.
3. `QdrantService` is created with the configured Qdrant client.
4. `ChatHistoryService` uses embedding generation plus Qdrant persistence to store conversation turns.
5. `ImageService` uses embedding generation plus Qdrant persistence to support upload and search.
6. Agents and controllers depend on the service interfaces rather than direct storage or provider access.

---

## Dependency Rules

Current intended dependency direction:

- pages and controllers depend on service interfaces
- agents depend on service interfaces
- service implementations may depend on other services when needed
- service implementations may depend on external SDKs and infrastructure clients

Current examples:

- `ChatHistoryService` depends on `IQdrantService` and `IEmbeddingService`
- `ImageService` depends on `IQdrantService` and `IEmbeddingService`
- `QdrantService` depends on the Qdrant SDK client

Avoid reversing this dependency flow by calling pages or components from services.

---

## Configuration Requirements

Services currently depend on these configuration sections:

```json
"OpenAI": {
  "ApiKey": "",
  "EmbeddingModel": "text-embedding-3-small",
  "EmbeddingDimension": 1536
},
"Qdrant": {
  "Host": "localhost",
  "GrpcPort": 6334,
  "ApiKey": ""
},
"ImageUpload": {
  "BasePath": "wwwroot/uploads/images"
}
```

Behavior:

- if `OpenAI:ApiKey` is missing, embedding generation falls back deterministically
- if Qdrant is unavailable, startup collection initialization logs warnings instead of crashing the app
- image upload paths should remain under the application web root

---

## Design Constraints

- Keep service interfaces stable because they are consumed by multiple layers.
- Keep provider-specific logic inside service implementations, not in pages or agents.
- Prefer graceful degradation when external infrastructure is unavailable.
- Avoid mixing UI concerns into the service layer.
- Preserve async APIs across the service layer.

---

## Extension Rules

When adding a new service:

1. Add an interface first if the service is consumed across layers.
2. Register the implementation in `Program.cs`.
3. Keep cross-service dependencies explicit and minimal.
4. Reuse existing models from `ChatRagApp/Models` instead of introducing duplicate DTOs.
5. Add fallback behavior when the new service depends on optional external systems.

---

## Related Files

- `ChatRagApp/Program.cs` — service registration and infrastructure wiring
- `ChatRagApp/Agents` — consumers of chat, image, and history services
- `ChatRagApp/Controllers/AccountController.cs` — consumer of user-related Qdrant service operations
- `ChatRagApp/Models` — shared data contracts used by the service layer
- `ChatRagApp/appsettings.json` — service configuration source

### Chat History by Session

#### `IChatHistoryService.cs`
- Now includes `GetHistoryBySessionIdAsync(string sessionId, CancellationToken ct = default)` to load all chat messages for a session.

#### `ChatHistoryService.cs`
- Implements `GetHistoryBySessionIdAsync` by calling the new Qdrant service method and sorting by timestamp.

#### `IQdrantService.cs`
- Now includes `GetAllChatMessagesBySessionIdAsync(string sessionId, CancellationToken ct = default)` for session-based retrieval.

#### `QdrantService.cs`
- Stub implementation for `GetAllChatMessagesBySessionIdAsync` (replace with actual Qdrant query logic as needed).
