using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LunaCatalogProbe;

internal static class Program
{
    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    internal static async Task<int> Main(string[] args)
    {
        string? input = null;
        string? output = null;
        var live = false;
        var verifyDetails = false;
        var userAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.8037.57 Safari/537.36";
        var version = "154.0.8037.57";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--live") live = true;
            else if (args[i] == "--verify-details") verifyDetails = true;
            else if (args[i] == "--fixtures" && i + 1 < args.Length) input = args[++i];
            else if (args[i] == "--output" && i + 1 < args.Length) output = args[++i];
            else if (args[i] == "--user-agent" && i + 1 < args.Length) userAgent = args[++i];
            else if (args[i] == "--browser-version" && i + 1 < args.Length) version = args[++i];
            else
            {
                Console.Error.WriteLine("Usage: LunaCatalogProbe (--live | --fixtures DIRECTORY) --output FILE [--verify-details] [--user-agent STRING] [--browser-version STRING]");
                return args[i] == "--help" ? 0 : 64;
            }
        }
        if (live == (input != null) || output == null || (verifyDetails && !live))
        {
            Console.Error.WriteLine("Choose one input mode, provide --output, and use --verify-details only with --live. Use --help for usage.");
            return 64;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            CatalogSnapshot snapshot;
            if (live)
            {
                // Disable cookies: every query is anonymous. Honor the system proxy and TLS trust.
                using var handler = new HttpClientHandler { UseCookies = false };
                using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                var provider = new LunaCatalogProvider(client, userAgent, version);
                snapshot = await provider.FetchAsync("US", cancellation.Token);
                if (verifyDetails && snapshot.AllQueriesSucceeded)
                {
                    // Up to five distinct examples covering categories and observed schema cases.
                    var samples = new[]
                    {
                        snapshot.Games.FirstOrDefault(g => g.Categories.Contains("Standard")),
                        snapshot.Games.FirstOrDefault(g => g.Categories.Contains("Premium") && !g.Categories.Contains("Standard")),
                        snapshot.Games.FirstOrDefault(g => g.Categories.Contains("GameNight")),
                        snapshot.Games.FirstOrDefault(g => g.Id.StartsWith("amzn1.luna.product.", StringComparison.Ordinal)),
                        snapshot.Games.FirstOrDefault(g => new Uri(g.Url).Segments.Length == 3)
                    }.Where(g => g != null).Select(g => g!).GroupBy(g => g.Id).Select(group => group.First()).ToList();
                    foreach (var game in samples)
                    {
                        await Task.Delay(500, cancellation.Token);
                        var check = await provider.FetchDetailAsync(game, cancellation.Token);
                        snapshot.DetailChecks.Add(check);
                        Console.WriteLine($"Detail {game.Title}: identity={check.IdentityMatches}, URL={check.UrlMatches}, button={check.PrimaryButtonText ?? "unknown"}");
                        foreach (var diagnostic in check.Diagnostics) Console.WriteLine("  " + diagnostic);
                    }
                }
            }
            else
            {
                var results = CatalogQuery.UsQueries.Select(q => LunaResponseParser.Parse(
                    File.ReadAllText(Path.Combine(input!, q.Name + ".json")), q)).ToList();
                snapshot = CatalogSnapshot.Merge(results);
                snapshot.Diagnostics.Add("Fixture replay uses reduced recorded samples; counts are not live catalog counts.");
            }
            foreach (var query in snapshot.Queries)
            {
                Console.WriteLine($"{query.Name}: {query.Games.Count} unique games, {query.TilesObserved} tiles, HTTP {query.HttpStatus?.ToString() ?? "n/a"}, {query.PaginationStatus}");
                foreach (var diagnostic in query.Diagnostics) Console.WriteLine("  " + diagnostic);
            }
            Console.WriteLine($"Unique total: {snapshot.Games.Count}; completeness: {snapshot.CoverageStatus}");
            foreach (var diagnostic in snapshot.Diagnostics) Console.WriteLine(diagnostic);
            // Failed runs write a separate diagnostic report and preserve an existing export.
            var destination = snapshot.AllRequestedChecksSucceeded ? output : output + ".diagnostics.json";
            await WriteAtomicAsync(destination, JsonConvert.SerializeObject(snapshot, JsonSettings), cancellation.Token);
            Console.WriteLine("Wrote " + destination);
            return snapshot.AllRequestedChecksSucceeded ? 2 : 1;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Probe cancelled."); return 130; }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            Console.Error.WriteLine("Probe could not read or write its files (" + ex.GetType().Name + ").");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancelHandler; }
    }

    private static async Task WriteAtomicAsync(string output, string text, CancellationToken token)
    {
        var path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
