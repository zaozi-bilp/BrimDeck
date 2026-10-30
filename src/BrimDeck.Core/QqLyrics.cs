using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrimDeck.Core;

public sealed partial class LyricsService
{
    private static bool QqSucceeded(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out int number) && number == 0;

    private async Task<Lyrics> FindQq(MediaTrack track, CancellationToken token)
    {
        string? mid = null;
        long? id = null;
        bool requiresPreview = false;
        if (track.SongId.StartsWith("qq:", StringComparison.Ordinal))
        {
            mid = track.SongId[3..];
            if (!Regex.IsMatch(mid, @"\A[a-zA-Z0-9]{8,32}\z")) return Lyrics.Empty;
        }
        else if (track.SongId.StartsWith("qqid:", StringComparison.Ordinal))
        {
            string value = track.SongId[5..];
            if (!Regex.IsMatch(value, @"\A[1-9][0-9]{0,15}\z") || !long.TryParse(value, out long number)) return Lyrics.Empty;
            id = number;
        }
        else
        {
            // The desktop transport currently has no song ID. Wait for its duration before
            // matching; otherwise an identically named live recording or cover can be selected.
            if (track.Duration <= TimeSpan.Zero) return Lyrics.Empty;
            var search = await Get("https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp?format=json&p=1&n=20&w=" +
                Uri.EscapeDataString(track.Title + " " + track.Artist), token);
            if (search is not { } root || !QqSucceeded(root) ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("song", out var song) || song.ValueKind != JsonValueKind.Object ||
                !song.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) return Lyrics.Empty;
            var candidates = new List<(string Mid, double Seconds, bool ExactArtists)>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("singer", out var singers) ||
                    singers.ValueKind != JsonValueKind.Array) continue;
                string artist = string.Join(" / ", singers.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object).Select(s => Str(s, "name")));
                string candidateMid = Str(item, "songmid");
                if (!Regex.IsMatch(candidateMid, @"\A[a-zA-Z0-9]{8,32}\z") ||
                    !Matches(track, WebUtility.HtmlDecode(Str(item, "songname")), WebUtility.HtmlDecode(artist), track.Duration.TotalSeconds) ||
                    track.IsQqMusic && !string.IsNullOrWhiteSpace(track.Album) && Normalize(track.Album) != Normalize(WebUtility.HtmlDecode(Str(item, "albumname")))) continue;
                var expectedArtists = Regex.Split(track.Artist, @"\s*[/、,，&;；]\s*").Select(Normalize).Where(a => a.Length > 0).ToHashSet();
                var actualArtists = singers.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object)
                    .Select(s => Normalize(WebUtility.HtmlDecode(Str(s, "name")))).Where(a => a.Length > 0);
                candidates.Add((candidateMid, Num(item, "interval"), expectedArtists.SetEquals(actualArtists)));
            }
            mid = candidates.Where(c => Math.Abs(c.Seconds - track.Duration.TotalSeconds) <= 2)
                .OrderBy(c => Math.Abs(c.Seconds - track.Duration.TotalSeconds)).Select(c => c.Mid).FirstOrDefault();
            if (mid is null && track.IsQqMusic && !string.IsNullOrWhiteSpace(track.Album))
            {
                // A VIP preview reports the clip duration. Require an unambiguous recording
                // and verify its preview length with QQ's song detail before accepting it.
                var previews = candidates.Where(c => c.ExactArtists && c.Seconds > track.Duration.TotalSeconds + 2)
                    .Select(c => c.Mid).Distinct().ToArray();
                if (previews.Length == 1) { mid = previews[0]; requiresPreview = true; }
            }
            if (mid is null) return Lyrics.Empty;
        }

        var parameter = new Dictionary<string, object>();
        if (mid is not null) parameter["songMID"] = mid;
        else parameter["songID"] = id!.Value;
        var detailParameter = new Dictionary<string, object> { ["song_type"] = 0 };
        if (mid is not null) detailParameter["song_mid"] = mid;
        else detailParameter["song_id"] = id!.Value;
        string payload = JsonSerializer.Serialize(new
        {
            comm = new { ct = 24, cv = 0 },
            lyric = new { module = "music.musichallSong.PlayLyricInfo", method = "GetPlayLyricInfo", param = parameter },
            info = new { module = "music.pf_song_detail_svr", method = "get_song_detail_yqq", param = detailParameter }
        });
        var response = await Get("https://u.y.qq.com/cgi-bin/musicu.fcg?data=" + Uri.EscapeDataString(payload), token);
        bool preview = IsQqPreview(response, track, mid, id);
        if (requiresPreview && !preview) return Lyrics.Empty;
        if (response is { } result && QqSucceeded(result) && result.TryGetProperty("lyric", out var lyric) && QqSucceeded(lyric) &&
            lyric.TryGetProperty("data", out var content) && content.ValueKind == JsonValueKind.Object)
        {
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(Str(content, "lyric")));
                var parsed = ParseQq(text, track);
                if (parsed.Lines.Count > 0) return parsed with { QqPreview = preview };
            }
            catch (FormatException) { }
        }
        // The legacy endpoint uses the same QQ song identity and catalog, never another provider.
        if (mid is null) return Lyrics.Empty;
        var legacy = await Get($"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={mid}&format=json&nobase64=1", token);
        return legacy is { } old && QqSucceeded(old) ? ParseQq(Str(old, "lyric"), track) with { QqPreview = preview } : Lyrics.Empty;
    }

    private static bool IsQqPreview(JsonElement? response, MediaTrack track, string? mid, long? id)
    {
        if (track.Duration <= TimeSpan.Zero || response is not { } root || !QqSucceeded(root) ||
            !root.TryGetProperty("info", out var info) || !QqSucceeded(info) ||
            !info.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("track_info", out var song) || song.ValueKind != JsonValueKind.Object ||
            (mid is not null ? Str(song, "mid") != mid : Num(song, "id") != id) ||
            Num(song, "interval") <= track.Duration.TotalSeconds + 2 ||
            !song.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.Object) return false;
        bool MatchesClip(string begin, string end)
        {
            double start = Num(file, begin), finish = Num(file, end);
            return start >= 0 && finish > start && finish <= Num(song, "interval") * 1000 + 2000 &&
                Math.Abs((finish - start) / 1000 - track.Duration.TotalSeconds) <= 2;
        }
        // These describe available clips, not necessarily the desktop player's active offset.
        return MatchesClip("b_30s", "e_30s") || MatchesClip("try_begin", "try_end");
    }

    private static Lyrics ParseQq(string text, MediaTrack track)
    {
        var lyrics = Lyrics.Parse(WebUtility.HtmlDecode(text), null, track.Duration.TotalSeconds, "QQ 音乐");
        // Empty timestamp placeholders are not a usable lyric result and must not be cached.
        return lyrics.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Text)) ? lyrics : Lyrics.Empty;
    }

    // The searches tried for a song from another player, in order: the full title with its artist, the full title, the
    // bracketed part alone (many Chinese titles put the part people search for in brackets), then the title without it.
    // Kugou's own lyric search returns no candidates, so these public-catalog queries stand in for it.
    public static IReadOnlyList<string> CatalogQueries(string title, string artist)
    {
        var queries = new List<string>();
        void Add(string value)
        {
            value = Regex.Replace(value ?? "", @"\s+", " ").Trim();
            if (value.Length > 0 && !queries.Contains(value)) queries.Add(value);
        }
        string decorated = title ?? "", artists = artist ?? "";
        string core = Regex.Replace(decorated, @"[\(（\[【].*?[\)）\]】]", " ").Trim();
        var bracketed = Regex.Matches(decorated, @"[\(（\[【](.*?)[\)）\]】]")
            .Select(match => match.Groups[1].Value.Trim()).Where(value => value.Length > 0).ToArray();
        Add($"{decorated} {artists}"); Add(decorated);
        foreach (string part in bracketed) Add($"{part} {artists}");
        foreach (string part in bracketed) Add(part);
        if (core.Length > 0 && core != decorated) { Add($"{core} {artists}"); Add(core); }
        return queries;
    }

    // Whether a catalog result is the same recording: a title variant matches, at least one of the track's artists is named,
    // a version tag (live, accompaniment, remix) is not added, and a known duration is close.
    public static bool CatalogMatch(MediaTrack track, string title, string artist, double seconds)
    {
        string candidateTitle = Normalize(title), candidateArtist = Normalize(artist);
        if (candidateTitle.Length == 0) return false;
        var variants = CatalogQueries(track.Title, "").Select(Normalize)
            .Where(value => value.Length >= 2).Distinct(StringComparer.Ordinal).ToArray();
        bool sameTitle = variants.Any(value => candidateTitle == value ||
            candidateTitle.Contains(value, StringComparison.Ordinal) || value.Contains(candidateTitle, StringComparison.Ordinal));
        if (!sameTitle) return false;
        var wanted = Regex.Split(track.Artist, @"\s*[/、,，&;；]\s*").Select(Normalize).Where(value => value.Length > 0).ToArray();
        if (wanted.Length > 0 && !wanted.Any(value => candidateArtist.Contains(value, StringComparison.Ordinal) || value.Contains(candidateArtist, StringComparison.Ordinal)))
            return false;
        foreach (string tag in new[] { "live", "现场", "伴奏", "remix", "dj", "cover", "翻唱", "纯音乐" })
            if (candidateTitle.Contains(tag, StringComparison.OrdinalIgnoreCase) && !Normalize(track.Title).Contains(tag, StringComparison.OrdinalIgnoreCase))
                return false;
        return track.Duration.TotalSeconds <= 0 || seconds <= 0 || Math.Abs(seconds - track.Duration.TotalSeconds) <= 4;
    }

    // A song from a player without a lyric provider (Kugou): search QQ Music's public catalog and fetch the matched
    // recording's LRC. The match's length is kept so the compact progress and the estimated position have an end.
    private async Task<Lyrics> FindQqCatalog(MediaTrack track, CancellationToken token)
    {
        var tried = new HashSet<string>(StringComparer.Ordinal);
        foreach (string query in CatalogQueries(track.Title, track.Artist).Take(5))
        {
            var search = await Get("https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp?format=json&p=1&n=20&w=" +
                Uri.EscapeDataString(query), token);
            if (search is not { ValueKind: JsonValueKind.Object } root || !QqSucceeded(root) ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("song", out var song) || song.ValueKind != JsonValueKind.Object ||
                !song.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) continue;
            var candidates = new List<(string Mid, string Title, double Seconds, bool ExactArtist)>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string mid = Str(item, "songmid");
                if (!Regex.IsMatch(mid, @"\A[a-zA-Z0-9]{8,32}\z") || !tried.Add(mid)) continue;
                string title = WebUtility.HtmlDecode(Str(item, "songname"));
                string artist = item.TryGetProperty("singer", out var singers) && singers.ValueKind == JsonValueKind.Array
                    ? string.Join(" / ", singers.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object).Select(s => WebUtility.HtmlDecode(Str(s, "name"))))
                    : "";
                double seconds = Num(item, "interval");
                if (!CatalogMatch(track, title, artist, seconds)) continue;
                var expected = Regex.Split(track.Artist, @"\s*[/、,，&;；]\s*").Select(Normalize).Where(value => value.Length > 0).ToHashSet();
                var actual = Regex.Split(artist, @"\s*/\s*").Select(Normalize).Where(value => value.Length > 0).ToHashSet();
                candidates.Add((mid, title, seconds, expected.SetEquals(actual)));
            }
            foreach (var candidate in candidates.OrderByDescending(value => value.ExactArtist)
                .ThenBy(value => track.Duration > TimeSpan.Zero && value.Seconds > 0 ? Math.Abs(value.Seconds - track.Duration.TotalSeconds) : 0)
                .Take(2))
            {
                var lyrics = await FindQqLyricsByMid(candidate.Mid, track, token);
                if (lyrics.Lines.Count > 0)
                    return candidate.Seconds > 0 ? lyrics with { Duration = TimeSpan.FromSeconds(candidate.Seconds) } : lyrics;
            }
        }
        return Lyrics.Empty;
    }

    private async Task<Lyrics> FindQqLyricsByMid(string mid, MediaTrack track, CancellationToken token)
    {
        string payload = JsonSerializer.Serialize(new
        {
            comm = new { ct = 24, cv = 0 },
            lyric = new { module = "music.musichallSong.PlayLyricInfo", method = "GetPlayLyricInfo", param = new { songMID = mid } }
        });
        var response = await Get("https://u.y.qq.com/cgi-bin/musicu.fcg?data=" + Uri.EscapeDataString(payload), token);
        if (response is { } result && QqSucceeded(result) && result.TryGetProperty("lyric", out var lyric) && QqSucceeded(lyric) &&
            lyric.TryGetProperty("data", out var content) && content.ValueKind == JsonValueKind.Object)
        {
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(Str(content, "lyric")));
                var parsed = ParseQq(text, track);
                if (parsed.Lines.Count > 0) return parsed;
            }
            catch (FormatException) { }
        }
        var legacy = await Get($"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={mid}&format=json&nobase64=1", token);
        return legacy is { } old && QqSucceeded(old) ? ParseQq(Str(old, "lyric"), track) : Lyrics.Empty;
    }
}
