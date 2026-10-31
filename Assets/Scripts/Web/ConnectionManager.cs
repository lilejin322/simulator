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
/// built into the simulator (<see cref="BuiltinAssetLibrary"/>).
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

        // No Python API without built-in assets, e.g. in a player built without the asset sources: the loader screen
        // says so, and in batch mode the simulator quits instead of running without the API.
        if (!BuiltinAssetLibrary.HasAssets)
        {
            if (Application.isBatchMode)
            {
                Debug.LogError("Quitting: the Python API needs assets, but none are built into this simulator");
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
