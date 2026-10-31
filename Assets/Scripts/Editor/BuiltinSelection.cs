using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Simulator.Editor
{
    /// <summary>Explicit source-folder switches for one player build. Outside a build the editor indexes all assets.</summary>
    public static class BuiltinSelection
    {
        public const string DefaultPath = "build-assets.yaml";

        static readonly (string Section, string Folder, string Descriptor)[] Kinds =
        {
            ("maps", "Maps", "map.json"),
            ("vehicles", "Vehicles", "vehicle.json"),
            ("sensors", "Sensors", "sensor.json"),
            ("bridges", "Bridges", "bridge.json"),
            ("npcs", "NPCs", "npc.json"),
            ("pedestrians", "Pedestrians", "pedestrian.json"),
            ("controllables", "Controllables", "controllable.json"),
        };

        static Dictionary<string, HashSet<string>> selected;
        static JObject resolved;
        static string yaml;

        public static bool Active => selected != null;

        public static bool Includes(string section, string assetGuid) =>
            selected == null || selected[section].Contains(assetGuid ?? "");

        /// <summary>
        /// Reads the entire manifest before changing selection. Missing entries are disabled; enabled entries must
        /// identify one installed, ready source folder. Display names, prefixes and wildcards are not selectors.
        /// </summary>
        public static IDisposable Use(string path = DefaultPath, string projectRoot = ".")
        {
            if (Active) throw new InvalidOperationException("A built-in asset selection is already active");
            path = Path.GetFullPath(Path.Combine(projectRoot, path));
            var text = File.ReadAllText(path);
            var document = new YamlStream();
            try
            {
                document.Load(new StringReader(text));
            }
            catch (YamlException ex)
            {
                throw new InvalidOperationException($"Invalid asset manifest {path}: {ex.Message}", ex);
            }
            if (document.Documents.Count != 1 || !(document.Documents[0].RootNode is YamlMappingNode root))
                throw new InvalidOperationException($"{path} must contain one YAML mapping");
            if (!root.Children.TryGetValue(new YamlScalarNode("version"), out var version) ||
                !(version is YamlScalarNode scalarVersion) || scalarVersion.Value != "1")
                throw new InvalidOperationException($"{path} requires version: 1");

            var choices = Kinds.ToDictionary(k => k.Section, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var assets = new JObject();
            foreach (var kind in Kinds) assets[kind.Section] = new JArray();
            foreach (var section in root.Children)
            {
                if (!(section.Key is YamlScalarNode key))
                    throw new InvalidOperationException($"{path}: section names must be strings");
                if (key.Value == "version") continue;
                if (!choices.ContainsKey(key.Value))
                    throw new InvalidOperationException($"{path}: unknown asset section '{key.Value}'");
                if (!(section.Value is YamlMappingNode switches))
                    throw new InvalidOperationException($"{path}: {key.Value} must map source folder names to true or false (use {{}} for none)");
                var kind = Kinds.Single(k => k.Section == key.Value);
                foreach (var entry in switches.Children)
                {
                    if (!(entry.Key is YamlScalarNode folder) || string.IsNullOrWhiteSpace(folder.Value) ||
                        folder.Value == "." || folder.Value == ".." || folder.Value.IndexOfAny(new[] { '/', '\\' }) >= 0)
                        throw new InvalidOperationException($"{path}: {key.Value} keys must be source folder names");
                    if (!(entry.Value is YamlScalarNode enabled) || enabled.Style != ScalarStyle.Plain ||
                        (enabled.Value != "true" && enabled.Value != "false"))
                        throw new InvalidOperationException($"{path}: {key.Value}.{folder.Value} must be true or false");
                    if (enabled.Value == "false") continue;
                    var relative = $"Assets/{kind.Folder}/{folder.Value}";
                    var descriptorPath = Path.Combine(projectRoot, relative, kind.Descriptor);
                    if (!File.Exists(descriptorPath))
                        throw new InvalidOperationException($"{path}: enabled source '{relative}' is missing {kind.Descriptor}; use the exact source folder name");
                    var descriptor = JObject.Parse(File.ReadAllText(descriptorPath));
                    if ((bool?)descriptor["ready"] != true)
                        throw new InvalidOperationException($"{path}: enabled source '{relative}' is not ready; set it to false");
                    var guid = (string)descriptor["assetGuid"];
                    if (!Guid.TryParse(guid, out _) || !choices[kind.Section].Add(guid))
                        throw new InvalidOperationException($"{path}: invalid or duplicate assetGuid in {descriptorPath}");
                    ((JArray)assets[kind.Section]).Add(new JObject {
                        ["source"] = relative, ["name"] = (string)descriptor["name"], ["assetGuid"] = guid,
                    });
                }
            }
            selected = choices;
            resolved = assets;
            yaml = text;
            return new Scope();
        }

        /// <summary>Records exactly which source folders this player was built from.</summary>
        public static void WriteManifest(string output)
        {
            if (!Active) throw new InvalidOperationException("No asset manifest is active");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, DefaultPath), yaml);
            File.WriteAllText(Path.Combine(output, "build-assets.resolved.json"), resolved.ToString() + "\n");
        }

        sealed class Scope : IDisposable
        {
            public void Dispose()
            {
                selected = null;
                resolved = null;
                yaml = null;
            }
        }
    }
}
