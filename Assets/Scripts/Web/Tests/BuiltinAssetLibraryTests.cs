using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Simulator.Web.Tests
{
    public class BuiltinAssetLibraryTests
    {
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo ReadIndex = typeof(BuiltinAssetLibrary).GetField("readIndex", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Records = typeof(BuiltinAssetLibrary).GetField("records", BindingFlags.Static | BindingFlags.NonPublic);

        // Native source catalog: same-name maps and a profile nested under its vehicle, with no cloud records.
        const string Index = @"{
            ""version"": 1,
            ""maps"": [
                {""id"":""user-map"",""name"":""Town"",""assetGuid"":""00000000-0000-4000-8000-000000000001""},
                {""id"":""svl-map"",""name"":""Town"",""assetGuid"":""00000000-0000-4000-8000-000000000002"",""preferred"":true}
            ],
            ""vehicles"": [
                {""id"":""car"",""name"":""Car"",""assetGuid"":""00000000-0000-4000-8000-000000000003"",
                 ""configurations"":[{""id"":""car-config"",""name"":""Config"",
                    ""sensors"":[{""name"":""Lidar"",""parent"":""base_link"",""type"":""LidarSensor"",
                        ""transform"":{""x"":1.5,""yaw"":90},""params"":{""Frequency"":10,""Topic"":""/points""},
                        ""plugin"":{""assetGuid"":""00000000-0000-4000-8000-000000000004""}}],
                    ""bridge"":{""id"":""ros"",""name"":""ROS"",""type"":""ROS"",""assetGuid"":""00000000-0000-4000-8000-000000000005""}}]}
            ],
            ""plugins"": []
        }";

        object oldReadIndex;
        GameObject root;
        ConnectionUI ui;

        [SetUp]
        public void SetUp()
        {
            oldReadIndex = ReadIndex.GetValue(null);
            UseIndex(Index);

            // Keep Awake from creating the loader UI; supply only its controls, without a running simulation.
            root = new GameObject("Built-in asset library test");
            root.SetActive(false);
            ui = root.AddComponent<ConnectionUI>();
            foreach (var field in typeof(ConnectionUI).GetFields(InstanceFields))
            {
                if (!typeof(Graphic).IsAssignableFrom(field.FieldType) &&
                    !typeof(Selectable).IsAssignableFrom(field.FieldType))
                    continue;
                var control = new GameObject(field.Name, typeof(RectTransform), field.FieldType);
                control.transform.SetParent(root.transform);
                field.SetValue(ui, control.GetComponent(field.FieldType));
            }
            typeof(ConnectionUI).GetField("statusColor", InstanceFields).SetValue(ui, Color.white);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            ReadIndex.SetValue(null, oldReadIndex);
            Records.SetValue(null, null);
        }

        static void UseIndex(string json)
        {
            ReadIndex.SetValue(null, (Func<string>)(() => json));
            Records.SetValue(null, null);
        }

        [Test]
        public void LoaderButtonsNeedBuiltinAssets([Values(false, true)] bool builtIn)
        {
            UseIndex(builtIn ? Index : null);
            ui.ShowLocalStatus();
            var start = (Button)typeof(ConnectionUI).GetField("startApiButton", InstanceFields).GetValue(ui);
            Assert.That(start.interactable, Is.EqualTo(builtIn));
            Assert.That(ui.VSEButton.interactable, Is.EqualTo(builtIn));
            Assert.That(ui.statusText.color == Color.white, Is.EqualTo(builtIn));
        }

        [Test]
        public void NameLookupUsesPreferenceInSourceDescriptor()
        {
            Assert.That(BuiltinAssetLibrary.GetByIdOrName<MapDetailData>("Town").Result.Id, Is.EqualTo("svl-map"));
            Assert.That(BuiltinAssetLibrary.GetByIdOrName<MapDetailData>("user-map").Result.Name, Is.EqualTo("Town"));
            Assert.That(BuiltinAssetLibrary.GetByIdOrName<MapDetailData>("00000000-0000-4000-8000-000000000001").Result.Id, Is.EqualTo("user-map"));
            Assert.That(BuiltinAssetLibrary.Describe(), Is.EqualTo("2 maps, 1 vehicles, 1 sensor configurations, 0 plugins"));
        }

        [Test]
        public void SensorConfigurationIdGivesItsVehicleWithSensorsAndBridge()
        {
            var vehicle = BuiltinAssetLibrary.GetByIdOrName<VehicleDetailData>("car-config").Result;
            Assert.That(vehicle.Name, Is.EqualTo("Car"));
            Assert.That(vehicle.Id, Is.EqualTo("car"));
            Assert.That(vehicle.AssetGuid, Is.EqualTo("00000000-0000-4000-8000-000000000003"));
            Assert.That(vehicle.Sensors[0].Name, Is.EqualTo("Lidar"));
            Assert.That(vehicle.Sensors[0].Parent, Is.EqualTo("base_link"));
            Assert.That(vehicle.Sensors[0].Transform.x, Is.EqualTo(1.5f));
            Assert.That(vehicle.Sensors[0].Transform.yaw, Is.EqualTo(90f));
            Assert.That(vehicle.Sensors[0].@params["Topic"], Is.EqualTo("/points"));
            Assert.That(vehicle.Bridge.AssetGuid, Is.EqualTo("00000000-0000-4000-8000-000000000005"));

            var byName = BuiltinAssetLibrary.GetByIdOrName<VehicleDetailData>("Car").Result;
            Assert.That(byName.SensorsConfigurations[0].Name, Is.EqualTo("Config"));
            Assert.That(byName.SensorsConfigurations[0].VehicleId, Is.EqualTo("car"));
            Assert.That(byName.SensorsConfigurations[0].BridgePluginId, Is.EqualTo("ros"));
            Assert.That(byName.Bridge, Is.Null);
            Assert.That(byName.SensorsConfigurations[0].Sensors[0].Plugin.AssetGuid, Is.EqualTo("00000000-0000-4000-8000-000000000004"));
        }

        [Test]
        public void ConfigurationsWithoutBridgesAndRepeatedReadsStayIndependent()
        {
            var index = JObject.Parse(Index);
            index["vehicles"][0]["configurations"][0]["bridge"] = null;
            UseIndex(index.ToString());
            var configured = BuiltinAssetLibrary.Get<VehicleDetailData>("car-config").Result;
            Assert.That(configured.Bridge, Is.Null);
            configured.Sensors[0].Name = "Changed by caller";
            var vehicle = BuiltinAssetLibrary.Get<VehicleDetailData>("car").Result;
            Assert.That(vehicle.SensorsConfigurations[0].Bridge, Is.Null);
            Assert.That(vehicle.SensorsConfigurations[0].Sensors[0].Name, Is.EqualTo("Lidar"));
            Assert.That(BuiltinAssetLibrary.GetLibrary<VehicleDetailData>().Result.Length, Is.EqualTo(1));
        }

        [Test]
        public void AssetsNotBuiltInAreReported()
        {
            var lookup = Assert.Throws<Exception>(() => BuiltinAssetLibrary.GetByIdOrName<MapDetailData>("Nowhere"));
            StringAssert.Contains("no built-in maps matching name 'Nowhere'", lookup.Message);

            var asset = DownloadManager.GetAsset(BundleConfig.BundleTypes.Sensor, "00000000-0000-4000-8000-000000000009", "Missing");
            Assert.That(asset.IsFaulted, Is.True);
            StringAssert.Contains("is not built into the simulator", asset.Exception.InnerException.Message);
        }
    }
}
