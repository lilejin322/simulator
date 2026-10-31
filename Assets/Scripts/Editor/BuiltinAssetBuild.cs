using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Simulator.Web;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Simulator.Editor
{
    /// <summary>Generates the runtime library directly from asset descriptors and vehicle sensor configuration sources.</summary>
    public class BuiltinAssetBuild : IPreprocessBuildWithReport
    {
        const string IndexPath = "Assets/Resources/" + BuiltinAssetLibrary.Index + ".json";
        static readonly (string Folder, string Descriptor, string Collection)[] Kinds =
        {
            ("Maps", "map.json", "maps"), ("Vehicles", "vehicle.json", "vehicles"),
            ("Sensors", "sensor.json", "plugins"), ("Bridges", "bridge.json", "plugins"),
        };
        static readonly Regex Source = new Regex(
            @"^Assets/(Maps|Vehicles|Sensors|Bridges)/[^/]+/((map|vehicle|sensor|bridge)\.json|SensorConfigurations(/[^/]+\.json)?)$");

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Refresh();

        [MenuItem("Simulator/Refresh Built-in Asset Library")]
        public static void Refresh()
        {
            var library = Collect();
            var json = library.ToString(Formatting.None);
            if (!File.Exists(IndexPath) || File.ReadAllText(IndexPath) != json)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IndexPath));
                File.WriteAllText(IndexPath, json);
                AssetDatabase.ImportAsset(IndexPath, ImportAssetOptions.ForceUpdate);
            }
            Debug.Log($"Indexed {((JArray)library["maps"]).Count} maps, {((JArray)library["vehicles"]).Count} vehicles, " +
                $"{((JArray)library["vehicles"]).Sum(v => ((JArray)v["configurations"]).Count)} sensor configurations, " +
                $"{((JArray)library["plugins"]).Count} plugins from asset sources");
        }

        public static JObject Collect(string projectRoot = ".")
        {
            var library = new JObject { ["version"] = 1, ["maps"] = new JArray(), ["vehicles"] = new JArray(), ["plugins"] = new JArray() };
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void RequireId(JObject value, string path)
            {
                if (string.IsNullOrWhiteSpace((string)value["id"]) || string.IsNullOrWhiteSpace((string)value["name"]) ||
                    !ids.Add((string)value["id"]))
                    throw new InvalidOperationException($"Missing name/id or duplicate id in {path}");
            }
            foreach (var (folder, descriptor, collection) in Kinds)
            {
                var source = Path.Combine(projectRoot, "Assets", folder);
                if (!Directory.Exists(source)) continue;
                foreach (var path in Directory.GetFiles(source, descriptor, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var asset = JObject.Parse(File.ReadAllText(path));
                    if ((bool?)asset["ready"] != true) continue;
                    var guid = (string)asset["assetGuid"];
                    if (!BuiltinSelection.Includes(folder.ToLowerInvariant(), guid)) continue;
                    if (!Guid.TryParse(guid, out _)) throw new InvalidOperationException($"Invalid assetGuid in {path}");
                    var entry = new JObject {
                        ["id"] = (string)asset["id"] ?? guid,
                        ["name"] = (string)asset["displayName"] ?? (string)asset["name"],
                        ["assetGuid"] = guid,
                        ["description"] = (string)asset["description"] ?? "",
                        ["preferred"] = (bool?)asset["preferred"] ?? false,
                    };
                    RequireId(entry, path);
                    if (collection == "plugins")
                    {
                        entry["category"] = folder == "Sensors" ? "sensor" : "bridge";
                        entry["type"] = (string)asset["type"] ?? (string)asset["bridgeType"] ?? (string)asset["name"];
                    }
                    if (collection == "vehicles")
                    {
                        var configurations = new JArray();
                        entry["configurations"] = configurations;
                        var references = asset["sensorConfigurations"] ?? new JArray();
                        if (!(references is JArray)) throw new InvalidOperationException($"sensorConfigurations must be an array of source paths in {path}");
                        // Descriptor order is also the UI order; its first configuration supplies default sensors.
                        foreach (var reference in references)
                        {
                            var relative = reference.Type == JTokenType.String ? (string)reference : null;
                            if (relative == null || !relative.StartsWith("SensorConfigurations/", StringComparison.Ordinal) ||
                                relative.IndexOf('\\') >= 0 || relative.Split('/').Contains("..") || !relative.EndsWith(".json", StringComparison.Ordinal))
                                throw new InvalidOperationException($"Invalid sensor configuration source '{reference}' in {path}");
                            var configPath = Path.Combine(Path.GetDirectoryName(path), relative);
                            var config = JObject.Parse(File.ReadAllText(configPath));
                            RequireId(config, configPath);
                            if (!(config["sensors"] is JArray sensors) ||
                                (config["bridge"] != null && config["bridge"].Type != JTokenType.Null && !(config["bridge"] is JObject)))
                                throw new InvalidOperationException($"Invalid sensors or bridge in {configPath}");
                            var compiledSensors = new JArray();
                            foreach (var sensor in sensors)
                            {
                                if (!(sensor is JObject sensorObject)) throw new InvalidOperationException($"Invalid sensor in {configPath}");
                                var compiled = Copy(sensorObject, "name", "parent", "type", "transform", "params");
                                if (sensorObject["plugin"] is JObject plugin)
                                    compiled["plugin"] = Copy(plugin, "id", "type", "assetGuid");
                                compiledSensors.Add(compiled);
                            }
                            configurations.Add(new JObject {
                                ["id"] = (string)config["id"], ["name"] = (string)config["name"], ["sensors"] = compiledSensors,
                                ["bridge"] = config["bridge"] is JObject bridge ? Copy(bridge, "id", "name", "assetGuid", "type", "connectionString") : null,
                            });
                        }
                    }
                    ((JArray)library[collection]).Add(entry);
                }
            }
            return library;
        }

        static JObject Copy(JObject source, params string[] fields)
        {
            var result = new JObject();
            foreach (var field in fields)
                if (source.TryGetValue(field, out var value)) result[field] = value.DeepClone();
            return result;
        }

        sealed class Watcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(path => Source.IsMatch(path))) Refresh();
            }
        }
    }
}
