/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Simulator.Web
{
    /// <summary>
    /// Runtime directory generated from map/vehicle/sensor/bridge.json and each vehicle's SensorConfigurations sources.
    /// Legacy IDs remain aliases for existing Python scripts; no cloud response records are read.
    /// </summary>
    public static class BuiltinAssetLibrary
    {
        public const string Index = "BuiltinAssets";
        enum Collection { Maps, Vehicles, Plugins }

        class Record
        {
            public string Id;
            public JObject Data;
            public JObject Configuration; // Null for asset records; configurations share their vehicle's identity.
            public string Name => (string)Data["name"];
            public string AssetGuid => (string)Data["assetGuid"];
            public bool Preferred => (bool?)Data["preferred"] == true;
        }

        static readonly object loadLock = new object();
        static Dictionary<Collection, Dictionary<string, Record>> records;
        static Func<string> readIndex = () =>
        {
            var index = Resources.Load<TextAsset>(Index);
            if (index == null) return null;
            var text = index.text;
            Resources.UnloadAsset(index);
            return text;
        };

        public static bool HasAssets => Records.Values.Any(c => c.Count > 0);
        public static string Describe() =>
            $"{Records[Collection.Maps].Count} maps, " +
            $"{Records[Collection.Vehicles].Values.Count(r => r.Configuration == null)} vehicles, " +
            $"{Records[Collection.Vehicles].Values.Count(r => r.Configuration != null)} sensor configurations, " +
            $"{Records[Collection.Plugins].Count} plugins";

        public static Task<T> Get<T>(string id) where T : CloudAssetDetails
        {
            var collection = CollectionOf<T>();
            if (!Records[collection].TryGetValue(id, out var record))
                throw new Exception($"Could not find {Singular(collection)} with Id {id} among the built-in assets");
            return Task.FromResult(Resolve(record).ToObject<T>());
        }

        public static Task<T[]> GetLibrary<T>() where T : CloudAssetDetails =>
            Task.FromResult(Listed(CollectionOf<T>()).Select(r => Resolve(r).ToObject<T>()).ToArray());

        public static Task<T> GetByIdOrName<T>(string idOrName) where T : CloudAssetDetails
        {
            var collection = CollectionOf<T>();
            if (Records[collection].ContainsKey(idOrName)) return Get<T>(idOrName);
            var byAssetGuid = Listed(collection).FirstOrDefault(r => r.AssetGuid == idOrName);
            if (byAssetGuid != null) return Get<T>(byAssetGuid.Id);
            if (Guid.TryParse(idOrName, out _))
                throw new Exception($"Could not find {Singular(collection)} with Id {idOrName} among the built-in assets");
            var matches = Listed(collection).Where(r => r.Name == idOrName).ToList();
            if (matches.Count == 0)
                matches = Listed(collection).Where(r => string.Equals(r.Name, idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1)
            {
                var preferred = matches.Where(r => r.Preferred).ToList();
                if (preferred.Count > 0) matches = preferred;
            }
            if (matches.Count > 1)
                throw new Exception($"multiple built-in {Collections[collection]} matching name '{idOrName}', please use Id ({string.Join(", ", matches.Select(m => m.Id))})");
            if (matches.Count == 0) throw new Exception($"no built-in {Collections[collection]} matching name '{idOrName}'");
            return Get<T>(matches[0].Id);
        }

        static readonly Dictionary<Collection, string> Collections = new Dictionary<Collection, string>
        {
            [Collection.Maps] = "maps", [Collection.Vehicles] = "vehicles", [Collection.Plugins] = "plugins",
        };
        static string Singular(Collection collection) => Collections[collection].TrimEnd('s');
        static Collection CollectionOf<T>()
        {
            if (typeof(MapDetailData).IsAssignableFrom(typeof(T))) return Collection.Maps;
            if (typeof(VehicleDetailData).IsAssignableFrom(typeof(T))) return Collection.Vehicles;
            if (typeof(PluginDetailData).IsAssignableFrom(typeof(T))) return Collection.Plugins;
            throw new ArgumentException($"{typeof(T).Name} is not a built-in asset record");
        }
        static IEnumerable<Record> Listed(Collection collection) => Records[collection].Values.Where(r => r.Configuration == null);

        // Adapt the source directory to the existing API/editor DTOs without repeating vehicle data in every profile.
        static JObject Resolve(Record record)
        {
            var data = (JObject)record.Data.DeepClone();
            var configurations = data["configurations"] as JArray;
            data.Remove("configurations");
            data.Remove("preferred");
            if (record.Configuration != null)
            {
                data["sensors"] = record.Configuration["sensors"].DeepClone();
                data["bridge"] = record.Configuration["bridge"]?.DeepClone();
            }
            else if (configurations != null)
            {
                foreach (var configuration in configurations.OfType<JObject>())
                {
                    configuration["vehicleId"] = record.Id;
                    configuration["bridgePluginId"] = configuration["bridge"] is JObject bridge ? bridge["id"]?.DeepClone() : null;
                }
                data["sensorsConfigurations"] = configurations;
            }
            return data;
        }

        static Dictionary<Collection, Dictionary<string, Record>> Records
        {
            get
            {
                lock (loadLock)
                {
                    if (records == null)
                    {
                        var text = readIndex();
                        records = text == null ? EmptyRecords() : Read(text);
                        Debug.Log($"Built-in assets: {Describe()}");
                    }
                    return records;
                }
            }
        }

        static Dictionary<Collection, Dictionary<string, Record>> EmptyRecords() =>
            Collections.Keys.ToDictionary(c => c, c => new Dictionary<string, Record>());

        static Dictionary<Collection, Dictionary<string, Record>> Read(string text)
        {
            var library = JObject.Parse(text);
            if ((int?)library["version"] != 1) throw new InvalidOperationException($"Unsupported {Index} version");
            var result = EmptyRecords();
            foreach (var collection in Collections)
            {
                if (!(library[collection.Value] is JArray entries))
                    throw new InvalidOperationException($"Missing {collection.Value} in {Index}");
                void Add(Record record)
                {
                    if (string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Name) || string.IsNullOrEmpty(record.AssetGuid) ||
                        result[collection.Key].ContainsKey(record.Id))
                        throw new InvalidOperationException($"Invalid or duplicate {collection.Value} entry in {Index}: {record.Id}");
                    result[collection.Key].Add(record.Id, record);
                }
                foreach (var entry in entries)
                {
                    if (!(entry is JObject data)) throw new InvalidOperationException($"Invalid {collection.Value} entry in {Index}");
                    Add(new Record { Id = (string)data["id"], Data = data });
                    if (collection.Key != Collection.Vehicles) continue;
                    if (!(data["configurations"] is JArray configurations))
                        throw new InvalidOperationException($"Missing vehicle configurations in {Index}");
                    foreach (var value in configurations)
                    {
                        if (!(value is JObject config) || !(config["sensors"] is JArray) || string.IsNullOrEmpty((string)config["name"]))
                            throw new InvalidOperationException($"Invalid vehicle configuration in {Index}");
                        Add(new Record { Id = (string)config["id"], Data = data, Configuration = config });
                    }
                }
            }
            return result;
        }
    }
}
