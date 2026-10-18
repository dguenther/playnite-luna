using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace LunaPlayniteSmoke
{
    // Exercise the production adapter with real SDK models and an isolated API implementation.
    // Actual host persistence and launch behavior remain manual Playnite checks.
    internal sealed class SmokeDatabase
    {
        internal readonly Items<Game> Games = new Items<Game>();
        internal readonly Items<GameSource> Sources = new Items<GameSource>();
        internal readonly Items<Tag> Tags = new Items<Tag>();
        internal IGameDatabaseAPI Api { get; }
        internal int ArtworkWrites;
        internal SmokeDatabase(string directory)
        {
            Api = Proxy.Create<IGameDatabaseAPI>((name, args) =>
            {
                switch (name)
                {
                    case "get_Games": return Games.Api;
                    case "get_Sources": return Sources.Api;
                    case "get_Tags": return Tags.Api;
                    case "AddFile":
                        var destination = Path.Combine(directory, ((Guid)args[1]).ToString("N") + ".png");
                        Directory.CreateDirectory(directory); File.Copy((string)args[0], destination, overwrite: false); ArtworkWrites++; return destination;
                    case "RemoveFile": File.Delete((string)args[0]); return null;
                    default: throw new NotSupportedException(name);
                }
            });
        }
        internal sealed class Items<T> where T : DatabaseObject, new()
        {
            internal readonly List<T> Values = new List<T>();
            internal IItemCollection<T> Api { get; }
            internal Items()
            {
                Api = Proxy.Create<IItemCollection<T>>((name, args) =>
                {
                    switch (name)
                    {
                        case "GetEnumerator": return Values.GetEnumerator();
                        case "get_Count": return Values.Count;
                        case "get_IsReadOnly": return false;
                        case "CopyTo": Values.CopyTo((T[])args[0], (int)args[1]); return null;
                        case "Get": case "get_Item": return Values.SingleOrDefault(v => v.Id == (Guid)args[0]);
                        case "Add":
                            if (args[0] is string text)
                            {
                                var existing = Values.FirstOrDefault(v => v.Name == text); if (existing != null) return existing;
                                var created = new T { Name = text }; Values.Add(created); return created;
                            }
                            Values.Add((T)args[0]); return null;
                        case "Update":
                            var updated = (T)args[0];
                            var index = Values.FindIndex(v => v.Id == updated.Id);
                            if (index < 0) throw new InvalidOperationException("Cannot update a missing game.");
                            Values[index] = updated; return null;
                        case "add_ItemCollectionChanged": case "remove_ItemCollectionChanged":
                        case "add_ItemUpdated": case "remove_ItemUpdated": return null;
                        default: throw new NotSupportedException(name);
                    }
                });
            }
        }
        private sealed class Proxy : RealProxy
        {
            private readonly Func<string, object[], object?> call;
            private Proxy(Type type, Func<string, object[], object?> call) : base(type) { this.call = call; }
            internal static T Create<T>(Func<string, object[], object?> call) where T : class => (T)new Proxy(typeof(T), call).GetTransparentProxy();
            public override IMessage Invoke(IMessage message)
            {
                var request = (IMethodCallMessage)message;
                try { return new ReturnMessage(call(request.MethodName, request.Args), null, 0, request.LogicalCallContext, request); }
                catch (Exception ex) { return new ReturnMessage(ex, request); }
            }
        }
    }
}
