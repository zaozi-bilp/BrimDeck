using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck.Native;

// Reads Kugou's own progress slider, which reports a whole percentage of the song. The slider is a UI Automation element
// inside Kugou's custom-drawn window; it is found by walking the control tree (a descendant search on this provider blocks)
// and then read through ValuePattern, which answers in about a millisecond. The walk and every read run on a dedicated STA
// thread with a message pump, so a slow or broken provider can never stall the panel; if the thread is stuck the panel
// simply keeps its estimated position.
internal sealed class KugouProgressReader : IDisposable
{
    private readonly object _gate = new();
    private readonly Thread _thread;
    private KugouSample? _sample;
    private Dispatcher? _dispatcher;
    private AutomationElement? _slider;
    private ValuePattern? _value;
    private DateTimeOffset _nextFind;
    private volatile bool _disposed;

    public KugouSample? Latest { get { lock (_gate) return _sample; } }

    public KugouProgressReader()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Kugou progress" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        var dispatcher = _dispatcher = Dispatcher.CurrentDispatcher;
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => Poll();
        timer.Start();
        Dispatcher.Run();
    }

    private void Poll()
    {
        if (_disposed) return;
        var now = DateTimeOffset.UtcNow;
        try
        {
            if (_slider is null)
            {
                if (now < _nextFind) { Clear(); return; }
                Find(now);
                if (_slider is null) { Clear(); _nextFind = now.AddSeconds(5); return; }
            }
            string text = _value!.Current.Value;
            if (!double.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
            {
                _slider = null; _value = null; _nextFind = now.AddSeconds(2); Clear(); return;
            }
            lock (_gate) _sample = new KugouSample(Math.Clamp(percent, 0, 100), now);
        }
        catch
        {
            // The window closed, the player restarted or its provider changed: find it again shortly.
            _slider = null; _value = null; _nextFind = now.AddSeconds(2); Clear();
        }
    }

    private void Find(DateTimeOffset now)
    {
        Process? player = null;
        try
        {
            foreach (var process in Process.GetProcessesByName("KuGou"))
            {
                if (process.MainWindowHandle == IntPtr.Zero) { process.Dispose(); continue; }
                player = process; break;
            }
            if (player is null) return;
            AutomationElement? window = null;
            foreach (AutomationElement candidate in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                try
                {
                    if (candidate.Current.ProcessId != player.Id) continue;
                    if (!candidate.Current.ClassName.Equals("kugou_ui", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!candidate.Current.Name.Contains("酷狗", StringComparison.Ordinal)) continue;
                    window = candidate; break;
                }
                catch { }
            }
            if (window is null) return;
            var slider = FindSlider(window);
            if (slider is null) return;
            if (!slider.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern value) return;
            _slider = slider; _value = value; _nextFind = now.AddSeconds(2);
        }
        finally { player?.Dispose(); }
    }

    // The control tree is walked by hand: FindFirst(TreeScope.Descendants) blocks in this provider, while the walk returns
    // in about a second and stops at the player bar's slider.
    private static AutomationElement? FindSlider(AutomationElement window)
    {
        var walker = TreeWalker.ControlViewWalker;
        int nodes = 0;
        AutomationElement? found = null;
        void Walk(AutomationElement element, int depth)
        {
            if (found is not null || depth > 4 || nodes++ > 200) return;
            try
            {
                if (element.Current.ControlType == ControlType.Slider) { found = element; return; }
                var child = walker.GetFirstChild(element);
                int guard = 0;
                while (child is not null && guard++ < 200)
                {
                    Walk(child, depth + 1);
                    child = walker.GetNextSibling(child);
                }
            }
            catch { }
        }
        Walk(window, 0);
        return found;
    }

    private void Clear()
    {
        lock (_gate) _sample = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background); } catch { }
    }
}
