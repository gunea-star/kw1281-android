using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace KW1281Android;

/// <summary>
/// Actualizare OTA: verifică ultimul Release din GitHub, descarcă APK-ul și îl instalează
/// prin PackageInstaller (Android cere confirmarea utilizatorului).
/// </summary>
internal static class Updater
{
    public const string Repo = "gunea-star/kw1281-android";
    public const string ActionInstallStatus = "ro.kw1281test.android.INSTALL_STATUS";

    public sealed record Release(long Version, string Tag, string ApkUrl, string Notes, long Size);

    public static long CurrentVersion(Context c)
    {
        try
        {
            var info = c.PackageManager!.GetPackageInfo(c.PackageName!, (PackageInfoFlags)0)!;
            return Build.VERSION.SdkInt >= BuildVersionCodes.P ? info.LongVersionCode : info.VersionCode;
        }
        catch
        {
            return 0;
        }
    }

    public static string CurrentName(Context c)
    {
        try { return c.PackageManager!.GetPackageInfo(c.PackageName!, (PackageInfoFlags)0)!.VersionName ?? "?"; }
        catch { return "?"; }
    }

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("STP-Programming-Updater/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Ultimul release cu APK, sau null dacă nu există.</summary>
    public static async Task<Release?> CheckAsync()
    {
        using var http = NewClient();
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var dot = tag.LastIndexOf('.');
        if (dot < 0 || !long.TryParse(tag[(dot + 1)..], out var version)) return null;

        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;
            var url = a.GetProperty("browser_download_url").GetString();
            if (url == null) continue;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            return new Release(version, tag, url, notes, size);
        }
        return null;
    }

    public static async Task DownloadAsync(string url, string path, Action<int> progress)
    {
        using var http = NewClient();
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var input = await resp.Content.ReadAsStreamAsync();
        await using var output = File.Create(path);
        var buf = new byte[81920];
        long done = 0;
        int n, last = -1;
        while ((n = await input.ReadAsync(buf)) > 0)
        {
            await output.WriteAsync(buf.AsMemory(0, n));
            done += n;
            if (total > 0)
            {
                var pct = (int)(done * 100 / total);
                if (pct != last) { last = pct; progress(pct); }
            }
        }
    }

    /// <summary>True dacă aplicația are voie să instaleze APK-uri; altfel deschide setarea.</summary>
    public static bool EnsureInstallPermission(Activity a)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return true;
        if (a.PackageManager!.CanRequestPackageInstalls()) return true;
        var i = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
            Android.Net.Uri.Parse("package:" + a.PackageName));
        a.StartActivity(i);
        return false;
    }

    public static void Install(Activity a, string apkPath)
    {
        var installer = a.PackageManager!.PackageInstaller;
        var p = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        var id = installer.CreateSession(p);
        using var session = installer.OpenSession(id);
        using (var input = File.OpenRead(apkPath))
        using (var output = session.OpenWrite("stp-update", 0, input.Length))
        {
            input.CopyTo(output);
            session.Fsync(output);
        }

        var intent = new Intent(ActionInstallStatus);
        intent.SetPackage(a.PackageName);
        var flags = PendingIntentFlags.UpdateCurrent |
                    (Build.VERSION.SdkInt >= BuildVersionCodes.S ? PendingIntentFlags.Mutable : (PendingIntentFlags)0);
        var pi = PendingIntent.GetBroadcast(a, 7, intent, flags)!;
        session.Commit(pi.IntentSender);
    }

    /// <summary>Primește răspunsul instalatorului și cere confirmarea utilizatorului.</summary>
    public sealed class InstallReceiver(Action<string> report) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null) return;
            var status = intent.GetIntExtra(PackageInstaller.ExtraStatus, -999);
            if (status == (int)PackageInstallStatus.PendingUserAction)
            {
                if (intent.GetParcelableExtra(Intent.ExtraIntent) is Intent confirm)
                {
                    confirm.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(confirm);
                }
            }
            else if (status == (int)PackageInstallStatus.Success)
            {
                report("Actualizare instalată ✓");
            }
            else
            {
                report("Instalare eșuată: " + (intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? status.ToString()));
            }
        }
    }
}
