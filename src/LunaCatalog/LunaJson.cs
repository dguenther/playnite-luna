using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LunaCatalog
{
    internal static class LunaJson
    {
        internal static JObject ParseObject(string json)
        {
            using (var input = new StringReader(json))
            using (var reader = new JsonTextReader(input) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
            {
                var result = JObject.Load(reader);
                while (reader.Read())
                    if (reader.TokenType != JsonToken.Comment)
                        throw new JsonReaderException("Unexpected content after JSON object.");
                return result;
            }
        }
    }
}
