using System.Text.Json;
using Superplay.Shared.Enums;
using Superplay.Shared.Messages;

namespace Superplay.Tests.Messages;

public sealed class MessageEnvelopeTests
{
    [Fact]
    public void Request_CreatesEnvelopeWithTypeAndPayload()
    {
        var payload = new LoginRequest { DeviceId = "device-123" };

        var envelope = MessageEnvelope.Request("Login", payload);

        Assert.Equal("Login", envelope.Type);
        Assert.True(envelope.Payload.HasValue);
        Assert.Null(envelope.Success);
        Assert.Null(envelope.Error);
    }

    [Fact]
    public void SuccessResponse_SetsSuccessToTrue()
    {
        var payload = new LoginResponse { PlayerId = "p1", Coins = 100, Rolls = 5 };

        var envelope = MessageEnvelope.SuccessResponse("LoginResponse", payload);

        Assert.Equal("LoginResponse", envelope.Type);
        Assert.True(envelope.Success);
        Assert.Null(envelope.Error);
        Assert.True(envelope.Payload.HasValue);
    }

    [Fact]
    public void ErrorResponse_SetsSuccessToFalseAndIncludesError()
    {
        var envelope = MessageEnvelope.ErrorResponse("LoginResponse", "Device not found");

        Assert.Equal("LoginResponse", envelope.Type);
        Assert.False(envelope.Success);
        Assert.Equal("Device not found", envelope.Error);
        Assert.Null(envelope.Payload);
    }

    [Fact]
    public void DeserializePayload_ReturnsTypedObject()
    {
        var original = new LoginRequest { DeviceId = "device-abc" };
        var envelope = MessageEnvelope.Request("Login", original);

        var deserialized = envelope.DeserializePayload<LoginRequest>();

        Assert.NotNull(deserialized);
        Assert.Equal("device-abc", deserialized.DeviceId);
    }

    [Fact]
    public void DeserializePayload_ReturnsNull_WhenPayloadIsNull()
    {
        var envelope = MessageEnvelope.ErrorResponse("Error", "some error");

        var result = envelope.DeserializePayload<LoginRequest>();

        Assert.Null(result);
    }

    [Fact]
    public void DeserializePayload_HandlesComplexPayload()
    {
        var original = new SendGiftRequest
        {
            FriendPlayerId = "friend-1",
            ResourceType = ResourceType.Coins,
            ResourceValue = 50
        };
        var envelope = MessageEnvelope.Request("SendGift", original);

        var deserialized = envelope.DeserializePayload<SendGiftRequest>();

        Assert.NotNull(deserialized);
        Assert.Equal("friend-1", deserialized.FriendPlayerId);
        Assert.Equal(ResourceType.Coins, deserialized.ResourceType);
        Assert.Equal(50, deserialized.ResourceValue);
    }

    [Fact]
    public void RoundTrip_SerializeAndDeserialize_PreservesData()
    {
        var payload = new UpdateResourcesResponse
        {
            ResourceType = ResourceType.Rolls,
            NewBalance = 42
        };
        var original = MessageEnvelope.SuccessResponse("UpdateResourcesResponse", payload);

        var json = JsonSerializer.Serialize(original, SerializerOptions.Default);
        var deserialized = JsonSerializer.Deserialize<MessageEnvelope>(json, SerializerOptions.Default);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Success, deserialized.Success);

        var innerPayload = deserialized.DeserializePayload<UpdateResourcesResponse>();
        Assert.NotNull(innerPayload);
        Assert.Equal(ResourceType.Rolls, innerPayload.ResourceType);
        Assert.Equal(42, innerPayload.NewBalance);
    }

    [Fact]
    public void Serialization_UseCamelCase_PropertyNaming()
    {
        var payload = new LoginResponse { PlayerId = "p1", Coins = 10, Rolls = 20 };
        var envelope = MessageEnvelope.SuccessResponse("LoginResponse", payload);

        var json = JsonSerializer.Serialize(envelope, SerializerOptions.Default);

        Assert.Contains("\"type\":", json);
        Assert.Contains("\"success\":", json);
        Assert.Contains("\"payload\":", json);
        // Verify camelCase in payload
        Assert.Contains("\"playerId\":", json);
        Assert.Contains("\"coins\":", json);
        Assert.Contains("\"rolls\":", json);
    }

    [Fact]
    public void Serialization_OmitsNullFields()
    {
        var envelope = MessageEnvelope.ErrorResponse("Error", "fail");

        var json = JsonSerializer.Serialize(envelope, SerializerOptions.Default);

        // Payload is null, should be omitted due to WhenWritingNull
        Assert.DoesNotContain("\"payload\":", json);
    }

    [Fact]
    public void Deserialization_IsCaseInsensitive()
    {
        var json = """{"Type":"Login","Payload":{"DeviceId":"d1"},"Success":true}""";

        var envelope = JsonSerializer.Deserialize<MessageEnvelope>(json, SerializerOptions.Default);

        Assert.NotNull(envelope);
        Assert.Equal("Login", envelope.Type);
        Assert.True(envelope.Success);
    }

    [Fact]
    public void Request_SerializesEnumValuesCorrectly()
    {
        var payload = new UpdateResourcesRequest
        {
            ResourceType = ResourceType.Rolls,
            ResourceValue = 10
        };

        var envelope = MessageEnvelope.Request("UpdateResources", payload);
        var deserialized = envelope.DeserializePayload<UpdateResourcesRequest>();

        Assert.NotNull(deserialized);
        Assert.Equal(ResourceType.Rolls, deserialized.ResourceType);
        Assert.Equal(10, deserialized.ResourceValue);
    }
}
