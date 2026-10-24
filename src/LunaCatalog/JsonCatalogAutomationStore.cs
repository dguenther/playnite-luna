using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LunaCatalog
{
    public sealed class CatalogAutomationState
    {
        public int SchemaVersion { get; set; } = 1;
        public string Region { get; set; } = "US";
        public bool HasBaseline { get; set; }
        public DateTimeOffset? LastObservation { get; set; }
        public List<string> KnownAsins { get; set; } = new List<string>();
        public List<string> PendingNewAsins { get; set; } = new List<string>();
        // Keep schema-1 observation counts compatible; one recorded absence now qualifies for expiry.
        public Dictionary<string, int> MissingAsins { get; set; } = new Dictionary<string, int>();
        public List<Guid> TaggedGameIds { get; set; } = new List<Guid>();
        public List<Guid> OpenedGameIds { get; set; } = new List<Guid>();
    }

    public sealed class JsonCatalogAutomationStore
    {
        private readonly object sync = new object();
        private readonly string path;
        public const int MaximumBytes = 4 * 1024 * 1024;
        private static readonly JsonSerializerSettings settings = new JsonSerializerSettings
        { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false } },
            TypeNameHandling = TypeNameHandling.None, MaxDepth = 64, Formatting = Formatting.Indented };
        public JsonCatalogAutomationStore(string directory) { path = Path.Combine(Path.GetFullPath(directory), "catalog-automation-US.json"); }
        public CatalogAutomationState Read() { lock (sync) return ReadCore(); }
        public void Update(Action<CatalogAutomationState> update)
        {
            lock (sync)
            {
                var state = ReadCore(); update(state); Validate(state);
                var bytes = new UTF8Encoding(false, true).GetBytes(JsonConvert.SerializeObject(state, settings) + "\n");
                if (bytes.Length > MaximumBytes) throw new IOException("Catalog automation state exceeds its size limit.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { output.Write(bytes, 0, bytes.Length); output.Flush(flushToDisk: true); }
                    if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        private CatalogAutomationState ReadCore()
        {
            try
            {
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var bytes = new MemoryStream())
                {
                    if (input.Length > MaximumBytes) throw new IOException("Catalog automation state exceeds its size limit.");
                    var buffer = new byte[8192]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    { if (bytes.Length + count > MaximumBytes) throw new IOException("Catalog automation state exceeds its size limit."); bytes.Write(buffer, 0, count); }
                    var root = LunaJson.ParseObject(new UTF8Encoding(false, true).GetString(bytes.ToArray()), true);
                    if (root["schemaVersion"]?.Type != JTokenType.Integer || root["schemaVersion"]!.ToString(Formatting.None) != "1" ||
                        root["region"]?.Value<string>() != "US" || !(root["knownAsins"] is JArray) || !(root["missingAsins"] is JObject) ||
                        !(root["pendingNewAsins"] is JArray) || !(root["taggedGameIds"] is JArray) || !(root["openedGameIds"] is JArray))
                        throw new FormatException("Catalog automation state is invalid or uses a newer format.");
                    var state = root.ToObject<CatalogAutomationState>(JsonSerializer.Create(settings))!; Validate(state); return state;
                }
            }
            catch (FileNotFoundException) { return new CatalogAutomationState(); }
            catch (DirectoryNotFoundException) { return new CatalogAutomationState(); }
        }
        private static void Validate(CatalogAutomationState state)
        {
            bool Asin(string value) => Regex.IsMatch(value ?? "", "^[A-Z0-9]{10}$");
            bool Asins(List<string> values) => values != null && values.Count <= 10000 && values.All(Asin) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;
            bool Ids(List<Guid> values) => values != null && values.Count <= 10000 && !values.Contains(Guid.Empty) && values.Distinct().Count() == values.Count;
            if (state.SchemaVersion != 1 || state.Region != "US" || !Asins(state.KnownAsins) || !Asins(state.PendingNewAsins) ||
                !Ids(state.TaggedGameIds) || !Ids(state.OpenedGameIds) || state.MissingAsins == null || state.MissingAsins.Count > 10000 ||
                state.MissingAsins.Any(kv => !Asin(kv.Key) || kv.Value < 1 || kv.Value > 2) ||
                state.HasBaseline != state.LastObservation.HasValue ||
                (!state.HasBaseline && (state.KnownAsins.Count > 0 || state.PendingNewAsins.Count > 0 || state.MissingAsins.Count > 0)) ||
                state.PendingNewAsins.Any(a => !state.KnownAsins.Contains(a)) || state.MissingAsins.Keys.Any(a => state.KnownAsins.Contains(a)))
                throw new FormatException("Invalid catalog automation state.");
        }
    }
}
