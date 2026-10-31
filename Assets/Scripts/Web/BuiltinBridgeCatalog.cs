using System;
using System.Collections.Generic;
using System.Linq;
using Simulator.Bridge;
using UnityEngine;

namespace Simulator.Web
{
    public class BuiltinBridgeCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string assetGuid, name, assetFormat, factoryType;
        }
        public Entry[] bridges = Array.Empty<Entry>();
        static readonly Dictionary<string, BridgePlugin> instances = new Dictionary<string, BridgePlugin>();

        public static bool Contains(string guid) => Resources.Load<BuiltinBridgeCatalog>("BuiltinBridges")?.bridges.Any(e => e.assetGuid == guid) == true;

        public static bool TryRegister(string guid) => TryRegister(guid, out _);

        /// <summary>
        /// Registers the bridge plugin with this assetGuid and returns it. Sensor configurations name their bridge for
        /// display ("ApolloCyberRT"), not by the factory's bridge name, so agents use this instance, not a name lookup.
        /// </summary>
        public static bool TryRegister(string guid, out BridgePlugin plugin)
        {
            plugin = null;
            var entry = Resources.Load<BuiltinBridgeCatalog>("BuiltinBridges")?.bridges.FirstOrDefault(e => e.assetGuid == guid);
            if (entry == null) return false;
            if (!instances.TryGetValue(guid, out plugin))
            {
                var type = Type.GetType(entry.factoryType, true);
                var factory = (IBridgeFactory)Activator.CreateInstance(type);
                plugin = new BridgePlugin(factory);
                instances.Add(guid, plugin);
            }
            // Select this GUID for the caller's subsequent name lookup. Existing agents retain
            // their own BridgePlugin references, even when another version is selected later.
            BridgePlugins.All[plugin.Name] = plugin;
            if (!Config.LoadedAssets.Any(m => m.assetGuid == guid))
                Config.LoadedAssets.Add(new Manifest { assetGuid = guid, assetName = entry.name, assetFormat = entry.assetFormat, assetType = "bridge" });
            return true;
        }
    }
}
