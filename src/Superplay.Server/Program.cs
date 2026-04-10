using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using Superplay.Server.Networking;
using Superplay.Server.Routing;
using Superplay.Server.Services;
using Superplay.Shared;

// Bootstrap Serilog for startup logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Superplay Game Server");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from appsettings.json
    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Storage provider selection
    var storageProvider = builder.Configuration.GetValue<string>("Storage:Provider")
                          ?? Defaults.StorageProvider;

    var isRedis = string.Equals(storageProvider, "Redis", StringComparison.OrdinalIgnoreCase);

    if (isRedis)
    {
        var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString")
                                    ?? Defaults.RedisConnectionString;
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));
        builder.Services.AddSingleton<IPlayerRepository, RedisPlayerRepository>();
        builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        Log.Information("Storage provider: Redis ({ConnectionString})", redisConnectionString);
    }
    else
    {
        var sqliteConnectionString = builder.Configuration.GetValue<string>("Sqlite:ConnectionString")
                                     ?? Defaults.SqliteConnectionString;
        builder.Services.AddDbContext<GameDbContext>(options =>
            options.UseSqlite(sqliteConnectionString));
        builder.Services.AddSingleton<IPlayerRepository, SqlitePlayerRepository>();
        builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        Log.Information("Storage provider: SQLite ({ConnectionString})", sqliteConnectionString);
    }

    // Application services
    builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
    builder.Services.AddSingleton<WebSocketHandler>();

    // Auto-register all IMessageHandler implementations from this assembly
    builder.Services.AddMessageHandlers();

    var app = builder.Build();

    // Ensure SQLite database is created on startup
    if (!string.Equals(storageProvider, "Redis", StringComparison.OrdinalIgnoreCase))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        db.Database.EnsureCreated();
        Log.Information("SQLite database initialized");
    }

    app.UseSerilogRequestLogging();
    app.UseWebSockets();

    // Log registered message handlers on startup
    var router = app.Services.GetRequiredService<MessageRouter>();
    Log.Information("Registered message handlers: {Handlers}", string.Join(", ", router.RegisteredTypes));

    // Single WebSocket endpoint
    app.Map("/ws", async (HttpContext context) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connections only");
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
        await handler.HandleAsync(socket, context.RequestAborted);
    });

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
