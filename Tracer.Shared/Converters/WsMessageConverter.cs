// Tracer.Shared/Converters/WsMessageConverter.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracer.Shared.Models;

namespace Tracer.Shared.Converters
{
    public class WsMessageConverter : JsonConverter<WsMessage>
    {
        public override WsMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return null;

            var type = (WsMessageType)typeProp.GetInt32();

            return type switch
            {
                WsMessageType.Action => JsonSerializer.Deserialize<WsActionMessage>(root.GetRawText(), options),
                WsMessageType.Notification => JsonSerializer.Deserialize<WsNotificationMessage>(root.GetRawText(), options),
                WsMessageType.File => JsonSerializer.Deserialize<WsFileMessage>(root.GetRawText(), options),
                _ => null
            };
        }

        public override void Write(Utf8JsonWriter writer, WsMessage value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, (object)value, options);
    }
}