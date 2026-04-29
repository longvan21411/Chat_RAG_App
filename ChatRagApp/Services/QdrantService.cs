using ChatRagApp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Microsoft.Extensions.Logging;

namespace ChatRagApp.Services;

public class QdrantService : IQdrantService
{
    // Expose collection existence check for seeder
    public async Task<bool> CollectionExistsAsync(string name, CancellationToken ct = default)
    {
        return await _client.CollectionExistsAsync(name, ct);
    }

    // Expose EnsureNamedVectorCollectionAsync for seeder
    public async Task EnsureNamedVectorCollectionIfNotExistsAsync(string name, CancellationToken ct = default)
    {
        await EnsureNamedVectorCollectionAsync(name, ct);
    }
    private const string UsersCollection = "users";
    private const string ChatHistoryCollection = "chat_history";
    private const string ImagesCollection = "images";
    private const ulong EmbeddingDim = 1536;

    private readonly QdrantClient _client;
    private readonly ILogger<QdrantService> _logger;

    public QdrantService(QdrantClient client, ILogger<QdrantService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task InitializeCollectionsAsync(CancellationToken ct = default)
    {
        await EnsureSingleVectorCollectionAsync(UsersCollection, 1, ct);
        await EnsureSingleVectorCollectionAsync(ChatHistoryCollection, EmbeddingDim, ct);
        await EnsureNamedVectorCollectionAsync(ImagesCollection, ct);
    }

    private async Task EnsureSingleVectorCollectionAsync(string name, ulong size, CancellationToken ct)
    {
        try
        {
            if (!await _client.CollectionExistsAsync(name, ct))
            {
                await _client.CreateCollectionAsync(name,
                    new VectorParams { Size = size, Distance = Distance.Cosine },
                    cancellationToken: ct);
                _logger.LogInformation("Created Qdrant collection: {Name}", name);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not ensure collection {Name}", name); }
    }

    private async Task EnsureNamedVectorCollectionAsync(string name, CancellationToken ct)
    {
        try
        {
            if (!await _client.CollectionExistsAsync(name, ct))
            {
                var paramsMap = new VectorParamsMap();
                paramsMap.Map.Add("text_description",
                    new VectorParams { Size = EmbeddingDim, Distance = Distance.Cosine });
                paramsMap.Map.Add("image_embedding",
                    new VectorParams { Size = EmbeddingDim, Distance = Distance.Cosine });
                await _client.CreateCollectionAsync(name, paramsMap, cancellationToken: ct);
                _logger.LogInformation("Created named-vector collection: {Name}", name);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not ensure named-vector collection {Name}", name); }
    }

    public async Task UpsertUserAsync(AppUser user, CancellationToken ct = default)
    {
        try
        {
            var dense = new DenseVector();
            dense.Data.Add(0f);
            var point = new PointStruct { Id = new PointId { Uuid = user.Id.ToString() } };
            point.Vectors = new Vectors { Vector = new Vector { Dense = dense } };
            point.Payload["username"] = new Value { StringValue = user.UserName };
            point.Payload["email"] = new Value { StringValue = user.Email };
            point.Payload["display_name"] = new Value { StringValue = user.DisplayName };
            point.Payload["provider"] = new Value { StringValue = user.Provider };
            point.Payload["password_hash"] = new Value { StringValue = user.PasswordHash };
            point.Payload["last_login"] = new Value { StringValue = user.LastLogin.ToString("O") };
            point.Payload["is_active"] = new Value { BoolValue = user.IsActive };
            await _client.UpsertAsync(UsersCollection, new List<PointStruct> { point }, cancellationToken: ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "UpsertUser failed for {Email}", user.Email); }
    }

    public async Task<AppUser?> GetUserByEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "email", Match = new Match { Text = email } }
            });
            var response = await _client.ScrollAsync(UsersCollection, filter, limit: 1, cancellationToken: ct);
            var p = response.Result.FirstOrDefault();
            if (p is null) return null;
            return new AppUser
            {
                Id = Guid.TryParse(p.Id.Uuid, out var g) ? g : Guid.Empty,
                UserName = GetString(p.Payload, "username"),
                Email = GetString(p.Payload, "email"),
                DisplayName = GetString(p.Payload, "display_name"),
                Provider = GetString(p.Payload, "provider"),
                PasswordHash = GetString(p.Payload, "password_hash"),
                IsActive = GetBool(p.Payload, "is_active"),
                LastLogin = DateTime.TryParse(GetString(p.Payload, "last_login"), out var dt) ? dt : DateTime.UtcNow
            };
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetUserByEmail failed for {Email}", email); return null; }
    }

    public async Task<AppUser?> GetUserByUserNameAsync(string userName, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "username", Match = new Match { Text = userName } }
            });
            var response = await _client.ScrollAsync(UsersCollection, filter, limit: 1, cancellationToken: ct);
            var p = response.Result.FirstOrDefault();
            if (p is null) return null;
            return new AppUser
            {
                Id = Guid.TryParse(p.Id.Uuid, out var g) ? g : Guid.Empty,
                UserName = GetString(p.Payload, "username"),
                Email = GetString(p.Payload, "email"),
                DisplayName = GetString(p.Payload, "display_name"),
                Provider = GetString(p.Payload, "provider"),
                PasswordHash = GetString(p.Payload, "password_hash"),
                IsActive = GetBool(p.Payload, "is_active"),
                LastLogin = DateTime.TryParse(GetString(p.Payload, "last_login"), out var dt) ? dt : DateTime.UtcNow
            };
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetUserByUserName failed for {UserName}", userName); return null; }
    }

    public async Task SaveChatMessageAsync(ChatMessage message, float[] embedding, CancellationToken ct = default)
    {
        try
        {
            var dense = new DenseVector();
            dense.Data.AddRange(embedding);
            var point = new PointStruct { Id = new PointId { Uuid = message.Id.ToString() } };
            point.Vectors = new Vectors { Vector = new Vector { Dense = dense } };
            point.Payload["session_id"] = new Value { StringValue = message.SessionId };
            point.Payload["user_id"] = new Value { StringValue = message.UserId };
            point.Payload["role"] = new Value { StringValue = message.Role };
            point.Payload["content"] = new Value { StringValue = message.Content };
            point.Payload["timestamp"] = new Value { StringValue = message.Timestamp.ToString("O") };
            point.Payload["timestamp_unix"] = new Value { IntegerValue = new DateTimeOffset(message.Timestamp).ToUnixTimeSeconds() };
            point.Payload["agent_id"] = new Value { StringValue = message.AgentId };
            point.Payload["input_tokens"] = new Value { IntegerValue = message.TokenUsage.InputTokens };
            point.Payload["output_tokens"] = new Value { IntegerValue = message.TokenUsage.OutputTokens };
            point.Payload["cached_tokens"] = new Value { IntegerValue = message.TokenUsage.CachedTokens };
            await _client.UpsertAsync(ChatHistoryCollection, new List<PointStruct> { point }, cancellationToken: ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SaveChatMessage failed"); }
    }

    public async Task<List<ChatMessage>> GetRecentChatMessagesAsync(string userId, string agentId, int limit = 5, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "user_id", Match = new Match { Text = userId } }
            });
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "agent_id", Match = new Match { Text = agentId } }
            });
            var orderBy = new OrderBy { Key = "timestamp_unix", Direction = Direction.Desc };
            var response = await _client.ScrollAsync(ChatHistoryCollection, filter,
                limit: (uint)limit, orderBy: orderBy, cancellationToken: ct);

            var messages = response.Result.Select(p => new ChatMessage
            {
                Id = Guid.TryParse(p.Id.Uuid, out var g) ? g : Guid.Empty,
                SessionId = GetString(p.Payload, "session_id"),
                UserId = GetString(p.Payload, "user_id"),
                Role = GetString(p.Payload, "role"),
                Content = GetString(p.Payload, "content"),
                AgentId = GetString(p.Payload, "agent_id"),
                Timestamp = DateTime.TryParse(GetString(p.Payload, "timestamp"), out var ts) ? ts : DateTime.UtcNow,
                TokenUsage = new Models.TokenUsage(
                    (int)GetLong(p.Payload, "input_tokens"),
                    (int)GetLong(p.Payload, "output_tokens"),
                    (int)GetLong(p.Payload, "cached_tokens"))
            }).ToList();
            messages.Reverse();
            return messages;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetRecentChatMessages failed"); return []; }
    }

    public async Task<List<ChatMessage>> GetAllChatMessagesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ScrollAsync(ChatHistoryCollection, filter: null, limit: 1000, cancellationToken: ct);
            var messages = response.Result.Select(p => new ChatMessage
            {
                Id = Guid.TryParse(p.Id.Uuid, out var g) ? g : Guid.Empty,
                SessionId = GetString(p.Payload, "session_id"),
                UserId = GetString(p.Payload, "user_id"),
                Role = GetString(p.Payload, "role"),
                Content = GetString(p.Payload, "content"),
                AgentId = GetString(p.Payload, "agent_id"),
                Timestamp = DateTime.TryParse(GetString(p.Payload, "timestamp"), out var ts) ? ts : DateTime.UtcNow,
                TokenUsage = new Models.TokenUsage(
                    (int)GetLong(p.Payload, "input_tokens"),
                    (int)GetLong(p.Payload, "output_tokens"),
                    (int)GetLong(p.Payload, "cached_tokens"))
            }).OrderBy(m => m.Timestamp).ToList();
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetAllChatMessagesAsync failed");
            return new List<ChatMessage>();
        }
    }

    public async Task<List<ChatMessage>> GetAllChatMessagesBySessionIdAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "session_id", Match = new Match { Text = sessionId } }
            });
            var orderBy = new OrderBy { Key = "timestamp_unix", Direction = Direction.Asc };
            var response = await _client.ScrollAsync(ChatHistoryCollection, filter,
                limit: 1000, // adjust as needed for your expected history size
                orderBy: orderBy,
                cancellationToken: ct);

            var messages = response.Result.Select(p => new ChatMessage
            {
                Id = Guid.TryParse(p.Id.Uuid, out var g) ? g : Guid.Empty,
                SessionId = GetString(p.Payload, "session_id"),
                UserId = GetString(p.Payload, "user_id"),
                Role = GetString(p.Payload, "role"),
                Content = GetString(p.Payload, "content"),
                AgentId = GetString(p.Payload, "agent_id"),
                Timestamp = DateTime.TryParse(GetString(p.Payload, "timestamp"), out var ts) ? ts : DateTime.UtcNow,
                TokenUsage = new Models.TokenUsage(
                    (int)GetLong(p.Payload, "input_tokens"),
                    (int)GetLong(p.Payload, "output_tokens"),
                    (int)GetLong(p.Payload, "cached_tokens"))
            }).ToList();

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetAllChatMessagesBySessionIdAsync failed for session {SessionId}", sessionId);
            return new List<ChatMessage>();
        }
    }

    public async Task UpsertImageAsync(ImagePoint image, float[] textEmbedding, float[] imageEmbedding, CancellationToken ct = default)
    {
        try
        {
            var textDense = new DenseVector();
            textDense.Data.AddRange(textEmbedding);
            var imgDense = new DenseVector();
            imgDense.Data.AddRange(imageEmbedding);
            var namedVectors = new NamedVectors();
            namedVectors.Vectors.Add("text_description", new Vector { Dense = textDense });
            namedVectors.Vectors.Add("image_embedding", new Vector { Dense = imgDense });
            var point = new PointStruct { Id = new PointId { Uuid = image.Id.ToString() } };
            point.Vectors = new Vectors { Vectors_ = namedVectors };
            point.Payload["file_name"] = new Value { StringValue = image.FileName };
            point.Payload["title"] = new Value { StringValue = image.Title };
            point.Payload["category"] = new Value { StringValue = image.Category };
            point.Payload["description"] = new Value { StringValue = image.Description };
            point.Payload["created_date"] = new Value { StringValue = image.CreatedDate.ToString("O") };
            point.Payload["created_date_unix"] = new Value { IntegerValue = new DateTimeOffset(image.CreatedDate).ToUnixTimeSeconds() };
            point.Payload["is_active"] = new Value { BoolValue = image.IsActive };
            await _client.UpsertAsync(ImagesCollection, new List<PointStruct> { point }, cancellationToken: ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "UpsertImage failed for {FileName}", image.FileName); }
    }

    public async Task<List<ImageSearchResult>> SearchImagesByTextAsync(float[] queryEmbedding, int topK = 10, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "is_active", Match = new Match { Boolean = true } }
            });
            var results = await _client.SearchAsync(
                ImagesCollection,
                queryEmbedding.AsMemory(),
                filter: filter,
                limit: (ulong)topK,
                vectorName: "text_description",
                cancellationToken: ct);
            return results.Select(r => new ImageSearchResult
            {
                Id = Guid.TryParse(r.Id.Uuid, out var g) ? g : Guid.Empty,
                FileName = GetString(r.Payload, "file_name"),
                Title = GetString(r.Payload, "title"),
                Category = GetString(r.Payload, "category"),
                Description = GetString(r.Payload, "description"),
                Score = r.Score,
                ImageUrl = $"/uploads/images/{GetString(r.Payload, "category")}/{GetString(r.Payload, "file_name")}"
            }).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SearchImagesByText failed"); return []; }
    }

    public async Task<DailyReport> GetDailyReportAsync(DateTime date, CancellationToken ct = default)
    {
        var report = new DailyReport { Date = date.Date };
        var todayUnix = new DateTimeOffset(date.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var tomorrowUnix = todayUnix + 86400;
        try
        {
            var imageFilter = new Filter();
            imageFilter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "created_date_unix",
                    Range = new Qdrant.Client.Grpc.Range { Gte = todayUnix, Lt = tomorrowUnix }
                }
            });
            var imageResp = await _client.ScrollAsync(ImagesCollection, imageFilter, limit: 1000, cancellationToken: ct);
            report.ImagesUploadedToday = imageResp.Result.Count;

            var chatFilter = new Filter();
            chatFilter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "timestamp_unix",
                    Range = new Qdrant.Client.Grpc.Range { Gte = todayUnix, Lt = tomorrowUnix }
                }
            });
            var chatResp = await _client.ScrollAsync(ChatHistoryCollection, chatFilter, limit: 10000, cancellationToken: ct);
            var userSet = new HashSet<string>();
            int searchCount = 0;
            foreach (var p in chatResp.Result)
            {
                var uid = GetString(p.Payload, "user_id");
                if (!string.IsNullOrEmpty(uid)) userSet.Add(uid);
                if (GetString(p.Payload, "content").ToLowerInvariant().Contains("[search]")) searchCount++;
                var agentId = GetString(p.Payload, "agent_id");
                if (!string.IsNullOrEmpty(agentId))
                {
                    if (!report.TokenUsageByAgent.TryGetValue(agentId, out var summary))
                    {
                        summary = new TokenUsageSummary { AgentId = agentId, AgentName = agentId };
                        report.TokenUsageByAgent[agentId] = summary;
                    }
                    summary.TotalInput += (int)GetLong(p.Payload, "input_tokens");
                    summary.TotalOutput += (int)GetLong(p.Payload, "output_tokens");
                    summary.TotalCached += (int)GetLong(p.Payload, "cached_tokens");
                    summary.TotalMessages++;
                }
            }
            report.ActiveUsersToday = userSet.Count;
            report.TextSearchesToday = searchCount;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetDailyReport failed"); }
        return report;
    }

    private static string GetString(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.StringValue : string.Empty;
    private static long GetLong(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.IntegerValue : 0L;
    private static bool GetBool(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) && v.BoolValue;
}
