using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TrayPilot;

internal static class UpdateChecker
{
    internal const string ProjectUrl = "https://github.com/HCLonely/TrayPilot";
    internal static Version CurrentVersion => typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
    internal static string CurrentVersionText => typeof(UpdateChecker).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? CurrentVersion.ToString(3);
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    internal sealed record Release(Version Version, string Tag)
    {
        internal string Url => ProjectUrl + "/releases/tag/" + Uri.EscapeDataString(Tag);
        internal bool IsNewer => Version > CurrentVersion;
    }

    internal static async Task<Release?> CheckAsync(CancellationToken cancellationToken, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/HCLonely/TrayPilot/releases/latest");
        request.Headers.UserAgent.ParseAdd("TrayPilot/" + CurrentVersionText);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await (client ?? Client).SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(L.T("invalidUpdateResponse"));
        string tag = tagValue.GetString()!;
        string number = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        if (!Version.TryParse(number, out var version)) throw new InvalidDataException(L.T("invalidUpdateResponse"));
        // Normalize absent build/revision components before comparing with assembly versions.
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        return new Release(version, tag);
    }
}
