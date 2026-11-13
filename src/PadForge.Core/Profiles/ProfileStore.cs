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

    /// <summary>Name of the profile that was open when the app last closed.</summary>
    public string? LastUsed
    {
        get
        {
            try
            {
                return File.Exists(LastUsedPath) ? File.ReadAllText(LastUsedPath).Trim() : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(_root);
                File.WriteAllText(LastUsedPath, SanitizeName(value));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string LastUsedPath => Path.Combine(_root, ".last");

    /// <summary>
    /// Opens the last used profile, then Default, then any profile. A file that
    /// cannot be read is skipped, so a broken profile never stops the app.
    /// </summary>
    public Profile LoadOrCreateDefault()
    {
        Directory.CreateDirectory(_root);
        var candidates = new List<string>();
        if (LastUsed is { Length: > 0 } last)
            candidates.Add(last);
        candidates.Add("Default");
        candidates.AddRange(List());

        foreach (string name in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryLoad(name, out Profile? profile))
                return profile;
        }

        var created = Profile.CreateDefault();
        if (!File.Exists(PathFor(created.Name)))
            Save(created);
        return created;
    }

    public bool TryLoad(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Profile? profile)
    {
        try
        {
            profile = Load(name);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            profile = null;
            return false;
        }
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    public void Delete(string name)
    {
        string path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
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
        profile.Upgrade();
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
