/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */
using System;
using System.Threading.Tasks;
using UnityEngine.Assertions;
using Simulator.Database;

namespace Simulator.Web
{
    /// <summary>
    /// Resolves the assets a simulation asks for to the ones built into the simulator; WISE downloaded them before.
    /// The returned <see cref="AssetModel.LocalPath"/> is what the loaders take: a map's scene path, or a
    /// "builtin-vehicle:", "builtin-bridge:" or "builtin-sensor:" reference.
    /// </summary>
    public static class DownloadManager
    {
        public static Task<AssetModel> GetAsset(BundleConfig.BundleTypes type, string assetGuid, string name = null,
            IProgress<Tuple<string, float>> progressCallback = null)
        {
            Assert.IsNotNull(assetGuid, $"{nameof(assetGuid)} must not be null when trying to get ${BundleConfig.singularOf(type)} asset.");

            var typeString = BundleConfig.singularOf(type);
            string localPath = null;
            switch (type)
            {
                case BundleConfig.BundleTypes.Environment:
                    BuiltinMap.TryGetScenePath(assetGuid, out localPath);
                    break;
                case BundleConfig.BundleTypes.Vehicle:
                    BuiltinVehicleCatalog.TryGetPath(assetGuid, out localPath);
                    break;
                case BundleConfig.BundleTypes.Bridge:
                    localPath = BuiltinBridgeCatalog.Contains(assetGuid) ? "builtin-bridge:" + assetGuid : null;
                    break;
                case BundleConfig.BundleTypes.Sensor:
                    localPath = BuiltinSensorCatalog.Contains(assetGuid) ? "builtin-sensor:" + assetGuid : null;
                    break;
            }

            name = name ?? typeString;
            if (localPath == null)
            {
                return Task.FromException<AssetModel>(new Exception($"{typeString} '{name}' ({assetGuid}) is not built into the simulator"));
            }

            progressCallback?.Report(new Tuple<string, float>(name, 100));
            return Task.FromResult(new AssetModel { AssetGuid = assetGuid, Type = typeString, Name = name, LocalPath = localPath });
        }

        public static void StopAssetDownload(string assetGuid)
        {
            // Built-in assets are there immediately, there is nothing to cancel.
        }
    }
}
