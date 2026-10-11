using System;
using System.IO;
using System.Threading.Tasks;
using LunaCatalogProbe;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class ProbeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-probe-test-" + Guid.NewGuid().ToString("N"));
    private string Fixtures => Path.Combine(directory, "Fixtures");
    private string Output => Path.Combine(directory, "catalog.json");

    public ProbeTests()
    {
        Directory.CreateDirectory(Fixtures);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.json"))
            File.Copy(file, Path.Combine(Fixtures, Path.GetFileName(file)));
    }

    [Fact]
    public async Task SuccessfulFixtureReplayExportsFiveGamesAndExplicitUnverifiedStatus()
    {
        Assert.Equal(2, await Program.Main(new[] { "--fixtures", Fixtures, "--output", Output }));
        var snapshot = JObject.Parse(File.ReadAllText(Output));
        Assert.Equal(5, ((JArray)snapshot["games"]!).Count);
        Assert.False((bool)snapshot["isComplete"]!);
        Assert.True((bool)snapshot["allRequestedChecksSucceeded"]!);
        Assert.Equal("Unverified", (string?)snapshot["coverageStatus"]);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task FailedFixtureRefreshPreservesPriorExportAndWritesDiagnosticsSeparately()
    {
        File.WriteAllText(Output, "previous validated export");
        File.WriteAllText(Path.Combine(Fixtures, "AllGames.json"), "{\"pageContext\":{\"pageType\":\"sign_in\"}}");
        Assert.Equal(1, await Program.Main(new[] { "--fixtures", Fixtures, "--output", Output }));
        Assert.Equal("previous validated export", File.ReadAllText(Output));
        Assert.True(File.Exists(Output + ".diagnostics.json"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task DetailChecksRequireLiveModeAndCannotModifyExistingFilesOnInvalidInvocation()
    {
        File.WriteAllText(Output, "existing export");
        Assert.Equal(64, await Program.Main(new[] { "--fixtures", Fixtures, "--output", Output, "--verify-details" }));
        Assert.Equal("existing export", File.ReadAllText(Output));
    }

    [Fact]
    public async Task MissingFixtureIsAnActionableFailureAndPreservesPriorExport()
    {
        File.WriteAllText(Output, "existing export");
        File.Delete(Path.Combine(Fixtures, "Premium.json"));
        Assert.Equal(1, await Program.Main(new[] { "--fixtures", Fixtures, "--output", Output }));
        Assert.Equal("existing export", File.ReadAllText(Output));
    }

    [Fact]
    public async Task MismatchedCatalogSetsPreservePriorExportAndReportIncompleteCoverage()
    {
        File.WriteAllText(Output, "existing export");
        var standard = JObject.Parse(File.ReadAllText(Path.Combine(Fixtures, "Standard.json")));
        ((JArray)standard.SelectToken("pageMemberGroups.mainContent.widgets[0].widgets")!)[1].Remove();
        File.WriteAllText(Path.Combine(Fixtures, "Standard.json"), standard.ToString());
        Assert.Equal(1, await Program.Main(new[] { "--fixtures", Fixtures, "--output", Output }));
        Assert.Equal("existing export", File.ReadAllText(Output));
        var diagnostics = JObject.Parse(File.ReadAllText(Output + ".diagnostics.json"));
        Assert.Equal("Incomplete", (string?)diagnostics["coverageStatus"]);
        Assert.False((bool)diagnostics["catalogSetsAgree"]!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AmbiguousRegionOrNonBrowseTilesPreservePriorExport(bool missingTerritory)
    {
        File.WriteAllText(Output, "existing export");
        var fixturePath = Path.Combine(Fixtures, "Standard.json");
        var response = JObject.Parse(File.ReadAllText(fixturePath));
        if (missingTerritory)
        {
            var tile = (JObject)response.SelectToken("pageMemberGroups.mainContent.widgets[0].widgets[0]")!;
            var data = JObject.Parse((string)tile["presentationData"]!);
            data["hoverDetails"]!["productUrl"] = "https://play.amazon.com/play?asin=B0GFWDM8NP";
            tile["presentationData"] = data;
        }
        else
        {
            var main = response["pageMemberGroups"]!["mainContent"]!.DeepClone();
            response["pageMemberGroups"] = new JObject
            {
                ["sidebar"] = new JObject { ["section"] = new JObject { ["mainContent"] = main } }
            };
        }
        File.WriteAllText(fixturePath, response.ToString());
        Assert.Equal(1, await Program.Main(new[] { "--fixtures", Fixtures, "--output", Output }));
        Assert.Equal("existing export", File.ReadAllText(Output));
        var diagnostics = JObject.Parse(File.ReadAllText(Output + ".diagnostics.json"));
        Assert.Equal("Incomplete", (string?)diagnostics["coverageStatus"]);
        Assert.False((bool)diagnostics["allRequestedChecksSucceeded"]!);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
