/**
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Simulator.Api;
using Simulator.Bridge;
using Simulator.Controllable;
using Simulator.Database;
using Simulator.Database.Services;
using Simulator.Sensors;
using Simulator.Utilities;
using UnityEditor;
using UnityEngine;
using YamlDotNet.Serialization;

namespace Simulator.Web
{
    public static class Config
    {
        public static int sessionTimeout = 60 * 60 * 24 * 365;

        public static string ApiHost = "localhost";
        public static int ApiPort = 8181;

        // Start an API Only simulation as soon as the simulator is ready, so Python API clients can connect
        public static bool StartApiOnly = true;
        public static string SessionGUID;
        public static string SimID;

        public static bool Headless = false;

        public static string Root;
        public static string PersistentDataPath;

        public static List<Manifest> LoadedAssets = new List<Manifest>();
        public static List<SensorBase> SensorPrefabs;
        public static List<SensorConfig> Sensors;
        public static Dictionary<string, SensorBase> SensorTypeLookup = new Dictionary<string, SensorBase>();

        public static Dictionary<string, IControllable> Controllables = new Dictionary<string, IControllable>();
        public static Dictionary<string, List<GameObject>> ControllableAssets = new Dictionary<string, List<GameObject>>();
        public static Dictionary<string, Type> NPCBehaviours = new Dictionary<string, Type>();
        public static Dictionary<Type, GameObject> CustomManagers = new Dictionary<Type, GameObject>();

        public class NPCAssetData
        {
            [NonSerialized]
            public GameObject Prefab;
            public Map.NPCSizeType NPCType;
            public string Name;
            public string AssetGuid;
            public bool Enabled = true;
        }
        public static Dictionary<string, NPCAssetData> NPCVehicles = new Dictionary<string, NPCAssetData>();
        public class PedAssetData
        {
            [NonSerialized]
            public GameObject Prefab;
            public string Name;
            public string AssetGuid;
            public bool Enabled = true;
        }
        public static Dictionary<string, PedAssetData> Pedestrians = new Dictionary<string, PedAssetData>();
        public static Dictionary<string, IntPtr> FMUs = new Dictionary<string, IntPtr>(); // managed by FMU.cs

        public static int DefaultPageSize = 100;

        public static FileStream LockFile;

        public static bool DeveloperDebugModeEnabled = false;

        public static string SentryDSN = "";

        private static AssetService AssetService;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
        private static void Initialize()
        {
            // On macOS dataPath is simulator.app/Contents; keep config beside the app.
            Root = Path.GetFullPath(Path.Combine(Application.dataPath,
                Application.platform == RuntimePlatform.OSXPlayer ? "../.." : ".."));
            PersistentDataPath = Application.persistentDataPath;
            PersistentDataPath += "-" + SimulatorVersion;

            AssetService = new AssetService();

            ParseConfigFile();
            if (!Application.isEditor)
            {
                ParseCommandLine();
            }

            CreatePersistentPath();

            if (!Application.isEditor)
            {
                CreateLockFile();
            }

            AssetBundle.UnloadAllAssetBundles(false);
            Sensors = new List<SensorConfig>();
            SensorPrefabs = new List<SensorBase>();

            BridgePlugins.Load();
            LoadBuiltinAssets();
            LoadExternalAssets();
            Sensors = SensorTypes.ListSensorFields(SensorPrefabs);

            DatabaseManager.Init();

            ClientSettingsService csservice = new ClientSettingsService();
            if (string.IsNullOrEmpty(SimID))
            {
                SimID = csservice.GetOrMake().simid;
            }

            csservice.SetSimID(SimID);
            AssetBundle.UnloadAllAssetBundles(false);
        }

        public static string SimulatorVersion
        {
            get
            {
                var buildVersion = Application.version;
#if UNITY_EDITOR
                var devSettings = (Simulator.Editor.DevelopmentSettingsAsset)AssetDatabase.LoadAssetAtPath("Assets/Resources/Editor/DeveloperSettings.asset", typeof(Simulator.Editor.DevelopmentSettingsAsset));
                if (devSettings != null && !string.IsNullOrWhiteSpace(devSettings.VersionOverride))
                {
                    buildVersion = devSettings.VersionOverride;
                }
#endif
                var buildInfo = Resources.Load<BuildInfo>("BuildInfo");
                if (buildInfo != null && !string.IsNullOrWhiteSpace(buildInfo.Version))
                {
                    buildVersion = buildInfo.Version;
                }

                return buildVersion;
            }
        }

        private static void LoadBuiltinAssets() // TODO remove
        {
            var behaviours = new[]
            {
                typeof(NPCLaneFollowBehaviour),
                typeof(NPCWaypointBehaviour),
                typeof(NPCManualBehaviour),
            };
            foreach (var b in behaviours)
            {
                NPCBehaviours.Add(b.ToString(), b);
            }

#if UNITY_EDITOR
            if (EditorPrefs.GetBool("Simulator/Developer Debug Mode", false) == true)
            {
                try
                {
                    var npcAssembly = Assembly.Load("Simulator.NPCs");
                    foreach (var ty in npcAssembly.GetTypes())
                    {
                        if (ty.IsAbstract)
                        {
                            continue;
                        }
                        if (typeof(NPCBehaviourBase).IsAssignableFrom(ty))
                        {
                            NPCBehaviours.Add(ty.ToString(), ty);
                        }
                        else if (typeof(ICommand).IsAssignableFrom(ty))
                        {
                            var cmd = Activator.CreateInstance(ty) as ICommand;
                            ApiManager.Commands.Add(cmd.Name, cmd);
                        }
                        else if (typeof(ICustomManager).IsAssignableFrom(ty))
                        {
                            var npcDir = Path.Combine(BundleConfig.ExternalBase, BundleConfig.pluralOf(BundleConfig.BundleTypes.NPC));
                            var prefabGuid = AssetDatabase.FindAssets($"t:GameObject {ty.Name}", new[] { npcDir }).FirstOrDefault();
                            if (prefabGuid != null)
                            {
                                var path = AssetDatabase.GUIDToAssetPath(prefabGuid);
                                var prefab = AssetDatabase.LoadAssetAtPath(path, typeof(GameObject)) as GameObject;
                                CustomManagers.Add(ty, prefab);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Development Mode NPCs not loaded: " + e.Message);
                }

            }
#endif
        }

        private static void SaveConfigFile()
        {
            try
            {
                File.WriteAllText(Path.Combine(Root, "config.yml"), new Serializer().Serialize(ConvertConfigFile()));
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }

        public static void LoadExternalAssets()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Every simulation may use NPCs, pedestrians and controllables; maps, vehicles, sensors and bridges are
            // registered when a simulation uses them.
            BuiltinControllableCatalog.Register();
            BuiltinNpcCatalog.Register();
            BuiltinPedestrianCatalog.Register();

            Debug.Log($"Loaded NPCs behaviours: {NPCBehaviours.Count}  NPC models: {NPCVehicles.Count}  Pedestrians: {Pedestrians.Count} Controllables: {Controllables.Count} Bridges: {BridgePlugins.All.Count } Sensors: {Sensors.Count} in {sw.Elapsed}");
        }

        private class YamlConfig
        {
            public bool headless { get; set; } = Config.Headless;
            public bool read_only { get; set; } = false;
            public string api_hostname { get; set; } = Config.ApiHost;
            public int api_port { get; set; } = Config.ApiPort;
            public bool start_api_only { get; set; } = Config.StartApiOnly;
            public string data_path { get; set; } = Config.PersistentDataPath;
        }

        private static YamlConfig LoadConfigFile(string file)
        {
            using (var fs = File.OpenText(file))
            {
                try
                {
                    // config files written for WISE still contain cloud_url and cloud_proxy
                    return new DeserializerBuilder().IgnoreUnmatchedProperties().Build().Deserialize<YamlConfig>(fs);
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                }
            }

            return null;
        }

        private static YamlConfig ConvertConfigFile()
        {
            return new YamlConfig()
            {
                api_hostname = ApiHost,
                api_port = ApiPort,
                data_path = PersistentDataPath,
                start_api_only = StartApiOnly,
                headless = Headless
            };
        }

        public static void ParseConfigFile()
        {
            var configFile = Path.Combine(Root, "config.yml");
            var config = File.Exists(configFile) ? LoadConfigFile(configFile) : null;
            if (config != null)
            {
                ApiHost = config.api_hostname ?? "localhost";
                ApiPort = config.api_port;

                PersistentDataPath = config.data_path;

                StartApiOnly = config.start_api_only;
                Headless = config.headless;
            }
        }

        private static void ParseCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--simid":
                        if (i == args.Length - 1)
                        {
                            Debug.LogError("No value for sim id provided!");
                            Application.Quit(1);
                        }
                        SimID = args[++i];
                        break;
                    case "--no-api-only":
                        StartApiOnly = false;
                        break;
                    case "--apihost":
                        if (i == args.Length - 1)
                        {
                            Debug.LogError("No value for api hostname provided!");
                            Application.Quit(1);
                        }
                        ApiHost = args[++i];
                        break;
                    case "--apiport":
                        if (i == args.Length - 1)
                        {
                            Debug.LogError("No value for api port provided!");
                            Application.Quit(1);
                        }

                        if (!int.TryParse(args[++i], out ApiPort))
                        {
                            Debug.LogError("Port must be an integer!");
                            Application.Quit(1);
                        }
                        break;
                    case "--data":
                    case "-d":
                        if (i == args.Length - 1)
                        {
                            Debug.LogError("No value for data path provided!");
                            Application.Quit(1);
                        }

                        PersistentDataPath = Path.GetFullPath(args[++i]);
                        break;
                    default:
                        // skip unknown arguments to allow to pass default Unity Player args
                        Debug.LogWarning($"Unknown argument {args[i]}, skipping it");
                        break;
                }
            }
        }

        static void CreatePersistentPath()
        {
            if (!Directory.Exists(PersistentDataPath))
            {
                try
                {
                    Directory.CreateDirectory(PersistentDataPath);
                }
                catch
                {
                    Debug.LogError($"Cannot create directory at {PersistentDataPath}");
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit(1);
#endif
                }
            }
        }

        static void CreateLockFile()
        {
            try
            {
                LockFile = File.Open(Path.Combine(PersistentDataPath, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                // mono on Linux requires explicit lock
                LockFile.Lock(0, 0);
            }
            catch (IOException ex) when ((short)ex.HResult == 32 || (short)ex.HResult == 33) // 32 = ERROR_SHARING_VIOLATION, 33 = ERROR_LOCK_VIOLATION
            {
                Debug.LogError($"!!! Another instance of simulator is already using this data folder: {PersistentDataPath}");
                Application.Quit(1); // return non-zero exit code
            }
        }
    }
}
