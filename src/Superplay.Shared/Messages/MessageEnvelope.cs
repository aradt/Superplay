using System.Text.Json;
using System.Text.Json.Serialization;

namespace Superplay.Shared.Messages;

/// <summary>
/// Wire-format envelope that wraps every WebSocket message exchanged between client and server.
/// Provides a uniform structure for requests, success responses, and error responses,
/// with the actual message data carried as a polymorphic <see cref="Payload"/>.
/// </summary>
public sealed record MessageEnvelope
{
    /// <summary>Message type identifier used for routing (e.g., "Login", "LoginResponse", "GiftEvent").</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Serialized message body. Null for error responses that carry no payload.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    /// <summary>Indicates whether the operation succeeded. Null for outgoing requests.</summary>
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    /// <summary>Human-readable error description. Present only when <see cref="Success"/> is false.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Creates a request envelope to be sent from client to server.
    /// </summary>
    /// <param name="type">The message type (e.g., "Login", "UpdateResources").</param>
    /// <param name="payload">The request payload object, which will be JSON-serialized.</param>
    /// <returns>A <see cref="MessageEnvelope"/> ready for transmission.</returns>
    public static MessageEnvelope Request(string type, object payload)
    {
        var json = JsonSerializer.SerializeToElement(payload, SerializerOptions.Default);
        return new MessageEnvelope { Type = type, Payload = json };
    }

    /// <summary>
    /// Creates a success response envelope sent from server to client.
    /// </summary>
    /// <param name="type">The response type (e.g., "LoginResponse").</param>
    /// <param name="payload">The response payload object, which will be JSON-serialized.</param>
    /// <returns>A <see cref="MessageEnvelope"/> with <see cref="Success"/> set to true.</returns>
    public static MessageEnvelope SuccessResponse(string type, object payload)
    {
        var json = JsonSerializer.SerializeToElement(payload, SerializerOptions.Default);
        return new MessageEnvelope { Type = type, Payload = json, Success = true };
    }

    /// <summary>
    /// Creates an error response envelope sent from server to client.
    /// </summary>
    /// <param name="type">The response type that failed.</param>
    /// <param name="error">Human-readable error description.</param>
    /// <returns>A <see cref="MessageEnvelope"/> with <see cref="Success"/> set to false and no payload.</returns>
    public static MessageEnvelope ErrorResponse(string type, string error)
    {
        return new MessageEnvelope { Type = type, Success = false, Error = error };
    }

    /// <summary>
    /// Deserializes the <see cref="Payload"/> JSON element into a strongly-typed object.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <returns>The deserialized object, or null if the payload is absent.</returns>
    public T? DeserializePayload<T>() where T : class
    {
        return Payload.HasValue
            ? JsonSerializer.Deserialize<T>(Payload.Value.GetRawText(), SerializerOptions.Default)
            : null;
    }
}

/// <summary>
/// Shared JSON serializer options used consistently across client and server
/// to ensure wire-format compatibility (camelCase naming, null omission, case-insensitive reads).
/// </summary>
public static class SerializerOptions
{
    /// <summary>The default serializer options instance. Reused to benefit from internal caching.</summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
