using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ValoSettings;

/// <summary>Ce que l'appli retient d'un compte (Riot ID vu à la connexion, surnom choisi).</summary>
public sealed class AccountInfo
{
    public string? RiotId { get; set; }
    public string? Label { get; set; }
    public DateTime? LastSeen { get; set; }

    /// <summary>Date d'activité avant une application faite par cet outil (qui réécrit les fichiers du compte).</summary>
    public DateTime? PlayedOverride { get; set; }
    public DateTime? AppliedAt { get; set; }
}

/// <summary>Compte Valorant trouvé dans %LOCALAPPDATA%\VALORANT\Saved\Config.</summary>
public sealed record LocalAccount(string Puuid, string Region, DateTime LastPlayed, double? Sensitivity);

public static partial class AccountStore
{
    static string FilePath => Path.Combine(AppConfig.Dir, "accounts.json");
    static readonly object Gate = new();

    public static Dictionary<string, AccountInfo> Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Dictionary<string, AccountInfo>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { /* fichier corrompu : on repart de zéro */ }
            return new();
        }
    }

    static void Save(Dictionary<string, AccountInfo> d)
    {
        lock (Gate) File.WriteAllText(FilePath, JsonSerializer.Serialize(d, Json.Pretty));
    }

    /// <summary>Mémorise le Riot ID du compte connecté.</summary>
    public static void Seen(string puuid, string riotId)
    {
        var d = Load();
        var a = d.TryGetValue(puuid, out var x) ? x : d[puuid] = new AccountInfo();
        if (a.RiotId == riotId && a.LastSeen > DateTime.Now.AddMinutes(-10)) return;
        a.RiotId = riotId;
        a.LastSeen = DateTime.Now;
        Save(d);
    }

    public static void SetLabel(string puuid, string? label)
    {
        var d = Load();
        var a = d.TryGetValue(puuid, out var x) ? x : d[puuid] = new AccountInfo();
        a.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Save(d);
    }

    [GeneratedRegex(@"^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})-(\w+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AccountDirName();

    /// <summary>Comptes ayant déjà lancé Valorant sur ce PC (un dossier par compte).</summary>
    public static List<LocalAccount> ListLocal()
    {
        var root = LocalFiles.Root;
        var list = new List<LocalAccount>();
        var known = Load().GroupBy(kv => kv.Key.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Value);
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var m = AccountDirName().Match(Path.GetFileName(dir));
            if (!m.Success) continue;
            var rus = Path.Combine(dir, "Windows", "RiotUserSettings.ini");
            var gus = Path.Combine(dir, "WindowsClient", "GameUserSettings.ini");
            var times = new[] { rus, gus }.Where(File.Exists).Select(File.GetLastWriteTime).ToList();
            var last = LastWriteOf(dir);
            var puuid = m.Groups[1].Value.ToLowerInvariant();
            // Les fichiers modifiés par une application de profil (ou la synchro Riot qui suit) ne comptent pas comme une partie.
            if (known.TryGetValue(puuid, out var inf) && inf.PlayedOverride is DateTime keep && inf.AppliedAt is DateTime ap && last <= ap.AddMinutes(10))
                last = keep;
            list.Add(new LocalAccount(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToUpperInvariant(), last, ReadSensitivity(rus)));
        }
        return list;
    }

    static DateTime LastWriteOf(string dir)
    {
        var times = new[] { Path.Combine(dir, "Windows", "RiotUserSettings.ini"), Path.Combine(dir, "WindowsClient", "GameUserSettings.ini") }
            .Where(File.Exists).Select(File.GetLastWriteTime).ToList();
        return times.Count > 0 ? times.Max() : Directory.GetLastWriteTime(dir);
    }

    /// <summary>À appeler avant et après une application de profil : garde la date d'activité d'origine du compte.</summary>
    public static void MarkOwnApply(string puuid)
    {
        var d = Load();
        var key = d.Keys.FirstOrDefault(k => string.Equals(k, puuid, StringComparison.OrdinalIgnoreCase)) ?? puuid;
        var a = d.TryGetValue(key, out var x) ? x : d[key] = new AccountInfo();
        var now = DateTime.Now;
        if (a.PlayedOverride is null || a.AppliedAt is null || now > a.AppliedAt.Value.AddMinutes(10))
        {
            var dir = LocalFiles.AccountDir(puuid);
            a.PlayedOverride = dir is null ? null : LastWriteOf(dir);
        }
        a.AppliedAt = now;
        Save(d);
    }

    static double? ReadSensitivity(string rus)
    {
        if (!File.Exists(rus)) return null;
        try
        {
            const string key = "EAresFloatSettingName::MouseSensitivity=";
            var line = File.ReadLines(rus).FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal));
            return line is not null && double.TryParse(line[key.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? Math.Round(v, 4) : null;
        }
        catch { return null; }
    }
}
