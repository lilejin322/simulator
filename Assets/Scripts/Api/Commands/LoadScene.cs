/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System.Collections;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using SimpleJSON;
using System.Threading.Tasks;
using Simulator.Web;

namespace Simulator.Api.Commands
{
    class LoadScene : IDistributedCommand, ILockingCommand
    {
        public string Name => "simulator/load_scene";

        public string LockingGuid { get; set; }

        public float StartRealtime { get; set; }

        public event Action<ILockingCommand> Executed;

        private async Task LoadMap(JSONNode args, string userMapId, int? seed = null)
        {
            var api = ApiManager.Instance;
            MapDetailData mapData = await BuiltinAssetLibrary.GetByIdOrName<MapDetailData>(userMapId);
            var ret = await DownloadManager.GetAsset(BundleConfig.BundleTypes.Environment, mapData.AssetGuid, mapData.Name);
            api.StartCoroutine(LoadMapAssets(this, mapData, ret.LocalPath, userMapId, seed));
        }

        static IEnumerator LoadMapAssets(LoadScene sourceCommand, MapDetailData map, string scenePath, string userMapId, int? seed = null)
        {
            var api = ApiManager.Instance;

            var loader = BuiltinMap.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            yield return new WaitUntil(() => loader.isDone);

            if (Loader.Instance.SimConfig != null)
            {
                Loader.Instance.SimConfig.Seed = seed;
                Loader.Instance.SimConfig.MapName = map.Name;
                Loader.Instance.SimConfig.MapAssetGuid = map.AssetGuid;
                // AnalysisManager records agents added through the API in SimConfig.Agents; they belong to the
                // previous scene and SimulatorManager.Init must not respawn them before the scene is set up
                Loader.Instance.SimConfig.Agents = Array.Empty<AgentConfig>();
            }

            var sim = Loader.Instance.CreateSimulatorManager();
            sim.Init(seed);

            if (Loader.Instance.CurrentSimulation != null)
            {
                Loader.Instance.reportStatus(SimulatorStatus.Running);
            }

            var resetTask = api.Reset();
            while (!resetTask.IsCompleted)
                yield return null;
            api.CurrentSceneId = map.Id;
            api.CurrentSceneName = map.Name;
            api.CurrentScene = userMapId;
            sourceCommand.Executed?.Invoke(sourceCommand);
            api.SendResult(sourceCommand);
        }

        public async void Execute(JSONNode args)
        {
            var api = ApiManager.Instance;
            var mapId = args["scene"].Value;
            int? seed = null;
            if (!args["seed"].IsNull)
            {
                seed = args["seed"].AsInt;
            }
            try
            {
                await LoadMap(args, mapId, seed);
            }
            catch (Exception e)
            {
                api.SendError(this, e.Message);
                // only unlock in error case as map loading continues in coroutine after which we unlock
                Executed?.Invoke(this);
            }
        }
    }
}
