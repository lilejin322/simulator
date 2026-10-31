using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Simulator.Web;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Simulator.Editor
{
    public static class BuiltinMapBuild
    {
        internal const string CatalogPath = "Assets/Resources/BuiltinMaps.asset";

        [Serializable]
        public class Descriptor
        {
            public string assetGuid, name, scenePath;
            public bool ready;
        }

        [InitializeOnLoadMethod]
        static void RegisterPointCloudPaths()
        {
            EditorSceneManager.sceneOpened += (scene, mode) =>
            {
                // A player build opens every map scene and keeps changes made here: leave the stored point-cloud
                // paths for the player to resolve against its own StreamingAssets.
                if (BuildPipeline.isBuildingPlayer) return;
                if (!scene.path.StartsWith("Assets/Maps/", StringComparison.Ordinal)) return;
                var directory = Path.GetDirectoryName(scene.path);
                while (!string.IsNullOrEmpty(directory) && directory.Replace('\\', '/').StartsWith("Assets/Maps/", StringComparison.Ordinal))
                {
                    var descriptor = Path.Combine(directory, "map.json");
                    if (File.Exists(descriptor))
                    {
                        var entry = JsonUtility.FromJson<BuiltinMapCatalog.Entry>(File.ReadAllText(descriptor));
                        try { BuiltinMap.PreparePointCloudData(scene, entry.assetGuid); }
                        catch (Exception ex) { Debug.LogWarning($"Point-cloud path recovery for {entry.name}: {ex.Message}"); }
                        return;
                    }
                    directory = Path.GetDirectoryName(directory);
                }
            };
        }

        /// <summary>Only the built player needs a StreamingAssets copy of raw map data.</summary>
        internal static int CopyPointCloudData(IEnumerable<BuiltinMapCatalog.Entry> maps, string streamingAssetsPath)
        {
            var count = 0;
            foreach (var map in maps)
            {
                var source = BuiltinMap.GetPointCloudSourceDirectory(map.scenePath, map.assetGuid);
                if (!Directory.Exists(source)) continue;
                foreach (var tree in Directory.GetDirectories(source))
                    if (!File.Exists(Path.Combine(tree, "index.pcindex")))
                        throw new BuildFailedException($"Point-cloud index is missing: {tree}");
                foreach (var path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    var relative = path.Substring(source.Length + 1);
                    var target = Path.Combine(streamingAssetsPath, "Maps", map.assetGuid, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(path, target, true);
                    count++;
                }
            }
            return count;
        }

        [MenuItem("Simulator/Refresh Built-in Maps")]
        public static void Refresh()
        {
            GetBuildScenes();
        }

        public static string[] GetBuildScenes()
        {
            var maps = new List<BuiltinMapCatalog.Entry>();
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists("Assets/Maps"))
            {
                foreach (var path in Directory.GetFiles("Assets/Maps", "map.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var descriptor = JsonUtility.FromJson<Descriptor>(File.ReadAllText(path));
                    if (!descriptor.ready) continue;
                    if (!BuiltinSelection.Includes("maps", descriptor.assetGuid)) continue;
                    if (string.IsNullOrEmpty(descriptor.assetGuid) || !Guid.TryParse(descriptor.assetGuid, out _) ||
                        string.IsNullOrEmpty(descriptor.scenePath) || !descriptor.scenePath.StartsWith("Assets/Maps/", StringComparison.Ordinal) ||
                        descriptor.scenePath.Split('/').Contains("..") || !File.Exists(descriptor.scenePath) ||
                        !guids.Add(descriptor.assetGuid) || !scenePaths.Add(descriptor.scenePath))
                        throw new InvalidOperationException($"Invalid or duplicate built-in map descriptor {path}");
                    maps.Add(new BuiltinMapCatalog.Entry { assetGuid = descriptor.assetGuid, name = descriptor.name, scenePath = descriptor.scenePath });
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinMapCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<BuiltinMapCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            if (!catalog.maps.Select(e => (e.assetGuid, e.name, e.scenePath)).SequenceEqual(maps.Select(e => (e.assetGuid, e.name, e.scenePath))))
            { catalog.maps = maps.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); }
            // The scenes go to the player build only: the editor loads built-in maps by path, and Build Settings stay
            // the same for every set of installed maps.
            var scenes = new[] { "Assets/Scenes/LoaderScene.unity", "Assets/Scenes/ScenarioEditor.unity" }
                .Concat(maps.Select(m => m.scenePath)).ToArray();
            Debug.Log($"Indexed {maps.Count} built-in maps");
            return scenes;
        }
    }

    public sealed class BuiltinMapPointCloudBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            var output = Path.GetFullPath(report.summary.outputPath);
            string data;
            switch (report.summary.platform)
            {
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    data = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_Data");
                    break;
                case BuildTarget.StandaloneOSX:
                    data = Path.Combine(output, "Contents", "Resources", "Data");
                    break;
                default:
                    throw new BuildFailedException($"Unsupported point-cloud build platform {report.summary.platform}");
            }
            var catalog = AssetDatabase.LoadAssetAtPath<BuiltinMapCatalog>(BuiltinMapBuild.CatalogPath);
            if (catalog == null)
                throw new BuildFailedException($"{BuiltinMapBuild.CatalogPath} is missing");
            // build output only: drop the data of maps an earlier build into this folder included
            var maps = Path.Combine(data, "StreamingAssets", "Maps");
            if (Directory.Exists(maps))
                Directory.Delete(maps, true);
            var count = BuiltinMapBuild.CopyPointCloudData(catalog.maps, Path.Combine(data, "StreamingAssets"));
            Debug.Log($"Copied {count} raw point-cloud files from map source directories into the player");
            // Unity signs the app before this callback. Adding StreamingAssets changes
            // its resource seal, so sign again after the point-cloud copy is complete.
            if (report.summary.platform == BuildTarget.StandaloneOSX)
            {
                var quotedOutput = "\"" + output.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/codesign",
                    Arguments = "--force --sign - --preserve-metadata=identifier,entitlements,flags,runtime --timestamp=none " + quotedOutput,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new BuildFailedException("macOS player signing failed: " + error);
                }
            }
        }
    }
}
