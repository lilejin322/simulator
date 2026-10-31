using System;
using System.Collections.Generic;
using UnityEngine;

namespace Simulator.Web
{
    /// <summary>Project vehicles discovered from Assets/Vehicles/*/vehicle.json at build time.</summary>
    public class BuiltinVehicleCatalog : ScriptableObject
    {
        /// <summary>Explicit source-vehicle repairs, applied to spawned instances; recovered prefabs stay unchanged.</summary>
        [Serializable]
        public class RuntimeOverrides
        {
            public string reason;
            public float wheelColliderVerticalOffset;
            public bool overrideCenterOfMass;
            public Vector3 centerOfMass;

            public void Apply(GameObject instance)
            {
                if (wheelColliderVerticalOffset != 0f)
                    foreach (var wheel in instance.GetComponentsInChildren<WheelCollider>(true))
                        wheel.transform.localPosition += Vector3.up * wheelColliderVerticalOffset;
                if (overrideCenterOfMass)
                {
                    instance.GetComponent<Rigidbody>().centerOfMass = centerOfMass;
                    var dynamics = instance.GetComponent<VehicleSMI>();
                    if (dynamics != null) dynamics.CenterOfMass = centerOfMass;
                }
            }
        }

        [Serializable]
        public class Entry
        {
            public string id, assetGuid, name, prefabPath, fmuLibraryPath;
            public GameObject prefab;
            public RuntimeOverrides runtimeOverrides;
        }

        /// <summary>Stands in for a bundle path; <see cref="Loader.LoadVehicle"/> resolves it to the prefab.</summary>
        public const string PathPrefix = "builtin-vehicle:";

        public Entry[] vehicles = Array.Empty<Entry>();

        static Dictionary<string, Entry> entries;

        static Dictionary<string, Entry> Entries
        {
            get
            {
                if (entries == null)
                {
                    entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                    var catalog = Resources.Load<BuiltinVehicleCatalog>("BuiltinVehicles");
                    foreach (var vehicle in catalog != null ? catalog.vehicles : Array.Empty<Entry>())
                    {
                        if (string.IsNullOrEmpty(vehicle.assetGuid) || vehicle.prefab == null || entries.ContainsKey(vehicle.assetGuid))
                            throw new InvalidOperationException("Invalid or duplicate built-in vehicle in BuiltinVehicles");
                        entries.Add(vehicle.assetGuid, vehicle);
                    }
                }
                return entries;
            }
        }

        public static bool TryGetPath(string assetGuid, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(assetGuid) || !Entries.ContainsKey(assetGuid)) return false;
            path = PathPrefix + assetGuid;
            return true;
        }

        /// <summary>Called once after instantiation and before the vehicle enters the simulation.</summary>
        public static void ApplyRuntimeOverrides(string path, GameObject instance)
        {
            if (path == null || !path.StartsWith(PathPrefix, StringComparison.Ordinal)) return;
            if (!Entries.TryGetValue(path.Substring(PathPrefix.Length), out var entry))
                throw new InvalidOperationException($"Built-in vehicle {path} is missing; rebuild the simulator.");
            entry.runtimeOverrides?.Apply(instance);
        }

        public static bool TryLoad(string path, out GameObject prefab)
        {
            prefab = null;
            if (path == null || !path.StartsWith(PathPrefix, StringComparison.Ordinal)) return false;
            if (!Entries.TryGetValue(path.Substring(PathPrefix.Length), out var entry))
                throw new InvalidOperationException($"Built-in vehicle {path} is missing; rebuild the simulator.");
            prefab = entry.prefab;
            if (!string.IsNullOrEmpty(entry.fmuLibraryPath))
            {
                var file = System.IO.Path.GetFileName(entry.fmuLibraryPath);
                var library = Application.isEditor ? System.IO.Path.GetFullPath(entry.fmuLibraryPath)
                    : System.IO.Path.Combine(Application.dataPath, "Plugins", "x86_64", file);
                if (!System.IO.File.Exists(library) && !Application.isEditor)
                    library = System.IO.Path.Combine(Application.dataPath, "Plugins", file);
                if (!System.IO.File.Exists(library) && Application.platform == RuntimePlatform.OSXPlayer)
                    library = System.IO.Path.Combine(Application.dataPath, "PlugIns", file);
                if (!System.IO.File.Exists(library))
                    throw new System.IO.FileNotFoundException($"Built-in vehicle FMU library is missing: {file}", library);
                // Like bundle loading, resolve the native path in memory before Instantiate/Awake.
                // Do not serialize an absolute machine-specific path into the recovered prefab.
                var dynamics = prefab.GetComponent<Simulator.FMU.VehicleFMU>();
                if (dynamics == null || dynamics.FMUData == null)
                    throw new InvalidOperationException($"Built-in vehicle {entry.assetGuid} has no FMU metadata.");
                dynamics.FMUData.Path = library;
            }
            return true;
        }
    }
}
