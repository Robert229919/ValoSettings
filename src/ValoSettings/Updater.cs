using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ValoSettings;

/// <summary>Vérifie sur GitHub si une version plus récente est publiée (aucune donnée perso envoyée).</summary>
static class Updater
{
    const string Repo = "Robert229919/ValoSettings";
    static string ReleasesUrl => $"https://github.com/{Repo}/releases/latest";

    // Ignore le 4e numéro : 1.1.0 et 1.1.0.0 sont la même version.
    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    static Version Current => Norm(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static async Task<JsonNode> CheckAsync()
    {
        var res = new JsonObject { ["current"] = Current.ToString(3), ["available"] = false };
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ValoSettings-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = JsonNode.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            var tag = (string?)json?["tag_name"] ?? "";
            if (Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            {
                latest = Norm(latest);
                res["latest"] = latest.ToString(3);
                res["available"] = latest > Current;
            }
        }
        catch { /* hors ligne ou limite GitHub : on ne dérange pas l'utilisateur */ }
        return res;
    }

    public static JsonNode OpenReleasePage()
    {
        Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
        return true;
    }
}
