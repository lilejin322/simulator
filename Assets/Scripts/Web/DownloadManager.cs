/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */
using System;
using System.IO;
using System.Runtime.InteropServices;
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

            var assetService = new AssetService();
            var found = assetService.Get(assetGuid);
            if (found != null)
            {
                if (File.Exists(found.LocalPath))
                {
                    progressCallback?.Report(new Tuple<string, float>(name, 100));
                    return Task.FromResult(found);
                }

                Debug.Log($"removing stale entry from assetService due to missing file: {found.LocalPath}");
                assetService.Delete(assetGuid);
            }

            var source = LocalAssetLibrary.GetBundlePath(assetGuid);
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

        [DllImport("libc", SetLastError = true)]
        static extern int symlink(string target, string linkPath);

        static void LinkOrCopy(string source, string destination)
        {
            // File.Delete removes a stale symlink without following it
            File.Delete(destination);

            if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
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
