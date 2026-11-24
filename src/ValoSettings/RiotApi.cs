using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ValoSettings;

public sealed class RiotException(string message) : Exception(message);

/// <summary>Compte actuellement connecté dans le Riot Client.</summary>
public sealed record RiotSession(string Puuid, string AccessToken, string Entitlement, string RiotId);

/// <summary>
/// Accès à l'API locale du Riot Client (lockfile) et au service "player preferences"
/// où Valorant stocke les settings du compte (Ares.PlayerSettings).
/// </summary>
public static class RiotApi
{
    public const string PrefType = "Ares.PlayerSettings";

    static readonly HttpClient Remote = new() { Timeout = TimeSpan.FromSeconds(20) };

    static string LockfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Riot Games", "Riot Client", "Config", "lockfile");

    public static async Task<RiotSession> ConnectAsync()
    {
        if (!File.Exists(LockfilePath))
            throw new RiotException("Riot Client non lancé : ouvre-le et connecte-toi au compte.");

        string text;
        using (var fs = new FileStream(LockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs))
            text = (await sr.ReadToEndAsync()).Trim();

        // Format : name:pid:port:password:protocol
        var parts = text.Split(':');
        if (parts.Length < 5) throw new RiotException("Lockfile du Riot Client illisible.");

        var handler = new HttpClientHandler
        {
            // Le Riot Client utilise un certificat auto-signé, uniquement en local.
            ServerCertificateCustomValidationCallback = (req, _, _, _) => req.RequestUri?.Host == "127.0.0.1",
        };
        using var local = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{parts[4]}://127.0.0.1:{parts[2]}"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        local.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + parts[3])));

        JsonNode? tok;
        try
        {
            using var r = await local.GetAsync("/entitlements/v1/token");
            if (!r.IsSuccessStatusCode) throw new RiotException("Aucun compte connecté dans le Riot Client.");
            tok = JsonNode.Parse(await r.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException)
        {
            throw new RiotException("Impossible de joindre le Riot Client (est-il bien lancé ?).");
        }

        var access = Json.Str(tok?["accessToken"]);
        var ent = Json.Str(tok?["token"]);
        var puuid = Json.Str(tok?["subject"]);
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(ent) || string.IsNullOrEmpty(puuid))
            throw new RiotException("Aucun compte connecté dans le Riot Client.");

        var riotId = await TryRiotIdAsync(local) ?? puuid;
        return new RiotSession(puuid, access, ent, riotId);
    }

    static async Task<string?> TryRiotIdAsync(HttpClient local)
    {
        var candidates = new[]
        {
            ("/player-account/aliases/v1/active", "game_name", "tag_line"),
            ("/chat/v1/session", "game_name", "game_tag"),
        };
        foreach (var (path, nameKey, tagKey) in candidates)
        {
            try
            {
                using var r = await local.GetAsync(path);
                if (!r.IsSuccessStatusCode) continue;
                var j = JsonNode.Parse(await r.Content.ReadAsStringAsync());
                var n = Json.Str(j?[nameKey]);
                var t = Json.Str(j?[tagKey]);
                if (!string.IsNullOrWhiteSpace(n))
                    return string.IsNullOrWhiteSpace(t) ? n : $"{n}#{t}";
            }
            catch { /* on essaie la suivante */ }
        }
        return null;
    }

    static HttpRequestMessage Req(HttpMethod m, string url, RiotSession s)
    {
        var q = new HttpRequestMessage(m, url);
        q.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
        q.Headers.Add("X-Riot-Entitlements-JWT", s.Entitlement);
        return q;
    }

    /// <summary>Settings Valorant du compte connecté (null si le compte n'en a aucun).</summary>
    public static async Task<JsonObject?> GetSettingsAsync(RiotSession s)
    {
        var url = $"{AppConfig.Current.PreferencesHost.TrimEnd('/')}/playerPref/v3/getPreference/{PrefType}";
        using var q = Req(HttpMethod.Get, url, s);
        using var r = await Remote.SendAsync(q);
        var body = await r.Content.ReadAsStringAsync();
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        if (!r.IsSuccessStatusCode)
            throw new RiotException($"Lecture des settings refusée par Riot (HTTP {(int)r.StatusCode}) : {Trunc(body)}");
        var data = Json.Str(JsonNode.Parse(body)?["data"]);
        return string.IsNullOrEmpty(data) ? null : Decode(data);
    }

    public static async Task SaveSettingsAsync(RiotSession s, JsonObject settings)
    {
        var url = $"{AppConfig.Current.PreferencesHost.TrimEnd('/')}/playerPref/v3/savePreference";
        var payload = new JsonObject { ["type"] = PrefType, ["data"] = Encode(settings) };
        using var q = Req(HttpMethod.Put, url, s);
        q.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var r = await Remote.SendAsync(q);
        if (!r.IsSuccessStatusCode)
            throw new RiotException($"Écriture des settings refusée par Riot (HTTP {(int)r.StatusCode}) : {Trunc(await r.Content.ReadAsStringAsync())}");
    }

    // "data" = base64 d'un flux DEFLATE brut (sans en-tête zlib) contenant le JSON.
    public static JsonObject Decode(string b64)
    {
        using var ms = new MemoryStream(Convert.FromBase64String(b64));
        using var ds = new DeflateStream(ms, CompressionMode.Decompress);
        return JsonNode.Parse(ds)?.AsObject() ?? throw new RiotException("Settings Riot vides ou illisibles.");
    }

    public static string Encode(JsonObject o)
    {
        var raw = Encoding.UTF8.GetBytes(o.ToJsonString());
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            ds.Write(raw);
        return Convert.ToBase64String(ms.ToArray());
    }

    static string Trunc(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

public static class Json
{
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string? Str(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
