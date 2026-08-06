using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ActiMetrics.Shared.Converters;

namespace ActiMetrics.Shared.Models
{

    public enum WsMessageType
    {
        Action,
        File,
        Notification,
        SyncData,
        UserInfo
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

    // Agente → servidor, enviado al conectar/reconectar el WebSocket
    public class WsSyncDataMessage : WsMessage
    {
        public string? Hostname { get; set; }
        public string? Username { get; set; }
        public string? MachineBrand { get; set; }
        public string? MachineModel { get; set; }
    }

    // Servidor → agente, respuesta al SyncData con datos de usuario/rol/permisos
    public class WsUserInfoMessage : WsMessage
    {
        public string? Appuser_Id { get; set; }
        public string? Full_Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public JsonElement? Access_Level { get; set; }
        public string? Group { get; set; }
        public int? Group_Id { get; set; }
        public string? Absence_Status { get; set; }
        // Preferencias generales del agente por grupo (ver AgentPreferences en tracer-ingestor),
        // ej. auto_shutdown_enabled. JSON libre igual que Access_Level: una clave ausente se
        // trata como "en default" en vez de reventar si el backend agrega una preferencia nueva.
        public JsonElement? Preferences { get; set; }
    }
}
