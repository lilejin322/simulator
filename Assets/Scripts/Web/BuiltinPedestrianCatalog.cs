using System;
using UnityEngine;

namespace Simulator.Web
{
    /// <summary>Recovered Pedestrian prefabs included in the player, registered before external bundles.</summary>
    public class BuiltinPedestrianCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public GameObject prefab;
        }
        public Entry[] pedestrians = Array.Empty<Entry>();

        public static void Register()
        {
            var catalog = Resources.Load<BuiltinPedestrianCatalog>("BuiltinPedestrians");
            if (catalog == null) return;
            foreach (var entry in catalog.pedestrians)
            {
                if (Config.LoadedAssets.Exists(m => m.assetGuid == entry.assetGuid ||
                    (m.assetType == "pedestrian" && m.assetName == entry.name))) continue;
                if (entry.prefab == null || string.IsNullOrEmpty(entry.name) || !Guid.TryParse(entry.assetGuid, out _))
                    throw new InvalidOperationException("Invalid built-in Pedestrian " + entry.name);
                Config.Pedestrians.Add(entry.name, new Config.PedAssetData {
                    Prefab = entry.prefab, Name = entry.name, AssetGuid = entry.assetGuid
                });
                Config.LoadedAssets.Add(new Manifest {
                    assetGuid = entry.assetGuid, assetName = entry.name, assetType = "pedestrian", assetFormat = entry.assetFormat
                });
            }
        }
    }
}
