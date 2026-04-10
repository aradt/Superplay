using Microsoft.Extensions.Configuration;
using Serilog;
using Superplay.Client;
using Superplay.Shared;
using Superplay.Shared.Enums;
using Superplay.Shared.Messages;

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
        Console.WriteLine("3. Test Duplicate Request (idempotency)");
        Console.WriteLine("4. Test Duplicate Gift (idempotency)");
        Console.WriteLine("5. Exit");
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
                await HandleDuplicateRequest(client);
                break;
            case "4":
                await HandleDuplicateGift(client);
                break;
            case "5":
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

static async Task HandleDuplicateRequest(GameClient client)
{
    Log.Information("--- Idempotency Test ---");
    Log.Information("Sending UpdateResources +100 Coins TWICE with the SAME RequestId...");

    var request = new UpdateResourcesRequest { ResourceType = ResourceType.Coins, ResourceValue = 100 };
    var envelope = MessageEnvelope.Request("UpdateResources", request);

    // First send
    Log.Information("Send #1 (RequestId: {RequestId})", envelope.RequestId);
    var response1 = await client.SendRawEnvelopeAsync(envelope);
    if (response1?.Success == true)
    {
        var payload1 = response1.DeserializePayload<UpdateResourcesResponse>();
        Log.Information("Response #1: Balance = {Balance}", payload1?.NewBalance);
    }
    else
    {
        Log.Warning("Response #1: {Error}", response1?.Error);
    }

    // Second send — same envelope, same RequestId
    Log.Information("Send #2 (same RequestId: {RequestId})", envelope.RequestId);
    var response2 = await client.SendRawEnvelopeAsync(envelope);
    if (response2?.Success == true)
    {
        var payload2 = response2.DeserializePayload<UpdateResourcesResponse>();
        Log.Information("Response #2: Balance = {Balance} (should be same as #1 — cached)", payload2?.NewBalance);
    }
    else
    {
        Log.Warning("Response #2: {Error}", response2?.Error);
    }

    Log.Information("--- End Idempotency Test ---");
}

static async Task HandleDuplicateGift(GameClient client)
{
    Console.Write("Enter friend's Player ID: ");
    var friendId = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(friendId))
    {
        Console.WriteLine("Player ID cannot be empty.");
        return;
    }

    Log.Information("--- Gift Idempotency Test ---");
    Log.Information("Sending gift of 10 Coins to {FriendId} TWICE with the SAME RequestId...", friendId);

    var request = new SendGiftRequest
    {
        FriendPlayerId = friendId,
        ResourceType = ResourceType.Coins,
        ResourceValue = 10
    };
    var envelope = MessageEnvelope.Request("SendGift", request);

    // First send
    Log.Information("Send #1 (RequestId: {RequestId})", envelope.RequestId);
    var response1 = await client.SendRawEnvelopeAsync(envelope);
    if (response1?.Success == true)
    {
        var payload1 = response1.DeserializePayload<SendGiftResponse>();
        Log.Information("Response #1: Your balance = {Balance}", payload1?.NewBalance);
    }
    else
    {
        Log.Warning("Response #1: {Error}", response1?.Error);
    }

    // Second send — same envelope, same RequestId
    Log.Information("Send #2 (same RequestId: {RequestId})", envelope.RequestId);
    var response2 = await client.SendRawEnvelopeAsync(envelope);
    if (response2?.Success == true)
    {
        var payload2 = response2.DeserializePayload<SendGiftResponse>();
        Log.Information("Response #2: Your balance = {Balance} (should be same as #1 — cached)", payload2?.NewBalance);
    }
    else
    {
        Log.Warning("Response #2: {Error}", response2?.Error);
    }

    Log.Information("--- End Gift Idempotency Test ---");
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
