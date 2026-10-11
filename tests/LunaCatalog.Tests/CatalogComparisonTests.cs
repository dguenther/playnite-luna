using System.Linq;
using LunaCatalog;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class CatalogComparisonTests
{
    [Fact]
    public void AdditionsRemovalsAndMetadataUpdatesUseIdentityRatherThanTitle()
    {
        var previous = RuntimeSamples.Validated();
        var snapshot = RuntimeSamples.Snapshot();
        var removedId = snapshot.Games.Single(g => g.Title.StartsWith("WRC")).Id;
        foreach (var query in snapshot.Queries)
        {
            query.Games.RemoveAll(g => g.Id == removedId);
            var alan = query.Games.FirstOrDefault(g => g.Asin == "B0GFWDM8NP");
            if (alan != null) alan.Title = "Updated Alan";
            if (query.Name == "AllGames" || query.Name == "Standard")
                query.Games.Add(new LunaGame
                {
                    Id = "B000000001", Asin = "B000000001", Title = "Alan Wake 2",
                    Url = "https://luna.amazon.com/game/another-edition/B000000001", HasPlayLink = true,
                    Categories = query.Name == "Standard" ? new() { "Standard" } : new()
                });
        }
        var current = CatalogValidator.ValidateSnapshot(RuntimeSamples.Merge(snapshot.Queries.ToArray()), "US", RuntimeSamples.Time);
        var changes = CatalogComparer.Compare(previous, current, RuntimeSamples.Time);
        Assert.Equal("B000000001", Assert.Single(changes.Added).Id);
        Assert.Equal(removedId, Assert.Single(changes.Removed).Id);
        Assert.Equal("Updated Alan", Assert.Single(changes.Updated).Current.Title);
        Assert.Empty(changes.IdentityMigrations);
    }

    [Fact]
    public void AsinToNativeIdMigrationDoesNotCreateAnAdditionOrRemoval()
    {
        var snapshot = RuntimeSamples.ChangeGame(RuntimeSamples.Snapshot(), "B0GFWDM8NP", g => g.Id = g.Asin!);
        var previous = CatalogValidator.ValidateSnapshot(snapshot, "US", RuntimeSamples.Time);
        var current = RuntimeSamples.Validated();
        var changes = CatalogComparer.Compare(previous, current, RuntimeSamples.Time);
        Assert.Empty(changes.Added);
        Assert.Empty(changes.Removed);
        var migration = Assert.Single(changes.IdentityMigrations);
        Assert.Equal("B0GFWDM8NP", migration.Previous.Id);
        Assert.StartsWith("amzn1.adg.product.", migration.Current.Id);
    }

    [Theory]
    [InlineData("native conflict")]
    [InlineData("downgrade")]
    [InlineData("ASIN conflict")]
    [InlineData("older")]
    [InlineData("region")]
    public void AmbiguousOrIncompatibleComparisonsAreRejected(string change)
    {
        var previous = RuntimeSamples.Validated();
        var snapshot = RuntimeSamples.Snapshot();
        if (change == "native conflict") snapshot = RuntimeSamples.ChangeGame(snapshot, "B0GFWDM8NP",
            g => g.Id = "amzn1.adg.product.00000000-0000-0000-0000-000000000099");
        if (change == "downgrade") snapshot = RuntimeSamples.ChangeGame(snapshot, "B0GFWDM8NP", g => g.Id = g.Asin!);
        if (change == "ASIN conflict") snapshot = RuntimeSamples.ChangeGame(snapshot, "B0GFWDM8NP", g =>
        {
            g.Asin = "B000000001"; g.Url = "https://luna.amazon.com/game/new/B000000001";
        });
        var current = CatalogValidator.ValidateSnapshot(snapshot, "US", RuntimeSamples.Time);
        if (change == "older") current.RetrievedAt = RuntimeSamples.Time.AddMinutes(-1);
        if (change == "region") current.Region = "CA";
        Assert.Throws<CatalogValidationException>(() => CatalogComparer.Compare(previous, current, RuntimeSamples.Time));
    }
}
