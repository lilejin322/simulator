using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Simulator.Web;
using UnityEditor;
using UnityEngine;

namespace Simulator.Editor
{
    public static class BuiltinVehicleBuild
    {
        [Serializable]
        public class Descriptor
        {
            public string id, assetGuid, name, prefabPath, fmuName, assetFormat;
            public bool ready;
            public BuiltinVehicleCatalog.RuntimeOverrides runtimeOverrides;
        }

        static (string, string, string, string, GameObject, string, string) Signature(BuiltinVehicleCatalog.Entry vehicle) =>
            (vehicle.id, vehicle.assetGuid, vehicle.name, vehicle.prefabPath, vehicle.prefab, vehicle.fmuLibraryPath,
                (vehicle.runtimeOverrides == null || (vehicle.runtimeOverrides.wheelColliderVerticalOffset == 0f && !vehicle.runtimeOverrides.overrideCenterOfMass))
                    ? "" : JsonUtility.ToJson(vehicle.runtimeOverrides));

        const string CatalogPath = "Assets/Resources/BuiltinVehicles.asset";

        [MenuItem("Simulator/Refresh Built-in Vehicles")]
        public static void Refresh()
        {
            var vehicles = new List<BuiltinVehicleCatalog.Entry>();
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists("Assets/Vehicles"))
            {
                foreach (var path in Directory.GetFiles("Assets/Vehicles", "vehicle.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var entry = JsonUtility.FromJson<Descriptor>(File.ReadAllText(path));
                    if (!entry.ready) continue;
                    if (!BuiltinSelection.Includes("vehicles", entry.assetGuid)) continue;
                    // A recovered prefab is imported against current project sources, independently of its original ZIP format.
                    string fmuLibraryPath = null;
                    if (!string.IsNullOrEmpty(entry.fmuName))
                    {
                        var linux = EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64;
                        var windows = EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64;
                        if (!linux && !windows)
                        {
                            if (BuiltinSelection.Active) throw new InvalidOperationException($"Selected FMU vehicle {entry.name} does not support the active build target");
                            continue;
                        }
                        fmuLibraryPath = Path.Combine(Path.GetDirectoryName(path), entry.fmuName + (linux ? "_linux.so" : "_windows.dll")).Replace('\\', '/');
                        if (!File.Exists(fmuLibraryPath))
                        {
                            if (BuiltinSelection.Active) throw new FileNotFoundException($"Selected FMU vehicle {entry.name} requires {fmuLibraryPath}");
                            continue;
                        }
                    }
                    var prefab = !string.IsNullOrEmpty(entry.prefabPath) && entry.prefabPath.StartsWith("Assets/Vehicles/", StringComparison.Ordinal) &&
                        !entry.prefabPath.Split('/').Contains("..") ? AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath) : null;
                    if (!Guid.TryParse(entry.assetGuid, out _) || prefab == null || !guids.Add(entry.assetGuid))
                        throw new InvalidOperationException($"Invalid or duplicate built-in vehicle descriptor {path}");
                    var repair = entry.runtimeOverrides;
                    if (repair != null)
                    {
                        var values = new[] { repair.wheelColliderVerticalOffset, repair.centerOfMass.x, repair.centerOfMass.y, repair.centerOfMass.z };
                        if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)) ||
                            (repair.overrideCenterOfMass && prefab.GetComponent<Rigidbody>() == null) ||
                            (repair.wheelColliderVerticalOffset != 0f && prefab.GetComponentsInChildren<WheelCollider>(true).Length == 0))
                            throw new InvalidOperationException($"Invalid runtime overrides in {path}");
                    }
                    vehicles.Add(new BuiltinVehicleCatalog.Entry { id = entry.id, assetGuid = entry.assetGuid, name = entry.name, prefabPath = entry.prefabPath, prefab = prefab, fmuLibraryPath = fmuLibraryPath, runtimeOverrides = entry.runtimeOverrides });
                }
            }
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinVehicleCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<BuiltinVehicleCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            if (!catalog.vehicles.Select(Signature).SequenceEqual(vehicles.Select(Signature)))
            {
                catalog.vehicles = vehicles.ToArray();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"Indexed {vehicles.Count} built-in vehicles");
        }
    }
}
