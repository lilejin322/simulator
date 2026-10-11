/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;
using Simulator.Database.Services;
using Simulator.Database;

namespace Simulator.Web
{
    /// <summary>
    /// Makes asset bundles from the <see cref="LocalAssetLibrary"/> available at the cache path the simulator loads
    /// them from (<see cref="WebUtilities.GenerateLocalPath"/>). Bundles are linked, not copied, so clearing the cache
    /// never touches the library.
    /// </summary>
    public static class DownloadManager
    {
        public static Task<AssetModel> GetAsset(BundleConfig.BundleTypes type, string assetGuid, string name = null,
            IProgress<Tuple<string, float>> progressCallback = null)
        {
            Assert.IsNotNull(assetGuid, $"{nameof(assetGuid)} must not be null when trying to get ${BundleConfig.singularOf(type)} asset.");

            var typeString = BundleConfig.singularOf(type);
            if (name == null) name = typeString;

            var source = LocalAssetLibrary.GetBundlePath(assetGuid);
            var assetService = new AssetService();
            var found = assetService.Get(assetGuid);
            if (found != null)
            {
                // The library is the only source: reuse the cache only if it still is the library's current bundle
                if (source != null && IsCopyOf(found.LocalPath, source))
                {
                    progressCallback?.Report(new Tuple<string, float>(name, 100));
                    return Task.FromResult(found);
                }

                Debug.Log($"removing stale entry from assetService, {found.LocalPath} is not {source ?? "in the local asset library"}");
                assetService.Delete(assetGuid);
            }

            if (source == null)
            {
                return Task.FromException<AssetModel>(new Exception($"{typeString} '{name}' ({assetGuid}) is not in the local asset library {LocalAssetLibrary.Root}"));
            }

            string localPath = WebUtilities.GenerateLocalPath(assetGuid, type);
            try
            {
                LinkOrCopy(source, localPath);
                var model = new AssetModel()
                {
                    AssetGuid = assetGuid,
                    Type = typeString,
                    Name = name,
                    LocalPath = localPath,
                    DateAdded = DateTime.UtcNow.ToString()
                };
                assetService.Add(model);
                Debug.Log($"{name} loaded from local asset library {source}");
                progressCallback?.Report(new Tuple<string, float>(name, 100));
                return Task.FromResult(model);
            }
            catch (Exception e)
            {
                return Task.FromException<AssetModel>(e);
            }
        }

        public static void StopAssetDownload(string assetGuid)
        {
            // Assets come from the local library immediately, there is nothing to cancel.
        }

        /// <summary>
        /// Forgets cached assets that do not come from the current library: files that cannot be opened anymore and
        /// links into another folder, e.g. after assets_path changed or the library moved. Everything listing the cache
        /// (like the Scenario Editor) then only sees library assets, which GetAsset links again on demand.
        /// </summary>
        public static void PruneCache()
        {
            var assetService = new AssetService();
            foreach (BundleConfig.BundleTypes type in Enum.GetValues(typeof(BundleConfig.BundleTypes)))
            {
                foreach (var asset in assetService.List(type).ToList())
                {
                    var target = LinkTarget(asset.LocalPath);
                    if (CanOpen(asset.LocalPath) && (target == null || IsUnder(target, Config.AssetsPath)))
                    {
                        continue;
                    }

                    Debug.Log($"removing stale entry from assetService, {asset.LocalPath} is missing or not from {Config.AssetsPath}");
                    assetService.Delete(asset.AssetGuid);
                    try
                    {
                        File.Delete(asset.LocalPath);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        Debug.LogWarning($"Could not delete {asset.LocalPath}: {e.Message}");
                    }
                }
            }
        }

        // Mono's File.Exists is true for a dangling symlink, e.g. after the library folder was moved
        static bool CanOpen(string path)
        {
            try
            {
                using (File.OpenRead(path))
                {
                    return true;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // Whether the cached file is the library's bundle: a link to it, or a copy of it where links are not available
        static bool IsCopyOf(string cached, string source)
        {
            if (!CanOpen(cached))
            {
                return false;
            }

            var target = LinkTarget(cached);
            if (target != null)
            {
                return string.Equals(Path.GetFullPath(target), Path.GetFullPath(source), StringComparison.Ordinal);
            }
            return new FileInfo(cached).Length == new FileInfo(source).Length;
        }

        static bool IsUnder(string path, string folder)
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal);
        }

        static bool LinksSupported =>
            Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;

        // Target of a symlink, or null when the path is not one
        static string LinkTarget(string path)
        {
            if (!LinksSupported)
            {
                return null;
            }

            try
            {
                var buffer = new byte[4096];
                var length = readlink(path, buffer, (UIntPtr)buffer.Length).ToInt64();
                return length > 0 ? Encoding.UTF8.GetString(buffer, 0, (int)length) : null;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return null;
            }
        }

        [DllImport("libc", SetLastError = true)]
        static extern int symlink(string target, string linkPath);

        [DllImport("libc", SetLastError = true)]
        static extern IntPtr readlink(string path, byte[] buffer, UIntPtr size);

        static void LinkOrCopy(string source, string destination)
        {
            // File.Delete removes a stale symlink without following it
            File.Delete(destination);

            if (LinksSupported)
            {
                try
                {
                    if (symlink(source, destination) == 0)
                    {
                        return;
                    }
                    Debug.LogWarning($"Could not link {destination} to {source} (errno {Marshal.GetLastWin32Error()}), copying instead");
                }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
                {
                    Debug.LogWarning($"Could not link {destination} to {source} ({e.Message}), copying instead");
                }
            }

            File.Copy(source, destination);
        }
    }
}
