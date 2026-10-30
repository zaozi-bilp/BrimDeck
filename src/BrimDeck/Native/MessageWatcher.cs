using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck.Native;

// Watches for a chat app's new-message popup. WeChat 4's main UI exposes no accessible tree, so the popup window itself is
// the signal: a small visible window of one of the app's processes at the bottom right of its monitor. A show event reports
// it at once; a slow poll catches a popup that was already open when BrimDeck started or whose event was missed. Only the
// window class, title and rectangle are read; nothing of the message is.
internal sealed class MessageWatcher : IDisposable
{
    private const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint MonitorDefaultToNearest = 2;
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr parameter);

    private readonly Dispatcher _dispatcher;
    private readonly string[] _processes;
    private readonly string _text;
    private readonly Action<string> _notice;
    private readonly Func<bool> _enabled;
    private readonly DispatcherTimer _poll;
    private readonly object _gate = new();
    private readonly HashSet<IntPtr> _seen = new();
    private readonly HashSet<uint> _pids = new();
    private WinEventDelegate? _callback;
    private IntPtr _hook;
    private DateTimeOffset _pidsAt, _lastNotice;
    private bool _disposed;

    public MessageWatcher(Dispatcher dispatcher, string[] processes, string text, Action<string> notice, Func<bool> enabled)
    {
        _dispatcher = dispatcher; _processes = processes; _text = text; _notice = notice; _enabled = enabled;
        _poll = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(1200) };
        _poll.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        if (_disposed) return;
        _callback = OnShow;
        _hook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _callback, 0, 0, WineventOutOfContext | WineventSkipOwnProcess);
        _poll.Start();
        Poll();
    }

    private void OnShow(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // OBJID_WINDOW and CHILDID_SELF: a window was shown, not a control inside one.
        if (idObject != 0 || idChild != 0) return;
        Consider(hwnd);
    }

    // The poll also sees popups that appeared before the hook was installed; the intervals are long enough to keep the
    // cost invisible and short enough that a popup is not missed.
    private void Poll()
    {
        if (_disposed || !_enabled()) { lock (_gate) _seen.Clear(); return; }
        RefreshPids();
        var current = new HashSet<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsAppWindow(hwnd)) return true;
            if (Consider(hwnd)) current.Add(hwnd);
            // A notification can be a child of a hidden owner window; it still appears over the desktop.
            EnumChildWindows(hwnd, (child, __) =>
            {
                if (IsWindowVisible(child) && Consider(child)) current.Add(child);
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        lock (_gate)
        {
            _seen.IntersectWith(current);
            foreach (var hwnd in current) _seen.Add(hwnd);
        }
    }

    private bool Consider(IntPtr hwnd)
    {
        if (_disposed || hwnd == IntPtr.Zero || !_enabled() || !IsAppWindow(hwnd) || !IsWindowVisible(hwnd)) return false;
        lock (_gate) if (_seen.Contains(hwnd)) return true;
        if (!GetWindowRect(hwnd, out var rect)) return false;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var window = new WeChatWindow(ClassName(hwnd), WindowText(hwnd), true, rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (!WeChatNotice.LooksLikeNotification(window, info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom)) return false;
        lock (_gate)
        {
            if (!_seen.Add(hwnd)) return true;
            var now = DateTimeOffset.UtcNow;
            // A burst (the popup shown and its child windows) is one message.
            if (now - _lastNotice < TimeSpan.FromSeconds(4)) return true;
            _lastNotice = now;
        }
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_disposed && _enabled()) _notice(_text);
        });
        return true;
    }

    private void RefreshPids()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate) if (now - _pidsAt < TimeSpan.FromSeconds(5)) return;
        var pids = new HashSet<uint>();
        foreach (string name in _processes)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (InvalidOperationException) { continue; }
            foreach (var process in processes)
            {
                try { pids.Add((uint)process.Id); }
                catch (InvalidOperationException) { /* The process left between the enumeration and the read. */ }
                finally { process.Dispose(); }
            }
        }
        lock (_gate)
        {
            _pids.Clear(); foreach (uint pid in pids) _pids.Add(pid);
            _pidsAt = now;
        }
    }

    private bool IsAppWindow(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        lock (_gate) return _pids.Contains(pid);
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string WindowText(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _poll.Stop();
        if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        _callback = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr parameter);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32")] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32")] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
