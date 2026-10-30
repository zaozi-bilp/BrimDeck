using BrimDeck.Core;

internal static class TwoIslandTests
{
    public static void Run(Action<string, bool> check)
    {
        var settings = new DeckSettings(); settings.Normalize();
        check("Single mode is the default", !settings.SplitIslands && !settings.SplitShown && settings.IslandFor(0) is null);
        check("The default small island is music and the large one is quotas, readings and clock",
            settings.Island1.Parts.SequenceEqual([CompactPart.Music, CompactPart.Equalizer]) && settings.Island1.Pages.SequenceEqual([DeckPage.Music]) &&
            settings.Island2.Parts.SequenceEqual([CompactPart.Rings, CompactPart.Monitor, CompactPart.Clock]) &&
            settings.Island2.Pages.SequenceEqual([DeckPage.Usage, DeckPage.Monitor]));

        settings.SplitIslands = true; settings.Style = CompactStyle.Capsule; settings.Normalize();
        check("Split mode exposes two lanes for the notch and capsule",
            settings.SplitShown && settings.IslandFor(0) is not null && settings.IslandFor(1) is not null);
        check("Quota and media data are wanted while either lane needs them",
            settings.QuotaWanted && settings.MediaWanted);
        check("A lane reports the parts it chose", settings.LaneShows(0, CompactPart.Music) && !settings.LaneShows(0, CompactPart.Rings) &&
            settings.LaneShows(1, CompactPart.Rings) && !settings.LaneShows(1, CompactPart.Music));

        settings.Style = CompactStyle.Line; settings.Normalize();
        check("The line has no split mode", !settings.SplitShown && settings.IslandFor(0) is null);

        settings.Style = CompactStyle.Capsule; settings.SplitIslands = false; settings.Normalize();
        check("Turning split off restores the single island", !settings.SplitShown && settings.IslandFor(0) is null);

        var lane = new IslandSettings();
        lane.Normalize(IslandSettings.Small());
        check("An empty lane takes the fallback parts, pages and size",
            lane.Parts.SequenceEqual([CompactPart.Music, CompactPart.Equalizer]) && lane.Pages.SequenceEqual([DeckPage.Music]) &&
            lane.Width == 196 && lane.Height == 32 && lane.Top is null);
        lane.Width = 9000; lane.Height = 2; lane.Top = 9000; lane.OffsetX = -9000;
        lane.Normalize(IslandSettings.Small());
        check("Lane values are kept within the style's limits",
            lane.Width == 8000 && lane.Height == 24 && lane.Top == 4000 && lane.OffsetX == -4000);

        var original = new DeckSettings(); original.Normalize();
        var copy = original.Copy();
        copy.Island1.Parts.Remove(CompactPart.Music);
        check("A copied settings object owns its island lists",
            original.Island1.Parts.Contains(CompactPart.Music) && !copy.Island1.Parts.Contains(CompactPart.Music));
    }
}
