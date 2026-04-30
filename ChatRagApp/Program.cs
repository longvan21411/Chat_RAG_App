using ChatRagApp.Agents;
using ChatRagApp.Components;
using ChatRagApp.Mcp;
using ChatRagApp.Models;
using ChatRagApp.Services;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

// Add serilog.json config
builder.Configuration.AddJsonFile("serilog.json", optional: true, reloadOnChange: true);
var config = builder.Configuration;
var googleClientId = config["Google:ClientId"];
var googleClientSecret = config["Google:ClientSecret"];
var hasGoogleAuth = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

// ── Authentication ──────────────────────────────────────────────────────────
var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/account/logout";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

if (hasGoogleAuth)
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google";
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    });
}
else
{
    builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Warning);
}

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

// ── Qdrant ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ =>
{
    var host = config["Qdrant:Host"] ?? "localhost";
    var port = int.TryParse(config["Qdrant:GrpcPort"], out var p) ? p : 6334;
    var apiKey = config["Qdrant:ApiKey"];

    var handler = new SocketsHttpHandler
    {
        EnableMultipleHttp2Connections = true,
        UseProxy = false
    };

    var channel = GrpcChannel.ForAddress($"http://{host}:{port}", new GrpcChannelOptions
    {
        HttpHandler = handler
    });

    var grpcClient = new QdrantGrpcClient(channel.CreateCallInvoker());
    return new QdrantClient(grpcClient);
});

// ── Services ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IEmbeddingService>(sp =>
{    
    var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
    return new EmbeddingService(logger);
});


builder.Services.AddScoped<IQdrantService, QdrantService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<AgentFactory>();

// Register ImageSeeder as singleton
builder.Services.AddScoped<ImageSeeder>(sp =>
{
    var qdrant = sp.GetRequiredService<IQdrantService>();
    var embedding = sp.GetRequiredService<IEmbeddingService>();
    var imageService = sp.GetRequiredService<IImageService>();
    var logger = sp.GetRequiredService<ILogger<ImageSeeder>>();
    var trainedImgPath = Path.Combine(AppContext.BaseDirectory, "TrainedImg");
    
    return new ImageSeeder(qdrant, embedding, imageService, logger, trainedImgPath);
});

// ── MCP Server ──────────────────────────────────────────────────────────────
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<McpTools>();

// ── Blazor & MVC ────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// ── Initialize Qdrant collections and seed images on startup ─────────────
_ = Task.Run(async () =>
{
    await Task.Delay(2000); // wait for Qdrant to be ready
    try
    {
        using var scope = app.Services.CreateScope();
        var qdrant = scope.ServiceProvider.GetRequiredService<IQdrantService>();
        await qdrant.InitializeCollectionsAsync();

        // Seed images if needed
        var seeder = scope.ServiceProvider.GetRequiredService<ImageSeeder>();
        await seeder.SeedImagesIfEmptyAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Qdrant initialization or image seeding failed");
    }
});

// ── HTTP pipeline ────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");

if (app.Configuration["ASPNETCORE_URLS"]?.Contains("https://", StringComparison.OrdinalIgnoreCase) == true)
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
// Serve TrainedImg as static files
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(app.Environment.ContentRootPath, "..", "TrainedImg")),
    RequestPath = "/TrainedImg"
});
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapMcp("/mcp");

app.Run();

