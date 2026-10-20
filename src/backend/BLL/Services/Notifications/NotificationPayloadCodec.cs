#nullable enable
using System.Text;
using System.Text.Json;

namespace BLL.Services.Notifications;

public static class NotificationPayloadCodec
{
    public const int MaxUtf8Bytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static (Guid MessageId, string Payload) Decode(NotificationDelivery delivery)
    {
        if (delivery.Body.Length is 0 or > MaxUtf8Bytes
            || !Guid.TryParse(delivery.MessageId, out var id) || id == Guid.Empty)
            throw new InvalidDataException("Notification identity or size is invalid.");
        try
        {
            var payload = Utf8.GetString(delivery.Body.Span);
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Notification envelope is invalid.");
            var matches = document.RootElement.EnumerateObject().Where(property =>
                string.Equals(property.Name, "CorrelationId", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String
                || !Guid.TryParse(matches[0].Value.GetString(), out var embedded) || embedded != id)
                throw new InvalidDataException("Notification identity does not match its envelope.");
            return (id, payload);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException)
        { throw new InvalidDataException("Notification encoding or envelope is invalid."); }
    }

    public static (Guid MessageId, byte[] Body) Encode(string payload)
    {
        try
        {
            if (string.IsNullOrEmpty(payload) || Utf8.GetByteCount(payload) > MaxUtf8Bytes)
                throw new InvalidDataException("Notification size is invalid.");
            var body = Utf8.GetBytes(payload);
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Notification envelope is invalid.");
            var ids = document.RootElement.EnumerateObject().Where(property =>
                string.Equals(property.Name, "CorrelationId", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (ids.Length != 1 || ids[0].Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Notification identity is required.");
            var validated = Decode(new(ids[0].Value.GetString(), body));
            return (validated.MessageId, body);
        }
        catch (Exception error) when (error is JsonException or EncoderFallbackException)
        { throw new InvalidDataException("Notification encoding or envelope is invalid."); }
    }
}
