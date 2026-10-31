using System;
using System.IO;
using System.Linq;
using Simulator.Web;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Simulator.Sensors;

namespace Simulator.Editor
{
    public class BuiltinSensorBuild : IPreprocessBuildWithReport
    {
        [Serializable]
        public class Descriptor
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public bool ready, preferred;
        }
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Refresh();

        [MenuItem("Simulator/Refresh Built-in Sensors")]
        public static void Refresh()
        {
            var entries = Directory.Exists("Assets/Sensors") ? Directory.GetFiles("Assets/Sensors", "sensor.json", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => JsonUtility.FromJson<Descriptor>(File.ReadAllText(p)))
                .Where(e => e.ready && BuiltinSelection.Includes("sensors", e.assetGuid)).Select(e => {
                    if (string.IsNullOrEmpty(e.prefabPath) || !e.prefabPath.StartsWith("Assets/Sensors/", StringComparison.Ordinal) ||
                        e.prefabPath.Split('/').Contains("..") || !Guid.TryParse(e.assetGuid, out _))
                        throw new InvalidOperationException("Invalid Sensor descriptor " + e.name);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.prefabPath);
                    if (prefab == null || prefab.GetComponent<SensorBase>() == null)
                        throw new InvalidOperationException("Invalid Sensor prefab " + e.prefabPath);
                    return new BuiltinSensorCatalog.Entry { assetGuid = e.assetGuid, name = e.name, prefabPath = e.prefabPath, assetFormat = e.assetFormat, prefab = prefab, preferred = e.preferred };
                }).ToArray() : Array.Empty<BuiltinSensorCatalog.Entry>();
            if (entries.Select(e => e.assetGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
                throw new InvalidOperationException("Duplicate built-in Sensor name or GUID");
            const string path = "Assets/Resources/BuiltinSensors.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinSensorCatalog>(path);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuiltinSensorCatalog>(); AssetDatabase.CreateAsset(catalog, path); }
            if (!catalog.sensors.Select(e => (e.assetGuid, e.name, e.prefabPath, e.assetFormat, e.prefab, e.preferred))
                .SequenceEqual(entries.Select(e => (e.assetGuid, e.name, e.prefabPath, e.assetFormat, e.prefab, e.preferred))))
            { catalog.sensors = entries; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); }
            Debug.Log($"Indexed {entries.Length} built-in Sensors");
        }
    }
}
