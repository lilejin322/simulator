/**
 * Copyright (c) 2020 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System.Collections;
using Simulator;
using Simulator.Web;
using UnityEngine;

/// <summary>
/// Starts simulations on this instance. WISE used to send simulation configurations over a cloud connection;
/// without it an API Only simulation is started as soon as the simulator is ready (disable with
/// start_api_only: false in config.yml or --no-api-only), and Python API clients load maps and vehicles
/// from the <see cref="LocalAssetLibrary"/>.
/// </summary>
public class ConnectionManager : MonoBehaviour
{
    public static ConnectionManager instance;

    private void Start()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(this);
        instance = this;

        if (Config.StartApiOnly)
        {
            StartCoroutine(StartApiOnlyWhenReady());
        }
    }

    IEnumerator StartApiOnlyWhenReady()
    {
        while (Loader.Instance == null || ConnectionUI.instance == null)
        {
            yield return null;
        }

        // No Python API without an asset library: the loader screen asks for a folder, and in batch mode, where
        // nobody can choose one, the simulator quits instead of running without the API.
        if (!LocalAssetLibrary.Exists)
        {
            if (Application.isBatchMode)
            {
                Debug.LogError($"Quitting: the Python API needs the asset library {Config.AssetsPath}, which does not exist");
                Application.Quit(1);
            }
            yield break;
        }

        if (Loader.Instance.Status == SimulatorStatus.Idle)
        {
            Debug.Log($"Starting API Only simulation, Python API listens on {Config.ApiHost}:{Config.ApiPort}");
            Loader.Instance.StartSimulation(SimulationConfigUtils.CreateApiOnlySimulation());
        }
    }
}
