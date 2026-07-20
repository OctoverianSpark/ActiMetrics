using System.Text.Json;

public class Session
{
    private readonly string _folder;
    private readonly string _file;
    private string? _email;

    public string? AppuserId { get; private set; }
    public string? FullName { get; private set; }
    public string? Role { get; private set; }
    public string? Group { get; private set; }
    public int? GroupId { get; private set; }
    public string? AbsenceStatus { get; private set; }
    public JsonElement? AccessLevel { get; private set; }
    public JsonElement? Preferences { get; private set; }

    // access_level es un JSON libre por rol (ver PERMISSION_GROUPS en tracer-dashboard). Ausencia
    // de la clave se trata como false, igual que en el middleware del dashboard — no hay un
    // "default true" a nivel de lectura, solo al crear un rol nuevo desde el formulario.
    public bool CanCreateTickets =>
        AccessLevel is { } level &&
        level.TryGetProperty("create_tickets", out var prop) &&
        prop.ValueKind == JsonValueKind.True;

    // preferences es un JSON libre por grupo (ver AgentPreferences en tracer-ingestor). Ausencia
    // de la clave (grupo sin configurar, o preferencia agregada en una versión de agente vieja)
    // se trata como false — el apagado automático es opt-in por grupo, nunca el default.
    public bool AutoShutdownEnabled =>
        Preferences is { } prefs &&
        prefs.TryGetProperty("auto_shutdown_enabled", out var prop) &&
        prop.ValueKind == JsonValueKind.True;

    public Session()
    {
        var localAppData =
            Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        _folder = Path.Combine(localAppData, "Tracer");
        _file = Path.Combine(_folder, "sessionUser.json");
        Directory.CreateDirectory(_folder);

        if (!File.Exists(_file))
            File.WriteAllText(_file, string.Empty);

        Validate();
    }

    private void Validate()
    {
        var data = File.ReadAllText(_file);
        if (string.IsNullOrWhiteSpace(data)) return;
        _email = JsonSerializer.Deserialize<string>(data);
        Console.WriteLine($"[SESSION]: Email cargado → {_email}");
    }

    public string? GetEmail() => _email;
    public bool IsAuthenticated() => !string.IsNullOrEmpty(_email);

    public void SaveEmail(string email)
    {
        _email = email;
        File.WriteAllText(_file, JsonSerializer.Serialize(email));
        Console.WriteLine($"[SESSION]: Email guardado → {email}");
    }

    public void Clear()
    {
        _email = null;
        File.WriteAllText(_file, string.Empty);
    }

    public void SetUserInfo(string? appuserId, string? fullName, string? role, string? group, int? groupId, string? absenceStatus, JsonElement? accessLevel, JsonElement? preferences)
    {
        AppuserId = appuserId;
        FullName = fullName;
        Role = role;
        Group = group;
        GroupId = groupId;
        AbsenceStatus = absenceStatus;
        AccessLevel = accessLevel;
        Preferences = preferences;
        Console.WriteLine($"[SESSION]: UserInfo actualizado → role={role}, group={group} (id={groupId}), absence_status={absenceStatus}");
    }
}
