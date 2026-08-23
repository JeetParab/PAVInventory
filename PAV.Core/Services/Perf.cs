using System.Diagnostics;
using System.Text;

namespace PAV.Core.Services;

/// <summary>
/// Lightweight startup/load timings. Off unless PAV_PERF=1 or a pav.perf file sits next to the exe.
/// Writes %TEMP%\PAV-perf.log when enabled.
/// </summary>
public static class Perf
{
    private static readonly bool Enabled = DetectEnabled();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "PAV-perf.log");
    private static readonly object Gate = new();
    private static readonly Stopwatch AppClock = Stopwatch.StartNew();
    private static readonly StringBuilder Buffer = new();
    private static bool _header;

    public static long ElapsedMs => AppClock.ElapsedMilliseconds;

    public static IDisposable Measure(string stage) => Enabled ? new Scope(stage) : Noop.Instance;

    public static void Log(string stage, long ms)
    {
        if (!Enabled) return;
        var line = $"[PERF] +{AppClock.ElapsedMilliseconds,6} ms | {stage,-36} {ms,6} ms";
        Debug.WriteLine(line);
        lock (Gate)
        {
            try
            {
                if (!_header)
                {
                    Buffer.AppendLine($"PAV perf {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId}");
                    _header = true;
                }
                Buffer.AppendLine(line);
                File.WriteAllText(LogPath, Buffer.ToString());
            }
            catch
            {
                // never fail the app because of diagnostics
            }
        }
    }

    public static void Mark(string stage) => Log(stage, 0);

    private static bool DetectEnabled()
    {
        var env = Environment.GetEnvironmentVariable("PAV_PERF");
        if (string.Equals(env, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "yes", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var dir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "pav.perf")))
                return true;
        }
        catch
        {
            // ignore
        }
        return false;
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();
        public void Dispose() { }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _stage;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public Scope(string stage) => _stage = stage;
        public void Dispose() => Log(_stage, _sw.ElapsedMilliseconds);
    }
}
