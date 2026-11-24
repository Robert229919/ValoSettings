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
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var m = AccountDirName().Match(Path.GetFileName(dir));
            if (!m.Success) continue;
            var rus = Path.Combine(dir, "Windows", "RiotUserSettings.ini");
            var gus = Path.Combine(dir, "WindowsClient", "GameUserSettings.ini");
            var times = new[] { rus, gus }.Where(File.Exists).Select(File.GetLastWriteTime).ToList();
            var last = times.Count > 0 ? times.Max() : Directory.GetLastWriteTime(dir);
            list.Add(new LocalAccount(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToUpperInvariant(), last, ReadSensitivity(rus)));
        }
        return list;
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
