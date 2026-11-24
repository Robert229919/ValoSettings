using System.Text.Json.Nodes;

namespace ValoSettings;

public enum Category { Viseur, Sensibilite, Touches, Minimap, Audio, Interface, Graphismes, Exclu }

/// <summary>Classement des settings Valorant par catégorie et fusion source → cible.</summary>
public static class Categories
{
    /// <summary>Catégories proposées dans l'interface, dans l'ordre d'affichage.</summary>
    public static readonly Category[] Selectable =
    [
        Category.Viseur, Category.Sensibilite, Category.Touches, Category.Minimap,
        Category.Audio, Category.Interface, Category.Graphismes,
    ];

    public static string Label(Category c) => c switch
    {
        Category.Viseur => "Viseur",
        Category.Sensibilite => "Sensibilité, souris & manette",
        Category.Touches => "Touches (keybinds)",
        Category.Minimap => "Minimap",
        Category.Audio => "Audio & chat vocal",
        Category.Interface => "Interface, HUD & autres options",
        Category.Graphismes => "Graphismes & résolution (fichiers locaux du PC)",
        _ => c.ToString(),
    };

    static string ShortName(string settingEnum)
    {
        var i = settingEnum.IndexOf("::", StringComparison.Ordinal);
        return i >= 0 ? settingEnum[(i + 2)..] : settingEnum;
    }

