using ChatRagApp.Agents;
using ChatRagApp.Components;
using ChatRagApp.Mcp;
using ChatRagApp.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── Authentication ──────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
})
.AddGoogle(options =>
{
    options.ClientId = config["Google:ClientId"] ?? string.Empty;
    options.ClientSecret = config["Google:ClientSecret"] ?? string.Empty;
    options.CallbackPath = "/signin-google";
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ── Qdrant ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ =>
{
    var host = config["Qdrant:Host"] ?? "localhost";
    var port = int.TryParse(config["Qdrant:GrpcPort"], out var p) ? p : 6334;
    var apiKey = config["Qdrant:ApiKey"];
    return string.IsNullOrEmpty(apiKey)
        ? new QdrantClient(host, port)
        : new QdrantClient(host, port, apiKey: apiKey);
});

// ── Services ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IEmbeddingService>(sp =>
{
    var apiKey = config["OpenAI:ApiKey"] ?? string.Empty;
    var model = config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
    var dim = int.TryParse(config["OpenAI:EmbeddingDimension"], out var d) ? d : 1536;
    var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
    return new EmbeddingService(apiKey, model, dim, logger);
});

builder.Services.AddScoped<IQdrantService, QdrantService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<AgentFactory>();

// ── MCP Server ──────────────────────────────────────────────────────────────
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<McpTools>();

// ── Blazor & MVC ────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// ── Initialize Qdrant collections on startup ─────────────────────────────
_ = Task.Run(async () =>
{
    await Task.Delay(2000); // wait for Qdrant to be ready
    try
    {
        using var scope = app.Services.CreateScope();
        var qdrant = scope.ServiceProvider.GetRequiredService<IQdrantService>();
        await qdrant.InitializeCollectionsAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Qdrant initialization failed — ensure Qdrant is running on localhost:6334");
    }
});

// ── HTTP pipeline ────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapMcp("/mcp");

app.Run();

