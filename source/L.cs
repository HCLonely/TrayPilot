using System.Globalization;
using System.Text.Json;

namespace TrayPilot;

internal static class L
{
    internal sealed class Pack
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new();
    }
    internal static string Folder => Path.Combine(AppContext.BaseDirectory, "languages");
    internal const string FallbackLanguage = "en-US";
    internal static string SystemLanguage => Packs().ContainsKey(CultureInfo.CurrentUICulture.Name)
        ? CultureInfo.CurrentUICulture.Name : FallbackLanguage;
    internal static string Current { get; private set; } = FallbackLanguage;
    static Dictionary<string, string> strings = new();
    static readonly Dictionary<string, string> defaults = LoadDefaults();
    static Dictionary<string, string> LoadDefaults()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("TrayPilot.languages.en-US.json")!;
        return JsonSerializer.Deserialize<Pack>(stream)!.Strings;
    }
    internal static Dictionary<string, Pack> Packs()
    {
        var packs = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);
        const string prefix = "TrayPilot.languages.";
        foreach (string resource in typeof(L).Assembly.GetManifestResourceNames().Where(x => x.StartsWith(prefix) && x.EndsWith(".json")))
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream(resource)!;
            packs[resource[prefix.Length..^5]] = JsonSerializer.Deserialize<Pack>(stream)!;
        }
        if (!Directory.Exists(Folder)) return packs;
        string[] files;
        try { files = Directory.GetFiles(Folder, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return packs; }
        foreach (var file in files)
        {
            try
            {
                var pack = JsonSerializer.Deserialize<Pack>(File.ReadAllText(file));
                if (pack != null && !string.IsNullOrWhiteSpace(pack.Name) && pack.Strings != null)
                    packs[Path.GetFileNameWithoutExtension(file)] = pack;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return packs;
    }
    internal static void Set(string? language)
    {
        var packs = Packs();
        Current = language != null && packs.ContainsKey(language) ? language : FallbackLanguage;
        strings = packs.TryGetValue(Current, out var pack) ? pack.Strings : new();
    }
    internal static string T(string key) => strings.TryGetValue(key, out var value) ? value : Default(key);
    static string Default(string key) => defaults.TryGetValue(key, out var value) ? value : key;
    internal static string F(string key, params object[] args)
    {
        try { return string.Format(T(key), args); }
        catch (FormatException) { return string.Format(Default(key), args); }
    }
}
