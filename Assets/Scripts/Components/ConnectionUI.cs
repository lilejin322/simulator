/**
 * Copyright (c) 2020-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System.Linq;
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

        // Replace the list of simulations WISE sent (offlineDropdown, offlineStartButton, offlineStopButton):
        // the only simulation left is API Only
        private Button startApiButton;
        private Button stopApiButton;

        // Python API port and whether other machines may connect; they change Config for this run only
        private InputField apiPortInput;
        private Toggle apiRemoteToggle;
        // api_hostname from config.yml or --apihost, used when remote connections are off
        private string configuredApiHost;

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
            // icons are Font Awesome glyphs, as on the buttons they replace
            startApiButton = CreateApiButton("StartApiButton", "\uF144", "Start API", OnStartApiButtonClicked);
            stopApiButton = CreateApiButton("StopApiButton", "\uF04D", "Stop API", OnStopApiButtonClicked);
            configuredApiHost = Config.ApiHost;
            CreateApiSettings();
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
            ShowLocalStatus();
            TaskProgressManager.Instance.OnUpdate += UpdateDownloadProgress;
        }

        // A copy of the Visual Editor button with another icon and label, placed where the dropdown was
        Button CreateApiButton(string objectName, string icon, string label, UnityEngine.Events.UnityAction onClick)
        {
            var button = Instantiate(VSEButton, VSEButton.transform.parent);
            button.name = objectName;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                text.text = text.name == "VSEText" ? icon : label;
            }

            var position = VSEButton.transform.position;
            button.transform.position = new Vector3(position.x, offlineDropdown.transform.position.y, position.z);
            return button;
        }

        // "Port [8181]" left of the Start API button and "[ ] Allow remote" right of it, styled like the dropdown they replace
        void CreateApiSettings()
        {
            var anchor = (RectTransform)startApiButton.transform;
            var height = anchor.rect.height;
            var gap = height * 0.4f;
            var background = offlineDropdown.GetComponent<Image>();
            var buttonLabel = startApiButton.GetComponentsInChildren<Text>(true).First(t => t.name != "VSEText");
            var resources = new DefaultControls.Resources { standard = background.sprite };

            var inputWidth = height * 2.2f;
            var inputOffset = -(anchor.rect.width / 2 + gap + inputWidth / 2);
            var input = DefaultControls.CreateInputField(resources);
            input.name = "ApiPortInput";
            Place(input, anchor, inputOffset, inputWidth, height);
            CopyLook(input.GetComponent<Image>(), background);
            var stock = input.GetComponent<InputField>();
            var (textComponent, placeholder) = (stock.textComponent, stock.placeholder);
            DestroyImmediate(stock);
            apiPortInput = input.AddComponent<KeyInputField>();
            apiPortInput.targetGraphic = input.GetComponent<Image>();
            apiPortInput.textComponent = textComponent;
            apiPortInput.placeholder = placeholder;
            apiPortInput.contentType = InputField.ContentType.IntegerNumber;
            apiPortInput.characterLimit = 5;
            CopyLook(apiPortInput.textComponent, buttonLabel, TextAnchor.MiddleCenter);
            CopyLook((Text)apiPortInput.placeholder, buttonLabel, TextAnchor.MiddleCenter);
            ((Text)apiPortInput.placeholder).text = "Port";
            apiPortInput.text = Config.ApiPort.ToString();

            var labelWidth = height * 1.6f;
            var label = DefaultControls.CreateText(resources);
            label.name = "ApiPortLabel";
            Place(label, anchor, inputOffset - inputWidth / 2 - gap / 2 - labelWidth / 2, labelWidth, height);
            var labelText = label.GetComponent<Text>();
            CopyLook(labelText, buttonLabel, TextAnchor.MiddleRight);
            labelText.text = "Port";

            var toggleWidth = height * 4.5f;
            var toggle = DefaultControls.CreateToggle(resources);
            toggle.name = "ApiRemoteToggle";
            Place(toggle, anchor, anchor.rect.width / 2 + gap + toggleWidth / 2, toggleWidth, height);
            apiRemoteToggle = toggle.GetComponent<Toggle>();
            apiRemoteToggle.isOn = Config.ApiHost == "*";
            var box = (RectTransform)apiRemoteToggle.targetGraphic.transform;
            box.anchorMin = box.anchorMax = new Vector2(0, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(height * 0.6f, height * 0.6f);
            box.anchoredPosition = new Vector2(height * 0.3f, 0);
            CopyLook((Image)apiRemoteToggle.targetGraphic, background);
            var check = (RectTransform)apiRemoteToggle.graphic.transform;
            check.anchorMin = Vector2.zero;
            check.anchorMax = Vector2.one;
            check.offsetMin = check.offsetMax = Vector2.zero;
            check.localScale = Vector3.one * 0.6f;
            apiRemoteToggle.graphic.color = startApiButton.GetComponent<Image>().color;
            var toggleLabel = toggle.GetComponentInChildren<Text>();
            var toggleLabelRect = (RectTransform)toggleLabel.transform;
            toggleLabelRect.offsetMin = new Vector2(height * 0.8f, 0);
            toggleLabelRect.offsetMax = Vector2.zero;
            CopyLook(toggleLabel, buttonLabel, TextAnchor.MiddleLeft);
            toggleLabel.text = "Allow remote";
        }

        // Same parent, size and vertical center as the anchor, moved horizontally by offset (in the anchor's units)
        static void Place(GameObject control, RectTransform anchor, float offset, float width, float height)
        {
            var rect = (RectTransform)control.transform;
            rect.SetParent(anchor.parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.position = anchor.position + anchor.TransformVector(new Vector3(offset, 0, 0));
        }

        static void CopyLook(Image image, Image from)
        {
            image.sprite = from.sprite;
            image.type = from.type;
            image.color = from.color;
        }

        static void CopyLook(Text text, Text from, TextAnchor alignment)
        {
            text.font = from.font;
            text.fontSize = from.fontSize;
            text.color = from.color;
            text.alignment = alignment;
        }

        void SetApiSettingsInteractable(bool interactable)
        {
            apiPortInput.interactable = interactable;
            apiRemoteToggle.interactable = interactable;
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

        // Idle loader screen: start the Python API or the Visual Editor.
        // Linking to a WISE cluster, going online and the list of simulations WISE sent no longer exist.
        public void ShowLocalStatus()
        {
            if (statusText == null || statusButtonIcon == null || statusButtonText == null || statusButton == null)
                return; // fix for editor stop playmode null

            statusText.text = "Start the API to connect with the Python API, or open the Visual Editor";
            statusButtonText.text = "Local";
            statusButtonIcon.color = onlineColor;
            statusMenuButton.gameObject.SetActive(false);
            unlinkButton.gameObject.SetActive(false);
            linkButton.gameObject.SetActive(false);
            offlineDropdown.gameObject.SetActive(false);
            offlineStartButton.gameObject.SetActive(false);
            offlineStopButton.gameObject.SetActive(false);
            startApiButton.gameObject.SetActive(true);
            stopApiButton.gameObject.SetActive(false);
            SetApiSettingsInteractable(true);
            VSEButton.gameObject.SetActive(true);
            CloudTypeText.text = $"Assets: {Config.AssetsPath}";
        }

        public void UpdateStatusText(string text)
        {
            statusText.text = text;
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

        public void OnStartApiButtonClicked()
        {
            if (!int.TryParse(apiPortInput.text, out var port) || port < 1 || port > 65535)
            {
                statusText.text = "Port must be a number from 1 to 65535";
                return;
            }

            Config.ApiPort = port;
            Config.ApiHost = apiRemoteToggle.isOn ? "*" : configuredApiHost == "*" ? "localhost" : configuredApiHost;
            Loader.Instance.StartSimulation(SimulationConfigUtils.CreateApiOnlySimulation());
        }

        public void OnStopApiButtonClicked()
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
                    startApiButton.gameObject.SetActive(false);
                    stopApiButton.gameObject.SetActive(false);
                    SetApiSettingsInteractable(false);
                    break;
                case LoaderUIStateType.READY:
                    statusButtonText.text = "API ready!";
                    var address = Config.ApiHost == "*" ? $"port {Config.ApiPort} of all network interfaces" : $"{Config.ApiHost}:{Config.ApiPort}";
                    statusText.text = $"API ready! Python API listens on {address}";
                    stopApiButton.gameObject.SetActive(true);
                    SetApiSettingsInteractable(false);
                    break;
            }
        }

        public void EnterScenarioEditor()
        {
            var nonBlockingTask = Loader.Instance.EnterScenarioEditor();
        }
    }
}
