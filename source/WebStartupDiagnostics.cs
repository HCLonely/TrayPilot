using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestWebStartup(string report, bool live = false)
    {
        report = Path.GetFullPath(report); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(report)!, "startup-test-" + Guid.NewGuid().ToString("N")));
        controller.Saved.Theme = "dark";
        var watch = Stopwatch.StartNew();
        using var form = new MainForm(controller, initialize: live) { WebReadOnlyPreview = true, WebDiagnosticsSuspendScanning = !live, WebTrayKeepOpenForDiagnostics = true };
        double construction = watch.Elapsed.TotalMilliseconds;
        form.PrepareWebStartup();
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.EnableWebInterfaceAsync();
                await form.WebLoaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
                double main = watch.Elapsed.TotalMilliseconds;
                var area = Screen.PrimaryScreen!.WorkingArea;
                var anchor = new Point(area.Right - 20, area.Bottom - 10);
                var trayWatch = Stopwatch.StartNew();
                await (Task)typeof(MainForm).GetMethod("ShowWebTrayAsync", DashboardFlags)!.Invoke(form, new object[] { anchor })!;
                double firstTray = trayWatch.Elapsed.TotalMilliseconds;
                var tray = DashboardField<WebView2>(form, "webTrayView");
                var core = tray.CoreWebView2;
                if (!(JsonSerializer.Deserialize<JsonElement>(await core.ExecuteScriptAsync("document.querySelector('#traySearch')!==null"))).GetBoolean())
                    throw new IOException("Tray interface not rendered");
                var repeated = new List<double>();
                for (int i = 0; i < 5; i++)
                {
                    DashboardCall(form, "HideWebTray"); trayWatch.Restart();
                    await (Task)typeof(MainForm).GetMethod("ShowWebTrayAsync", DashboardFlags)!.Invoke(form, new object[] { anchor })!;
                    repeated.Add(trayWatch.Elapsed.TotalMilliseconds);
                    if (!ReferenceEquals(tray, DashboardField<WebView2>(form, "webTrayView"))) throw new IOException("Tray view was recreated");
                }
                using (var stream = File.Create(Path.ChangeExtension(report, ".png")))
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, live = live, constructionMs = construction, mainPresentedMs = main, stages = form.WebStartupTimings, firstTrayMs = firstTray, repeatedTrayMs = repeated }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                failure = ex; File.WriteAllText(report + ".error.txt", ex.ToString());
                var view = form.Controls.OfType<WebView2>().FirstOrDefault(v => v.Name == "mainWebView");
                string? page = view?.CoreWebView2 == null ? null : await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,images:document.images.length,scripts:document.scripts.length,ready:typeof state==='undefined'?null:state,body:document.body?.innerText.slice(0,300)})");
                File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, stages = form.WebStartupTimings, page }));
            }
            finally { form.CloseWebDiagnosticsWindow(); }
        };
        Application.Run(form); if (failure != null) throw failure; return 0;
    }
}
