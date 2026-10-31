using System;
using System.IO;
using System.Linq;
using Simulator.Web;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Simulator.Editor
{
    public class BuiltinPedestrianBuild : IPreprocessBuildWithReport
    {
        [Serializable]
        public class Descriptor
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public bool ready;
        }
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Refresh();

        [MenuItem("Simulator/Refresh Built-in Pedestrians")]
        public static void Refresh()
        {
            var entries = Directory.Exists("Assets/Pedestrians") ? Directory.GetFiles("Assets/Pedestrians", "pedestrian.json", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => JsonUtility.FromJson<Descriptor>(File.ReadAllText(p)))
                .Where(e => e.ready && BuiltinSelection.Includes("pedestrians", e.assetGuid)).Select(e => {
                    if (string.IsNullOrEmpty(e.prefabPath) || !e.prefabPath.StartsWith("Assets/Pedestrians/", StringComparison.Ordinal) ||
                        e.prefabPath.Split('/').Contains("..") || !Guid.TryParse(e.assetGuid, out _))
                        throw new InvalidOperationException("Invalid Pedestrian descriptor " + e.name);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefabPath);
                    if (prefab == null)
                        throw new InvalidOperationException("Invalid Pedestrian prefab " + e.prefabPath);
                    return new BuiltinPedestrianCatalog.Entry { assetGuid = e.assetGuid, name = e.name, prefabPath = e.prefabPath, assetFormat = e.assetFormat, prefab = prefab };
                }).ToArray() : Array.Empty<BuiltinPedestrianCatalog.Entry>();
            if (entries.Select(e => e.assetGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length ||
                entries.Select(e => e.name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
                throw new InvalidOperationException("Duplicate built-in Pedestrian name or GUID");
            const string path = "Assets/Resources/BuiltinPedestrians.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinPedestrianCatalog>(path);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuiltinPedestrianCatalog>(); AssetDatabase.CreateAsset(catalog, path); }
            if (!catalog.pedestrians.Select(e => (e.assetGuid, e.name, e.prefabPath, e.assetFormat, e.prefab))
                .SequenceEqual(entries.Select(e => (e.assetGuid, e.name, e.prefabPath, e.assetFormat, e.prefab))))
            { catalog.pedestrians = entries; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); }
            Debug.Log($"Indexed {entries.Length} built-in Pedestrians");
        }
    }
}
