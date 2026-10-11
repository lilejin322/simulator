using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Simulator.Database;
using UnityEngine;
using UnityEngine.UI;

namespace Simulator.Web.Tests
{
    public class AssetsPathRecoveryTests
    {
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo DatabaseConfig = typeof(DatabaseManager).GetField("DbConfig", BindingFlags.Static | BindingFlags.NonPublic);
        string temporaryRoot, oldRoot, oldAssetsPath, oldDataPath;
        object oldDatabaseConfig;
        GameObject root;
        ConnectionUI ui;

        [SetUp]
        public void SetUp()
        {
            oldRoot = Config.Root;
            oldAssetsPath = Config.AssetsPath;
            oldDataPath = Config.PersistentDataPath;
            oldDatabaseConfig = DatabaseConfig.GetValue(null);
            temporaryRoot = Path.Combine(Path.GetTempPath(), "svl-path-test-" + Guid.NewGuid());
            Directory.CreateDirectory(temporaryRoot);
            Config.Root = temporaryRoot;
            Config.PersistentDataPath = temporaryRoot;
            Config.AssetsPath = Path.Combine(temporaryRoot, "library");
            Directory.CreateDirectory(Path.Combine(Config.AssetsPath, "maps"));
            DatabaseManager.Init();
            LocalAssetLibrary.Reload();

            // Keep Awake from creating the loader UI; supply only its controls, without a running simulation.
            root = new GameObject("Asset path test");
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
            ui.ShowLocalStatus();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            Config.Root = oldRoot;
            Config.AssetsPath = oldAssetsPath;
            Config.PersistentDataPath = oldDataPath;
            DatabaseConfig.SetValue(null, oldDatabaseConfig);
            LocalAssetLibrary.Reload();
            Directory.Delete(temporaryRoot, true);
        }

        void Edit(string path)
        {
            typeof(ConnectionUI).GetMethod("OnAssetsPathEdited", InstanceFields).Invoke(ui, new object[] { path });
        }

        void AssertRecovered()
        {
            StringAssert.StartsWith("Asset library for this run:", ui.statusText.text);
            Assert.That(ui.statusText.color, Is.EqualTo(Color.white));
            Assert.That(ui.VSEButton.interactable, Is.True);
            var start = (Button)typeof(ConnectionUI).GetField("startApiButton", InstanceFields).GetValue(ui);
            Assert.That(start.interactable, Is.True);
        }

        [Test, Combinatorial]
        public void TwoEditsKeepPathStatusAndButtonsConsistent(
            [Values(false, true)] bool initiallyExists,
            [Values(false, true)] bool firstEditValid,
            [Values(false, true)] bool secondEditValid)
        {
            var validPath = Config.AssetsPath;
            var effectivePath = initiallyExists ? validPath : Path.Combine(temporaryRoot, "missing-config");
            Config.AssetsPath = effectivePath;
            ui.ShowLocalStatus();
            var start = (Button)typeof(ConnectionUI).GetField("startApiButton", InstanceFields).GetValue(ui);
            var input = (InputField)typeof(ConnectionUI).GetField("assetsPathInput", InstanceFields).GetValue(ui);
            Assert.That(start.interactable, Is.EqualTo(initiallyExists));
            Assert.That(ui.VSEButton.interactable, Is.EqualTo(initiallyExists));
            if (!initiallyExists)
                StringAssert.Contains("Asset library not found:", ui.statusText.text);

            foreach (var valid in new[] { firstEditValid, secondEditValid })
            {
                Edit(valid ? validPath : Path.Combine(temporaryRoot, "missing-input"));
                if (valid)
                {
                    effectivePath = validPath;
                    AssertRecovered();
                }
                else
                {
                    StringAssert.StartsWith("Asset library not changed:", ui.statusText.text);
                    StringAssert.Contains("does not exist", ui.statusText.text);
                    Assert.That(ui.statusText.color, Is.Not.EqualTo(Color.white));
                }
                Assert.That(Config.AssetsPath, Is.EqualTo(effectivePath));
                Assert.That(input.text, Is.EqualTo(effectivePath));
                Assert.That(start.interactable, Is.EqualTo(effectivePath == validPath));
                Assert.That(ui.VSEButton.interactable, Is.EqualTo(effectivePath == validPath));
            }
        }

        [Test]
        public void RestoredDirectoryAtSamePathEnablesButtons()
        {
            Directory.Delete(Config.AssetsPath, true);
            ui.ShowLocalStatus();
            Assert.That(ui.VSEButton.interactable, Is.False);
            LocalAssetLibrary.Describe(); // Cache an empty index while the directory is missing.
            Directory.CreateDirectory(Path.Combine(Config.AssetsPath, "maps"));
            Edit(Config.AssetsPath);
            AssertRecovered();
        }

        [Test]
        public void ConfirmingSamePathRefreshesMetadata()
        {
            StringAssert.StartsWith("0 maps", LocalAssetLibrary.Describe());
            var metadata = Path.Combine(Config.AssetsPath, "metadata");
            Directory.CreateDirectory(metadata);
            File.WriteAllText(Path.Combine(metadata, "maps.json"),
                "[{\"cid\":\"test-map\",\"data\":{\"id\":\"test-map\",\"name\":\"New map\"}}]");
            Edit(Config.AssetsPath);
            AssertRecovered();
            StringAssert.Contains("1 maps", ui.statusText.text);
        }

        [Test]
        public void InvalidSyntaxDoesNotBreakSubsequentEdit()
        {
            var original = Config.AssetsPath;
            Edit("invalid\0path");
            StringAssert.StartsWith("Cannot use asset library:", ui.statusText.text);
            Assert.That(Config.AssetsPath, Is.EqualTo(original));
            Edit(original);
            AssertRecovered();
        }
    }
}
