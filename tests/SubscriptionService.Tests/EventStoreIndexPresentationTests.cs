using SubscriptionService.Worker.Ui;

namespace SubscriptionService.Tests;

public sealed class EventStoreIndexPresentationTests
{
    [Fact]
    public void ParseIndexList_MapsArrayItemsToCards()
    {
        var json = """
        [
          { "name": "events-by-organisation", "state": "enabled", "stream_prefix": "organisation-" },
          { "id": "events-by-user", "status": "disabled" }
        ]
        """;

        var items = EventStoreIndexPresentation.ParseIndexList(json);

        Assert.Equal(2, items.Count);
        Assert.Equal("events-by-organisation", items[0].Name);
        Assert.Equal("enabled", items[0].State);
        Assert.Contains("stream prefix", items[0].Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("events-by-user", items[1].Name);
        Assert.Equal("disabled", items[1].State);
    }

    [Fact]
    public void CombineIndexLists_MarksBuiltInIndexesReadOnlyAndPreservesUserIndexes()
    {
        var userIndexes = new[]
        {
            new EventStoreIndexListItemViewModel("orders-by-country", "started", "User index", string.Empty, false, "User-defined")
        };
        var builtInIndexes = new[]
        {
            EventStoreIndexPresentation.CreateBuiltInIndex("$idx-et-OrderPlaced", "Event type")
        };

        var result = EventStoreIndexPresentation.CombineIndexLists(userIndexes, builtInIndexes);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Name == "$idx-et-OrderPlaced" && item.IsBuiltIn && item.IsReadOnly);
        Assert.Contains(result, item => item.Name == "orders-by-country" && !item.IsBuiltIn && !item.IsReadOnly);
    }

    [Fact]
    public void GetResponseSummary_ReportsItemCountForArrayPayloads()
    {
        var json = """
        [
          { "name": "events-by-organisation" },
          { "name": "events-by-user" }
        ]
        """;

        var summary = EventStoreIndexPresentation.GetResponseSummary(json);

        Assert.Equal("2 indexes returned", summary);
    }

    [Fact]
    public void FormatJson_LeavesInvalidJsonUntouched()
    {
        var formatted = EventStoreIndexPresentation.FormatJson("not json");

        Assert.Equal("not json", formatted);
    }

    [Theory]
    [InlineData("INDEX_STATE_STARTED", "▶")]
    [InlineData("INDEX_STATE_STOPPED", "⏸")]
    [InlineData("INDEX_STATE_BUILDING", "⚙")]
    [InlineData("INDEX_STATE_FAILED", "✕")]
    [InlineData("anything-else", "•")]
    public void GetStateIcon_MapsKnownIndexStates(string state, string expectedIcon)
    {
        var icon = EventStoreIndexPresentation.GetStateIcon(state);

        Assert.Equal(expectedIcon, icon);
    }
}
