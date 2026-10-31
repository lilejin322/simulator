/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Simulator.Api;
using Simulator.Bridge;
using Simulator.Database;
using Simulator.Database.Services;
using Simulator.Network.Core.Configs;
using Simulator.Network.Shared;
using Simulator.Utilities;
using Simulator.Web;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Simulator
{
    using System.Reflection;
    using UnityEditor;

    public class AgentConfig
    {
        public string Name;
        public uint GTID;
        public string AssetGuid;
        public string AssetBundle;
        [NonSerialized]
        public GameObject Prefab;
        [NonSerialized]
        public GameObject AgentGO;
        public BridgeData BridgeData;
        public BridgePlugin Bridge;
        public string Connection;
        public SensorData[] Sensors;
        public Vector3 Position;
        public Quaternion Rotation;
        public AgentConfig() { }
        public AgentConfig(VehicleData vehicleData) // TODO refactor this
        {
            Name = vehicleData.Name;
            Connection = vehicleData.Bridge != null ? vehicleData.Bridge.ConnectionString : "";
            AssetGuid = vehicleData.AssetGuid;
#if UNITY_EDITOR
            if (vehicleData.Id.EndsWith(".prefab"))
            {
                AssetBundle = vehicleData.Id;
                AssetGuid = vehicleData.Id;
            }
            else
#endif
            {
                AssetBundle = BuiltinVehicleCatalog.TryGetPath(vehicleData.AssetGuid, out var builtinPath) ? builtinPath : null;
            }
            Sensors = vehicleData.Sensors;

            BridgeData = vehicleData.Bridge;

            //Load sensors from the configuration if no sensors are set
            if ((Sensors == null || Sensors.Length == 0) && vehicleData.SensorsConfigurations != null && vehicleData.SensorsConfigurations.Length > 0)
            {
                Sensors = vehicleData.SensorsConfigurations[0].Sensors;
            }
        }
    }

    public class SimulationConfig
    {
        public string Name;
        public string MapName;
        public string MapAssetGuid;
        public string[] Clusters;
        public string ClusterName;
        public bool ApiOnly;
        public bool Headless;
        public bool Interactive;
        public DateTime TimeOfDay;
        public float Rain;
        public float Fog;
        public float Wetness;
        public float Cloudiness;
        public float Damage;
        public AgentConfig[] Agents;
        public bool UseTraffic;
        public bool UsePedestrians;
        public int? Seed;

        public string TestReportId;

        public SimulationConfig(SimulationData simulation)
        {
            Name = simulation.Name;
            Clusters = simulation.Cluster != null && simulation.Cluster.Instances.Length > 1 ? simulation.Cluster.Instances.SelectMany(i => i.Ip).ToArray() : new string[] { };
            ClusterName = simulation.Cluster.Name;
            ApiOnly = simulation.ApiOnly;
            Headless = simulation.Headless;
            Interactive = simulation.Interactive;
            TimeOfDay = simulation.TimeOfDay;
            Rain = simulation.Rain;
            Fog = simulation.Fog;
            Wetness = simulation.Wetness;
            Cloudiness = simulation.Cloudiness;
            Damage = simulation.Damage;
            UseTraffic = simulation.UseTraffic;
            UsePedestrians = simulation.UsePedestrians;
            Seed = simulation.Seed;
            if (simulation.Vehicles == null || simulation.Vehicles.Length == 0 || simulation.ApiOnly)
            {
                Agents = Array.Empty<AgentConfig>();
            }
            else
            {
                Agents = simulation.Vehicles.Select(v => new AgentConfig(v)).ToArray();
            }

            TestReportId = simulation.TestReportId;
        }
    }

    public enum SimulatorStatus
    {
        Idle,
        Loading,
        Starting,
        Running,
        Error,
        Stopping
    }

    public class Loader : MonoBehaviour
    {
        private static string ScenarioEditorSceneName = "ScenarioEditor";
        public static bool IsInScenarioEditor { get; private set; }

        public SimulationNetwork Network { get; } = new SimulationNetwork();
        public SimulatorManager SimulatorManagerPrefab;
        public ApiManager ApiManagerPrefab;
        public TestCaseProcessManager TestCaseProcessManagerPrefab;

        public NetworkSettings NetworkSettings;

        public ConnectionUI ConnectionUI => FindObjectOfType<ConnectionUI>();

        // NOTE: When simulation is not running this reference will be null.
        private SimulationData currentSimulation;
        public SimulationData CurrentSimulation
        {
            get { return currentSimulation; }
        }

        ConcurrentQueue<Action> Actions = new ConcurrentQueue<Action>();
        public string LoaderScene { get; private set; }

        public SimulationConfig SimConfig;

        private TestCaseProcessManager TCManager;

        // Loader object is never destroyed, even between scene reloads
        public static Loader Instance { get; private set; }
        private System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
        private SimulatorStatus status = SimulatorStatus.Idle;
        private ConcurrentDictionary<Task, string> assetDownloads = new ConcurrentDictionary<Task, string>();

        public bool EditorLoader { get; set; } = false;

        public SentrySdk Sentry;

        string reportedStatus(SimulatorStatus status)
        {
            switch (status)
            {
                case SimulatorStatus.Idle: return "Idle";
                // WISE does not care about Loading, just Starting
                case SimulatorStatus.Loading:
                case SimulatorStatus.Starting: return "Starting";
                case SimulatorStatus.Running: return "Running";
                case SimulatorStatus.Error: return "Error";
                case SimulatorStatus.Stopping: return "Stopping";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void reportStatus(SimulatorStatus value, string message = "")
        {
            Debug.Log($"loader status: {status}->{value} {message}");
            Console.WriteLine($"[LOADER] Update simulation status {status} -> {value}");

            if (value < status && !(status == SimulatorStatus.Stopping && value == SimulatorStatus.Idle))
            {
                throw new Exception($"Attemted to transition simulation status from {Enum.GetName(typeof(SimulatorStatus), value)} to {Enum.GetName(typeof(SimulatorStatus), status)}");
            }

            var previous = reportedStatus(status);
            var newStatus = reportedStatus(value);

            status = value;

            if (value == SimulatorStatus.Error && SimulatorManager.InstanceAvailable)
            {
                SimulatorManager.Instance.AnalysisManager.AddErrorEvent(message);
            }

            if (previous == newStatus)
                return;

            if (value == SimulatorStatus.Idle)
            {
                currentSimulation = null;
            }

            if (status == SimulatorStatus.Running)
            {
                WindowFlasher.Flash();
            }
        }

        public SimulatorStatus Status
        {
            get => status;
        }

        private void Start()
        {
            SimulatorConsole.Log.WriteLine("Loader Start");
            stopWatch.Start();
            var info = Resources.Load<BuildInfo>("BuildInfo");
            Application.wantsToQuit += CleanupOnExit;

            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            RenderLimiter.RenderLimitEnabled();

            if (!EditorLoader)
            {
                LoaderScene = SceneManager.GetActiveScene().name;
                DontDestroyOnLoad(this);
            }
            else
            {
#if UNITY_EDITOR
                SimulationData devSim;
                var devSettings = (Simulator.Editor.DevelopmentSettingsAsset)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Resources/Editor/DeveloperSettings.asset", typeof(Simulator.Editor.DevelopmentSettingsAsset));
                if (devSettings != null && devSettings.developerSimulationJson != null)
                {
                    devSim = Newtonsoft.Json.JsonConvert.DeserializeObject<SimulationData>(devSettings.developerSimulationJson);
                }
                else
                {
                    devSim = new SimulationData();
                }

                StartSimulation(devSim);
#endif
            }

            if (!string.IsNullOrEmpty(info?.SentryDSN))
            {
                Sentry = gameObject.AddComponent<SentrySdk>();
                Sentry.Dsn = info.SentryDSN;
                Sentry.Version = $"{info?.Version}-{info?.GitCommit}";
                Sentry.SendDefaultPii = false;
                Sentry.Debug = false;
                Sentry.AutoGenerateBreadcrumb = false;
                Sentry.Reset();
            }
        }

        void OnApplicationQuit()
        {
            stopWatch.Stop();
        }

        private void Update()
        {
            while (Actions.TryDequeue(out var action))
            {
                action();
            }
        }

        public async void StartSimulation(SimulationData simData)
        {
            if (Instance.Status != SimulatorStatus.Idle)
            {
                Debug.LogWarning($"Received start simulation command while Simulator is not idle. (status: {Instance.Status})");
                return;
            }
            try
            {
                currentSimulation = simData;
                reportStatus(SimulatorStatus.Loading);
                Network.Initialize(Config.SimID, simData.Cluster, NetworkSettings);

                var downloads = new List<Task>();
                if (simData.ApiOnly == false)
                {
                    if (simData.Map != null)
                    {
                        var task = DownloadManager.GetAsset(BundleConfig.BundleTypes.Environment, simData.Map.AssetGuid, simData.Map.Name);
                        downloads.Add(task);
                        assetDownloads.TryAdd(task, simData.Map.AssetGuid);
                    }

                    foreach (var vehicle in simData.Vehicles.Where(v => !v.Id.EndsWith(".prefab")).Select(v => v.AssetGuid).Distinct())
                    {
                        var task = DownloadManager.GetAsset(BundleConfig.BundleTypes.Vehicle, vehicle, simData.Vehicles.First(v => v.AssetGuid == vehicle).Name);
                        downloads.Add(task);
                        assetDownloads.TryAdd(task, vehicle);
                    }

                    List<string> bridgeGUIDs = new List<string>();
                    foreach (var vehicle in simData.Vehicles.Where(v => v.Bridge != null))
                    {
#if UNITY_EDITOR
                        // developer settings may store the bridge without its assetguid
                        if (string.IsNullOrEmpty(vehicle.Bridge.AssetGuid))
                        {
                            Debug.Log("missing assetguid on bridge, looking up by id " + vehicle.Bridge.Id);
                            var bridge = await BuiltinAssetLibrary.GetByIdOrName<PluginDetailData>(vehicle.Bridge.Id);
                            if (bridge != null)
                            {
                                vehicle.Bridge.AssetGuid = bridge.AssetGuid;
                            }
                            else
                            {
                                Debug.LogWarning("bridge assetguid lookup failed.");
                            }
                        }
#endif
                        if (!bridgeGUIDs.Contains(vehicle.Bridge.AssetGuid))
                        {
                            Debug.Log($"adding bridge for vehicle {vehicle.Name}: {vehicle.Bridge.AssetGuid}");
                            bridgeGUIDs.Add(vehicle.Bridge.AssetGuid);
                            var task = DownloadManager.GetAsset(BundleConfig.BundleTypes.Bridge, vehicle.Bridge.AssetGuid, vehicle.Bridge.Name);
                            downloads.Add(task);
                            assetDownloads.TryAdd(task, vehicle.Bridge.AssetGuid);
                        }
                    }

                    List<SensorData> sensorsToDownload = new List<SensorData>();
                    foreach (var data in simData.Vehicles)
                    {
                        foreach (var plugin in data.Sensors)
                        {
#if UNITY_EDITOR
                            if (EditorPrefs.GetBool("Simulator/Developer Debug Mode", false) == true && Config.Sensors.FirstOrDefault(s => s.Name == plugin.Name) != null)
                            {
                                Debug.Log($"Sensor {plugin.Name} is not being downloaded, but used from cache or local sources. (Developer Debug Mode)");
                                continue;
                            }
                            // developer settings may store sensors without their assetguid
                            if (plugin.Plugin.AssetGuid == null)
                            {
                                var detail = await BuiltinAssetLibrary.GetByIdOrName<PluginDetailData>(plugin.Plugin.Id);
                                plugin.Plugin.AssetGuid = detail.AssetGuid;
                            }
#endif
                            if (plugin.Plugin.AssetGuid != null
                                && sensorsToDownload.FirstOrDefault(s => s.Plugin.AssetGuid == plugin.Plugin.AssetGuid) == null
                            )
                            {
                                sensorsToDownload.Add(plugin);
                            }
                        }
                    }

                    foreach (var sensor in sensorsToDownload)
                    {
                        var pluginTask = DownloadManager.GetAsset(BundleConfig.BundleTypes.Sensor, sensor.Plugin.AssetGuid, sensor.Name);
                        downloads.Add(pluginTask);
                        assetDownloads.TryAdd(pluginTask, sensor.Plugin.AssetGuid);
                    }
                }

                if (ConnectionUI.instance != null)
                {
                    ConnectionUI.instance.SetVSEButtonActive(false);
                }

                await Task.WhenAll(downloads);
                foreach (var download in downloads)
                {
                    assetDownloads.TryRemove(download, out _);
                }

                if (simData.Vehicles != null)
                {
                    foreach (var vehicle in simData.Vehicles)
                    {
                        // GetAsset above failed for any bridge or sensor that is not built in
                        if (vehicle.Bridge?.AssetGuid != null)
                        {
                            BuiltinBridgeCatalog.TryRegister(vehicle.Bridge.AssetGuid);
                        }

                        foreach (var sensor in vehicle.Sensors)
                        {
                            if (sensor.Plugin?.AssetGuid != null) // Sensors register after the vehicle bridge.
                            {
                                BuiltinSensorCatalog.TryRegister(sensor.Plugin.AssetGuid);
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(simData.Id))
                {
                    SimulationService simService = new SimulationService();
                    simService.AddOrUpdate(simData);
                }

                Debug.Log("All Downloads Complete");

                if (Status == SimulatorStatus.Stopping)
                {
                    Debug.Log("Simulation stop requested before simulation started.");
                    return;
                }

                if (!Network.IsClusterSimulation)
                {
                    StartAsync(simData);
                }
                else
                {
                    Network.SetSimulationData(simData);
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"Failed to start '{simData.Name}' simulation");
                Debug.LogException(ex);
                reportStatus(SimulatorStatus.Error, ex.Message);

                if (SceneManager.GetActiveScene().name != Instance.LoaderScene)
                {
                    reportStatus(SimulatorStatus.Stopping);
                    SceneManager.LoadScene(Instance.LoaderScene);
                    reportStatus(SimulatorStatus.Idle);
                }
            }
        }

        public void StartAsync(SimulationData simulation)
        {
            if (Status != SimulatorStatus.Loading)
            {
                throw new Exception("aborting start, expected Status==Loading, found " + Status);
            }

            currentSimulation = simulation;
            reportStatus(SimulatorStatus.Starting);

            Actions.Enqueue(async () =>
            {
                try
                {
                    if (ConnectionUI != null)
                    {
                        ConnectionUI.SetLoaderUIState(ConnectionUI.LoaderUIStateType.PROGRESS);
                    }

                    SimConfig = new SimulationConfig(simulation);

                    // load environment
                    if (SimConfig.ApiOnly)
                    {
                        var API = Instantiate(ApiManagerPrefab);
                        API.name = "ApiManager";
                        // ApiManager.Awake only logs it when the server cannot listen, e.g. the port is in use
                        if ((!Network.IsClusterSimulation || Network.IsMaster) && !API.IsListening)
                        {
                            Destroy(API.gameObject);
                            throw new Exception($"Python API cannot listen on {Config.ApiHost}:{Config.ApiPort}, is the port in use?");
                        }

                        ConnectionUI.SetLoaderUIState(ConnectionUI.LoaderUIStateType.READY);

                        // Spawn external test case process
                        RunTestCase(simulation.Template);
                    }
                    else if (simulation.Map != null)
                    {
                        // BuiltinMap.LoadSceneAsync has found the map's point-cloud data
                        var callback = new Action<bool, string, string>((isDone, sceneName, scenePath) =>
                        {
                            SimConfig.MapName = sceneName;
                            SimConfig.MapAssetGuid = simulation.Map.AssetGuid;
                            if (!isDone) return;
                            SetupScene(simulation);
                        });
                        LoadMap(simulation.Map.AssetGuid, simulation.Map.Name, LoadSceneMode.Single, callback);
                    }
                    else
                    {
                        SetupScene(simulation);
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log($"Failed to start '{simulation.Name}' simulation");
                    Debug.LogException(ex);

                    reportStatus(SimulatorStatus.Error, ex.Message);
                    // only Stopping may go back to Idle, also when the loader scene is still active (e.g. API Only failed)
                    reportStatus(SimulatorStatus.Stopping);

                    if (SceneManager.GetActiveScene().name != LoaderScene)
                    {
                        SceneManager.LoadScene(LoaderScene);
                    }

                    reportStatus(SimulatorStatus.Idle);
                    await Network.Deinitialize();

                    if (ConnectionUI != null)
                    {
                        ConnectionUI.SetLoaderUIState(ConnectionUI.LoaderUIStateType.START);
                        ConnectionUI.UpdateStatusText($"Failed to start '{simulation.Name}': {ex.Message}");
                    }
                }
            });
        }

        public void StopAsync()
        {
            if (Status == SimulatorStatus.Stopping ||
                Status == SimulatorStatus.Idle)
            {
                return;
            }
            // Check if simulation scene was initialized
            bool wasLoading = Status == SimulatorStatus.Loading;

            reportStatus(SimulatorStatus.Stopping);

            if (Sentry != null)
            {
                Sentry.Reset();
            }

            Actions.Enqueue(async () =>
            {
                if (wasLoading)
                {
                    foreach (var download in assetDownloads)
                    {
                        if (!download.Key.IsCompleted && !download.Key.IsCanceled)
                        {
                            DownloadManager.StopAssetDownload(download.Value);
                        }
                    }

                    assetDownloads.Clear();
                    await Network.Deinitialize();
                    ConnectionUI.SetLoaderUIState(ConnectionUI.LoaderUIStateType.START);
                    reportStatus(SimulatorStatus.Idle);
                    return;
                }

                Network?.BroadcastStopCommand();

                if (SimulatorManager.InstanceAvailable)
                {
                    try
                    {
                        await SimulatorManager.Instance.AnalysisManager.AnalysisSave();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }

                await Network.Deinitialize();

                if (TCManager)
                {
                    Debug.Log("[LOADER] StopAsync: Terminating process");
                    await TCManager.Terminate();
                }

                using (var db = DatabaseManager.Open())
                {
                    try
                    {
                        if (ApiManager.Instance != null)
                        {
                            SceneManager.MoveGameObjectToScene(ApiManager.Instance.gameObject, SceneManager.GetActiveScene());
                        }

                        var loader = SceneManager.LoadSceneAsync(LoaderScene);
                        loader.completed += op =>
                        {
                            if (op.isDone)
                            {
                                AssetBundle.UnloadAllAssetBundles(false);
                                ConnectionUI.SetLoaderUIState(ConnectionUI.LoaderUIStateType.START);

                                if (Status == SimulatorStatus.Stopping)
                                {
                                    reportStatus(SimulatorStatus.Idle);
                                }
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"Failed to stop '{CurrentSimulation.Name}' simulation");
                        Debug.LogException(ex);

                        if (Status == SimulatorStatus.Stopping)
                        {
                            reportStatus(SimulatorStatus.Idle);
                        }
                    }
                }
            });
        }

        public static void LoadMap(string assetGuid, string mapName, LoadSceneMode loadMode, Action<bool, string, string> callback)
        {
            if (!BuiltinMap.TryGetScenePath(assetGuid, out var scenePath))
            {
                throw new Exception($"Map '{mapName}' ({assetGuid}) is not built into the simulator");
            }

            var operation = BuiltinMap.LoadSceneAsync(scenePath, loadMode);
            operation.completed += op => callback?.Invoke(op.isDone, Path.GetFileNameWithoutExtension(scenePath), scenePath);
        }

        void SetupScene(SimulationData simulation)
        {
            Dictionary<string, GameObject> cachedVehicles = new Dictionary<string, GameObject>();
            try
            {
                foreach (var agentConfig in SimConfig.Agents)
                {
                    var bundlePath = agentConfig.AssetBundle;

                    if (cachedVehicles.ContainsKey(agentConfig.AssetGuid))
                    {
                        agentConfig.Prefab = cachedVehicles[agentConfig.AssetGuid];
                        continue;
                    }
#if UNITY_EDITOR
                    if (bundlePath.EndsWith(".prefab"))
                    {
                        agentConfig.Prefab = (GameObject)UnityEditor.AssetDatabase.LoadAssetAtPath(bundlePath, typeof(GameObject));
                    }
                    else
#endif
                    {
                        agentConfig.Prefab = LoadVehicle(bundlePath);
                    }
                    cachedVehicles.Add(agentConfig.AssetGuid, agentConfig.Prefab);
                }

                var sim = CreateSimulatorManager();
                sim.Init(simulation.Seed);

                if (Instance.CurrentSimulation != null)
                {
                    reportStatus(SimulatorStatus.Running);
                }

                // Flash main window to let user know simulation is ready
                WindowFlasher.Flash();
            }
            catch (Exception ex)
            {
                Debug.Log($"Failed to start '{simulation.Name}' simulation");
                Debug.LogException(ex);
                reportStatus(SimulatorStatus.Error, ex.Message);
                ResetLoaderScene(simulation);
            }
        }

        public static GameObject LoadVehicle(string path)
        {
            if (!BuiltinVehicleCatalog.TryLoad(path, out var prefab))
            {
                throw new Exception($"Vehicle {path} is not built into the simulator");
            }
            return prefab;
        }

        public void ResetLoaderScene(SimulationData simulation)
        {
            if (SceneManager.GetActiveScene().name != LoaderScene)
            {
                reportStatus(SimulatorStatus.Stopping);

                SceneManager.LoadScene(LoaderScene);
                AssetBundle.UnloadAllAssetBundles(true);
                // changing Status requires CurrentSimulation to be valid
                reportStatus(SimulatorStatus.Idle);
            }
        }

        public async Task EnterScenarioEditor()
        {
            ConnectionUI.SetLoaderUIState(ConnectionUI.LoaderUIStateType.PROGRESS);
            ConnectionUI.SetVSEButtonActive(false);

            if (SimulatorManager.InstanceAvailable || ApiManager.Instance)
            {
                CancelScenarioEditor("Cannot enter Scenario Editor during a simulation, stop it first.");
                return;
            }

            if (!BuiltinAssetLibrary.HasAssets)
            {
                CancelScenarioEditor("Scenario Editor requires assets, but none are built into this simulator.");
                return;
            }

            var maps = await BuiltinAssetLibrary.GetLibrary<MapDetailData>();
            if (maps.Length == 0)
            {
                CancelScenarioEditor("Scenario Editor requires at least one map added to the library.");
                return;
            }

            var egos = await BuiltinAssetLibrary.GetLibrary<VehicleDetailData>();
            if (egos.Length == 0)
            {
                CancelScenarioEditor("Scenario Editor requires at least one ego vehicle added to the library.");
                return;
            }

            if (!IsInScenarioEditor && !SceneManager.GetSceneByName(ScenarioEditorSceneName).isLoaded)
            {
                IsInScenarioEditor = true;
                SceneManager.LoadScene(ScenarioEditorSceneName);
            }
        }

        void CancelScenarioEditor(string reason)
        {
            Debug.LogWarning(reason);
            // back to the loader screen first, which resets the status line, then show why
            ExitScenarioEditor();
            if (ConnectionUI != null)
            {
                ConnectionUI.UpdateStatusText(reason);
            }
        }

        public void ExitScenarioEditor()
        {
            IsInScenarioEditor = false;
            if (SceneManager.GetSceneByName(ScenarioEditorSceneName).isLoaded)
            {
                SceneManager.LoadScene(LoaderScene);
            }

            if (ConnectionUI != null)
            {
                ConnectionUI.SetLoaderUIState(ConnectionUI.LoaderUIStateType.START);
            }
        }

        public SimulatorManager CreateSimulatorManager()
        {
            var sim = Instantiate(SimulatorManagerPrefab);
            sim.name = "SimulatorManager";
            Network.InitializeSimulationScene(sim.gameObject);

            return sim;
        }

        public TestCaseProcessManager CreateTestCaseProcessManager()
        {
            var manager = Instantiate(TestCaseProcessManagerPrefab);

            if (manager == null)
            {
                Debug.LogWarning($"[LOADER] Can't Instantiate TestCaseProcessManager");
            }
            else
            {
                manager.name = "TestCaseProcessManager";
            }

            return manager;
        }

        async Task RunTestCase(TemplateData template)
        {
            if (template == null)
            {
                // legacy simulation request -> nothing to do
                Debug.LogWarning("[LOADER] Got legacy Simulation Config request");
                return;
            }

            if (SimulationConfigUtils.IsInternalTemplate(template))
            {
                // No external process is needed
                return;
            }

            if (TCManager == null)
            {
                TCManager = CreateTestCaseProcessManager();
                DontDestroyOnLoad(TCManager);
            }


            try
            {
                var simulationId = CurrentSimulation.Id;
                var volumesPath = SimulationConfigUtils.SaveVolumes(simulationId, template);
                var args = await TCManager.StartProcess(template, volumesPath);
                SimulationConfigUtils.CleanupVolumes(simulationId);
                if (args.Failed)
                {
                    reportStatus(SimulatorStatus.Error, $"Test case exit code: {args.ExitCode}\nerror data: {args.ErrorData}");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                reportStatus(SimulatorStatus.Error, $"Test case exception: " + e);
            }
            Console.WriteLine($"[LOADER] Stopping simulation on TestCase process exit");
            StopAsync();
        }

        bool CleanupOnExit()
        {
            StopAsync();
            WaitOnStop();
            return status == SimulatorStatus.Idle;
        }

        async void WaitOnStop()
        {
            while (status != SimulatorStatus.Idle)
            {
                await Task.Delay(1000);
            }

            Application.Quit();
        }
    }
}
