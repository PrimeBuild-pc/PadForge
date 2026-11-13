using System.Text.Json;
using System.Text.Json.Serialization;

namespace PadForge.Core.Profiles;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _root;

    public ProfileStore(string root)
    {
        _root = root;
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadForge", "profiles");

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_root))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(_root, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Profile LoadOrCreateDefault()
    {
        Directory.CreateDirectory(_root);
        string defaultPath = PathFor("Default");
        if (!File.Exists(defaultPath) && List().Count == 0)
        {
            var created = Profile.CreateDefault();
            Save(created);
            return created;
        }

        if (File.Exists(defaultPath))
            return Load("Default");

        string? first = List().FirstOrDefault();
        return first is null ? Profile.CreateDefault() : Load(first);
    }

    public Profile Load(string name)
    {
        string path = PathFor(name);
        if (!File.Exists(path))
            throw new FileNotFoundException("Profile was not found.", path);

        string json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions) ?? new Profile();
        profile.Name = SanitizeName(string.IsNullOrWhiteSpace(profile.Name) ? name : profile.Name);
        profile.RightStick ??= new StickSettings();
        profile.Bindings ??= new Dictionary<string, int>(StringComparer.Ordinal);
        return profile;
    }

    public void Save(Profile profile)
    {
        profile.Name = SanitizeName(profile.Name);
        Directory.CreateDirectory(_root);
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(PathFor(profile.Name), json);
    }

    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Profile";

        var filtered = new string(name.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').ToArray());
        filtered = filtered.Trim();
        if (filtered.Length == 0)
            return "Profile";

        if (filtered.Length > 40)
            filtered = filtered[..40].Trim();

        return filtered.Length == 0 ? "Profile" : filtered;
    }

    private string PathFor(string name)
    {
        string safe = SanitizeName(name);
        string root = Path.GetFullPath(_root);
        if (!root.EndsWith(Path.DirectorySeparatorChar) && !root.EndsWith(Path.AltDirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        string path = Path.GetFullPath(Path.Combine(root, safe + ".json"));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Profile path escaped the profile folder.");

        return path;
    }
}
