using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class DetailTests
{
    private static LunaGame Game(string title) => LunaResponseParser.Parse(ParserTests.Fixture(
        title == "Fortnite" ? "EdgeCases" : "AllGames"), CatalogQuery.UsQueries[0]).Games.Single(g => g.Title == title);

    private static JObject Widget(JObject root, string kind) => root.DescendantsAndSelf()
        .OfType<JObject>().Single(w => (string?)w["type"] == kind);

    [Theory]
    [InlineData("AlienDetail", "Alien: Isolation")]
    [InlineData("FortniteDetail", "Fortnite")]
    public void RecordedDetailIdentityAndCanonicalUrlMatchCatalog(string fixture, string title)
    {
        var result = LunaGameDetailParser.Parse(ParserTests.Fixture(fixture), Game(title));
        Assert.True(result.ResponseValid);
        Assert.True(result.IdentityMatches);
        Assert.True(result.UrlMatches);
        Assert.Equal(title, result.Title);
        Assert.Equal("Not Available", result.PrimaryButtonText);
        // This localized anonymous button does not filter the catalog entry or establish a removal.
        Assert.Contains(result.Diagnostics, d => d.Contains("not verified entitlement"));
    }

    [Fact]
    public void RecommendationsAndDlcCannotReplaceMainProductIdentity()
    {
        var root = JObject.Parse(ParserTests.Fixture("AlienDetail"));
        var main = Widget(root, "PRODUCT_DETAIL");
        var data = JObject.Parse((string)main["presentationData"]!);
        data["productId"] = "amzn1.adg.product.00000000-0000-0000-0000-000000000001";
        main["presentationData"] = data.ToString();
        // A matching recommendation must not rescue a mismatched main product.
        Widget(root, "GAME_TILE_VERTICAL")["presentationData"] = new JObject
        {
            ["gameId"] = Game("Alien: Isolation").Id,
            ["title"] = "Alien: Isolation"
        };
        var result = LunaGameDetailParser.Parse(root.ToString(), Game("Alien: Isolation"));
        Assert.False(result.ResponseValid);
        Assert.False(result.IdentityMatches);
    }

    [Theory]
    [InlineData("https://example.com/game/alien-isolation/B09HFKWMTQ")]
    [InlineData("https://luna.amazon.com/game/different-title/B09HFKWMTQ")]
    public void WrongCanonicalLinkInvalidatesDetailCheck(string canonical)
    {
        var root = JObject.Parse(ParserTests.Fixture("AlienDetail"));
        Widget(root, "SEO_DETAIL")["presentationData"] = new JObject { ["canonicalUrl"] = canonical };
        var result = LunaGameDetailParser.Parse(root.ToString(), Game("Alien: Isolation"));
        Assert.False(result.ResponseValid);
        Assert.False(result.UrlMatches);
    }

    [Fact]
    public void DifferentAsinInvalidatesIdentityEvenWhenNativeIdMatches()
    {
        var root = JObject.Parse(ParserTests.Fixture("AlienDetail"));
        var main = Widget(root, "PRODUCT_DETAIL");
        var data = JObject.Parse((string)main["presentationData"]!);
        data["asin"] = "B000000001";
        main["presentationData"] = data;
        Assert.False(LunaGameDetailParser.Parse(root.ToString(), Game("Alien: Isolation")).IdentityMatches);
    }

    [Fact]
    public void MainProductIsRequiredAndMultipleMainProductsAreAmbiguous()
    {
        var root = JObject.Parse(ParserTests.Fixture("AlienDetail"));
        var main = Widget(root, "PRODUCT_DETAIL");
        var parent = (JArray)main.Parent!;
        var copy = main.DeepClone();
        parent.Add(copy);
        Assert.False(LunaGameDetailParser.Parse(root.ToString(), Game("Alien: Isolation")).ResponseValid);
        main.Remove();
        copy.Remove();
        Assert.False(LunaGameDetailParser.Parse(root.ToString(), Game("Alien: Isolation")).ResponseValid);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("{\"pageContext\":{\"pageType\":\"sign_in\"}}")]
    public void InvalidResponsesAreReportedWithoutBecomingValidChecks(string raw)
    {
        Assert.False(LunaGameDetailParser.Parse(raw, Game("Alien: Isolation")).ResponseValid);
    }

    [Fact]
    public void DuplicateQueryNamesCannotSatisfyAllFourQueries()
    {
        var query = CatalogQuery.UsQueries[0];
        var repeated = Enumerable.Range(0, 4).Select(_ => LunaResponseParser.Parse(ParserTests.Fixture("AllGames"), query));
        Assert.False(CatalogSnapshot.Merge(repeated).AllQueriesSucceeded);
    }

    [Fact]
    public void FailedOptionalDetailCheckPreventsSuccessfulExportStatus()
    {
        var snapshot = CatalogSnapshot.Merge(CatalogQuery.UsQueries.Select(q =>
            LunaResponseParser.Parse(ParserTests.Fixture(q.Name), q)));
        Assert.True(snapshot.AllRequestedChecksSucceeded);
        snapshot.DetailChecks.Add(new GameDetailObservation { ResponseValid = false });
        Assert.True(snapshot.AllQueriesSucceeded);
        Assert.False(snapshot.AllRequestedChecksSucceeded);
        Assert.False(snapshot.IsComplete);
    }

    private sealed class DetailHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Null(body["searchContext"]);
            Assert.Equal("game_detail", (string?)body.SelectToken("pageContext.pageType"));
            Assert.Equal(Game("Fortnite").Id, (string?)body.SelectToken("pageContext.pageId"));
            Assert.False((bool)body.SelectToken("pageContext.encryptPageId")!);
            Assert.Equal("Release", (string?)body.SelectToken("pageContext.productStage"));
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ParserTests.Fixture("FortniteDetail")) };
        }
    }

    [Fact]
    public async Task DetailRequestReproducesAnonymousNavigationContextWithoutServiceTokens()
    {
        using var client = new HttpClient(new DetailHandler());
        var provider = new LunaCatalogProvider(client, "TestBrowser/154", "154.0.8037.57");
        var result = await provider.FetchDetailAsync(Game("Fortnite"), CancellationToken.None);
        Assert.True(result.ResponseValid);
        Assert.Equal(200, result.HttpStatus);
    }
}
