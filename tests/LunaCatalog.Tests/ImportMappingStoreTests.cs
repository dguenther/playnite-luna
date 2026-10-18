using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LunaCatalog;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class ImportMappingStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-mapping-test-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(directory, "imports-US.json");
    private JsonImportMappingStore Store() => new(directory);
    private static ImportMappingDocument Document()
    {
        var game = RuntimeSamples.Validated().Games[0];
        return new() { Mappings = new() { new() { Region = "US", LunaId = game.Id, Asin = game.Asin!, PlayniteId = Guid.NewGuid() } } };
    }

    [Fact]
    public void MissingStateIsEmptyWithoutCreatingFilesAndValidStateRoundTrips()
    {
        Assert.Empty(Store().Load().Document!.Mappings); Assert.False(Directory.Exists(directory));
        var document = Document(); Store().Save(document);
        Assert.Equal(document.Mappings[0].PlayniteId, Store().Load().Document!.Mappings[0].PlayniteId);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void CorruptPrimaryRecoversBackupAndCannotReplaceItWithCorruptBytes()
    {
        var previous = Document(); Store().Save(previous); var original = File.ReadAllBytes(PathName);
        Store().Save(Document()); File.WriteAllText(PathName, "sensitive corruption");
        Assert.Contains("backup", Store().Load().Diagnostic);
        Assert.Equal(previous.Mappings[0].PlayniteId, Store().Load().Document!.Mappings[0].PlayniteId);
        Store().Save(Document()); Assert.Equal(original, File.ReadAllBytes(PathName + ".bak"));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("missing schema")]
    [InlineData("missing mappings")]
    [InlineData("duplicate property")]
    [InlineData("duplicate nested")]
    [InlineData("trailing")]
    [InlineData("region")]
    [InlineData("identity")]
    [InlineData("empty guid")]
    [InlineData("duplicate mapping")]
    [InlineData("duplicate guid")]
    [InlineData("depth")]
    [InlineData("schema overflow")]
    public void InvalidStateIsBlockedAndNeverTreatedAsEmpty(string corruption)
    {
        Store().Save(Document()); var root = JObject.Parse(File.ReadAllText(PathName));
        switch (corruption)
        {
            case "missing schema": root.Property("schemaVersion")!.Remove(); break;
            case "missing mappings": root.Property("mappings")!.Remove(); break;
            case "region": root["mappings"]![0]!["region"] = "CA"; break;
            case "identity": root["mappings"]![0]!["lunaId"] = "same title"; break;
            case "empty guid": root["mappings"]![0]!["playniteId"] = Guid.Empty; break;
            case "duplicate mapping": ((JArray)root["mappings"]!).Add(root["mappings"]![0]!.DeepClone()); break;
            case "duplicate guid":
                var clone = root["mappings"]![0]!.DeepClone(); clone["lunaId"] = "B000000001"; clone["asin"] = "B000000001"; ((JArray)root["mappings"]!).Add(clone); break;
        }
        var text = root.ToString();
        if (corruption == "malformed") text = "sensitive invalid JSON";
        if (corruption == "duplicate property") text = text.Insert(1, "\"schemaVersion\":1,");
        if (corruption == "duplicate nested") text = text.Replace("\"region\":", "\"region\":\"US\",\"region\":");
        if (corruption == "trailing") text += " {}";
        if (corruption == "depth") text = text.Insert(1, "\"unknown\":" + new string('[', 65) + "0" + new string(']', 65) + ",");
        if (corruption == "schema overflow") text = "{\"schemaVersion\":999999999999999999999999999999999,\"mappings\":[]}";
        File.WriteAllText(PathName, text);
        var result = Store().Load(); Assert.Null(result.Document); Assert.DoesNotContain("sensitive", result.Diagnostic); Assert.Equal(text, File.ReadAllText(PathName));
    }

    [Fact]
    public void FutureSchemaCannotBeOverwrittenOrDowngradedToTheBackup()
    {
        Store().Save(Document()); Store().Save(Document());
        const string future = "{\"schemaVersion\":2,\"mappings\":[]}"; File.WriteAllText(PathName, future);
        Assert.Null(Store().Load().Document); Assert.Contains("newer", Store().Load().Diagnostic);
        Assert.Throws<IOException>(() => Store().Save(Document())); Assert.Equal(future, File.ReadAllText(PathName)); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void OversizedStateIsRejectedBeforeParsing()
    {
        Directory.CreateDirectory(directory); using (var file = File.Create(PathName)) file.SetLength(JsonImportMappingStore.MaximumBytes + 1);
        Assert.Null(Store().Load().Document);
    }

    [Fact]
    public void FailedReplacementRetainsPrimaryAndCleansTemporaryFiles()
    {
        Store().Save(Document()); var original = File.ReadAllBytes(PathName); Directory.CreateDirectory(PathName + ".bak");
        var failure = Assert.ThrowsAny<Exception>(() => Store().Save(Document())); Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(original, File.ReadAllBytes(PathName)); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
