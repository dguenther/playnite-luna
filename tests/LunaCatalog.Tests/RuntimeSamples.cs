using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;

namespace LunaCatalog.Tests;

internal static class RuntimeSamples
{
    internal static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
    internal static CatalogSnapshot Snapshot() => Merge(CatalogQuery.UsQueries.Select(q =>
        LunaResponseParser.Parse(ParserTests.Fixture(q.Name), q)).ToArray());
    internal static CatalogSnapshot Merge(QueryResult[] queries)
    {
        var snapshot = CatalogSnapshot.Merge(queries);
        snapshot.RetrievedAt = Time;
        return snapshot;
    }
    internal static CatalogSnapshot ChangeGame(CatalogSnapshot snapshot, string asin, Action<LunaGame> change)
    {
        foreach (var game in snapshot.Queries.SelectMany(q => q.Games).Where(g => g.Asin == asin)) change(game);
        var result = CatalogSnapshot.Merge(snapshot.Queries);
        result.RetrievedAt = snapshot.RetrievedAt;
        return result;
    }
    internal static ValidatedCatalog Validated() => CatalogValidator.ValidateSnapshot(Snapshot(), "US", Time);
}

internal sealed class SampleProvider : ILunaCatalogProvider
{
    internal int Calls { get; private set; }
    internal Func<CancellationToken, Task<CatalogSnapshot>> Fetch { get; set; } = _ => Task.FromResult(RuntimeSamples.Snapshot());
    public Task<CatalogSnapshot> FetchAsync(string region, CancellationToken cancellationToken)
    {
        if (region != "US") throw new InvalidOperationException("Unexpected region.");
        Calls++;
        return Fetch(cancellationToken);
    }
}
