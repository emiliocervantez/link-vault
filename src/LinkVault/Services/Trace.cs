using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Threading;
using LinkVault.Native;

namespace LinkVault.Services;

/// <summary>Opt-in diagnostics (start with --trace). Writes timestamped steps to trace.log in the vault folder.</summary>
internal static class Trace
{
    private const int WatchdogPeriodMs = 200;
    private const int WatchdogReportMs = 300;

    private static StreamWriter? _writer;
    private static readonly object Lock = new();

    public static bool Enabled => _writer is not null;

    public static void Enable(string path, Dispatcher uiDispatcher)
    {
        _writer = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
        Log($"---- trace started, pid {Environment.ProcessId} ----");
        var watchdog = new Thread(() => Watchdog(uiDispatcher)) { IsBackground = true, Name = "ui-watchdog" };
        watchdog.Start();
    }

    public static void Log(string message)
    {
        if (_writer is null) return;
        lock (Lock) _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,2}] {message}");
    }

    /// <summary>Handle, owning process and title of a window, for log lines.</summary>
    public static string Window(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "hwnd 0";
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var title = new StringBuilder(128);
        NativeMethods.GetWindowText(hwnd, title, title.Capacity);
        return $"hwnd 0x{hwnd:X} pid {pid} '{title}'";
    }

    public static string Foreground() => Window(NativeMethods.GetForegroundWindow());

    /// <summary>Pings the UI thread and reports every time it takes too long to answer.</summary>
    private static void Watchdog(Dispatcher dispatcher)
    {
        while (true)
        {
            Thread.Sleep(WatchdogPeriodMs);
            var sw = Stopwatch.StartNew();
            try
            {
                dispatcher.InvokeAsync(() => { }, DispatcherPriority.Send).Task.Wait();
            }
            catch (Exception)
            {
                return;   // dispatcher shut down
            }
            if (sw.ElapsedMilliseconds >= WatchdogReportMs)
                Log($"UI thread was unresponsive for {sw.ElapsedMilliseconds} ms");
        }
    }
}
