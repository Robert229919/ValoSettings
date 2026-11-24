using System.Text;
using System.Text.Json.Nodes;

namespace ValoSettings;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Diagnostic en lecture seule : ValoSettings.exe --check <dossier>
        if (args.Length >= 2 && args[0] == "--check")
            return CheckAsync(args[1]).GetAwaiter().GetResult();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    static async Task<int> CheckAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        int code = 0;
        try
        {
            var s = await RiotApi.ConnectAsync();
            sb.AppendLine($"Compte : {s.RiotId} ({s.Puuid})");
            var st = await RiotApi.GetSettingsAsync(s);
            if (st is null) sb.AppendLine("Aucun settings côté Riot.");
            else
            {
                sb.AppendLine("Clés : " + string.Join(", ", st.Select(kv =>
                    $"{kv.Key}[{(kv.Value is JsonArray a ? a.Count.ToString() : kv.Value?.GetValueKind().ToString())}]")));
                foreach (var (c, n) in Categories.Count(st)) sb.AppendLine($"  {c} : {n}");
                var rt = RiotApi.Decode(RiotApi.Encode(st));
                sb.AppendLine("Aller-retour encodage : " + (rt.ToJsonString() == st.ToJsonString() ? "OK" : "DIFFÉRENT"));
                File.WriteAllText(Path.Combine(outDir, "settings.json"), st.ToJsonString(Json.Pretty));

                var cats = new StringBuilder();
                foreach (var (key, node) in st)
                    if (node is JsonArray arr)
                        foreach (var it in arr)
                            if (Json.Str(it?["settingEnum"]) is { } e)
                                cats.AppendLine($"{Categories.Classify(e),-12} {e}");
                File.WriteAllText(Path.Combine(outDir, "categories.txt"), cats.ToString());
            }
            var g = LocalFiles.Read(s.Puuid);
            sb.AppendLine(g is null
                ? "Graphismes locaux : aucun dossier"
                : $"Graphismes locaux : GameUserSettings={(g.GameUserSettingsIni is not null)}, {g.RiotUserSettings.Count} clés RiotUserSettings ({string.Join(", ", g.RiotUserSettings.Keys)})");
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERREUR : " + ex.Message);
            code = 1;
        }
        File.WriteAllText(Path.Combine(outDir, "check.txt"), sb.ToString());
        return code;
    }
}
