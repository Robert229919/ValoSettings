using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace ValoSettings;

/// <summary>
/// Pont entre l'interface web et le code C#.
/// La page envoie {id, cmd, args} ; on répond {id, ok, result, error}.
/// Pendant une application, on pousse aussi des événements {event:"log", text}.
/// </summary>
public sealed class Bridge(Form owner, CoreWebView2 core)
{
    RiotSession? lastSession;

    public async Task HandleAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(MainForm.Origin, StringComparison.OrdinalIgnoreCase)) return;
        JsonNode? msg;
        try { msg = JsonNode.Parse(e.WebMessageAsJson); } catch { return; }

        var cmd = Json.Str(msg?["cmd"]) ?? "";
        var args = msg?["args"] as JsonObject ?? new JsonObject();
        JsonNode? result = null;
        string? error = null;
        try { result = await DispatchAsync(cmd, args); }
        catch (RiotException ex) { error = ex.Message; }
        catch (Exception ex) { error = "Erreur inattendue : " + ex.Message; }

        var reply = new JsonObject
        {
            ["id"] = msg?["id"]?.DeepClone(),
            ["ok"] = error is null,
            ["result"] = result,
            ["error"] = error,
        };
        core.PostWebMessageAsJson(reply.ToJsonString());
    }

    void Push(string evt, string text) =>
        core.PostWebMessageAsJson(new JsonObject { ["event"] = evt, ["text"] = text }.ToJsonString());

    async Task<JsonNode?> DispatchAsync(string cmd, JsonObject a) => cmd switch
    {
        "status" => await StatusAsync(),
        "accounts" => Accounts(),
        "profiles" => Profiles(),
        "currentSettings" => await CurrentSettingsAsync(),
        "saveProfile" => await SaveProfileAsync(a),
        "renameProfile" => Rename(a),
        "deleteProfile" => Delete(a),
        "apply" => await ApplyAsync(a),
        "exportProfile" => Export(a),
        "copyCode" => CopyCode(a),
        "importFile" => ImportFile(),
        "importCode" => Dto(AppService.ImportCode(Arg(a, "code"))),
        "setLabel" => SetLabel(a),
        "openFolder" => OpenFolder(),
        _ => throw new RiotException("Commande inconnue : " + cmd),
    };

    static string Arg(JsonObject a, string key) =>
        Json.Str(a[key]) is { Length: > 0 } s ? s.Trim() : throw new RiotException($"Paramètre manquant : {key}");

    async Task<RiotSession> SessionAsync()
    {
        lastSession = null;
        lastSession = await RiotApi.ConnectAsync();
        AccountStore.Seen(lastSession.Puuid, lastSession.RiotId);
        return lastSession;
    }

    async Task<JsonNode> StatusAsync()
    {
        string? message = null;
        try { await SessionAsync(); }
        catch (RiotException ex) { message = ex.Message; }
        return new JsonObject
        {
            ["connected"] = lastSession is not null,
            ["riotId"] = lastSession?.RiotId,
            ["puuid"] = lastSession?.Puuid,
            ["valorantRunning"] = LocalFiles.IsValorantRunning(),
            ["message"] = message,
        };
    }

    // ---------- Comptes ----------

    JsonNode Accounts()
    {
        // Uniquement les comptes déjà connectés au moins une fois dans l'appli (pas les dossiers
        // Valorant locaux seuls, ni les profils importés d'amis).
        var store = AccountStore.Load()
            .Where(kv => !string.IsNullOrEmpty(kv.Value.RiotId))
            .GroupBy(kv => kv.Key.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Value);
        var local = AccountStore.ListLocal().GroupBy(l => l.Puuid).ToDictionary(g => g.Key, g => g.First());
        var profiles = ProfileStore.LoadAll(includeBackups: true);
        var session = lastSession;
        var puuids = store.Keys
            .Concat(session is null ? Array.Empty<string>() : new[] { session.Puuid })
            .Where(p => Guid.TryParse(p, out _))
            .Select(p => p.ToLowerInvariant())
            .Distinct();

        var arr = new JsonArray();
        foreach (var puuid in puuids)
        {
            store.TryGetValue(puuid, out var info);
            local.TryGetValue(puuid, out var loc);
            var own = profiles.Where(p => string.Equals(p.SourcePuuid, puuid, StringComparison.OrdinalIgnoreCase)).ToList();
            var riotId = info?.RiotId
                ?? (string.Equals(session?.Puuid, puuid, StringComparison.OrdinalIgnoreCase) ? session!.RiotId : null);
            var lastProfile = own.Where(p => !p.IsBackup).MaxBy(p => p.CreatedAt);
            arr.Add(new JsonObject
            {
                ["puuid"] = puuid,
                ["riotId"] = riotId,
                ["label"] = info?.Label,
                ["region"] = loc?.Region,
                ["lastPlayed"] = loc?.LastPlayed.ToString("o"),
                ["lastSeen"] = info?.LastSeen?.ToString("o"),
                ["sensitivity"] = loc?.Sensitivity,
                ["profileCount"] = own.Count(p => !p.IsBackup),
                ["backupCount"] = own.Count(p => p.IsBackup),
                ["lastProfile"] = lastProfile?.Name,
                ["connected"] = string.Equals(lastSession?.Puuid, puuid, StringComparison.OrdinalIgnoreCase),
            });
        }
        return arr;
    }

    static JsonNode SetLabel(JsonObject a)
    {
        AccountStore.SetLabel(Arg(a, "puuid"), Json.Str(a["label"]));
        return true;
    }

    // ---------- Settings du compte connecté ----------

    async Task<JsonNode> CurrentSettingsAsync()
    {
        var s = await SessionAsync();
        var items = Categories.Items(await RiotApi.GetSettingsAsync(s), LocalFiles.Read(s.Puuid));
        var cats = new JsonArray();
        foreach (var cat in Categories.Selectable)
        {
            var list = items.Where(i => i.Category == cat).ToList();
            if (list.Count == 0) continue;
            cats.Add(new JsonObject
            {
                ["key"] = cat.ToString(),
                ["label"] = Categories.Label(cat),
                ["items"] = new JsonArray(list.Select(i => (JsonNode)new JsonObject
                {
                    ["id"] = i.Id, ["name"] = i.Name, ["value"] = i.Value,
                }).ToArray()),
            });
        }
        return new JsonObject { ["riotId"] = s.RiotId, ["puuid"] = s.Puuid, ["categories"] = cats };
    }

    async Task<JsonNode> SaveProfileAsync(JsonObject a)
    {
        var name = Arg(a, "name");
        HashSet<string>? include = null;
        if (a["include"] is JsonArray arr)
            include = arr.Select(n => Json.Str(n)).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToHashSet();
        var s = await SessionAsync();
        var expected = Json.Str(a["puuid"]);
        if (!string.IsNullOrEmpty(expected) && !string.Equals(expected, s.Puuid, StringComparison.OrdinalIgnoreCase))
            throw new RiotException("Le compte connecté a changé, recommence.");
        return Dto(await AppService.SaveCurrentAsync(s, name, include));
    }

    // ---------- Profils ----------

    static JsonNode Profiles()
    {
        var all = ProfileStore.LoadAll(includeBackups: true);
        return new JsonObject
        {
            ["profiles"] = new JsonArray(all.Where(p => !p.IsBackup).Select(Dto).ToArray()),
            ["backups"] = new JsonArray(all.Where(p => p.IsBackup).Select(Dto).ToArray()),
        };
    }

    static JsonNode Dto(Profile p)
    {
        var counts = Categories.Count(p.Settings);
        var c = new JsonObject();
        bool partial = false;
        foreach (var cat in Categories.Selectable)
        {
            var n = cat == Category.Graphismes ? (p.Graphics is null ? 0 : 1) : counts[cat];
            c[cat.ToString()] = n;
            if (n > 0 && p.FullCategories is not null && !p.FullCategories.Contains(cat.ToString())) partial = true;
        }

        double? sensi = null;
        if (p.Settings?["floatSettings"] is JsonArray floats)
            foreach (var it in floats)
                if (Json.Str(it?["settingEnum"]) == "EAresFloatSettingName::MouseSensitivity" &&
                    it?["value"] is JsonValue v && v.TryGetValue<double>(out var d))
                    sensi = Math.Round(d, 4);

        return new JsonObject
        {
            ["id"] = p.FilePath,
            ["name"] = p.Name,
            ["source"] = p.SourceRiotId,
            ["sourcePuuid"] = p.SourcePuuid,
            ["created"] = p.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
            ["isBackup"] = p.IsBackup,
            ["expiresAt"] = p.Expiry?.ToString("o", CultureInfo.InvariantCulture),
            ["sensitivity"] = sensi,
            ["counts"] = c,
            ["partial"] = partial,
            ["full"] = p.FullCategories is null ? null : new JsonArray(p.FullCategories.Select(f => (JsonNode)f).ToArray()),
        };
    }

    static Profile Find(JsonObject a)
    {
        var id = Arg(a, "id");
        // L'id doit correspondre à un profil réellement présent dans le dossier des profils.
        return ProfileStore.LoadAll(includeBackups: true).FirstOrDefault(p => p.FilePath == id)
               ?? throw new RiotException("Profil introuvable (supprimé entre-temps ?).");
    }

    static JsonNode Rename(JsonObject a)
    {
        var p = Find(a);
        p.Name = Arg(a, "name");
        ProfileStore.Save(p);
        return Dto(p);
    }

    static JsonNode Delete(JsonObject a)
    {
        ProfileStore.Delete(Find(a));
        return true;
    }

    async Task<JsonNode> ApplyAsync(JsonObject a)
    {
        var p = Find(a);
        var cats = new HashSet<Category>();
        if (a["cats"] is JsonArray arr)
            foreach (var n in arr)
                if (Enum.TryParse<Category>(Json.Str(n), out var c) && Categories.Selectable.Contains(c)) cats.Add(c);

        var s = await SessionAsync();
        var expected = Json.Str(a["targetPuuid"]);
        if (expected is not null && !string.Equals(expected, s.Puuid, StringComparison.OrdinalIgnoreCase))
            throw new RiotException($"Le compte connecté a changé (c'est maintenant {s.RiotId}). Vérifie et recommence.");

        await AppService.ApplyAsync(s, p, cats, text => Push("log", text));
        return true;
    }

    // ---------- Export / import ----------

    JsonNode? Export(JsonObject a)
    {
        var p = Find(a);
        using var dlg = new SaveFileDialog
        {
            Title = "Exporter le profil",
            Filter = $"Profil ValoSettings (*{AppService.FileExtension})|*{AppService.FileExtension}",
            FileName = string.Concat(p.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) + AppService.FileExtension,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        AppService.ExportFile(p, dlg.FileName);
        return dlg.FileName;
    }

    static JsonNode CopyCode(JsonObject a)
    {
        var code = AppService.ToCode(Find(a));
        Clipboard.SetText(code);
        return code.Length;
    }

    JsonNode? ImportFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Importer des profils",
            Filter = $"Profil ValoSettings (*{AppService.FileExtension};*.json)|*{AppService.FileExtension};*.json",
            Multiselect = true,
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        var names = new JsonArray();
        var errors = new JsonArray();
        foreach (var f in dlg.FileNames)
        {
            try { names.Add(AppService.ImportFile(f).Name); }
            catch (RiotException ex) { errors.Add($"{Path.GetFileName(f)} : {ex.Message}"); }
        }
        return new JsonObject { ["imported"] = names, ["errors"] = errors };
    }

    static JsonNode OpenFolder()
    {
        Directory.CreateDirectory(ProfileStore.ProfilesDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ProfileStore.ProfilesDir}\"") { UseShellExecute = true });
        return true;
    }
}
