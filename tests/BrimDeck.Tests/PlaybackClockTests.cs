using BrimDeck.Core;

internal static class PlaybackClockTests
{
    public static void Run(Action<string, bool> check)
    {
        var start = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var song = new MediaTrack { Id = "kugou", Source = "酷狗音乐", Title = "甲乙丙丁", Artist = "李佳薇", State = MediaState.Playing };
        var clock = new PlaybackClock();
        check("Kugou tracks use the estimated clock", MediaTrackSupports(song) && !MediaTrackSupports(song with { Source = "Spotify" }));

        clock.Observe(song, start);
        check("A new Kugou song starts at zero", clock.Position(song, start) == TimeSpan.Zero);
        check("The estimated position advances while playing", clock.Position(song, start.AddSeconds(10)) == TimeSpan.FromSeconds(10));

        var paused = song with { State = MediaState.Paused };
        clock.Observe(paused, start.AddSeconds(10));
        check("Pausing freezes the estimated position", clock.Position(paused, start.AddSeconds(30)) == TimeSpan.FromSeconds(10));
        clock.Observe(song, start.AddSeconds(40));
        check("Resuming continues from the paused position", clock.Position(song, start.AddSeconds(45)) == TimeSpan.FromSeconds(15));

        var next = song with { Title = "下一首" };
        clock.Observe(next, start.AddSeconds(50));
        check("A new song starts its estimate over", clock.Position(next, start.AddSeconds(55)) == TimeSpan.FromSeconds(5));

        clock.SetDuration(TimeSpan.FromSeconds(12));
        check("A matched duration stops the estimate at the end", clock.Position(next, start.AddSeconds(100)) == TimeSpan.FromSeconds(12));
        var pausedNext = next with { State = MediaState.Paused };
        clock.Observe(pausedNext, start.AddSeconds(55));
        check("A paused estimate keeps the position it stopped at", clock.Position(pausedNext, start.AddSeconds(100)) == TimeSpan.FromSeconds(5));

        var other = new MediaTrack { Id = "spotify", Source = "Spotify", Title = "Song", Artist = "Artist", State = MediaState.Playing };
        check("Other players never use the estimated clock", clock.Position(other, start.AddSeconds(10)) == TimeSpan.Zero);
        clock.Observe(null, start);
        check("Clearing the track resets the estimate", clock.Position(song, start.AddSeconds(60)) == TimeSpan.Zero);
    }

    private static bool MediaTrackSupports(MediaTrack track) => PlaybackClock.Supports(track);
}
