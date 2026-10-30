using BrimDeck.Core;

internal static class KugouSyncTests
{
    public static void Run(Action<string, bool> check)
    {
        var start = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromSeconds(200);
        var half = new KugouSample(50, start);
        check("A Kugou reading places the song in the middle of its percent step",
            KugouSync.Position(half, start, duration, playing: false) == TimeSpan.FromSeconds(101));
        check("The wall clock carries a reading forward while playing",
            KugouSync.Position(half, start.AddSeconds(2), duration, playing: true) == TimeSpan.FromSeconds(103));
        check("A paused reading does not advance",
            KugouSync.Position(half, start.AddSeconds(10), duration, playing: false) == TimeSpan.FromSeconds(101));
        check("A reading is clamped to the song's length",
            KugouSync.Position(new KugouSample(100, start), start.AddSeconds(100), duration, playing: true) == duration);
        check("Zero percent is the start of the first step",
            KugouSync.Position(new KugouSample(0, start), start, duration, playing: false) == TimeSpan.FromSeconds(1));
        check("A reading without a song length gives no position",
            KugouSync.Position(half, start, TimeSpan.Zero, playing: true) == TimeSpan.Zero);

        var lyrics = Lyrics.Parse("[00:00]甲\n[00:10]乙\n[00:20]丙", null, 100, "QQ 音乐");
        var now = start;
        var track = new MediaTrack
        {
            Source = "酷狗音乐", Title = "Song", Artist = "Artist", State = MediaState.Playing,
            End = TimeSpan.FromSeconds(100), ReportedPosition = TimeSpan.Zero, PositionAt = now.AddSeconds(-5)
        };
        check("A positive lyric offset shows the next line earlier",
            lyrics.At(track, now).Current == "甲" && lyrics.At(track, now, 6).Current == "乙");
        var later = track with { PositionAt = now.AddSeconds(-15) };
        check("A negative lyric offset holds the previous line longer",
            lyrics.At(later, now).Current == "乙" && lyrics.At(later, now, -6).Current == "甲");

        var settings = new DeckSettings { LyricOffsetMs = 5000 }; settings.Normalize();
        check("The lyric offset is kept within three seconds",
            settings.LyricOffsetMs == 3000 && settings.LyricOffset == 3);

        var timed = Lyrics.Parse("[00:10]甲\n[00:20]乙\n[00:30]丙", null, 40, "test");
        check("Lyric progress runs from a line's timestamp to the next line",
            timed.Progress(5) == 0 && timed.Progress(15) == .5 && timed.Progress(20) == 0 && timed.Progress(25) == .5 && timed.Progress(40) == 1);
        check("Unsynced lyrics have no progress", Lyrics.Parse(null, "甲\n乙", 40, "test").Progress(15) == 0);
    }
}
