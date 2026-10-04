using System.Collections.Concurrent;
using System.Text;

namespace GnomeWin.Services.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

public static class Log
{
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxFiles = 5;

    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 10_000);
    private static Thread? _writer;
    private static string _prefix = "gnomewin";
    private static string? _dir;
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void Initialize(string directory, string prefix, bool verbose)
    {
        _dir = directory;
        _prefix = prefix;
        MinimumLevel = verbose ? LogLevel.Debug : LogLevel.Info;
        if (_writer != null) return;
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "GnomeWin.Log", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg, null);
    public static void Info(string msg) => Write(LogLevel.Info, msg, null);
    public static void Warn(string msg, Exception? ex = null) => Write(LogLevel.Warn, msg, ex);
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, msg, ex);

    public static void Write(LogLevel level, string msg, Exception? ex)
    {
        if (level < MinimumLevel) return;
        var sb = new StringBuilder(128);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
          .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
          .Append('[').Append(Environment.CurrentManagedThreadId).Append("] ")
          .Append(msg);
        if (ex != null) sb.AppendLine().Append(ex);
        string line = sb.ToString();
        System.Diagnostics.Debug.WriteLine(line);
        if (_writer == null) return;
        Queue.TryAdd(line);
    }

    public static void Flush(int timeoutMs = 1000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Queue.Count > 0 && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(10);
    }

    private static void WriterLoop()
    {
        StreamWriter? writer = null;
        string? currentPath = null;
        foreach (string line in Queue.GetConsumingEnumerable())
        {
            try
            {
                string path = Path.Combine(_dir!, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log");
                if (writer == null || path != currentPath || writer.BaseStream.Length > MaxFileBytes)
                {
                    writer?.Dispose();
                    if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                        path = Path.Combine(_dir!, $"{_prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                    currentPath = path;
                    writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) { AutoFlush = true };
                    Prune();
                }
                writer.WriteLine(line);
            }
            catch
            {
                writer?.Dispose();
                writer = null;
            }
        }
    }

    private static void Prune()
    {
        try
        {
            var files = new DirectoryInfo(_dir!).GetFiles(_prefix + "-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var f in files.Skip(MaxFiles)) f.Delete();
        }
        catch { /* best effort */ }
    }
}
