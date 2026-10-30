namespace BrimDeck.Core;

// A playback position for a player that publishes a song and its state through Windows media controls but no timeline.
// Kugou does this: its media session has an empty start, position and end, and seeking is disabled. The clock anchors a new
// song at the moment it is first seen playing, advances with the wall clock while it plays, freezes on pause and continues
// from that point when it resumes. Starting the app in the middle of a song, or seeking inside the player, is invisible
// here, so the estimate can be off until the next song; a duration from a matched lyric keeps it from running past the end.
public sealed class PlaybackClock
{
    private string _key = "";
    private DateTimeOffset _anchorAt;
    private TimeSpan _anchorPosition;
    private TimeSpan _frozenPosition;
    private MediaState _state = MediaState.Stopped;
    private TimeSpan? _duration;

    public static bool Supports(MediaTrack track) => track.IsKugouMusic;

    // The length a lyric match supplied, used to keep the estimate inside the song.
    public TimeSpan? Duration => _duration;
    public void SetDuration(TimeSpan? duration) => _duration = duration is { } value && value > TimeSpan.Zero ? value : null;

    public void Reset()
    {
        _key = "";
        _anchorAt = default;
        _anchorPosition = TimeSpan.Zero;
        _frozenPosition = TimeSpan.Zero;
        _state = MediaState.Stopped;
    }

    public void Observe(MediaTrack? track, DateTimeOffset now)
    {
        if (track is null || !Supports(track) || track.State == MediaState.Stopped) { Reset(); return; }
        string key = track.Id + "\n" + track.SongKey;
        if (key != _key)
        {
            _key = key; _anchorAt = now; _anchorPosition = TimeSpan.Zero; _frozenPosition = TimeSpan.Zero; _state = track.State;
            return;
        }
        if (track.State == MediaState.Playing)
        {
            // A song that was paused (or briefly unknown while the player changed tracks) continues from where it stopped.
            if (_state != MediaState.Playing) { _anchorPosition = _frozenPosition; _anchorAt = now; }
        }
        else if (_state == MediaState.Playing)
        {
            _frozenPosition = Current(now);
        }
        _state = track.State;
    }

    public TimeSpan Position(MediaTrack track, DateTimeOffset now)
    {
        if (!Supports(track) || track.State == MediaState.Stopped) return TimeSpan.Zero;
        string key = track.Id + "\n" + track.SongKey;
        if (key != _key) Observe(track, now);
        return Current(now);
    }

    private TimeSpan Current(DateTimeOffset now)
    {
        if (_state != MediaState.Playing) return Clamp(_frozenPosition);
        var elapsed = now - _anchorAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return Clamp(_anchorPosition + elapsed);
    }

    private TimeSpan Clamp(TimeSpan position)
    {
        if (position < TimeSpan.Zero) return TimeSpan.Zero;
        return _duration is { } duration && position > duration ? duration : position;
    }
}
