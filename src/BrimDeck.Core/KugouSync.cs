namespace BrimDeck.Core;

// A reading of Kugou's progress slider: a whole percentage of the song, and when it was read.
public readonly record struct KugouSample(double Percent, DateTimeOffset At);

// Turns Kugou's whole-percent slider into a playback position. The slider moves in 1% steps, so a reading places the song
// in the middle of its step; between readings the wall clock advances the position while the song plays, and a new reading
// re-anchors it. This follows a seek and a song started part way through, which a clock anchored at the start cannot.
public static class KugouSync
{
    // Added to the estimated clock only when the slider cannot be read: Windows reports the new song a little after it
    // starts, so an estimate anchored at that report runs behind the singer.
    public const double EstimateLead = 0.8;

    public static TimeSpan Position(KugouSample sample, DateTimeOffset now, TimeSpan duration, bool playing)
    {
        if (!(duration > TimeSpan.Zero) || !double.IsFinite(sample.Percent)) return TimeSpan.Zero;
        double fraction = Math.Clamp((sample.Percent + .5) / 100, 0, 1);
        var position = TimeSpan.FromTicks((long)(duration.Ticks * fraction));
        if (playing && now > sample.At) position += now - sample.At;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;
    }
}
