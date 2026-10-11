using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LunaDependencySmoke
{
    // No references to our plugin, catalog, SDK, or JSON packages: resolution must
    // succeed using only the installed archive and the official host assemblies.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var dataDirectory = Path.Combine(Path.GetTempPath(), "luna-dependency-smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (args.Length != 3) throw new ArgumentException("Expected host directory, extracted package directory, and fixtures directory.");
                var host = Path.GetFullPath(args[0]); var package = Path.GetFullPath(args[1]); var fixtures = Path.GetFullPath(args[2]);
                var hostJson = Assembly.LoadFrom(Path.Combine(host, "Newtonsoft.Json.dll"));
                var hostSdk = Assembly.LoadFrom(Path.Combine(host, "Playnite.SDK.dll"));
                var plugin = Assembly.LoadFrom(Path.Combine(package, "LunaPlaynite.dll"));
                var pluginType = plugin.GetTypes().Single(type => type.Name == "LunaPlugin");
                if (pluginType.BaseType!.Assembly != hostSdk) throw new InvalidOperationException("Plugin did not bind to the host SDK.");
                var catalog = Assembly.LoadFrom(Path.Combine(package, "LunaCatalog.dll"));
                var queryType = catalog.GetType("LunaCatalog.CatalogQuery", true)!;
                var queries = ((IEnumerable)queryType.GetProperty("UsQueries")!.GetValue(null)).Cast<object>().ToArray();
                var results = Array.CreateInstance(catalog.GetType("LunaCatalog.QueryResult", true)!, queries.Length);
                var parse = catalog.GetType("LunaCatalog.LunaResponseParser", true)!.GetMethod("Parse")!;
                for (var i = 0; i < queries.Length; i++)
                {
                    var name = (string)queryType.GetProperty("Name")!.GetValue(queries[i]);
                    results.SetValue(parse.Invoke(null, new[] { File.ReadAllText(Path.Combine(fixtures, name + ".json")), queries[i], "US" }), i);
                }
                var snapshot = catalog.GetType("LunaCatalog.CatalogSnapshot", true)!.GetMethod("Merge")!.Invoke(null, new object[] { results });
                var accepted = catalog.GetType("LunaCatalog.CatalogValidator", true)!.GetMethod("ValidateSnapshot")!.Invoke(null,
                    new[] { snapshot, "US", (object)DateTimeOffset.UtcNow });
                var cacheType = catalog.GetType("LunaCatalog.JsonCatalogCache", true)!;
                var cache = Activator.CreateInstance(cacheType, new object?[] { dataDirectory, null });
                ((Task)cacheType.GetMethod("SaveAsync")!.Invoke(cache, new[] { accepted, (object)CancellationToken.None })).GetAwaiter().GetResult();
                var savedCatalog = LoadCatalog(cacheType, cache!);
                var games = (ICollection)savedCatalog!.GetType().GetProperty("Games")!.GetValue(savedCatalog);
                if (games.Count != 5) throw new InvalidOperationException("Installed dependencies did not validate and reload representative data.");
                var path = Path.Combine(dataDirectory, "catalog-US.json");
                var validText = File.ReadAllText(path);
                var corruptions = new[]
                {
                    validText.Insert(1, "\"schemaVersion\":1,"),
                    validText.Insert(1, "\"schema\\u0056ersion\":1,"),
                    validText.Replace("\"title\":", "\"title\":\"ignored duplicate\",\"title\":"),
                    validText.Insert(1, "\"unknown\":" + new string('[', 65) + "0" + new string(']', 65) + ","),
                    validText + " {}"
                };
                foreach (var invalid in corruptions)
                {
                    File.WriteAllText(path, invalid);
                    if (LoadCatalog(cacheType, cache!) != null) throw new InvalidOperationException("Host JSON accepted corrupt or ambiguous cache data.");
                }
                // API parsing must also enforce the reader's depth bound with JSON 10.
                var firstQueryName = (string)queryType.GetProperty("Name")!.GetValue(queries[0]);
                var deepResponse = File.ReadAllText(Path.Combine(fixtures, firstQueryName + ".json")).Insert(1,
                    "\"unknown\":" + new string('[', 65) + "0" + new string(']', 65) + ",");
                var deepResult = parse.Invoke(null, new[] { deepResponse, queries[0], "US" });
                if ((bool)deepResult!.GetType().GetProperty("ResponseValid")!.GetValue(deepResult))
                    throw new InvalidOperationException("Host JSON accepted an excessively nested API response.");
                var jsonAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Newtonsoft.Json").ToArray();
                if (hostJson.GetName().Version.Major != 10 || jsonAssemblies.Length != 1 || jsonAssemblies[0] != hostJson ||
                    catalog.GetReferencedAssemblies().Single(a => a.Name == "Newtonsoft.Json").Version.Major != 10)
                    throw new InvalidOperationException("Catalog did not share the single host JSON 10 assembly.");
                if (Directory.GetFiles(package, "Newtonsoft.Json.dll", SearchOption.AllDirectories).Length != 0)
                    throw new InvalidOperationException("The package bundles a JSON library.");
                Console.WriteLine("Installed archive dependency checks passed using only host JSON 10, including duplicate-property and depth limits.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true); }
        }

        private static object? LoadCatalog(Type cacheType, object cache)
        {
            var loaded = (Task)cacheType.GetMethod("LoadAsync")!.Invoke(cache, new object[] { "US", CancellationToken.None });
            loaded.GetAwaiter().GetResult();
            var read = loaded.GetType().GetProperty("Result")!.GetValue(loaded);
            return read!.GetType().GetProperty("Catalog")!.GetValue(read);
        }
    }
}
