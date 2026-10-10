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

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
