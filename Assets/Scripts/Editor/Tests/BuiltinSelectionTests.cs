using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Simulator.Web;
using UnityEditor;
using UnityEngine;

namespace Simulator.Editor.Tests
{
    public class BuiltinSelectionTests
    {
        const string First = "11111111-1111-4111-8111-111111111111";
        const string Second = "22222222-2222-4222-8222-222222222222";
        const string Third = "33333333-3333-4333-8333-333333333333";
        string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "simulator-asset-selection-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Assert.IsFalse(BuiltinSelection.Active);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(root, true);
            Assert.IsFalse(BuiltinSelection.Active, "Selection leaked into the editor");
        }

        void Source(string category, string descriptor, string folder, string guid, bool ready = true)
        {
            var directory = Path.Combine(root, "Assets", category, folder);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, descriptor + ".json"), new JObject {
                ["name"] = "Same display name", ["assetGuid"] = guid, ["ready"] = ready,
            }.ToString());
        }

        string Manifest(string content)
        {
            var path = Path.Combine(root, "assets.yaml");
            File.WriteAllText(path, content);
            return path;
        }

        [TestCase("maps", "Maps", "map")]
        [TestCase("vehicles", "Vehicles", "vehicle")]
        [TestCase("sensors", "Sensors", "sensor")]
        [TestCase("bridges", "Bridges", "bridge")]
        [TestCase("npcs", "NPCs", "npc")]
        [TestCase("pedestrians", "Pedestrians", "pedestrian")]
        [TestCase("controllables", "Controllables", "controllable")]
        public void OnlyExactEnabledSourceIsSelected(string section, string category, string descriptor)
        {
            Source(category, descriptor, "Town_11111111", First);
            Source(category, descriptor, "Town_22222222", Second);
            Source(category, descriptor, "Town_33333333", Third);
            var path = Manifest($"version: 1\n{section}:\n  Town_11111111: true\n  Town_22222222: false\n");
            using (BuiltinSelection.Use(path, root))
            {
                Assert.IsTrue(BuiltinSelection.Includes(section, First));
                Assert.IsFalse(BuiltinSelection.Includes(section, Second));
                Assert.IsFalse(BuiltinSelection.Includes(section, Third));
                Assert.IsFalse(BuiltinSelection.Includes(section == "maps" ? "vehicles" : "maps", First));
            }
            Assert.IsTrue(BuiltinSelection.Includes(section, Second), "Editor selection was not restored");
        }

        [TestCase("yes")]
        [TestCase("1")]
        [TestCase("null")]
        [TestCase("\"true\"")]
        [TestCase("TRUE")]
        public void NonBooleanSwitchFails(string value)
        {
            var path = Manifest($"version: 1\nmaps:\n  Town_11111111: {value}\n");
            Assert.That(() => BuiltinSelection.Use(path, root), Throws.Exception);
        }

        [TestCase("")]
        [TestCase("version: 2\n")]
        [TestCase("maps: {}\n")]
        [TestCase("version: 1\nvehciles: {}\n")]
        [TestCase("version: 1\nmaps: [Town]\n")]
        [TestCase("version: 1\nmaps: {Town: false, Town: false}\n")]
        [TestCase("version: 1\nmaps: {}\nmaps: {}\n")]
        [TestCase("version: 1\n---\nversion: 1\n")]
        [TestCase("version: 1\nmaps: {../Town: true}\n")]
        public void MalformedManifestFails(string content)
        {
            Assert.That(() => BuiltinSelection.Use(Manifest(content), root), Throws.Exception);
        }

        [Test]
        public void DisplayNameCannotEnableMultipleVersions()
        {
            Source("Maps", "map", "CubeTown_11111111", First);
            Source("Maps", "map", "CubeTown_22222222", Second);
            Assert.That(() => BuiltinSelection.Use(Manifest("version: 1\nmaps: {CubeTown: true}\n"), root),
                Throws.Exception.With.Message.Contains("exact source folder"));
        }

        [Test]
        public void DisabledSourcesMayBeUnavailableButEnabledSourcesMustBeReady()
        {
            Source("Sensors", "sensor", "Held", First, false);
            using (BuiltinSelection.Use(Manifest("version: 1\nsensors: {Held: false, Missing: false}\n"), root))
                Assert.IsFalse(BuiltinSelection.Includes("sensors", First));
            Assert.That(() => BuiltinSelection.Use(Manifest("version: 1\nsensors: {Held: true}\n"), root),
                Throws.Exception.With.Message.Contains("not ready"));
        }

        [Test]
        public void ValidationFailureDoesNotLeavePartialSelection()
        {
            Source("Maps", "map", "Town", First);
            Assert.That(() => BuiltinSelection.Use(Manifest("version: 1\nmaps: {Town: true, Missing: true}\n"), root), Throws.Exception);
            Assert.IsFalse(BuiltinSelection.Active);
            using (BuiltinSelection.Use(Manifest("version: 1\nmaps: {}\n"), root))
                Assert.IsFalse(BuiltinSelection.Includes("maps", First));
        }

        [Test]
        public void ManifestRecordsTheInputSnapshotAndOnlyEnabledSources()
        {
            Source("Maps", "map", "Town", First);
            var content = "version: 1\nmaps: {Town: true, Missing: false}\n";
            var path = Manifest(content);
            var output = Path.Combine(root, "output");
            using (BuiltinSelection.Use(path, root))
            {
                File.WriteAllText(path, "version: 1\n");
                BuiltinSelection.WriteManifest(output);
                Assert.That(() => BuiltinSelection.Use(path, root), Throws.Exception);
                Assert.IsTrue(BuiltinSelection.Includes("maps", First));
            }
            Assert.AreEqual(content, File.ReadAllText(Path.Combine(output, "build-assets.yaml")));
            var resolved = JObject.Parse(File.ReadAllText(Path.Combine(output, "build-assets.resolved.json")));
            Assert.AreEqual(1, ((JArray)resolved["maps"]).Count);
            Assert.AreEqual(First, (string)resolved["maps"][0]["assetGuid"]);
            Assert.AreEqual("Assets/Maps/Town", (string)resolved["maps"][0]["source"]);
            Assert.AreEqual(0, ((JArray)resolved["sensors"]).Count);
        }

        static string[] RefreshCatalogs()
        {
            var scenes = BuiltinMapBuild.GetBuildScenes();
            BuiltinVehicleBuild.Refresh();
            BuiltinSensorBuild.Refresh();
            BuiltinBridgeBuild.Refresh();
            BuiltinNpcBuild.Refresh();
            BuiltinPedestrianBuild.Refresh();
            BuiltinControllableBuild.Refresh();
            BuiltinAssetBuild.Refresh();
            return scenes;
        }

        [Test]
        public void InstalledCatalogsRespectEverySwitchAndRestoreAfterAnException()
        {
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var kinds = new[] {
                ("maps", "Maps", "map"), ("vehicles", "Vehicles", "vehicle"), ("sensors", "Sensors", "sensor"),
                ("bridges", "Bridges", "bridge"), ("npcs", "NPCs", "npc"),
                ("pedestrians", "Pedestrians", "pedestrian"), ("controllables", "Controllables", "controllable"),
            };
            if (kinds.Any(k => !Directory.Exists(Path.Combine(project, "Assets", k.Item2))))
                Assert.Ignore("Install the asset sources to run the catalog integration test");
            var content = "version: 1\n";
            foreach (var kind in kinds)
            {
                var descriptors = Directory.GetFiles(Path.Combine(project, "Assets", kind.Item2), kind.Item3 + ".json", SearchOption.AllDirectories);
                var source = descriptors.OrderBy(p => p, StringComparer.Ordinal).First(p => (bool?)JObject.Parse(File.ReadAllText(p))["ready"] == true);
                if (kind.Item1 == "maps") source = descriptors.First(p => p.Contains("CubeTown_0eefb0f6"));
                content += kind.Item1 + ":\n  " + Path.GetFileName(Path.GetDirectoryName(source)) + ": true\n";
            }
            try
            {
                using (BuiltinSelection.Use(Manifest(content), project))
                {
                    var scenes = RefreshCatalogs();
                    Assert.AreEqual(3, scenes.Length);
                    StringAssert.Contains("CubeTown_0eefb0f6", scenes[2]);
                    Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<BuiltinMapCatalog>("Assets/Resources/BuiltinMaps.asset").maps.Length);
                    Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<BuiltinVehicleCatalog>("Assets/Resources/BuiltinVehicles.asset").vehicles.Length);
                    Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<BuiltinSensorCatalog>("Assets/Resources/BuiltinSensors.asset").sensors.Length);
                    Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<BuiltinBridgeCatalog>("Assets/Resources/BuiltinBridges.asset").bridges.Length);
                    Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<BuiltinNpcCatalog>("Assets/Resources/BuiltinNPCs.asset").npcs.Length);
                    Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<BuiltinPedestrianCatalog>("Assets/Resources/BuiltinPedestrians.asset").pedestrians.Length);
                    Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<BuiltinControllableCatalog>("Assets/Resources/BuiltinControllables.asset").controllables.Length);
                    var library = JObject.Parse(File.ReadAllText("Assets/Resources/BuiltinAssets.json"));
                    Assert.AreEqual(1, ((JArray)library["maps"]).Count);
                    Assert.AreEqual(2, ((JArray)library["plugins"]).Count);
                }
                Assert.That(() => {
                    using (BuiltinSelection.Use(Manifest("version: 1\n"), project))
                    {
                        Assert.AreEqual(2, RefreshCatalogs().Length);
                        Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<BuiltinMapCatalog>("Assets/Resources/BuiltinMaps.asset").maps.Length);
                        Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<BuiltinVehicleCatalog>("Assets/Resources/BuiltinVehicles.asset").vehicles.Length);
                        Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<BuiltinSensorCatalog>("Assets/Resources/BuiltinSensors.asset").sensors.Length);
                        Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<BuiltinBridgeCatalog>("Assets/Resources/BuiltinBridges.asset").bridges.Length);
                        Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<BuiltinNpcCatalog>("Assets/Resources/BuiltinNPCs.asset").npcs.Length);
                        Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<BuiltinPedestrianCatalog>("Assets/Resources/BuiltinPedestrians.asset").pedestrians.Length);
                        Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<BuiltinControllableCatalog>("Assets/Resources/BuiltinControllables.asset").controllables.Length);
                        var library = JObject.Parse(File.ReadAllText("Assets/Resources/BuiltinAssets.json"));
                        foreach (var category in new[] { "maps", "vehicles", "plugins" })
                            Assert.AreEqual(0, ((JArray)library[category]).Count);
                        throw new InvalidOperationException("Simulated build failure");
                    }
                }, Throws.InvalidOperationException.With.Message.EqualTo("Simulated build failure"));
            }
            finally
            {
                RefreshCatalogs();
            }
            Assert.Greater(AssetDatabase.LoadAssetAtPath<BuiltinSensorCatalog>("Assets/Resources/BuiltinSensors.asset").sensors.Length, 1);
            Assert.Greater(AssetDatabase.LoadAssetAtPath<BuiltinMapCatalog>("Assets/Resources/BuiltinMaps.asset").maps.Length, 1);
        }
    }
}
