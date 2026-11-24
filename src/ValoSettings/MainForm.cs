using System.Diagnostics;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ValoSettings;

/// <summary>Fenêtre unique qui affiche l'interface web embarquée (dossier web/) via WebView2.</summary>
public sealed class MainForm : Form
{
    public const string Host = "app.valosettings";
    public static readonly string Origin = $"https://{Host}/";
    static readonly Color Wall = Color.FromArgb(0xF4, 0xEF, 0xE6);

    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Wall };

    public MainForm()
    {
        Text = "ValoSettings";
        Size = new Size(1240, 840);
        MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Wall;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Controls.Add(web);
        Load += async (_, _) => await InitAsync();

        // Sauvegardes automatiques : supprimées définitivement 1 h après leur création.
        ProfileStore.PurgeExpiredBackups();
        var purge = new System.Windows.Forms.Timer { Interval = 60_000 };
        purge.Tick += (_, _) => ProfileStore.PurgeExpiredBackups();
        purge.Start();
        FormClosed += (_, _) => purge.Dispose();
    }

    async Task InitAsync()
    {
        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValoSettings", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Le composant Microsoft Edge WebView2 est introuvable. Installe-le depuis " +
                "https://go.microsoft.com/fwlink/p/?LinkId=2124703 puis relance ValoSettings.\n\n" + ex.Message,
                "ValoSettings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = Debugger.IsAttached;

        // Les fichiers de l'interface sont servis depuis les ressources du .exe.
        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
            if (path.Length == 0) path = "index.html";
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + path);
            e.Response = stream is null
                ? core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "")
                : core.Environment.CreateWebResourceResponse(stream, 200, "OK",
                    $"Content-Type: {Mime(path)}\nCache-Control: no-store");
        };

        // L'interface ne doit jamais quitter son origine (liens externes, fichiers glissés…).
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;

        var bridge = new Bridge(this, core);
        core.WebMessageReceived += async (_, e) => await bridge.HandleAsync(e);
        core.Navigate(Origin + "index.html");
    }

    static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
}
