using System;
using System.Linq;
using LunaCatalog;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class ValidationTests
{
    private static CatalogQuery Query(string name) => CatalogQuery.UsQueries.Single(q => q.Name == name);
    private static JObject Response(string name = "Standard") => JObject.Parse(ParserTests.Fixture(name));
    private static JObject Tile(JObject root) => (JObject)root.SelectToken("pageMemberGroups.mainContent.widgets[0].widgets[0]")!;
    private static JObject Presentation(JObject root) => JObject.Parse((string)Tile(root)["presentationData"]!);
    private static QueryResult Parse(JObject root, string name = "Standard") => LunaResponseParser.Parse(root.ToString(), Query(name));

    [Theory]
    [InlineData("nextPageIndex")]
    [InlineData("nextOffset")]
    [InlineData("loadMore")]
    public void AdditionalContinuationMarkersInvalidateQuery(string key)
    {
        var root = Response();
        root[key] = 12;
        Assert.False(Parse(root).ResponseValid);
    }

    [Theory]
    [InlineData("NEXT_PAGE")]
    [InlineData("LOAD_MORE")]
    [InlineData("PAGINATION")]
    public void ContinuationActionsInvalidateQuery(string type)
    {
        var root = Response();
        root["actions"] = new JArray(new JObject { ["type"] = type });
        Assert.Equal("UnresolvedContinuation", Parse(root).PaginationStatus);
        Assert.False(Parse(root).ResponseValid);
    }

    [Theory]
    [InlineData("totalCount")]
    [InlineData("totalResults")]
    public void ReportedTotalAboveTileCountIsNotAccepted(string key)
    {
        var root = Response();
        root[key] = 99;
        Assert.False(Parse(root).ResponseValid);
        Assert.Contains(Parse(root).Diagnostics, d => d.Contains("Reported total"));
        root[key] = 2;
        Assert.True(Parse(root).ResponseValid);
    }

    [Fact]
    public void OversizedReportedTotalIsDiagnosedWithoutCrashing()
    {
        var root = Response();
        root["totalCount"] = JToken.Parse("99999999999999999999999999999999");
        Assert.False(Parse(root).ResponseValid);
    }

    [Fact]
    public void EmptyPaginationMetadataDoesNotInventContinuation()
    {
        var root = Response();
        root["pagination"] = new JObject();
        root["loadMore"] = new JArray();
        Assert.True(Parse(root).ResponseValid);
    }

    [Fact]
    public void PromotionalTilesOutsideMainContentDoNotEnterCatalog()
    {
        var root = Response();
        var promotional = (JObject)Tile(root).DeepClone();
        promotional["presentationData"] = "invalid";
        root["pageMemberGroups"]!["sidebar"] = new JObject { ["widgets"] = new JArray(promotional) };
        var result = Parse(root);
        Assert.True(result.ResponseValid);
        Assert.Equal(2, result.TilesObserved);
        root["pageMemberGroups"]!["mainContent"]!.Parent!.Remove();
        Assert.False(Parse(root).ResponseValid);
        Assert.Empty(Parse(root).Games);
    }

    [Fact]
    public void NestedSidebarMainContentCannotReplaceMissingBrowseCollection()
    {
        var root = Response();
        var main = root["pageMemberGroups"]!["mainContent"]!.DeepClone();
        root["pageMemberGroups"] = new JObject
        {
            ["sidebar"] = new JObject { ["section"] = new JObject { ["mainContent"] = main } }
        };
        var result = Parse(root);
        Assert.False(result.ResponseValid);
        Assert.Empty(result.Games);
        Assert.Equal(0, result.TilesObserved);
        Assert.Contains(result.Diagnostics, d => d.Contains("mainContent.widgets"));
    }

    [Fact]
    public void NestedSidebarMainContentCannotAddTilesToValidBrowseCollection()
    {
        var root = Response();
        var main = Response("GameNight")["pageMemberGroups"]!["mainContent"]!.DeepClone();
        root["pageMemberGroups"]!["sidebar"] = new JObject
        {
            ["section"] = new JObject { ["mainContent"] = main }
        };
        var result = Parse(root);
        Assert.True(result.ResponseValid);
        Assert.Equal(2, result.TilesObserved);
        Assert.Equal(new[] { "Alan Wake 2", "WRC Generations Fully Loaded Edition" }, result.Games.Select(g => g.Title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CA")]
    [InlineData("US&territory=US")]
    public void PlayLinkRequiresOneMatchingTerritory(string? territory)
    {
        var root = Response();
        var data = Presentation(root);
        data["hoverDetails"]!["productUrl"] = "https://play.amazon.com/play?asin=B0GFWDM8NP" +
            (territory == null ? "" : "&territory=" + territory);
        Tile(root)["presentationData"] = data;
        var result = Parse(root);
        Assert.False(result.ResponseValid);
        Assert.DoesNotContain(result.Games, game => game.Title == "Alan Wake 2");
        Assert.Contains(result.Diagnostics, d => d.Contains("Rejected GAME_TILE"));
    }

    [Theory]
    [InlineData("https://play.amazon.com/play?asin=B000000001&territory=US")]
    [InlineData("https://play.amazon.com/play?asin=B0GFWDM8NP&territory=CA")]
    [InlineData("https://example.com/play?asin=B0GFWDM8NP&territory=US")]
    [InlineData("https://play.amazon.com:444/play?asin=B0GFWDM8NP")]
    [InlineData("https://user@play.amazon.com/play?asin=B0GFWDM8NP")]
    [InlineData("https://play.amazon.com/play?asin=B0GFWDM8NP&asin=B000000001")]
    [InlineData(null)]
    public void UnreliablePlayLinkCannotProduceUsableQuery(string? url)
    {
        var root = Response();
        var data = Presentation(root);
        data["hoverDetails"]!["productUrl"] = url;
        Tile(root)["presentationData"] = data;
        Assert.False(Parse(root).ResponseValid);
    }

    [Theory]
    [InlineData("https://luna.amazon.com:444/game/title/B0GFWDM8NP")]
    [InlineData("https://user@luna.amazon.com/game/title/B0GFWDM8NP")]
    public void UnexpectedAuthorityCannotBecomeLunaAction(string url)
    {
        var root = Response();
        Tile(root)["actions"] = new JArray(new JObject { ["target"] = url });
        Assert.False(Parse(root).ResponseValid);
    }

    [Fact]
    public void ConflictingProductIdsForSameAsinAreRejectedWithinQuery()
    {
        var root = Response();
        var duplicate = (JObject)Tile(root).DeepClone();
        var data = JObject.Parse((string)duplicate["presentationData"]!);
        data["gameId"] = "amzn1.adg.product.00000000-0000-0000-0000-000000000001";
        duplicate["presentationData"] = data;
        ((JArray)root.SelectToken("pageMemberGroups.mainContent.widgets[0].widgets")!).Add(duplicate);
        Assert.False(Parse(root).ResponseValid);
        Assert.All(Parse(root).Games, game => Assert.False(game.IsImportable));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrossQueryIdentityConflictsPreventSuccessfulSnapshot(bool changeAsin)
    {
        var results = CatalogQuery.UsQueries.Select(q => LunaResponseParser.Parse(ParserTests.Fixture(q.Name), q)).ToList();
        var standard = results.Single(q => q.Name == "Standard").Games.First();
        if (changeAsin) standard.Asin = "B000000001";
        else standard.Id = "amzn1.adg.product.00000000-0000-0000-0000-000000000001";
        var snapshot = CatalogSnapshot.Merge(results);
        Assert.True(snapshot.HasIdentityConflicts);
        Assert.False(snapshot.AllQueriesSucceeded);
        Assert.Equal("Incomplete", snapshot.CoverageStatus);
        Assert.Contains(snapshot.Games, game => game.IdentityConflict && !game.IsImportable);
        Assert.False(standard.IdentityConflict); // Merge must not mutate observations.
    }

    [Theory]
    [InlineData("AllGames")]
    [InlineData("Standard")]
    public void AllGamesAndCategorySetMismatchInvalidatesSnapshot(string omitFrom)
    {
        var results = CatalogQuery.UsQueries.Select(q => LunaResponseParser.Parse(ParserTests.Fixture(q.Name), q)).ToList();
        var target = results.Single(q => q.Name == omitFrom);
        // Remove a category-exclusive game, or one of All Games' IDs.
        target.Games.Remove(target.Games.Last());
        var snapshot = CatalogSnapshot.Merge(results);
        Assert.False(snapshot.CatalogSetsAgree);
        Assert.False(snapshot.AllQueriesSucceeded);
        Assert.Equal("Incomplete", snapshot.CoverageStatus);
        Assert.False(snapshot.IsComplete);
    }

    [Fact]
    public void TrailingJsonCannotHideAdditionalResponseData()
    {
        Assert.False(LunaResponseParser.Parse(ParserTests.Fixture("Standard") + " {}", Query("Standard")).ResponseValid);
    }

    [Fact]
    public void ExcessivelyNestedEncodedPresentationIsRejected()
    {
        var root = Response();
        var nested = new string('[', 70) + "0" + new string(']', 70);
        Tile(root)["presentationData"] = "{\"title\":\"example\",\"nested\":" + nested + "}";
        Assert.False(Parse(root).ResponseValid);
    }
}
