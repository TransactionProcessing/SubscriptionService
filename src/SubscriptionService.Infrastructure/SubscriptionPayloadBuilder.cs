using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SubscriptionService.Infrastructure;

internal static class SubscriptionPayloadBuilder
{
    internal static byte[] Build(ReadOnlyMemory<byte> originalPayload, string eventId)
    {
        var payloadText = Encoding.UTF8.GetString(originalPayload.Span);

        try
        {
            var payloadNode = JsonNode.Parse(payloadText);

            if (payloadNode is JsonObject payloadObject)
            {
                payloadObject["EventId"] = eventId;
                return JsonSerializer.SerializeToUtf8Bytes(payloadObject);
            }

            return JsonSerializer.SerializeToUtf8Bytes(new JsonObject
            {
                ["EventId"] = eventId,
                ["Payload"] = payloadNode
            });
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToUtf8Bytes(new JsonObject
            {
                ["EventId"] = eventId,
                ["Payload"] = Convert.ToBase64String(originalPayload.ToArray())
            });
        }
    }
}
