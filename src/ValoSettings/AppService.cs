using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ValoSettings;

/// <summary>Actions métier : enregistrer, appliquer, exporter, importer.</summary>
public static class AppService
{
    /// <param name="include">Ids choisis (voir <see cref="Categories.Items"/>) ; null = tout enregistrer.</param>
    public static async Task<Profile> SaveCurrentAsync(RiotSession s, string name, ISet<string>? include = null)
    {
        var settings = await RiotApi.GetSettingsAsync(s);
        var graphics = LocalFiles.Read(s.Puuid);
        if (settings is null && graphics is null)
            throw new RiotException($"Aucun settings trouvé pour {s.RiotId} (le compte n'a jamais lancé Valorant ?).");
        var p = new Profile
        {
            Name = name, SourceRiotId = s.RiotId, SourcePuuid = s.Puuid,
            Settings = settings, Graphics = graphics,
        };
        if (include is not null) ApplySelection(p, include);
        ProfileStore.Save(p);
        return p;
    }

    /// <summary>Réduit un profil complet aux éléments choisis et calcule ses catégories complètes.</summary>
    public static void ApplySelection(Profile p, ISet<string> include)
    {
        var available = Categories.Items(p.Settings, p.Graphics);
        int kept = 0;
        if (p.Settings is not null)
        {
            (p.Settings, kept) = Categories.Filter(p.Settings, include);
        }
        if (p.Graphics is not null && !include.Contains(Categories.GraphicsId)) p.Graphics = null;
        if (p.Graphics is not null) kept++;
        if (kept == 0) throw new RiotException("Choisis au moins un setting à enregistrer.");
        p.FullCategories = Categories.FullCategories(available, include);
    }

    /// <summary>Noms de catégories → ensemble (null reste null = tout complet).</summary>
    public static HashSet<Category>? ParseCategories(List<string>? names) =>
        names?.Select(n => Enum.TryParse<Category>(n, out var c) ? c : (Category?)null)
              .Where(c => c is not null).Select(c => c!.Value).ToHashSet();

    public static async Task ApplyAsync(RiotSession s, Profile p, ISet<Category> cats, Action<string> log)
    {
        if (cats.Count == 0) throw new RiotException("Coche au moins une catégorie.");
        if (LocalFiles.IsValorantRunning())
            throw new RiotException("Valorant est lancé. Ferme le jeu (garde le Riot Client ouvert) puis réessaie : " +
                                    "sinon le jeu peut réécrire ses anciens settings par-dessus.");

        log($"Lecture des settings actuels de {s.RiotId}…");
        var current = await RiotApi.GetSettingsAsync(s);
        var backup = new Profile
        {
            Name = $"Sauvegarde {s.RiotId} {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
            SourceRiotId = s.RiotId, SourcePuuid = s.Puuid, IsBackup = true,
            Settings = current, Graphics = LocalFiles.Read(s.Puuid),
            ExpiresAt = DateTime.Now + ProfileStore.BackupLifetime,
        };
        ProfileStore.Save(backup);
        log($"Sauvegarde automatique ({ProfileStore.BackupLifetime.TotalMinutes:0} min) : {backup.Name}");

        var cloud = new HashSet<Category>(cats);
        cloud.Remove(Category.Graphismes);
        if (cloud.Count > 0)
        {
            if (p.Settings is null)
                log("Ce profil ne contient pas de settings de compte, rien à envoyer.");
            else
            {
                var merged = Categories.Merge(current, p.Settings, cloud, ParseCategories(p.FullCategories));
                var (values, keybinds) = Categories.Diff(current, merged, cloud);
                if (values == 0 && !keybinds)
                    log("Settings de compte déjà identiques, rien à envoyer.");
                else
                {
                    log($"Envoi à Riot : {values} valeur(s) modifiée(s){(keybinds ? " + touches" : "")}…");
                    await RiotApi.SaveSettingsAsync(s, merged);
                    var check = await RiotApi.GetSettingsAsync(s);
                    var (left, kbLeft) = Categories.Diff(check, merged, cloud);
                    log(left == 0 && !kbLeft
                        ? "✔ Settings de compte appliqués et vérifiés côté Riot."
                        : $"⚠ Riot a accepté l'envoi mais {left} valeur(s){(kbLeft ? " et les touches" : "")} diffèrent à la relecture.");
                }
            }
        }

        if (cats.Contains(Category.Graphismes))
        {
            if (p.Graphics is null) log("Ce profil ne contient pas de graphismes, ignoré.");
            else log("✔ Graphismes : " + LocalFiles.Write(s.Puuid, p.Graphics));
        }
        log($"Terminé. Lance Valorant sur {s.RiotId} pour voir le résultat.");
    }

    // ---------- Export / import ----------

    public const string FileExtension = ".valoprofile";
    const string CodePrefix = "VALOSETTINGS1:";

    static string Serialize(Profile p) => JsonSerializer.Serialize(new Profile
    {
        Name = p.Name, CreatedAt = p.CreatedAt, SourceRiotId = p.SourceRiotId, SourcePuuid = p.SourcePuuid,
        Settings = p.Settings, Graphics = p.Graphics, FullCategories = p.FullCategories,
    }, Json.Pretty);

    public static void ExportFile(Profile p, string path) => File.WriteAllText(path, Serialize(p), new UTF8Encoding(false));

    public static string ToCode(Profile p)
    {
        var raw = Encoding.UTF8.GetBytes(Serialize(p));
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) ds.Write(raw);
        return CodePrefix + Convert.ToBase64String(ms.ToArray());
    }

    public static Profile ImportFile(string path) => ImportJson(File.ReadAllText(path));

    public static Profile ImportCode(string code)
    {
        code = string.Concat(code.Where(c => !char.IsWhiteSpace(c)));
        if (!code.StartsWith(CodePrefix, StringComparison.Ordinal))
            throw new RiotException("Ce n'est pas un code ValoSettings (il doit commencer par VALOSETTINGS1:).");
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(code[CodePrefix.Length..]));
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var sr = new StreamReader(ds, Encoding.UTF8);
            return ImportJson(sr.ReadToEnd());
        }
        catch (Exception e) when (e is FormatException or InvalidDataException)
        {
            throw new RiotException("Code incomplet ou abîmé : recopie-le en entier.");
        }
    }

    static Profile ImportJson(string json)
    {
        Profile? p;
        try { p = JsonSerializer.Deserialize<Profile>(json); }
        catch (JsonException) { throw new RiotException("Fichier de profil illisible."); }
        if (p is null || (p.Settings is null && p.Graphics is null))
            throw new RiotException("Ce profil ne contient aucun setting.");
        p.IsBackup = false;
        p.FilePath = "";
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Profil importé";
        ProfileStore.Save(p);
        return p;
    }
}
