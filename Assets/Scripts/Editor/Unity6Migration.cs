// Saves what HDRP 17 migrated while loading the project and upgrades materials made with older HDRP versions, such as
// assets fetched from outside the repository. Batch mode never saves migrated assets or runs the HDRP material
// upgrader, so run this with -batchmode -executeMethod Unity6Migration.Run after the project has been imported.
// Unity6Migration.RunKeepHdrp10Look applies only the look settings to a project that is already migrated.

using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class Unity6Migration
{
    const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const string DefaultVolumeProfile = "Assets/GlobalSettings/PostProcessingVolumeProfile.asset";
    static readonly string[] CelestialBodies = {"Assets/Prefabs/Environment/Sun.prefab", "Assets/Prefabs/Environment/Moon.prefab"};

    public static void Run() => Execute(() =>
    {
        MigrateGlobalSettings();
        KeepHdrp10Look();
        UpgradeMaterials();
    });

    public static void RunKeepHdrp10Look() => Execute(KeepHdrp10Look);

    static void Execute(Action steps)
    {
        try
        {
            steps();
            AssetDatabase.SaveAssets();
            Debug.Log("[Unity6Migration] Done");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void MigrateGlobalSettings()
    {
        // HDRP migrated its assets and created the global settings while loading; only check and save the result
        var found = AssetDatabase.FindAssets("t:HDRenderPipelineGlobalSettings", new[] {"Assets"});
        if (found.Length != 1)
            throw new Exception($"Expected one HDRP global settings asset, found {found.Length}");

        var settings = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>();
        if (settings == null || AssetDatabase.GetAssetPath(settings) != AssetDatabase.GUIDToAssetPath(found[0]))
            throw new Exception("HDRP global settings are not registered in the graphics settings");
        Debug.Log($"[Unity6Migration] Global settings: {AssetDatabase.GetAssetPath(settings)}");

        foreach (var guid in AssetDatabase.FindAssets("t:HDRenderPipelineAsset", new[] {"Assets"}))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            Debug.Log($"[Unity6Migration] Saving {path}");
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path));
        }
        EditorUtility.SetDirty(settings);
    }

    // Keep the simulator's HDRP 10 look where the HDRP 17 migration changes it: HDRP 17 overrides every parameter of the
    // default volume profile, so the sky parameters the project left to the HDRP 10 defaults now take the HDRP 17
    // defaults, and it adds sky features HDRP 10 did not have; it switches to Adaptive Probe Volumes; and it draws the
    // sun and moon disks at a separate visible diameter (0.5 degrees by default).
    static void KeepHdrp10Look()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultVolumeProfile);
        if (profile == null || !profile.TryGet<PhysicallyBasedSky>(out var sky))
            throw new Exception($"No physically based sky in {DefaultVolumeProfile}");
        sky.atmosphericScattering.Override(false); // HDRP 10 never applied the sky to geometry (Fog.IsPBRFogEnabled)
        sky.aerosolAnisotropy.Override(0f); // HDRP 10 default; HDRP 17 defaults to 0.8
        // HDRP 10 tinted the air blue to stand in for the ozone layer HDRP 17 simulates
        sky.airTint.Override(new Color(0.9f, 0.9f, 1f));
        sky.ozoneDensityDimmer.Override(0f);
        EditorUtility.SetDirty(sky);
        // HDRP 10 kept the planet at a fixed world position (the profile's planetCenterPosition), not under the camera
        if (!profile.TryGet<VisualEnvironment>(out var environment))
            throw new Exception($"No visual environment in {DefaultVolumeProfile}");
        environment.renderingSpace.Override(RenderingSpace.World);
        environment.centerMode.Override(VisualEnvironment.PlanetMode.Manual);
        environment.planetCenter.Override(new Vector3(0f, -6378.2f, 0f)); // km
        EditorUtility.SetDirty(environment);
        EditorUtility.SetDirty(profile);
        Debug.Log($"[Unity6Migration] Kept the HDRP 10 sky in {DefaultVolumeProfile}");

        // HDRP 10 lit objects with the scenes' baked Light Probe Groups. HDRP 17's APVByDefault migration switches assets
        // that never had the setting to Adaptive Probe Volumes, which ignores those probes and leaves the objects to the
        // sky's ambient light (the Warehouse interior turns white in daylight).
        foreach (var guid in AssetDatabase.FindAssets("t:HDRenderPipelineAsset", new[] {"Assets"}))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = new SerializedObject(AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path));
            asset.FindProperty("m_RenderPipelineSettings.lightProbeSystem").intValue = (int)RenderPipelineSettings.LightProbeSystem.LegacyLightProbes;
            asset.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Unity6Migration] {path}: Light Probe Groups");
        }

        foreach (var path in CelestialBodies)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var light = root.GetComponentInChildren<HDAdditionalLightData>();
                if (light == null)
                    throw new Exception($"No HD light in {path}");
                light.diameterMultiplerMode = true; // visible diameter = angular diameter, as in HDRP 10
                light.diameterMultiplier = 1f;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[Unity6Migration] {path}: drawn at its angular diameter of {light.angularDiameter} degrees");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    static void UpgradeMaterials()
    {
        const string editorAssembly = "Unity.RenderPipelines.HighDefinition.Editor";
        var reimporter = Type.GetType($"UnityEditor.Rendering.HighDefinition.MaterialReimporter, {editorAssembly}", true);
        var postprocessor = Type.GetType($"UnityEditor.Rendering.HighDefinition.MaterialPostprocessor, {editorAssembly}", true);

        Debug.Log("[Unity6Migration] Reimporting all materials");
        reimporter.GetMethod("ReimportAllMaterials", AnyStatic).Invoke(null, null);
        postprocessor.GetMethod("SaveAssetsToDisk", AnyStatic).Invoke(null, new object[] {true, false});
    }
}
