using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LunaCatalog
{
    internal static class LunaJson
    {
        internal static JObject ParseObject(string json, bool rejectDuplicateProperties = false)
        {
            using (var input = new StringReader(json))
            using (var reader = new CatalogJsonReader(input, rejectDuplicateProperties) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
            {
                var result = JObject.Load(reader);
                while (reader.Read())
                    if (reader.TokenType != JsonToken.Comment)
                        throw new JsonReaderException("Unexpected content after JSON object.");
                return result;
            }
        }

        // JSON 10 has no DuplicatePropertyNameHandling. Check decoded property names
        // while reading, including nested objects, before JObject can replace them.
        private sealed class CatalogJsonReader : JsonTextReader
        {
            private readonly bool rejectDuplicates;
            private readonly Stack<HashSet<string>> objects = new Stack<HashSet<string>>();
            internal CatalogJsonReader(TextReader input, bool rejectDuplicates) : base(input) { this.rejectDuplicates = rejectDuplicates; }
            public override bool Read()
            {
                var read = base.Read();
                if (!read || !rejectDuplicates) return read;
                switch (TokenType)
                {
                    case JsonToken.StartObject: objects.Push(new HashSet<string>(StringComparer.Ordinal)); break;
                    case JsonToken.EndObject: objects.Pop(); break;
                    case JsonToken.PropertyName:
                        if (!objects.Peek().Add((string)Value!)) throw new JsonReaderException("Duplicate JSON property.");
                        break;
                }
                return read;
            }
        }
    }
}
