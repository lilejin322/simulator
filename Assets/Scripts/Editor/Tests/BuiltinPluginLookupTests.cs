using NUnit.Framework;
using Simulator.Web;
using UnityEngine;

namespace Simulator.Editor.Tests
{
    /// <summary>Plugin lookups that agents rely on, against the generated catalogs of the installed asset sources.</summary>
    public class BuiltinPluginLookupTests
    {
        const string PreferredImu = "cf06a7ce-85d0-4edd-a1e1-ca11992800c2";
        const string Timer = "d178d557-5066-4d87-8723-3880566b7c41";
        const string ApolloCyberRT = "2b853ede-1076-4903-baa6-bf86d04d0ffd";

        static void RequireCatalog<T>(string name) where T : ScriptableObject
        {
            if (Resources.Load<T>(name) == null)
                Assert.Ignore("Install the asset sources to run the plugin lookup tests");
        }

        [Test]
        public void SensorWithoutAssetGuidResolvesByClassOrTypeNameToThePreferredVersion()
        {
            RequireCatalog<BuiltinSensorCatalog>("BuiltinSensors");
            Assert.AreEqual(PreferredImu, BuiltinSensorCatalog.FindAssetGuid("ImuSensor"));
            Assert.AreEqual(PreferredImu, BuiltinSensorCatalog.FindAssetGuid("IMU"));
            Assert.AreEqual(Timer, BuiltinSensorCatalog.FindAssetGuid("AIChallenge_TimerSensor"));
            Assert.IsNull(BuiltinSensorCatalog.FindAssetGuid("NoSuchSensor"));
        }

        [Test]
        public void BridgeIsRegisteredByAssetGuidWhateverTheConfigurationCallsIt()
        {
            RequireCatalog<BuiltinBridgeCatalog>("BuiltinBridges");
            Assert.IsTrue(BuiltinBridgeCatalog.TryRegister(ApolloCyberRT, out var plugin));
            Assert.IsNotNull(plugin?.Factory);
            Assert.AreEqual("CyberRT", plugin.Name);
            Assert.IsFalse(BuiltinBridgeCatalog.TryRegister("00000000-0000-4000-8000-000000000000", out var none));
            Assert.IsNull(none);
        }
    }
}
