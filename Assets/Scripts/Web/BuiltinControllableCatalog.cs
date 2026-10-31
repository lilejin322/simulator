using System;
using UnityEngine;
using System.Collections.Generic;
using Simulator.Controllable;

namespace Simulator.Web
{
    /// <summary>Recovered Controllable prefabs included in the player, registered before external bundles.</summary>
    public class BuiltinControllableCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public GameObject prefab;
            public GameObject[] additionalPrefabs = Array.Empty<GameObject>();
        }
        public Entry[] controllables = Array.Empty<Entry>();

        public static void Register()
        {
            var catalog = Resources.Load<BuiltinControllableCatalog>("BuiltinControllables");
            if (catalog == null) return;
            foreach (var entry in catalog.controllables)
            {
                if (Config.LoadedAssets.Exists(m => m.assetGuid == entry.assetGuid ||
                    (m.assetType == "controllable" && m.assetName == entry.name))) continue;
                if (entry.prefab == null || string.IsNullOrEmpty(entry.name) || !Guid.TryParse(entry.assetGuid, out _))
                    throw new InvalidOperationException("Invalid built-in Controllable " + entry.name);
                var component = entry.prefab.GetComponent<IControllable>();
                if (component == null) throw new InvalidOperationException("Missing IControllable on " + entry.name);
                Config.Controllables.Add(entry.name, component);
                Config.ControllableAssets.Add(entry.name, new List<GameObject>(entry.additionalPrefabs));
                Config.LoadedAssets.Add(new Manifest {
                    assetGuid = entry.assetGuid, assetName = entry.name, assetType = "controllable", assetFormat = entry.assetFormat
                });
            }
        }
    }
}
