using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Simulator.Bridge;
using Simulator.Sensors;
using Simulator.Utilities;
using UnityEngine;

namespace Simulator.Web
{
    /// <summary>Recovered sensors retain package GUIDs and isolated assemblies for every version.</summary>
    public class BuiltinSensorCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public GameObject prefab;
            public bool preferred;
        }
        public Entry[] sensors = Array.Empty<Entry>();
        static readonly HashSet<(Type, BridgePlugin)> registrations = new HashSet<(Type, BridgePlugin)>();

        public static bool Contains(string assetGuid)
        {
            var catalog = Resources.Load<BuiltinSensorCatalog>("BuiltinSensors");
            return catalog != null && catalog.sensors.Any(e => e.assetGuid == assetGuid);
        }

        /// <summary>A configuration names a sensor type by class ("ImuSensor") or by [SensorType] name ("IMU").</summary>
        public static bool MatchesType(SensorBase sensor, string type) =>
            sensor.GetType().Name == type || sensor.GetType().GetCustomAttribute<SensorType>()?.Name == type;

        /// <summary>
        /// The built-in sensor for a configuration entry without a plugin assetGuid: the version of that type marked
        /// preferred in its sensor.json, otherwise the first one, so the choice does not depend on what loaded before.
        /// </summary>
        public static string FindAssetGuid(string type)
        {
            var catalog = Resources.Load<BuiltinSensorCatalog>("BuiltinSensors");
            return catalog?.sensors
                .Where(e => e.prefab != null && e.prefab.GetComponent<SensorBase>() is SensorBase sensor && MatchesType(sensor, type))
                .OrderBy(e => e.preferred ? 0 : 1)
                .Select(e => e.assetGuid)
                .FirstOrDefault();
        }

        public static bool TryRegister(string assetGuid)
        {
            var catalog = Resources.Load<BuiltinSensorCatalog>("BuiltinSensors");
            var entry = catalog?.sensors.FirstOrDefault(e => e.assetGuid == assetGuid);
            if (entry == null) return false;
            var sensor = entry.prefab != null ? entry.prefab.GetComponent<SensorBase>() : null;
            if (sensor == null) throw new InvalidOperationException("Missing sensor component: " + assetGuid);
            // Register again when a different bridge instance is loaded after the sensor.
            foreach (var type in sensor.GetType().Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ISensorBridgePlugin).IsAssignableFrom(t)))
                foreach (var bridge in BridgePlugins.All.Values)
                    if (!registrations.Contains((type, bridge)))
                    {
                        ((ISensorBridgePlugin)Activator.CreateInstance(type)).Register(bridge);
                        registrations.Add((type, bridge));
                    }
            if (Config.SensorTypeLookup.ContainsKey(assetGuid)) return true;
            var config = SensorTypes.GetConfig(sensor);
            config.AssetGuid = assetGuid;
            Config.Sensors.Add(config);
            Config.SensorPrefabs.Add(sensor);
            Config.SensorTypeLookup.Add(assetGuid, sensor);
            Config.LoadedAssets.Add(new Manifest { assetGuid = assetGuid, assetName = entry.name, assetType = "sensor", assetFormat = entry.assetFormat });
            return true;
        }
    }
}