    /// <summary>Drapeaux propres au compte (CGU acceptées, tutos vus, versions…) : jamais copiés.</summary>
    public static bool IsAccountFlag(string name) =>
        name.StartsWith("HasSeen", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("HasAccepted", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("HasEver", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("LastSeen", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("LastAccepted", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("Version", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tutorial", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("ModuleComplete", StringComparison.OrdinalIgnoreCase);

    public static Category Classify(string settingEnum)
    {
        var n = ShortName(settingEnum);
        bool Has(params string[] keys) => keys.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (IsAccountFlag(n)) return Category.Exclu;
        if (Has("Crosshair")) return Category.Viseur;
        if (Has("Minimap")) return Category.Minimap;
        if (Has("HoldInput") || n.EndsWith("Key", StringComparison.OrdinalIgnoreCase)) return Category.Touches;
        if (Has("Sensitivity", "InvertMouse", "MouseAccel", "RotationSpeed", "Gamepad", "Controller", "Deadzone"))
            return Category.Sensibilite;
        if (Has("Audio", "Volume", "Sound", "Voice", "Music", "Mic", "Speaker", "HRTF", "PushToTalk"))
            return Category.Audio;
        return Category.Interface;
    }

    static bool IsKeybindKey(string key) => key.Contains("Mapping", StringComparison.OrdinalIgnoreCase);

    static bool IsSettingArray(string key, JsonNode? node) =>
        node is JsonArray a && !IsKeybindKey(key) &&
        (key.EndsWith("Settings", StringComparison.OrdinalIgnoreCase) ||
         a.Any(i => i is JsonObject o && o.ContainsKey("settingEnum")));

    static string EnumOf(JsonNode? item) => Json.Str(item?["settingEnum"]) ?? "";

    /// <summary>Nombre de valeurs par catégorie dans un JSON de settings.</summary>
    public static Dictionary<Category, int> Count(JsonObject? settings)
    {
        var d = Enum.GetValues<Category>().ToDictionary(c => c, _ => 0);
        if (settings is null) return d;
        foreach (var (key, node) in settings)
        {
            if (IsKeybindKey(key) && node is JsonArray km) d[Category.Touches] += km.Count;
            else if (IsSettingArray(key, node))
                foreach (var it in (JsonArray)node!) d[Classify(EnumOf(it))]++;
        }
        return d;
    }

    // ---------- Détail des settings (sélection fine) ----------

    public const string GraphicsId = "graphics";

    /// <summary>Un élément sélectionnable : une valeur de setting, un tableau de touches ou les graphismes.</summary>
    public sealed record SettingItem(string Id, Category Category, string Name, string Value);

    static string MapId(string key) => "map|" + key;
    static string SettingId(string arrayKey, string settingEnum) => arrayKey + "|" + settingEnum;

    /// <summary>
    /// Liste les éléments sélectionnables (hors drapeaux du compte). Les ids sont ceux attendus par
    /// <see cref="Filter"/> : "tableau|settingEnum", "map|clé" pour les touches, "graphics".
    /// </summary>
    public static List<SettingItem> Items(JsonObject? settings, LocalGraphics? graphics)
    {
        var list = new List<SettingItem>();
        var seen = new HashSet<string>();
        if (settings is not null)
            foreach (var (key, node) in settings)
            {
                if (IsKeybindKey(key))
                {
                    if (node is JsonArray km && seen.Add(MapId(key)))
                        list.Add(new(MapId(key), Category.Touches, key, $"{km.Count} raccourci{(km.Count > 1 ? "s" : "")}"));
                }
                else if (IsSettingArray(key, node))
                    foreach (var it in (JsonArray)node!)
                    {
                        var e = EnumOf(it);
                        var cat = Classify(e);
                        if (cat == Category.Exclu || !seen.Add(SettingId(key, e))) continue;
                        list.Add(new(SettingId(key, e), cat, ShortName(e), Display(it?["value"])));
                    }
            }
        if (graphics is not null)
        {
            var parts = new List<string>();
            if (graphics.GameUserSettingsIni is not null) parts.Add("GameUserSettings.ini");
            var n = graphics.RiotUserSettings?.Count ?? 0;
            if (n > 0) parts.Add($"{n} clé{(n > 1 ? "s" : "")}");
            list.Add(new(GraphicsId, Category.Graphismes, "Fichiers graphiques locaux", string.Join(" + ", parts)));
        }
        return list;
    }

    /// <summary>Valeur JSON en texte court pour l'affichage.</summary>
    public static string Display(JsonNode? v)
    {
        string s;
        if (v is null) s = "null";
        else if (v is JsonValue jv && jv.GetValueKind() == System.Text.Json.JsonValueKind.Number && jv.TryGetValue<double>(out var d))
            s = d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        else if (v is JsonValue jb && jb.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            s = jb.GetValueKind() == System.Text.Json.JsonValueKind.True ? "true" : "false";
        else s = Json.Str(v) ?? v.ToJsonString();
        return s.Length > 60 ? s[..60] + "…" : s;
    }

    /// <summary>
    /// Ne garde que les éléments dont l'id est dans <paramref name="includeIds"/> ; les autres champs
    /// (versions, profils…) sont copiés tels quels et les drapeaux du compte toujours retirés.
    /// Renvoie aussi le nombre d'éléments de catégorie conservés.
    /// </summary>
    public static (JsonObject Result, int Kept) Filter(JsonObject settings, ISet<string> includeIds)
    {
        var result = new JsonObject();
        int kept = 0;
        foreach (var (key, node) in settings)
        {
            if (IsKeybindKey(key))
            {
                if (includeIds.Contains(MapId(key))) { result[key] = node?.DeepClone(); kept++; }
                continue;
            }
            if (IsSettingArray(key, node))
            {
                // Tableau gardé même vide : la présence de la clé compte pour Merge.
                var arr = new JsonArray();
                foreach (var it in (JsonArray)node!)
                {
                    var e = EnumOf(it);
                    if (Classify(e) != Category.Exclu && includeIds.Contains(SettingId(key, e))) { arr.Add(it?.DeepClone()); kept++; }
                }
                result[key] = arr;
                continue;
            }
            result[key] = node?.DeepClone();
        }
        return (result, kept);
    }

    /// <summary>
    /// Catégories dont tous les éléments disponibles sont inclus (catégories vides exclues),
    /// ou null si absolument tout est inclus (profil complet).
    /// </summary>
    public static List<string>? FullCategories(IReadOnlyList<SettingItem> available, ISet<string> includeIds)
    {
        if (available.All(i => includeIds.Contains(i.Id))) return null;
        return available.GroupBy(i => i.Category)
            .Where(g => g.All(i => includeIds.Contains(i.Id)))
            .Select(g => g.Key.ToString())
            .ToList();
    }

    /// <summary>
    /// Part des settings actuels de la cible et remplace uniquement les catégories choisies
    /// par celles de la source. Tout le reste (dont les drapeaux du compte) reste celui de la cible.
    /// Pour une catégorie partielle (absente de <paramref name="fullCats"/>), seules les valeurs
    /// présentes dans la source sont écrasées ; null = toutes les catégories sont complètes.
    /// </summary>
    public static JsonObject Merge(JsonObject? target, JsonObject source, ISet<Category> cats, ISet<Category>? fullCats = null)
    {
        var result = target?.DeepClone().AsObject() ?? new JsonObject();
        foreach (var (key, srcNode) in source)
        {
            if (IsKeybindKey(key))
            {
                if (cats.Contains(Category.Touches)) result[key] = srcNode?.DeepClone();
                continue;
            }
            if (IsSettingArray(key, srcNode))
            {
                var src = (JsonArray)srcNode!;
                var srcEnums = src.Select(EnumOf).ToHashSet();
                var merged = new JsonArray();
                if (result[key] is JsonArray ta)
                    foreach (var it in ta)
                    {
                        var e = EnumOf(it);
                        var c = Classify(e);
                        bool replace = cats.Contains(c) && (fullCats is null || fullCats.Contains(c) || srcEnums.Contains(e));
                        if (!replace) merged.Add(it?.DeepClone());
                    }
                foreach (var it in src)
                    if (cats.Contains(Classify(EnumOf(it)))) merged.Add(it?.DeepClone());
                result[key] = merged;
                continue;
            }
            // Autres champs (versions, etc.) : on garde ceux de la cible, sauf s'ils n'existent pas.
            if (!result.ContainsKey(key)) result[key] = srcNode?.DeepClone();
        }
        return result;
    }

    /// <summary>
    /// Nombre de valeurs des catégories choisies qui diffèrent entre <paramref name="a"/> et
    /// <paramref name="b"/>, et si les keybinds diffèrent.
    /// </summary>
    public static (int Values, bool Keybinds) Diff(JsonObject? a, JsonObject b, ISet<Category> cats)
    {
        static Dictionary<string, string> Flatten(JsonObject? o, ISet<Category> cats)
        {
            var d = new Dictionary<string, string>();
            if (o is null) return d;
            foreach (var (key, node) in o)
                if (IsSettingArray(key, node))
                    foreach (var it in (JsonArray)node!)
                    {
                        var e = EnumOf(it);
                        if (cats.Contains(Classify(e))) d[key + "|" + e] = it?["value"]?.ToJsonString() ?? "null";
                    }
            return d;
        }
        static string Keys(JsonObject? o) => o is null ? "" :
            string.Join("\n", o.Where(kv => IsKeybindKey(kv.Key)).OrderBy(kv => kv.Key)
                               .Select(kv => kv.Key + "=" + kv.Value?.ToJsonString()));

        var fa = Flatten(a, cats);
        var fb = Flatten(b, cats);
        int n = fb.Count(kv => !fa.TryGetValue(kv.Key, out var v) || v != kv.Value)
              + fa.Keys.Count(k => !fb.ContainsKey(k));
        bool kb = cats.Contains(Category.Touches) && Keys(a) != Keys(b);
        return (n, kb);
    }
}
