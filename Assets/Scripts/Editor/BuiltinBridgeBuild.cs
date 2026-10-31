using System;
using System.IO;
using System.Linq;
using System.Security;
using Newtonsoft.Json.Linq;
using Simulator.Web;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Simulator.Editor
{
    public class BuiltinBridgeBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Refresh();
        [MenuItem("Simulator/Refresh Built-in Bridges")]
        public static void Refresh()
        {
            var entries = (Directory.Exists("Assets/Bridges") ? Directory.GetFiles("Assets/Bridges", "bridge.json", SearchOption.AllDirectories) : Array.Empty<string>())
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => JObject.Parse(File.ReadAllText(p)))
                .Where(d => (bool)d["ready"] && BuiltinSelection.Includes("bridges", (string)d["assetGuid"])).Select(d => new BuiltinBridgeCatalog.Entry {
                    assetGuid = (string)d["assetGuid"], name = (string)d["name"], assetFormat = (string)d["assetFormat"], factoryType = (string)d["factoryType"]
                }).ToArray();
            if (entries.Any(e => !Guid.TryParse(e.assetGuid, out _) || Type.GetType(e.factoryType) == null) || entries.Select(e => e.assetGuid).Distinct().Count() != entries.Length)
                throw new InvalidOperationException("Invalid built-in bridge descriptor");
            const string path = "Assets/Resources/BuiltinBridges.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinBridgeCatalog>(path);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuiltinBridgeCatalog>(); AssetDatabase.CreateAsset(catalog, path); }
            catalog.bridges = entries;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            // Factory discovery and protocol serialization use reflection in player builds.
            var assemblies = entries.Select(e => Type.GetType(e.factoryType).Assembly.GetName().Name).Distinct();
            Directory.CreateDirectory("Assets/Bridges");
            File.WriteAllText("Assets/Bridges/link.xml", "<linker>\n" + string.Join("\n", assemblies.Select(a => "  <assembly fullname=\"" + SecurityElement.Escape(a) + "\" preserve=\"all\" />")) + "\n</linker>\n");
        }
    }
}
