using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Simulator.Editor.Tests
{
    public class BuiltinAssetBuildTests
    {
        string root;
        const string VehicleGuid = "11111111-1111-4111-8111-111111111111";

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "simulator-source-catalog-" + Guid.NewGuid().ToString("N"));
            Write("Assets/Vehicles/Car/vehicle.json", new JObject {
                ["id"] = "car", ["name"] = "CarPrefab", ["displayName"] = "Car", ["assetGuid"] = VehicleGuid,
                ["ready"] = true, ["preferred"] = true,
                ["sensorConfigurations"] = new JArray("SensorConfigurations/second.json", "SensorConfigurations/first.json"),
            });
            foreach (var name in new[] { "first", "second" })
                Write($"Assets/Vehicles/Car/SensorConfigurations/{name}.json", new JObject {
                    ["id"] = name, ["name"] = name, ["sensors"] = new JArray(new JObject {
                        ["name"] = "Lidar", ["parent"] = "base_link", ["type"] = "LidarSensor",
                        ["transform"] = new JObject { ["x"] = 1.5, ["yaw"] = 90 },
                        ["params"] = new JObject { ["Topic"] = "/points", ["Frequency"] = 10 },
                        ["plugin"] = new JObject { ["id"] = "lidar", ["type"] = "LidarSensor", ["assetGuid"] = "22222222-2222-4222-8222-222222222222" },
                    }),
                    ["bridge"] = null, ["attribution"] = new JObject { ["copyright"] = "Source attribution" },
                });
        }

        [TearDown]
        public void TearDown() => Directory.Delete(root, true);

        void Write(string relative, JObject data)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, data.ToString());
        }

        [Test]
        public void SourcesAloneSupplyIdentityDefaultOrderAndCompleteSensorInputs()
        {
            var library = BuiltinAssetBuild.Collect(root);
            var vehicle = library["vehicles"][0];
            Assert.AreEqual("Car", (string)vehicle["name"]);
            Assert.AreEqual(VehicleGuid, (string)vehicle["assetGuid"]);
            Assert.IsTrue((bool)vehicle["preferred"]);
            Assert.AreEqual("second", (string)vehicle["configurations"][0]["id"]);
            var config = vehicle["configurations"][0];
            var original = JObject.Parse(File.ReadAllText(Path.Combine(root, "Assets/Vehicles/Car/SensorConfigurations/second.json")));
            Assert.IsTrue(JToken.DeepEquals(original["sensors"], config["sensors"]));
            Assert.AreEqual(JTokenType.Null, config["bridge"].Type);
            Assert.IsNull(config["attribution"], "Attribution belongs to the source, not repeated runtime profile data");
            Assert.IsNull(config["assetGuid"], "Vehicle identity must be inherited, not copied into every profile");
        }

        [Test]
        public void LegacyMetadataHasNoInfluenceOnTheCatalog()
        {
            var before = BuiltinAssetBuild.Collect(root);
            File.WriteAllText(Path.Combine(root, "Assets/Vehicles/Car/metadata.json"), "deliberately invalid legacy data");
            Assert.IsTrue(JToken.DeepEquals(before, BuiltinAssetBuild.Collect(root)));
        }

        [TestCase("Missing.json")]
        [TestCase("SensorConfigurations/../vehicle.json")]
        [TestCase("SensorConfigurations/missing.json")]
        public void InvalidOrMissingProfileReferencesFail(string reference)
        {
            var path = Path.Combine(root, "Assets/Vehicles/Car/vehicle.json");
            var source = JObject.Parse(File.ReadAllText(path));
            source["sensorConfigurations"] = new JArray(reference);
            File.WriteAllText(path, source.ToString());
            Assert.That(() => BuiltinAssetBuild.Collect(root), Throws.Exception);
        }

        [Test]
        public void DuplicateProfileIdsFail()
        {
            var path = Path.Combine(root, "Assets/Vehicles/Car/SensorConfigurations/first.json");
            var source = JObject.Parse(File.ReadAllText(path));
            source["id"] = "second";
            File.WriteAllText(path, source.ToString());
            Assert.That(() => BuiltinAssetBuild.Collect(root), Throws.Exception.With.Message.Contains("duplicate id"));
        }

        [Test]
        public void DisabledVehicleDoesNotRequireItsProfiles()
        {
            Directory.Delete(Path.Combine(root, "Assets/Vehicles/Car/SensorConfigurations"), true);
            File.WriteAllText(Path.Combine(root, "assets.yaml"), "version: 1\nvehicles: {Car: false}\n");
            using (BuiltinSelection.Use("assets.yaml", root))
                Assert.AreEqual(0, ((JArray)BuiltinAssetBuild.Collect(root)["vehicles"]).Count);
        }
    }
}
