using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using ActiMetrics.Shared.Converters;

namespace ActiMetrics.Shared.Models
{

    public enum WsMessageType
    {
        Action,
        File,
        Notification
    }

    public enum WsActionType
    {
        Lock,
        Restart,
        Shutdown,
        Logoff,
        Sync,
        Screenshot,
        RestartApp
    }


    [JsonConverter(typeof(WsMessageConverter))]
    public abstract class WsMessage
    {
        public WsMessageType Type { get; set; }
    }

    public class WsActionMessage : WsMessage
    {
        public WsActionType Action { get; set; }
    }

    public class WsNotificationMessage : WsMessage
    {
        public string Title { get; set; } = "Tracer te informa";
        public string Text { get; set; } = string.Empty;
    }
    public class WsFileMessage : WsMessage
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty; // ← coincide con TS
        public string? SavePath { get; set; }
    }
}
