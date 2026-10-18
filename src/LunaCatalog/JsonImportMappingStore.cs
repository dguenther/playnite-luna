using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LunaCatalog
{
    public sealed class JsonImportMappingStore : IImportMappingStore
    {
        public const int MaximumBytes = 4 * 1024 * 1024;
        private readonly string path;
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        { ContractResolver = new CamelCasePropertyNamesContractResolver(), TypeNameHandling = TypeNameHandling.None, MaxDepth = 64, Formatting = Formatting.Indented };
        public JsonImportMappingStore(string directory) { path = Path.Combine(Path.GetFullPath(directory), "imports-US.json"); }

        public ImportMappingReadResult Load()
        {
            var invalid = false;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try { return new ImportMappingReadResult(Read(attempt == 0 ? path : path + ".bak"), attempt == 1 ? "Recovered import mappings from backup." : ""); }
                catch (UnsupportedSchemaException) { return new ImportMappingReadResult(null, "Import mappings use a newer format. Update the extension before importing."); }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException) { }
                catch (Exception ex) when (IsReadFailure(ex)) { invalid = true; }
            }
            return invalid ? new ImportMappingReadResult(null, "Import mappings are unreadable. Restore imports-US.json or its backup before importing.") :
                new ImportMappingReadResult(new ImportMappingDocument());
        }

        public void Save(ImportMappingDocument document)
        {
            Validate(document);
            var bytes = new UTF8Encoding(false, true).GetBytes(JsonConvert.SerializeObject(document, Settings) + "\n");
            if (bytes.Length > MaximumBytes) throw new IOException("Import mappings exceed the size limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(bytes, 0, bytes.Length); output.Flush(flushToDisk: true); }
                var validPrimary = false;
                try { Read(path); validPrimary = true; }
                catch (UnsupportedSchemaException) { throw new IOException("Refusing to replace newer import mappings."); }
                catch (Exception ex) when (IsReadFailure(ex)) { }
                if (File.Exists(path)) File.Replace(temporary, path, validPrimary ? path + ".bak" : null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static bool IsReadFailure(Exception ex) => ex is IOException || ex is UnauthorizedAccessException ||
            ex is JsonException || ex is FormatException || ex is OverflowException || ex is DecoderFallbackException;
        private sealed class UnsupportedSchemaException : Exception { }
        private static ImportMappingDocument Read(string filename)
        {
            using (var input = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var bytes = new MemoryStream())
            {
                if (input.Length > MaximumBytes) throw new IOException("Import mappings exceed the size limit.");
                var buffer = new byte[8192]; int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                { if (bytes.Length + read > MaximumBytes) throw new IOException("Import mappings exceed the size limit."); bytes.Write(buffer, 0, read); }
                var root = LunaJson.ParseObject(new UTF8Encoding(false, true).GetString(bytes.ToArray()), rejectDuplicateProperties: true);
                if (root["schemaVersion"]?.Type != JTokenType.Integer) throw new FormatException("Missing import schema.");
                if (root["schemaVersion"]!.ToString(Formatting.None) != "1") throw new UnsupportedSchemaException();
                if (!(root["mappings"] is JArray)) throw new FormatException("Missing import mappings.");
                var document = root.ToObject<ImportMappingDocument>(JsonSerializer.Create(Settings))!;
                Validate(document); return document;
            }
        }
        private static void Validate(ImportMappingDocument document)
        {
            if (document == null || document.SchemaVersion != 1 || document.Mappings == null || document.Mappings.Count > 10000)
                throw new FormatException("Invalid import mapping structure.");
            var keys = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<Guid>();
            foreach (var entry in document.Mappings)
                if (entry == null || !LunaImportIdentity.IsValid(entry.Region, entry.LunaId, entry.Asin) || entry.PlayniteId == Guid.Empty ||
                    !keys.Add(entry.Region + "|" + entry.LunaId) || !ids.Add(entry.PlayniteId)) throw new FormatException("Conflicting import mappings.");
        }
    }
}
