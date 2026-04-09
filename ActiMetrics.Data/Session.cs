using System.Text.Json;

public class Session
{
    private readonly string _folder;
    private readonly string _file;
    private string? _email;

    public Session()
    {
        _folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tracer"
        );
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
}
