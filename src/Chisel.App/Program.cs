using Chisel.Core;

namespace Chisel.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\Chisel.Launcher.Singleton", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("凿境已经在运行了。", "凿境", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var paths = AppPaths.Create();
        Application.ThreadException += (_, e) => LogFatal(paths, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogFatal(paths, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal(paths, e.Exception);
            e.SetObserved();
        };

        try
        {
            Application.Run(new MainForm(paths));
        }
        catch (Exception ex)
        {
            LogFatal(paths, ex);
            MessageBox.Show("启动器遇到了无法恢复的问题，详情已写入日志。\n\n" + ex.Message, "凿境",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        GC.KeepAlive(mutex);
    }

    private static void LogFatal(AppPaths paths, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var path = Path.Combine(paths.LogsDir, "app.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
