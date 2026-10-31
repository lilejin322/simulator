using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Simulator.PointCloud.Trees;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Simulator.Web
{
    /// <summary>Loads the map scenes listed in BuiltinMaps.asset, generated from Assets/Maps/*/map.json.</summary>
    public static class BuiltinMap
    {
        static Dictionary<string, BuiltinMapCatalog.Entry> entries;

        public static bool TryGetScenePath(string assetGuid, out string scenePath)
        {
            scenePath = null;
            if (string.IsNullOrEmpty(assetGuid)) return false;
            if (entries == null)
            {
                entries = new Dictionary<string, BuiltinMapCatalog.Entry>(StringComparer.OrdinalIgnoreCase);
                var catalog = Resources.Load<BuiltinMapCatalog>("BuiltinMaps");
                foreach (var map in catalog != null ? catalog.maps : Array.Empty<BuiltinMapCatalog.Entry>())
                {
                    if (string.IsNullOrEmpty(map.assetGuid) || string.IsNullOrEmpty(map.scenePath) || entries.ContainsKey(map.assetGuid))
                        throw new InvalidOperationException("Invalid or duplicate built-in map in BuiltinMaps.asset");
                    entries.Add(map.assetGuid, map);
                }
            }
            if (!entries.TryGetValue(assetGuid, out var entry)) return false;
#if UNITY_EDITOR
            bool available = System.IO.File.Exists(entry.scenePath);
#else
            bool available = Application.CanStreamedLevelBeLoaded(entry.scenePath);
#endif
            if (!available)
                throw new InvalidOperationException($"Built-in map {entry.name} is missing; rebuild the simulator with {entry.scenePath}.");
            scenePath = entry.scenePath;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>Keep each map's raw data beside its descriptor in the source project.</summary>
        public static string GetPointCloudSourceDirectory(string scenePath, string assetGuid)
        {
            var directory = Path.GetDirectoryName(scenePath);
            while (!string.IsNullOrEmpty(directory) && directory.Replace('\\', '/').StartsWith("Assets/Maps/", StringComparison.Ordinal))
            {
                var descriptor = Path.Combine(directory, "map.json");
                if (File.Exists(descriptor))
                {
                    var entry = JsonUtility.FromJson<BuiltinMapCatalog.Entry>(File.ReadAllText(descriptor));
                    if (!string.Equals(entry.assetGuid, assetGuid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Map descriptor does not match {assetGuid}: {descriptor}");
                    return Path.GetFullPath(Path.Combine(directory, "PointCloud"));
                }
                directory = Path.GetDirectoryName(directory);
            }
            throw new FileNotFoundException($"No map descriptor for scene {scenePath}");
        }
#endif

        /// <summary>Resolve original point-cloud paths without rewriting the recovered scene file.</summary>
        public static void PreparePointCloudData(Scene scene, string assetGuid)
        {
#if UNITY_EDITOR
            var root = GetPointCloudSourceDirectory(scene.path, assetGuid);
#else
            var root = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Maps", assetGuid));
#endif
            foreach (var loader in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NodeTreeLoader>(true)))
            {
                var originalPath = loader.GetDataPath();
                var directory = originalPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    ? originalPath
                    : Path.Combine(root, "pointcloud_" + Simulator.Utilities.Utility.StringToGUID(originalPath));
                if (!File.Exists(Path.Combine(directory, "index.pcindex")))
                    throw new FileNotFoundException($"Point-cloud data for map {assetGuid} is missing", directory);
                if (!string.Equals(originalPath, directory, StringComparison.Ordinal))
                    loader.UpdateData(directory);
                if (loader.Tree == null)
                    throw new InvalidOperationException($"Unable to load recovered point-cloud tree {directory}");
            }
        }

        public static AsyncOperation LoadSceneAsync(string scenePath, LoadSceneMode mode)
        {
            Debug.Log($"Loading built-in map scene {scenePath}");
            var entry = entries.Values.Single(e => e.scenePath == scenePath);
#if UNITY_EDITOR
            var operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath, new LoadSceneParameters(mode));
#else
            var operation = SceneManager.LoadSceneAsync(scenePath, mode);
#endif
            operation.completed += _ => PreparePointCloudData(SceneManager.GetSceneByPath(scenePath), entry.assetGuid);
            return operation;
        }
    }
}
