using System.Linq;
using LunaCatalog;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class RuntimeValidationTests
{
    [Fact]
    public void RepresentativeBrowseSamplesProduceValidatedRuntimeCatalogWithoutChangingResearchSnapshot()
    {
        var snapshot = RuntimeSamples.Snapshot();
        var catalog = CatalogValidator.ValidateSnapshot(snapshot, "US", RuntimeSamples.Time);
        Assert.True(catalog.IsComplete);
        Assert.Equal("Validated", catalog.CoverageStatus);
        Assert.Equal(5, catalog.Games.Count); // No fixed historical catalog count.
        Assert.Equal(new[] { "Premium", "Standard" }, catalog.Games.Single(g => g.Title == "Alan Wake 2").Categories);
        Assert.Equal(4, catalog.Queries.Count);
        Assert.False(snapshot.IsComplete);
        Assert.Equal("Unverified", snapshot.CoverageStatus);
        Assert.DoesNotContain(catalog.Diagnostics, d => d.Contains("research snapshot"));
        catalog.Games[0].Title = "consumer edit";
        Assert.DoesNotContain(snapshot.Games, g => g.Title == "consumer edit");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("partial")]
    [InlineData("duplicate query")]
    [InlineData("failed query")]
    [InlineData("query transport failure")]
    [InlineData("page type")]
    [InlineData("continuation")]
    [InlineData("pages")]
    [InlineData("tile count")]
    [InlineData("http failure")]
    [InlineData("region")]
    [InlineData("marketplace")]
    [InlineData("unsafe URL")]
    [InlineData("ASIN mismatch")]
    [InlineData("set mismatch")]
    [InlineData("normalized mismatch")]
    [InlineData("detail failure")]
    [InlineData("detail transport failure")]
    [InlineData("wrong membership")]
    [InlineData("null games")]
    [InlineData("future retrieval")]
    public void InvalidSnapshotsCannotBeAcceptedBasedOnSuccessFlags(string corruption)
    {
        var snapshot = RuntimeSamples.Snapshot();
        var query = snapshot.Queries[0];
        switch (corruption)
        {
            case "empty": snapshot.Games.Clear(); break;
            case "partial": snapshot.Queries.RemoveAt(3); break;
            case "duplicate query": snapshot.Queries[3] = snapshot.Queries[0]; break;
            case "failed query": query.ResponseValid = false; break;
            case "query transport failure": query.FailureKind = CatalogFetchFailureKind.Network; break;
            case "page type": query.ObservedPageType = "sign_in"; break;
            case "continuation": query.PaginationStatus = "UnresolvedContinuation"; break;
            case "pages": query.PagesRetrieved = 2; break;
            case "tile count": query.TilesObserved = 1; break;
            case "http failure": query.HttpStatus = 403; break;
            case "region": snapshot.Region = "CA"; break;
            case "marketplace": snapshot.MarketplaceId = "other"; break;
            case "unsafe URL": query.Games[0].Url = "https://example.com/game/test"; break;
            case "ASIN mismatch": query.Games[0].Asin = "B000000001"; break;
            case "set mismatch": query.Games.RemoveAt(0); break;
            case "normalized mismatch": snapshot.Games[0].Title = "changed outside observations"; break;
            case "detail failure": snapshot.DetailChecks.Add(new GameDetailObservation { ResponseValid = false }); break;
            case "detail transport failure": snapshot.DetailChecks.Add(new GameDetailObservation
                { ResponseValid = true, FailureKind = CatalogFetchFailureKind.Network }); break;
            case "wrong membership": query.Games[0].Categories.Add("Premium"); break;
            case "null games": snapshot.Games = null!; break;
            case "future retrieval": snapshot.RetrievedAt = RuntimeSamples.Time.AddHours(1); break;
        }
        Assert.Throws<CatalogValidationException>(() => CatalogValidator.ValidateSnapshot(snapshot, "US", RuntimeSamples.Time));
    }

    [Fact]
    public void AsinFallbackCanBeValidatedWhenAllQueriesAgreeOnIdentity()
    {
        var snapshot = RuntimeSamples.Snapshot();
        snapshot = RuntimeSamples.ChangeGame(snapshot, "B0GFWDM8NP", game => game.Id = game.Asin!);
        var catalog = CatalogValidator.ValidateSnapshot(snapshot, "US", RuntimeSamples.Time);
        Assert.Contains(catalog.Games, g => g.Id == "B0GFWDM8NP");
    }
}
