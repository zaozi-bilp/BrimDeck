using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace BrimDeck.Native;

// A small local bridge for other programs: a JSON file in BrimDeck's data folder is shown as a short activity in the
// collapsed island. A build script, a downloader or an AI task can write:
//   { "title": "Building", "detail": "3/5", "progress": 0.6, "seconds": 8 }
// Only a local file is read; nothing is sent anywhere.
internal sealed class ActivityBridge : IDisposable
{
    public const string FileName = "activity.json";
    private readonly Dispatcher _dispatcher;
    private readonly string _path;
    private readonly Action<string, double> _notice;
    private readonly Func<bool> _enabled;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _poll;
    private readonly object _gate = new();
    private string _lastText = "";
    private DateTimeOffset _lastAt;
    private bool _disposed;

    public ActivityBridge(Dispatcher dispatcher, string folder, Action<string, double> notice, Func<bool> enabled)
    {
        _dispatcher = dispatcher; _path = Path.Combine(folder, FileName); _notice = notice; _enabled = enabled;
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Read(), dispatcher);
        try
        {
            _watcher = new FileSystemWatcher(folder, FileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            _watcher.Changed += (_, _) => Read();
            _watcher.Created += (_, _) => Read();
            _watcher.Renamed += (_, _) => Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    public void Start() { try { _watcher?.EnableRaisingEvents = true; } catch { } _poll.Start(); Read(); }

    private void Read()
    {
        if (_disposed || !_enabled()) return;
        string text = "";
        double seconds = 6;
        try
        {
            if (!File.Exists(_path)) return;
            string json;
            for (int attempt = 0; ; attempt++)
            {
                try { json = File.ReadAllText(_path); break; }
                catch (IOException) when (attempt < 2) { Thread.Sleep(40); }
            }
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            string title = Text(root, "title").Trim(), detail = Text(root, "detail").Trim();
            if (title.Length == 0) return;
            string progress = Progress(root, out double percent);
            text = title.Length > 48 ? title[..48] : title;
            if (detail.Length > 0) text += " · " + (detail.Length > 40 ? detail[..40] : detail);
            if (progress.Length > 0) text += " · " + progress;
            if (text.Length > 96) text = text[..96];
            if (root.TryGetProperty("seconds", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double wanted))
                seconds = Math.Clamp(wanted, 2, 30);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { return; }
        if (text.Length == 0) return;
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            // A writer updating progress several times a second is one activity, not a flicker of notices.
            if (text == _lastText && now - _lastAt < TimeSpan.FromSeconds(2)) return;
            _lastText = text; _lastAt = now;
        }
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_disposed && _enabled()) _notice(text, seconds); }));
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Progress(JsonElement root, out double percent)
    {
        percent = 0;
        if (!root.TryGetProperty("progress", out var value)) return "";
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number)) return "";
        percent = number > 1 ? Math.Clamp(number, 0, 100) : Math.Clamp(number * 100, 0, 100);
        return $"{percent:0}%";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _poll.Stop();
        if (_watcher is not null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); }
    }
}
