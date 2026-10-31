using System;
using UnityEngine;

namespace Simulator.Web
{
    /// <summary>Recovered NPC prefabs included in the player, registered before external bundles.</summary>
    public class BuiltinNpcCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string assetGuid, name, prefabPath, assetFormat;
            public GameObject prefab;
        }
        public Entry[] npcs = Array.Empty<Entry>();

        public static void Register()
        {
            var catalog = Resources.Load<BuiltinNpcCatalog>("BuiltinNPCs");
            if (catalog == null) return;
            foreach (var entry in catalog.npcs)
            {
                if (Config.LoadedAssets.Exists(m => m.assetGuid == entry.assetGuid ||
                    (m.assetType == "npc" && m.assetName == entry.name))) continue;
                var meta = entry.prefab != null ? entry.prefab.GetComponent<NPCMetaData>() : null;
                if (meta == null || string.IsNullOrEmpty(entry.name) || !Guid.TryParse(entry.assetGuid, out _))
                    throw new InvalidOperationException("Invalid built-in NPC " + entry.name);
                Config.NPCVehicles.Add(entry.name, new Config.NPCAssetData {
                    Prefab = entry.prefab, Name = entry.name, AssetGuid = entry.assetGuid, NPCType = meta.SizeType
                });
                Config.LoadedAssets.Add(new Manifest {
                    assetGuid = entry.assetGuid, assetName = entry.name, assetType = "npc", assetFormat = entry.assetFormat
                });
            }
        }
    }
}
