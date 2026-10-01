using System.Security.Principal;
using RAMRazor.Core;
using RAMRazor.UI;

namespace RAMRazor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Headless self-test mode (used by CI). Must not touch WinForms types.
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.RunAll();

        // Admin gate: the app refuses to run without Administrator access.
        if (!IsElevated())
        {
            try
            {
                MessageBox.Show(
                    "RAM Razor requires Administrator privileges to operate.\n\n" +
                    "Without elevation, process control and memory cleaning are not possible,\n" +
                    "so the application will not start.\n\n" +
                    "Right-click RAMRazor.exe and choose \u201cRun as administrator\u201d.",
                    "RAM Razor \u2014 Administrator required",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // headless crash-refuse path
            }
            LogService.TryQuickLog("STARTUP", "Startup refused: not elevated (exit 740).");
            return 740; // ERROR_ELEVATION_REQUIRED
        }

        // Single instance guard
        bool createdNew;
        using var mutex = new Mutex(true, @"Local\RAMRazor.SingleInstance", out createdNew);
        if (!createdNew)
        {
            try
            {
                MessageBox.Show("RAM Razor is already running.", "RAM Razor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
            return 0;
        }

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
        catch (Exception ex)
        {
            LogService.TryQuickLog("ERR", "Boot failure: " + ex.Message);
            return 1;
        }

        Application.ThreadException += (s, e) =>
            LogService.Add("ERR", "UI", e.Exception.Message);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LogService.Add("ERR", "APP", e.ExceptionObject?.ToString() ?? "unknown exception");

        // load persisted settings and acquire cleaning privileges up-front
        var store = new SettingsStore();
        var settings = store.Load();
        LogService.FileLogging = settings.LogToFile;
        var priv = MemoryService.EnableAllPrivileges();
        LogService.TryQuickLog("STARTUP",
            "privileges: " + string.Join(", ", priv.Select(kv => kv.Key + "=" + (kv.Value ? "on" : "off"))));

        Application.Run(new MainForm(settings));
        return 0;
    }

    internal static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(new SecurityIdentifier("S-1-5-32-544")); // BUILTIN\Administrators
        }
        catch
        {
            return false;
        }
    }
}
