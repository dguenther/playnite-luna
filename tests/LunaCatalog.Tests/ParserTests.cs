using System;
using System.IO;
using System.Linq;
using LunaCatalog;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class ParserTests
{
    private static CatalogQuery Query(string name) => CatalogQuery.UsQueries.Single(q => q.Name == name);
    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));
    private static JObject Response(string name = "Standard") => JObject.Parse(Fixture(name));
    private static JObject FirstTile(JObject root) => (JObject)root.SelectToken("pageMemberGroups.mainContent.widgets[0].widgets[0]")!;

    [Theory]
    [InlineData("AllGames", 5)]
    [InlineData("Standard", 2)]
    [InlineData("Premium", 2)]
    [InlineData("GameNight", 2)]
    public void RecordedStringPresentationDataParses(string name, int count)
    {
        var result = LunaResponseParser.Parse(Fixture(name), Query(name));
        Assert.True(result.ResponseValid);
        Assert.Equal(count, result.Games.Count);
        Assert.Equal("NoContinuationObserved", result.PaginationStatus);
        Assert.All(result.Games, g => Assert.StartsWith("https://luna.amazon.com/game/", g.Url));
        Assert.All(result.Games, g => Assert.NotEmpty(g.Id));
    }

    [Fact]
    public void ObjectPresentationDataAndAdditionalNestingAreSupported()
    {
        var response = Response();
        var tile = FirstTile(response);
        tile["presentationData"] = JObject.Parse((string)tile["presentationData"]!);
        var members = response["pageMemberGroups"]!.DeepClone();
        response["pageMemberGroups"] = new JObject { ["extra"] = new JArray(members) };
        Assert.True(LunaResponseParser.Parse(response.ToString(), Query("Standard")).ResponseValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("broken-json")]
    public void MissingOrMalformedPresentationDataInvalidatesQuery(string? value)
    {
        var response = Response();
        FirstTile(response)["presentationData"] = value == null ? JValue.CreateNull() : new JValue(value);
        var result = LunaResponseParser.Parse(response.ToString(), Query("Standard"));
        Assert.False(result.ResponseValid);
        Assert.Single(result.Games);
        Assert.Contains(result.Diagnostics, d => d.Contains("Rejected GAME_TILE"));
    }

    [Fact]
    public void UnknownPageTypeIsNotTreatedAsSuccess()
    {
        var response = Response();
        response["pageContext"]!["pageType"] = "sign_in";
        var result = LunaResponseParser.Parse(response.ToString(), Query("Standard"));
        Assert.False(result.ResponseValid);
        Assert.Empty(result.Games);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"error\":\"backend failure\"}")]
    public void InvalidResponseCannotBecomeEmptyValidCatalog(string raw)
    {
        Assert.False(LunaResponseParser.Parse(raw, Query("Standard")).ResponseValid);
    }

    [Fact]
    public void EmptyCatalogIsNotValidated()
    {
        var response = Response();
        response.SelectToken("pageMemberGroups.mainContent.widgets[0]")!["widgets"] = new JArray();
        Assert.False(LunaResponseParser.Parse(response.ToString(), Query("Standard")).ResponseValid);
    }

    [Theory]
    [InlineData("nextToken", "a-cursor")]
    [InlineData("continuationToken", "a-cursor")]
    [InlineData("nextCursor", "a-cursor")]
    public void ContinuationMakesOnePageUnusableAsValidatedCatalog(string key, string value)
    {
        var response = Response();
        response.SelectToken("pageMemberGroups.mainContent.widgets[0]")![key] = value;
        var result = LunaResponseParser.Parse(response.ToString(), Query("Standard"));
        Assert.False(result.ResponseValid);
        Assert.Equal("UnresolvedContinuation", result.PaginationStatus);
        Assert.Contains(result.Diagnostics, d => d.Contains(key));
    }

    [Fact]
    public void HasMoreIsHandledEvenWithoutCursor()
    {
        var response = Response();
        response["hasMore"] = true;
        Assert.False(LunaResponseParser.Parse(response.ToString(), Query("Standard")).ResponseValid);
        response["hasMore"] = false;
        Assert.True(LunaResponseParser.Parse(response.ToString(), Query("Standard")).ResponseValid);
    }

    [Fact]
    public void StableIdentifiersDeduplicateAndTitleCollisionsDoNot()
    {
        var response = Response();
        var tiles = (JArray)response.SelectToken("pageMemberGroups.mainContent.widgets[0].widgets")!;
        var duplicate = FirstTile(response).DeepClone();
        tiles.Add(duplicate);
        Assert.Equal(2, LunaResponseParser.Parse(response.ToString(), Query("Standard")).Games.Count);
        var distinct = (JObject)duplicate.DeepClone();
        var data = JObject.Parse((string)distinct["presentationData"]!);
        data["gameId"] = "amzn1.adg.product.00000000-0000-0000-0000-000000000001";
        distinct["presentationData"] = data.ToString();
        tiles.Add(distinct);
        var result = LunaResponseParser.Parse(response.ToString(), Query("Standard"));
        Assert.True(result.ResponseValid);
        Assert.Equal(3, result.Games.Count);
        Assert.Equal(2, result.Games.Count(g => g.Title == "Alan Wake 2"));
    }

    [Fact]
    public void RegionIsPartOfIdentity()
    {
        var us = LunaResponseParser.Parse(Fixture("Standard"), Query("Standard"), "US");
        var ca = LunaResponseParser.Parse(Fixture("Standard"), Query("Standard"), "CA");
        Assert.Equal(4, CatalogSnapshot.Merge(new[] { us, ca }).Games.Count);
    }

    [Fact]
    public void MembershipsMergeWithoutMutatingIndividualQueriesOrClaimingCompleteness()
    {
        var results = CatalogQuery.UsQueries.Select(q => LunaResponseParser.Parse(Fixture(q.Name), q)).ToList();
        var snapshot = CatalogSnapshot.Merge(results);
        var alan = snapshot.Games.Single(g => g.Title == "Alan Wake 2");
        Assert.Equal(new[] { "Premium", "Standard" }, alan.Categories);
        Assert.Empty(results[0].Games.Single(g => g.Title == "Alan Wake 2").Categories);
        Assert.Equal(5, snapshot.Games.Count);
        Assert.True(snapshot.AllQueriesSucceeded);
        Assert.False(snapshot.IsComplete);
        Assert.Equal("Unverified", snapshot.CoverageStatus);
    }

    [Theory]
    [InlineData("https://example.com/game/title/B0GFWDM8NP")]
    [InlineData("//example.com/game/title/B0GFWDM8NP")]
    [InlineData("javascript:alert(1)")]
    public void NonLunaActionsCannotBeImported(string target)
    {
        var response = Response();
        FirstTile(response)["actions"] = new JArray(new JObject { ["target"] = target });
        Assert.False(LunaResponseParser.Parse(response.ToString(), Query("Standard")).ResponseValid);
    }

    [Fact]
    public void UnexpectedMetadataTypesAreRejectedWithoutCrashing()
    {
        var response = Response();
        var data = JObject.Parse((string)FirstTile(response)["presentationData"]!);
        data["title"] = new JObject { ["unexpected"] = "object" };
        FirstTile(response)["presentationData"] = data.ToString();
        Assert.False(LunaResponseParser.Parse(response.ToString(), Query("Standard")).ResponseValid);
    }

    [Fact]
    public void RecordedLunaNamespaceAndFortniteSlugOnlyActionAreSupported()
    {
        var result = LunaResponseParser.Parse(Fixture("EdgeCases"), Query("AllGames"));
        Assert.True(result.ResponseValid);
        Assert.Equal(2, result.Games.Count);
        Assert.StartsWith("amzn1.luna.product.", result.Games.Single(g => g.Title.StartsWith("Batman")).Id);
        var fortnite = result.Games.Single(g => g.Title == "Fortnite");
        Assert.Equal("https://luna.amazon.com/game/fortnite", fortnite.Url);
        Assert.Equal("B0BTRVRN6M", fortnite.Asin);
    }

    [Fact]
    public void MissingNativeIdFallsBackToRecordedAsin()
    {
        var response = Response();
        var tile = FirstTile(response);
        var data = JObject.Parse((string)tile["presentationData"]!);
        data.Remove("gameId");
        tile["presentationData"] = data.ToString();
        var result = LunaResponseParser.Parse(response.ToString(), Query("Standard"));
        Assert.True(result.ResponseValid);
        Assert.Equal("B0GFWDM8NP", result.Games.Single(g => g.Title == "Alan Wake 2").Id);
    }
}
