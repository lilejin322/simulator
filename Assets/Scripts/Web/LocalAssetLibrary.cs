/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Simulator.Web
{
    /// <summary>
    /// Asset library backed by a local folder, replacing the WISE cloud library.
    ///
    /// Layout of <see cref="Config.AssetsPath"/>:
    ///   maps/, vehicles/, plugins/ - asset bundles, named by assetGuid (other names are read from the bundle manifest)
    ///   metadata/*.json - WISE records: [{ "cid": id, "ctype": "vehicle"|"configuration", "endpoint": "/api/v1/maps/&lt;id&gt;", "data": {...} }]
    ///   npcs/, pedestrians/, controllables/ - bundles loaded at startup by <see cref="Config.LoadExternalAssets"/>, not served from here
    ///
    /// Records are looked up by their WISE id, so ids used by existing Python API scripts keep working.
    /// Looking up a vehicle by a sensor configuration id returns the vehicle with that configuration's sensors and bridge.
    /// Bundles without a metadata record are listed using the name from their manifest.
    /// </summary>
    public static class LocalAssetLibrary
    {
        enum Collection { Maps, Vehicles, Plugins }

        class Record
        {
            public string Id;
            public string Kind; // "vehicle" or "configuration" for vehicles, null otherwise
            public JObject Data;
            public string Name => (string)Data["name"];
            public string AssetGuid => (string)Data["assetGuid"];
        }

        static readonly object loadLock = new object();
        static Dictionary<Collection, Dictionary<string, Record>> records;
        static Dictionary<string, string> bundles; // assetGuid -> file path
        static Dictionary<string, Collection> bundleFolders; // assetGuid -> folder the bundle is in
        static Dictionary<string, Manifest> manifests; // assetGuid -> manifest, for bundles whose manifest was read
        static string loadedFrom;

        public static string Root => Config.AssetsPath;

        /// <summary>Forget everything read from disk; the next request reads the library again.</summary>
        public static void Reload()
        {
            lock (loadLock)
            {
                records = null;
                bundles = null;
            }
        }

        /// <returns>Path of the bundle file for the asset, or null when the library does not have it.</returns>
        public static string GetBundlePath(string assetGuid)
        {
            EnsureLoaded();
            return assetGuid != null && bundles.TryGetValue(assetGuid, out var path) ? path : null;
        }

        public static Task<T> Get<T>(string id) where T : CloudAssetDetails
        {
            EnsureLoaded();
            var collection = CollectionOf<T>();
            if (!records[collection].TryGetValue(id, out var record))
            {
                throw new Exception($"Could not find {Singular(collection)} with Id {id} in local asset library {Root}");
            }

            return Task.FromResult(Resolve(record).ToObject<T>());
        }

        public static Task<T[]> GetLibrary<T>() where T : CloudAssetDetails
        {
            EnsureLoaded();
            var library = Listed(CollectionOf<T>()).Select(r => Resolve(r).ToObject<T>()).ToArray();
            return Task.FromResult(library);
        }

        public static Task<T> GetByIdOrName<T>(string idOrName) where T : CloudAssetDetails
        {
            EnsureLoaded();
            var collection = CollectionOf<T>();
            if (records[collection].ContainsKey(idOrName))
            {
                return Get<T>(idOrName);
            }

            var byAssetGuid = Listed(collection).FirstOrDefault(r => r.AssetGuid == idOrName);
            if (byAssetGuid != null)
            {
                return Get<T>(byAssetGuid.Id);
            }

            if (Guid.TryParse(idOrName, out _))
            {
                throw new Exception($"Could not find {Singular(collection)} with Id {idOrName} in local asset library {Root}");
            }

            var matches = Listed(collection).Where(r => r.Name == idOrName).ToList();
            if (matches.Count == 0)
            {
                matches = Listed(collection).Where(r => string.Equals(r.Name, idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (matches.Count > 1)
            {
                // Several users published assets with the same name; prefer the ones we have bundles for, then the official ones.
                var available = matches.Where(r => r.AssetGuid != null && bundles.ContainsKey(r.AssetGuid)).ToList();
                if (available.Count > 0)
                {
                    matches = available;
                }
                var official = matches.Where(IsOfficial).ToList();
                if (official.Count > 0)
                {
                    matches = official;
                }
            }
            if (matches.Count > 1)
            {
                throw new Exception($"multiple {Collections[collection]} matching name '{idOrName}' in local asset library, please use Id ({string.Join(", ", matches.Select(m => m.Id))})");
            }
            if (matches.Count == 0)
            {
                throw new Exception($"no {Collections[collection]} matching name '{idOrName}' in local asset library {Root}");
            }

            return Get<T>(matches[0].Id);
        }

        static readonly Dictionary<Collection, string> Collections = new Dictionary<Collection, string>
        {
            [Collection.Maps] = "maps",
            [Collection.Vehicles] = "vehicles",
            [Collection.Plugins] = "plugins",
        };

        static string Singular(Collection collection) => Collections[collection].TrimEnd('s');

        static Collection CollectionOf<T>()
        {
            if (typeof(MapDetailData).IsAssignableFrom(typeof(T))) return Collection.Maps;
            if (typeof(VehicleDetailData).IsAssignableFrom(typeof(T))) return Collection.Vehicles;
            if (typeof(PluginDetailData).IsAssignableFrom(typeof(T))) return Collection.Plugins;
            throw new ArgumentException($"{typeof(T).Name} is not stored in the local asset library");
        }

        // Records that show up in library listings; sensor configurations are only reachable through their vehicle or by id.
        static IEnumerable<Record> Listed(Collection collection) =>
            records[collection].Values.Where(r => r.Kind != "configuration");

        static bool IsOfficial(Record record) =>
            (string)record.Data.SelectToken("owner.firstName") == "SVL" && (string)record.Data.SelectToken("owner.lastName") == "Content";

        static JObject Resolve(Record record)
        {
            switch (record.Kind)
            {
                case "configuration":
                    return ResolveConfiguration(record);
                case "vehicle":
                    return ResolveVehicle(record);
                default:
                    return record.Data;
            }
        }

        // A sensor configuration resolves to its vehicle carrying the configuration's sensors and bridge, as WISE returned it.
        static JObject ResolveConfiguration(Record record)
        {
            var data = (JObject)record.Data.DeepClone();
            if (data["bridge"] is JObject bridge && bridge["assetGuid"] == null && bridge["assetBundles"] is JArray assetBundles && assetBundles.Count > 0)
            {
                bridge["assetGuid"] = assetBundles[0]["assetGuid"];
            }
            return data;
        }

        // Sensor configurations listed in a vehicle record are summaries whose plugins lack assetGuids;
        // fill in sensors and bridge from the configuration's own record.
        static JObject ResolveVehicle(Record record)
        {
            if (!(record.Data["sensorsConfigurations"] is JArray summaries) || summaries.Count == 0)
            {
                return record.Data;
            }

            var data = (JObject)record.Data.DeepClone();
            foreach (var configuration in ((JArray)data["sensorsConfigurations"]).OfType<JObject>())
            {
                var id = (string)configuration["id"];
                if (id != null && records[Collection.Vehicles].TryGetValue(id, out var full) && full.Kind == "configuration")
                {
                    var resolved = ResolveConfiguration(full);
                    configuration["sensors"] = resolved["sensors"];
                    configuration["bridge"] = resolved["bridge"];
                }
            }
            return data;
        }

        static void EnsureLoaded()
        {
            lock (loadLock)
            {
                if (records != null && loadedFrom == Root)
                {
                    return;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                records = Collections.Keys.ToDictionary(c => c, c => new Dictionary<string, Record>());
                bundles = new Dictionary<string, string>();
                bundleFolders = new Dictionary<string, Collection>();
                manifests = new Dictionary<string, Manifest>();
                loadedFrom = Root;

                if (string.IsNullOrEmpty(Root) || !Directory.Exists(Root))
                {
                    Debug.LogWarning($"Local asset library not found at '{Root}'. Set assets_path in config.yml or pass --assets <path>.");
                    return;
                }

                foreach (var collection in Collections)
                {
                    var dir = Path.Combine(Root, collection.Value);
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        // Downloaded bundles are named by their assetGuid, freshly built ones (e.g. vehicle_Jaguar2015XE) are not
                        var assetGuid = Path.GetFileName(file);
                        if (!Guid.TryParse(assetGuid, out _))
                        {
                            var manifest = ReadManifest(file);
                            if (manifest?.assetGuid == null)
                            {
                                continue;
                            }
                            assetGuid = manifest.assetGuid;
                            manifests[assetGuid] = manifest;
                        }
                        bundles[assetGuid] = file;
                        bundleFolders[assetGuid] = collection.Key;
                    }
                }

                var metadataDir = Path.Combine(Root, "metadata");
                if (Directory.Exists(metadataDir))
                {
                    foreach (var file in Directory.GetFiles(metadataDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                    {
                        LoadMetadata(file);
                    }
                }

                AddBundlesWithoutMetadata();

                Debug.Log($"Local asset library {Root}: {records[Collection.Maps].Count} maps, " +
                          $"{records[Collection.Vehicles].Values.Count(r => r.Kind != "configuration")} vehicles, " +
                          $"{records[Collection.Vehicles].Values.Count(r => r.Kind == "configuration")} sensor configurations, " +
                          $"{records[Collection.Plugins].Count} plugins, {bundles.Count} bundles in {sw.Elapsed}");
            }
        }

        static void LoadMetadata(string file)
        {
            JArray entries;
            try
            {
                entries = JToken.Parse(File.ReadAllText(file)) as JArray;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Skipping local asset library metadata {file}: {e.Message}");
                return;
            }
            if (entries == null)
            {
                return;
            }

            var stem = Path.GetFileNameWithoutExtension(file);
            foreach (var entry in entries.OfType<JObject>())
            {
                var id = (string)entry["cid"];
                var data = entry["data"] as JObject;
                if (id == null || data == null)
                {
                    continue;
                }

                // WISE endpoint tells which collection a record belongs to; fall back to the file name.
                var endpoint = (string)entry["endpoint"] ?? $"/api/v1/{stem}/";
                var collection = Collections.FirstOrDefault(c => endpoint.StartsWith($"/api/v1/{c.Value}/"));
                if (collection.Value == null)
                {
                    continue;
                }

                records[collection.Key][id] = new Record { Id = id, Kind = (string)entry["ctype"], Data = data };
            }
        }

        static void AddBundlesWithoutMetadata()
        {
            var known = new HashSet<string>(records.Values.SelectMany(c => c.Values).Select(r => r.AssetGuid).Where(g => g != null));
            foreach (var bundle in bundles.Where(b => !known.Contains(b.Key)))
            {
                if (!manifests.TryGetValue(bundle.Key, out var manifest))
                {
                    manifest = ReadManifest(bundle.Value);
                }
                if (manifest == null)
                {
                    continue;
                }

                var collection = bundleFolders[bundle.Key];
                var data = new JObject
                {
                    ["id"] = bundle.Key,
                    ["name"] = manifest.assetName,
                    ["assetGuid"] = bundle.Key,
                    ["description"] = manifest.description,
                };
                if (collection == Collection.Plugins)
                {
                    data["category"] = manifest.assetType;
                    data["type"] = string.IsNullOrEmpty(manifest.bridgeType) ? manifest.assetName : manifest.bridgeType;
                }
                if (collection == Collection.Vehicles)
                {
                    data["sensorsConfigurations"] = new JArray();
                }

                records[collection][bundle.Key] = new Record { Id = bundle.Key, Kind = collection == Collection.Vehicles ? "vehicle" : null, Data = data };
            }
        }

        static Manifest ReadManifest(string bundlePath)
        {
            try
            {
                using (var zip = new ZipFile(bundlePath))
                {
                    var entry = zip.GetEntry("manifest.json");
                    if (entry == null)
                    {
                        return null;
                    }
                    using (var reader = new StreamReader(zip.GetInputStream(entry), Encoding.UTF8))
                    {
                        return Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(reader.ReadToEnd());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Skipping {bundlePath}, not an asset bundle: {e.Message}");
                return null;
            }
        }
    }
}
