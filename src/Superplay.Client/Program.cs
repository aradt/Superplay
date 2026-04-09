using Microsoft.Extensions.Configuration;
using Serilog;
using Superplay.Client;
using Superplay.Shared;
using Superplay.Shared.Enums;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

var serverUrl = args.Length > 0 ? args[0] : configuration["Server:Url"] ?? Defaults.ClientWebSocketUrl;

Log.Information("Superplay Game Client");
Log.Information("Server URL: {ServerUrl}", serverUrl);

using var client = new GameClient(serverUrl, Log.Logger);

try
{
    await client.ConnectAsync();

    // Auto-login with a device ID
    Console.Write("Enter Device ID: ");
    var deviceId = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(deviceId))
    {
        Log.Error("Device ID cannot be empty");
        return;
    }

    var loginResult = await client.LoginAsync(deviceId);
    if (loginResult is null)
    {
        Log.Error("Login failed, exiting");
        return;
    }

    // Interactive menu loop
    while (client.IsConnected)
    {
        Console.WriteLine();
        Console.WriteLine("=== Menu ===");
        Console.WriteLine("1. Update Resources");
        Console.WriteLine("2. Send Gift");
        Console.WriteLine("3. Exit");
        Console.Write("Choose: ");

        var choice = Console.ReadLine()?.Trim();

        switch (choice)
        {
            case "1":
                await HandleUpdateResources(client);
                break;
            case "2":
                await HandleSendGift(client);
                break;
            case "3":
                await client.DisconnectAsync();
                Log.Information("Goodbye!");
                return;
            default:
                Console.WriteLine("Invalid choice, try again.");
                break;
        }
    }
}
catch (Exception ex)
{
    Log.Error(ex, "Client error");
}
finally
{
    Log.CloseAndFlush();
}

static async Task HandleUpdateResources(GameClient client)
{
    var resourceType = PromptResourceType();
    if (resourceType is null) return;

    Console.Write("Enter value (positive to add, negative to subtract): ");
    if (!long.TryParse(Console.ReadLine()?.Trim(), out var value) || value == 0)
    {
        Console.WriteLine("Invalid value.");
        return;
    }

    await client.UpdateResourcesAsync(resourceType.Value, value);
}

static async Task HandleSendGift(GameClient client)
{
    Console.Write("Enter friend's Player ID: ");
    var friendId = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(friendId))
    {
        Console.WriteLine("Player ID cannot be empty.");
        return;
    }

    var resourceType = PromptResourceType();
    if (resourceType is null) return;

    Console.Write("Enter gift amount (positive): ");
    if (!long.TryParse(Console.ReadLine()?.Trim(), out var value) || value <= 0)
    {
        Console.WriteLine("Invalid amount.");
        return;
    }

    await client.SendGiftAsync(friendId, resourceType.Value, value);
}

static ResourceType? PromptResourceType()
{
    Console.Write("Resource type (1=Coins, 2=Rolls): ");
    var input = Console.ReadLine()?.Trim();
    return input switch
    {
        "1" => ResourceType.Coins,
        "2" => ResourceType.Rolls,
        _ => null
    };
}
