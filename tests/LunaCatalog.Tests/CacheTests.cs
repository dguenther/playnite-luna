using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class CacheTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-cache-test-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(directory, "catalog-US.json");
    private JsonCatalogCache Cache() => new(directory, () => RuntimeSamples.Time.AddDays(10));

    [Fact]
    public async Task CacheSurvivesRestartAndStoresOnlyNormalizedGamesAndQuerySummaries()
    {
        var catalog = RuntimeSamples.Validated();
        await Cache().SaveAsync(catalog, CancellationToken.None);
        var reloaded = await Cache().LoadAsync("US", CancellationToken.None);
        Assert.NotNull(reloaded.Catalog);
        Assert.False(reloaded.RecoveredFromBackup);
        Assert.Empty(reloaded.Diagnostics);
        Assert.Equal(catalog.RetrievedAt, reloaded.Catalog.RetrievedAt);
        Assert.Equal(catalog.Games.Select(g => g.Id), reloaded.Catalog.Games.Select(g => g.Id));
        Assert.Equal(catalog.Games.Single(g => g.Title == "Alan Wake 2").Categories,
            reloaded.Catalog.Games.Single(g => g.Title == "Alan Wake 2").Categories);
        var json = JObject.Parse(await File.ReadAllTextAsync(PathName));
        Assert.Equal(4, ((JArray)json["queries"]!).Count);
        Assert.All(json["queries"]!, q => { Assert.Null(q["games"]); Assert.NotEmpty((JArray)q["gameIds"]!); });
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task AtomicReplacementKeepsPreviousSnapshotAndCorruptPrimaryRecoversWithoutLosingBackup()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var original = await File.ReadAllBytesAsync(PathName);
        var snapshot = RuntimeSamples.ChangeGame(RuntimeSamples.Snapshot(), "B0GFWDM8NP", g => g.Title = "new title");
        var current = CatalogValidator.ValidateSnapshot(snapshot, "US", RuntimeSamples.Time);
        await Cache().SaveAsync(current, CancellationToken.None);
        Assert.Equal(original, await File.ReadAllBytesAsync(PathName + ".bak"));
        await File.WriteAllTextAsync(PathName, "corrupt primary");
        var recovered = await Cache().LoadAsync("US", CancellationToken.None);
        Assert.True(recovered.RecoveredFromBackup);
        Assert.Contains(recovered.Catalog!.Games, g => g.Title == "Alan Wake 2");
        Assert.DoesNotContain(recovered.Catalog.Games, g => g.Title == "new title");
        await Cache().SaveAsync(current, CancellationToken.None);
        Assert.Equal(original, await File.ReadAllBytesAsync(PathName + ".bak"));
        Assert.Contains((await Cache().LoadAsync("US", CancellationToken.None)).Catalog!.Games, g => g.Title == "new title");
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Theory]
    [InlineData("broken JSON")]
    [InlineData("schema")]
    [InlineData("missing contract")]
    [InlineData("region")]
    [InlineData("marketplace")]
    [InlineData("empty games")]
    [InlineData("null game")]
    [InlineData("null categories")]
    [InlineData("unsafe URL")]
    [InlineData("duplicate identity")]
    [InlineData("missing query")]
    [InlineData("query identity mismatch")]
    [InlineData("tile count")]
    [InlineData("continuation")]
    [InlineData("future time")]
    [InlineData("trailing JSON")]
    [InlineData("duplicate property")]
    [InlineData("nested duplicate property")]
    [InlineData("escaped duplicate property")]
    [InlineData("excessive depth")]
    public async Task UntrustedCacheContentIsValidatedAndNeverReturnedAsAnEmptyCatalog(string corruption)
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var root = JObject.Parse(await File.ReadAllTextAsync(PathName));
        switch (corruption)
        {
            case "schema": root["schemaVersion"] = 999; break;
            case "missing contract": root.Property("validationContract")!.Remove(); break;
            case "region": root["region"] = "CA"; break;
            case "marketplace": root["marketplaceId"] = "other"; break;
            case "empty games": root["games"] = new JArray(); break;
            case "null game": root["games"]![0] = JValue.CreateNull(); break;
            case "null categories": root["games"]![0]!["categories"] = JValue.CreateNull(); break;
            case "unsafe URL": root["games"]![0]!["url"] = "https://example.com/game/test"; break;
            case "duplicate identity": ((JArray)root["games"]!).Add(root["games"]![0]!.DeepClone()); break;
            case "missing query": root["queries"]![0]!.Remove(); break;
            case "query identity mismatch": root["queries"]![0]!["gameIds"]![0] = "B000000001"; break;
            case "tile count": root["queries"]![0]!["tilesObserved"] = 1; break;
            case "continuation": root["queries"]![0]!["paginationStatus"] = "UnresolvedContinuation"; break;
            case "future time": root["validatedAt"] = RuntimeSamples.Time.AddDays(11); break;
        }
        var text = root.ToString();
        if (corruption == "broken JSON") text = "sensitive malformed body";
        if (corruption == "trailing JSON") text += " {}";
        if (corruption == "duplicate property") text = text.Insert(1, "\"schemaVersion\":1,");
        if (corruption == "nested duplicate property") text = text.Replace("\"title\":", "\"title\":\"ignored duplicate\",\"title\":");
        if (corruption == "escaped duplicate property") text = text.Insert(1, "\"schema\\u0056ersion\":1,");
        if (corruption == "excessive depth") text = text.Insert(1, "\"unknown\":" + new string('[', 65) + "0" + new string(']', 65) + ",");
        await File.WriteAllTextAsync(PathName, text);
        var result = await Cache().LoadAsync("US", CancellationToken.None);
        Assert.Null(result.Catalog);
        Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("sensitive"));
        Assert.Equal(text, await File.ReadAllTextAsync(PathName));
    }

    [Fact]
    public async Task MissingCacheIsDistinctFromInvalidCacheAndNoDirectoryIsCreatedOnLoad()
    {
        var result = await Cache().LoadAsync("US", CancellationToken.None);
        Assert.Null(result.Catalog);
        Assert.Empty(result.Diagnostics);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task OversizedCacheIsRejectedBeforeDeserialization()
    {
        Directory.CreateDirectory(directory);
        using (var file = File.Create(PathName)) file.SetLength(JsonCatalogCache.MaximumCacheBytes + 1);
        Assert.Null((await Cache().LoadAsync("US", CancellationToken.None)).Catalog);
    }

    [Fact]
    public async Task FailedReplacementPreservesPrimaryBytesAndCleansTemporaryFile()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(PathName);
        Directory.CreateDirectory(PathName + ".bak"); // A filesystem failure at the actual replacement step.
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(before, await File.ReadAllBytesAsync(PathName));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task RejectedAndCancelledWritesCannotTouchTheLastGoodCache()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(PathName);
        var invalid = RuntimeSamples.Validated(); invalid.Games.Clear();
        await Assert.ThrowsAsync<CatalogValidationException>(() => Cache().SaveAsync(invalid, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cache().SaveAsync(RuntimeSamples.Validated(), cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(PathName));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
