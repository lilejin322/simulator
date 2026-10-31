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
    public class BuiltinNpcBuild : IPreprocessBuildWithReport
    {
        [Serializable]
        public class Descriptor
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public bool ready;
        }
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Refresh();

        [MenuItem("Simulator/Refresh Built-in NPCs")]
        public static void Refresh()
        {
            var entries = Directory.Exists("Assets/NPCs") ? Directory.GetFiles("Assets/NPCs", "npc.json", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => JsonUtility.FromJson<Descriptor>(File.ReadAllText(p)))
                .Where(e => e.ready && BuiltinSelection.Includes("npcs", e.assetGuid)).Select(e => {
                    if (string.IsNullOrEmpty(e.prefabPath) || !e.prefabPath.StartsWith("Assets/NPCs/", StringComparison.Ordinal) ||
                        e.prefabPath.Split('/').Contains("..") || !Guid.TryParse(e.assetGuid, out _))
                        throw new InvalidOperationException("Invalid NPC descriptor " + e.name);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefabPath);
                    if (prefab == null || prefab.GetComponent<NPCMetaData>() == null)
                        throw new InvalidOperationException("Invalid NPC prefab " + e.prefabPath);
                    return new BuiltinNpcCatalog.Entry { assetGuid = e.assetGuid, name = e.name, prefabPath = e.prefabPath, assetFormat = e.assetFormat, prefab = prefab };
                }).ToArray() : Array.Empty<BuiltinNpcCatalog.Entry>();
            if (entries.Select(e => e.assetGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length ||
                entries.Select(e => e.name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
                throw new InvalidOperationException("Duplicate built-in NPC name or GUID");
            const string path = "Assets/Resources/BuiltinNPCs.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinNpcCatalog>(path);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuiltinNpcCatalog>(); AssetDatabase.CreateAsset(catalog, path); }
            if (!catalog.npcs.Select(e => (e.assetGuid, e.name, e.prefabPath, e.assetFormat, e.prefab))
                .SequenceEqual(entries.Select(e => (e.assetGuid, e.name, e.prefabPath, e.assetFormat, e.prefab))))
            { catalog.npcs = entries; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); }
            Debug.Log($"Indexed {entries.Length} built-in NPCs");
        }
    }
}
