using System.Net;
using System.Text;
using System.Text.Json;
using BrimDeck.Core;

internal static class CatalogLyricsTests
{
    private static string Search(params object[] songs) => JsonSerializer.Serialize(new { code = 0, data = new { song = new { list = songs } } });
    private static object Song(string mid, string title, string artist, int seconds) =>
        new { songmid = mid, songname = title, singer = new[] { new { name = artist } }, interval = seconds };
    private static string Rpc(string lrc) => JsonSerializer.Serialize(new
    {
        code = 0,
        lyric = new { code = 0, data = new { lyric = Convert.ToBase64String(Encoding.UTF8.GetBytes(lrc)) } }
    });

    public static async Task Run(Action<string, bool> check)
    {
        var queries = LyricsService.CatalogQueries("甲乙丙丁 (你我怎么两清)", "李佳薇");
        check("Catalog queries try the bracketed title and the title without it",
            queries.Contains("甲乙丙丁 (你我怎么两清) 李佳薇") && queries.Contains("你我怎么两清 李佳薇") &&
            queries.Contains("你我怎么两清") && queries.Contains("甲乙丙丁 李佳薇"));

        var track = new MediaTrack { Source = "酷狗音乐", Title = "甲乙丙丁 (你我怎么两清)", Artist = "李佳薇", State = MediaState.Playing };
        check("A catalog match accepts the same title and artist",
            LyricsService.CatalogMatch(track, "甲乙丙丁 (你我怎么两清)", "李佳薇", 210));
        check("A catalog match rejects another artist",
            !LyricsService.CatalogMatch(track, "甲乙丙丁 (你我怎么两清)", "张碧晨", 210));
        check("A catalog match rejects an added live version",
            !LyricsService.CatalogMatch(track, "甲乙丙丁 (你我怎么两清) (Live)", "李佳薇", 210));
        check("A catalog match keeps a version the track names",
            LyricsService.CatalogMatch(track with { Title = "甲乙丙丁 (Live)" }, "甲乙丙丁 (Live)", "李佳薇", 210));
        var timed = track with { End = TimeSpan.FromSeconds(120) };
        check("A known duration rejects a recording of another length",
            !LyricsService.CatalogMatch(timed, "甲乙丙丁 (你我怎么两清)", "李佳薇", 180) &&
            LyricsService.CatalogMatch(timed, "甲乙丙丁 (你我怎么两清)", "李佳薇", 122));

        // The first query fails (as Kugou's own title does), then the bracketed part finds the song.
        using var handler = new Handler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("search_for_qq_cp"))
            {
                string keyword = Uri.UnescapeDataString(url[(url.IndexOf("w=", StringComparison.Ordinal) + 2)..]);
                return keyword == "你我怎么两清"
                    ? Search(Song("002AZauQ1UKQxe", "甲乙丙丁 (你我怎么两清)", "李佳薇", 210))
                    : Search();
            }
            return Rpc("[00:02.00]还没来得及习惯\n[00:21.25]独自入睡的不安\n[00:46.16]我的心你还没还");
        });
        using var http = new HttpClient(handler);
        using var service = new LyricsService(http);
        var lyrics = await service.FindAsync(track, true, default);
        check("A Kugou track without a lyric provider takes the catalog lyric", lyrics.Lines.Count == 3 && lyrics.Source == "QQ 音乐");
        check("The catalog match supplies the duration Kugou omits", lyrics.Duration == TimeSpan.FromSeconds(210));
        check("The catalog lookup only asks for the bracketed title after the full title fails",
            handler.Urls.Count == 5 && handler.Urls[0].Contains(Uri.EscapeDataString("甲乙丙丁"), StringComparison.Ordinal) &&
            handler.Urls[3].Contains(Uri.EscapeDataString("你我怎么两清"), StringComparison.Ordinal));

        using var empty = new Handler(_ => Search());
        using var emptyHttp = new HttpClient(empty);
        using var emptyService = new LyricsService(emptyHttp);
        var missing = await emptyService.FindAsync(track with { Title = "没有这首歌" }, true, default);
        check("An unmatched catalog song falls through without lyrics", missing.Lines.Count == 0);
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _reply;
        public Handler(Func<HttpRequestMessage, string> reply) => _reply = reply;
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_reply(request), Encoding.UTF8, "application/json")
            });
        }
    }
}
