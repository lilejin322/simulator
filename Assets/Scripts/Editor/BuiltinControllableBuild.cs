using System;
using System.IO;
using System.Linq;
using Simulator.Web;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Simulator.Controllable;

namespace Simulator.Editor
{
    public class BuiltinControllableBuild : IPreprocessBuildWithReport
    {
        [Serializable]
        public class Descriptor
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public bool ready;
            public string[] additionalPrefabPaths;
        }
        static string Signature(BuiltinControllableCatalog.Entry e) => JsonUtility.ToJson(e);
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Refresh();

        [MenuItem("Simulator/Refresh Built-in Controllables")]
        public static void Refresh()
        {
            var entries = Directory.Exists("Assets/Controllables") ? Directory.GetFiles("Assets/Controllables", "controllable.json", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => JsonUtility.FromJson<Descriptor>(File.ReadAllText(p)))
                .Where(e => e.ready && BuiltinSelection.Includes("controllables", e.assetGuid)).Select(e => {
                    if (string.IsNullOrEmpty(e.prefabPath) || !e.prefabPath.StartsWith("Assets/Controllables/", StringComparison.Ordinal) ||
                        e.prefabPath.Split('/').Contains("..") || !Guid.TryParse(e.assetGuid, out _))
                        throw new InvalidOperationException("Invalid Controllable descriptor " + e.name);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefabPath);
                    if (prefab == null || prefab.GetComponent<IControllable>() == null)
                        throw new InvalidOperationException("Invalid Controllable prefab " + e.prefabPath);
                    return new BuiltinControllableCatalog.Entry { assetGuid = e.assetGuid, name = e.name, prefabPath = e.prefabPath, assetFormat = e.assetFormat, prefab = prefab,
                        additionalPrefabs = (e.additionalPrefabPaths ?? Array.Empty<string>()).Select(p => {
                            if (!p.StartsWith("Assets/Controllables/", StringComparison.Ordinal) || p.Split('/').Contains(".."))
                                throw new InvalidOperationException("Invalid additional prefab path " + p);
                            var additional = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                            if (additional == null) throw new InvalidOperationException("Missing additional prefab " + p);
                            return additional;
                        }).ToArray() };
                }).ToArray() : Array.Empty<BuiltinControllableCatalog.Entry>();
            if (entries.Select(e => e.assetGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length ||
                entries.Select(e => e.name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
                throw new InvalidOperationException("Duplicate built-in Controllable name or GUID");
            const string path = "Assets/Resources/BuiltinControllables.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinControllableCatalog>(path);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuiltinControllableCatalog>(); AssetDatabase.CreateAsset(catalog, path); }
            if (!catalog.controllables.Select(Signature).SequenceEqual(entries.Select(Signature)))
            { catalog.controllables = entries; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); }
            Debug.Log($"Indexed {entries.Length} built-in Controllables");
        }
    }
}
