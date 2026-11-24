using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ValoSettings;

/// <summary>Graphismes : stockés uniquement en local (par compte et par PC), pas sur les serveurs Riot.</summary>
public sealed class LocalGraphics
{
    /// <summary>Contenu complet de WindowsClient\GameUserSettings.ini (résolution, qualité, FPS…).</summary>
    public string? GameUserSettingsIni { get; set; }

    /// <summary>Lignes graphiques de Windows\RiotUserSettings.ini (UIQuality, TextureQuality…).</summary>
    public Dictionary<string, string> RiotUserSettings { get; set; } = new();
}

public static partial class LocalFiles
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VALORANT", "Saved", "Config");

    public static bool IsValorantRunning() =>
        Process.GetProcessesByName("VALORANT-Win64-Shipping").Length > 0 ||
        Process.GetProcessesByName("VALORANT").Length > 0;

    /// <summary>Dossier local du compte : Config\{puuid}-{région}.</summary>
    public static string? AccountDir(string puuid) =>
        Directory.Exists(Root)
            ? Directory.GetDirectories(Root, puuid + "-*").OrderByDescending(Directory.GetLastWriteTime).FirstOrDefault()
            : null;

    [GeneratedRegex(@"Quality|VSync|AntiAlias|Anisotropic|Clarity|Sharpen|Bloom|Distortion|Shadow|FrameRateLimit|LimitFPS|MaxFPS|Reflex|Gamma|Brightness|Multithread|Letterbox", RegexOptions.IgnoreCase)]
    private static partial Regex GraphicsName();

    static bool IsGraphicsKey(string key)
    {
        var i = key.IndexOf("::", StringComparison.Ordinal);
        var n = i >= 0 ? key[(i + 2)..] : key;
        return GraphicsName().IsMatch(n)
            && !n.Contains("Crosshair", StringComparison.OrdinalIgnoreCase)
            && !n.StartsWith("PlayerPerf", StringComparison.OrdinalIgnoreCase);
    }

    public static LocalGraphics? Read(string puuid)
    {
        var dir = AccountDir(puuid);
        if (dir is null) return null;
        var g = new LocalGraphics();
        var gus = Path.Combine(dir, "WindowsClient", "GameUserSettings.ini");
        if (File.Exists(gus)) g.GameUserSettingsIni = File.ReadAllText(gus);
        var rus = Path.Combine(dir, "Windows", "RiotUserSettings.ini");
        if (File.Exists(rus))
            foreach (var line in File.ReadAllLines(rus))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith('[') || line.StartsWith(';')) continue;
                var k = line[..eq];
                if (IsGraphicsKey(k)) g.RiotUserSettings[k] = line[(eq + 1)..];
            }
        return g.GameUserSettingsIni is null && g.RiotUserSettings.Count == 0 ? null : g;
    }

    /// <summary>Écrit les graphismes sur le dossier local du compte. Renvoie un message de résultat.</summary>
    public static string Write(string puuid, LocalGraphics g)
    {
        var dir = AccountDir(puuid)
            ?? throw new RiotException("Ce compte n'a pas encore de dossier Valorant sur ce PC : lance une partie une fois avec lui.");
        int n = 0;
        if (g.GameUserSettingsIni is not null)
        {
            var gus = Path.Combine(dir, "WindowsClient", "GameUserSettings.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(gus)!);
            File.WriteAllText(gus, g.GameUserSettingsIni);
            n++;
        }
        if (g.RiotUserSettings?.Count > 0)
        {
            var rus = Path.Combine(dir, "Windows", "RiotUserSettings.ini");
            var lines = File.Exists(rus) ? File.ReadAllLines(rus).ToList() : ["[Settings]"];
            // Un profil importé ne doit pouvoir toucher que des clés graphiques, sur une seule ligne.
            var todo = g.RiotUserSettings
                .Where(kv => IsGraphicsKey(kv.Key) && !kv.Key.Contains('=')
                             && !(kv.Key + kv.Value).Any(c => c is '\r' or '\n'))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            for (int i = 0; i < lines.Count; i++)
            {
                var eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                var k = lines[i][..eq];
                if (todo.Remove(k, out var v)) lines[i] = k + "=" + v;
            }
            var at = lines.FindIndex(l => l.Trim() == "[Settings]");
            if (at < 0) { lines.Add("[Settings]"); at = lines.Count - 1; }
            lines.InsertRange(at + 1, todo.Select(kv => kv.Key + "=" + kv.Value));
            Directory.CreateDirectory(Path.GetDirectoryName(rus)!);
            File.WriteAllLines(rus, lines, new UTF8Encoding(false));
            n++;
        }
        return $"{n} fichier(s) local(aux) mis à jour dans {dir}";
    }
}
