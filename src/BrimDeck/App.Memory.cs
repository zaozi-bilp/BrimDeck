using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace BrimDeck;

// Gives memory back while BrimDeck sits collapsed, when the user turns it on (DeckSettings.TrimMemoryWhenIdle, off by default).
// Reading the usage logs, building a page and loading covers allocate in bursts; afterwards the garbage collector keeps the room
// it grew into, and Windows keeps the pages in the working set, so an idle panel that had peaked at over 200 MB stayed there. Once every panel has been collapsed and left alone for a while, the heap is
// compacted, the room it no longer needs is returned, and the working set is trimmed. Never while a panel is open or a surface
// is being resized, and at most every few minutes: what is trimmed comes back from memory on the next use, which must not
// fall on an opening panel's first frames. A process already below TrimThreshold is left alone: there is nothing to gain,
// and the pause would only risk the next opening.
//
// The wait runs on the thread pool rather than the dispatcher: an equalizer, a scrolling lyric or a carousel keeps the
// dispatcher busy, and a dispatcher timer, whatever its priority, could be postponed for as long as the music plays.
public partial class App
{
    private const long TrimThreshold = 192L * 1024 * 1024;
    private static readonly TimeSpan TrimIdle = TimeSpan.FromSeconds(30), TrimEvery = TimeSpan.FromMinutes(3);
    private CancellationTokenSource? _trim;
    private DateTime _trimmedAt = DateTime.MinValue;

    // Called after a burst of work (a usage refresh), whenever a panel collapses, and when the settings window closes: the
    // trim waits for a quiet stretch after it.
    internal void RequestTrim()
    {
        if (Exiting || !Settings.TrimMemoryWhenIdle) { _trim?.Cancel(); _trim = null; return; }
        var wait = _trimmedAt + TrimEvery - DateTime.UtcNow;
        if (wait < TrimIdle) wait = TrimIdle;
        _trim?.Cancel();
        var request = _trim = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(wait, request.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (Exiting || !Settings.TrimMemoryWhenIdle || !ReferenceEquals(_trim, request)) return;
                // Someone is using a panel: try again after the next quiet stretch.
                if (Decks.Any(deck => deck.IsExpanded || deck.IsResizing)) { RequestTrim(); return; }
                using var process = Process.GetCurrentProcess();
                if (process.PrivateMemorySize64 < TrimThreshold) { _trimmedAt = DateTime.UtcNow; return; }
                _trimmedAt = DateTime.UtcNow;
                // The collection blocks every managed thread while it runs; on the pool its pause does not hold up the panel.
                _ = Task.Run(() =>
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    // Aggressive also returns the memory the heap no longer uses to the system.
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
                });
            }));
        });
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);
}
