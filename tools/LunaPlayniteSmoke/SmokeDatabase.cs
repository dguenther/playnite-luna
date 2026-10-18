using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Newtonsoft.Json;

namespace LunaPlayniteSmoke
{
    // Exercise the production adapter with real SDK models and an isolated API implementation.
    // Actual host persistence and launch behavior remain manual Playnite checks.
    internal sealed class SmokeDatabase
    {
        internal static void InitializeSdkSettings()
        {
            // Playnite normally installs this service before loading extensions. This
            // isolated host supplies JSON settings I/O through that same SDK interface.
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 };
            var serializer = Proxy.CreateGeneric<IDataSerializer>((name, args, types) =>
            {
                switch (name)
                {
                    case "ToJson": return JsonConvert.SerializeObject(args[0], (bool)args[1] ? Formatting.Indented : Formatting.None, settings);
                    case "FromJsonFile": return JsonConvert.DeserializeObject(File.ReadAllText((string)args[0]), types.Single(), settings);
                    default: throw new NotSupportedException(name);
                }
            });
            typeof(Serialization).GetMethod("Init", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { serializer });
        }

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
        internal sealed class Host
        {
            internal ApplicationMode Mode = ApplicationMode.Desktop;
            internal Window? MainWindow;
            internal Window? DialogWindow;
            internal int Errors;
            internal IPlayniteAPI Api { get; }
            internal Host(SmokeDatabase library, string directory)
            {
                var info = Proxy.Create<IPlayniteInfoAPI>((name, _) => name == "get_Mode" ? (object)Mode : throw new NotSupportedException(name));
                var paths = Proxy.Create<IPlaynitePathsAPI>((name, _) => name == "get_ExtensionsDataPath" ? directory : throw new NotSupportedException(name));
                var dispatcher = Dispatcher.CurrentDispatcher;
                var view = Proxy.Create<IMainViewAPI>((name, _) => name == "get_UIDispatcher" ? dispatcher : throw new NotSupportedException(name));
                var dialogs = Proxy.Create<IDialogsFactory>((name, _) =>
                {
                    switch (name)
                    {
                        case "CreateWindow": return DialogWindow = new Window();
                        case "GetCurrentAppWindow": return MainWindow;
                        case "ShowMessage": return MessageBoxResult.Yes;
                        case "ShowErrorMessage": Errors++; return null;
                        default: throw new NotSupportedException(name);
                    }
                });
                Api = Proxy.Create<IPlayniteAPI>((name, _) =>
                {
                    switch (name)
                    {
                        case "get_ApplicationInfo": return info;
                        case "get_Paths": return paths;
                        case "get_Database": return library.Api;
                        case "get_MainView": return view;
                        case "get_Dialogs": return dialogs;
                        default: throw new NotSupportedException(name);
                    }
                });
            }
        }

        private sealed class Proxy : RealProxy
        {
            private readonly Func<string, object[], Type[], object?> call;
            private Proxy(Type type, Func<string, object[], Type[], object?> call) : base(type) { this.call = call; }
            internal static T Create<T>(Func<string, object[], object?> call) where T : class => CreateGeneric<T>((name, args, _) => call(name, args));
            internal static T CreateGeneric<T>(Func<string, object[], Type[], object?> call) where T : class => (T)new Proxy(typeof(T), call).GetTransparentProxy();
            public override IMessage Invoke(IMessage message)
            {
                var request = (IMethodCallMessage)message;
                try { return new ReturnMessage(call(request.MethodName, request.Args, ((MethodInfo)request.MethodBase).GetGenericArguments()), null, 0, request.LogicalCallContext, request); }
                catch (Exception ex) { return new ReturnMessage(ex, request); }
            }
        }
    }
}
