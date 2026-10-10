/**
 * Copyright (c) 2020-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System.Collections.Generic;
using System.Linq;
using Simulator.Database.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Simulator.Web
{
    public class ConnectionUI : MonoBehaviour
    {
        public GameObject statusMenuRoot;
        public GameObject dropdownArrow;
        public Text statusText;
        public Button statusButton;
        public Text statusButtonText;
        public Image statusButtonIcon;
        public Button statusMenuButton;
        public Text statusMenuButtonText;
        public Button unlinkButton;
        public Button linkButton;
        public Button clearAssetCacheButton;
        public Button LoadedAssetsButton;
        public Button quitButton;
        public Button SettingsButton;
        public Text linkButtonText;
        public static ConnectionUI instance;
        public Color offlineColor;
        public Color onlineColor;
        public Dropdown offlineDropdown;
        public Button offlineStartButton;
        public Button offlineStopButton;
        public Text CloudTypeText;
        public Text SimulatorVersionText;
        public Text UnityVersionText;
        public Button VSEButton;
        public CacheControlWindow CacheControlWindow;
        public GameObject LoadedAssetsWindow;
        public GameObject SettingsWindow;

        public enum LoaderUIStateType { START, PROGRESS, READY };
        public LoaderUIStateType LoaderUIState = LoaderUIStateType.START;

        private SimulationService simulationService = new SimulationService();
        private List<SimulationData> simulationData;
        private int selectedSim;

        public void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            SimulatorVersionText.text = $"Simulator Version: {Config.SimulatorVersion}";
            UnityVersionText.text = $"Unity Version: {Application.unityVersion}";
            ColorUtility.TryParseHtmlString("#1F2940", out offlineColor);
            ColorUtility.TryParseHtmlString("#FFFFFF", out onlineColor);
            statusButtonIcon.material.color = Color.white;
            instance = this;
            statusButton.onClick.AddListener(OnStatusButtonClicked);
            offlineStartButton.onClick.AddListener(OnOfflineStartButtonClicked);
            offlineStopButton.onClick.AddListener(OnOfflineStopButtonClicked);
            clearAssetCacheButton.onClick.AddListener(() =>
            {
                CacheControlWindow.gameObject.SetActive(true);
            });
            LoadedAssetsButton.onClick.AddListener(() =>
            {
                LoadedAssetsWindow.SetActive(true);
            });
            quitButton.onClick.AddListener(OnQuitButtonClicked);
            SettingsButton.onClick.AddListener(OnSettingsButtonClicked);
            offlineDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            ShowLocalStatus();
            TaskProgressManager.Instance.OnUpdate += UpdateDownloadProgress;
        }

        public void UpdateDownloadProgress()
        {
            var text = string.Empty;
            foreach (var item in TaskProgressManager.Instance.Tasks)
            {
                text += $"{item.Description} {Mathf.Floor(item.Progress * 100)}%\n";
            }
            if (statusText != null)
                statusText.text = text;
        }

        // Idle loader screen: simulations are started from the list of previous simulations or by Python API clients.
        // Linking to a WISE cluster and going online no longer exist.
        public void ShowLocalStatus()
        {
            if (statusText == null || statusButtonIcon == null || statusButtonText == null || statusButton == null)
                return; // fix for editor stop playmode null

            statusText.text = "Start a simulation below, or connect with the Python API after starting API Only";
            statusButtonText.text = "Local";
            statusButtonIcon.color = onlineColor;
            statusMenuButton.gameObject.SetActive(false);
            unlinkButton.gameObject.SetActive(false);
            linkButton.gameObject.SetActive(false);
            offlineDropdown.gameObject.SetActive(true);
            offlineStartButton.gameObject.SetActive(true);
            offlineStopButton.gameObject.SetActive(false);
            VSEButton.gameObject.SetActive(true);
            UpdateDropdown();
            CloudTypeText.text = $"Assets: {Config.AssetsPath}";
        }

        public void UpdateStatusText(string text)
        {
            statusText.text = text;
        }

        public void UpdateDropdown()
        {
            simulationData = simulationService.List().ToList();
            offlineDropdown.ClearOptions();
            offlineDropdown.AddOptions(simulationData.Select(s => s.Name).ToList());
            offlineDropdown.value = 0;
            selectedSim = 0;
            if (simulationData.Count == 0)
            {
                offlineDropdown.gameObject.SetActive(false);
                offlineStartButton.gameObject.SetActive(false);
            }
        }

        public void OnDropdownValueChanged(int value)
        {
            selectedSim = value;
        }

        public void OnStatusButtonClicked()
        {
            bool active = !statusMenuRoot.gameObject.activeSelf;
            statusMenuRoot.SetActive(active);
            dropdownArrow.transform.localScale = new Vector3(1, active ? 1 : -1, 1);
        }

        public void OnSettingsButtonClicked()
        {
            if (SettingsWindow == null)
                return;

            SettingsWindow.SetActive(true);
        }

        public void OnQuitButtonClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
             Application.Quit();
#endif
        }

        public void OnOfflineStartButtonClicked()
        {
            Loader.Instance.StartSimulation(simulationData[selectedSim]);
            if (simulationData[selectedSim].ApiOnly)
            {
                offlineStopButton.gameObject.SetActive(true);
            }
        }

        public void OnOfflineStopButtonClicked()
        {
            Loader.Instance.StopAsync();
        }

        public void SetVSEButtonActive(bool active)
        {
            VSEButton.gameObject.SetActive(active);
        }

        public void SetLoaderUIState(LoaderUIStateType state)
        {
            LoaderUIState = state;
            switch (LoaderUIState)
            {
                case LoaderUIStateType.START:
                    ShowLocalStatus();
                    break;
                case LoaderUIStateType.PROGRESS:
                    statusButtonText.text = "Loading...";
                    statusText.text = "Loading...";
                    offlineDropdown.gameObject.SetActive(false);
                    offlineStartButton.gameObject.SetActive(false);
                    break;
                case LoaderUIStateType.READY:
                    statusButtonText.text = "API ready!";
                    statusText.text = $"API ready! Python API listens on {Config.ApiHost}:{Config.ApiPort}";
                    offlineStopButton.gameObject.SetActive(true);
                    break;
            }
        }

        public void EnterScenarioEditor()
        {
            var nonBlockingTask = Loader.Instance.EnterScenarioEditor();
        }
    }
}
