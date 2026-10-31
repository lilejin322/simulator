/**
 * Copyright (c) 2020-2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using System.IO;
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

        // Python API port, whether other machines may connect and headless; they change Config for this run only
        private InputField apiPortInput;
        private Toggle apiRemoteToggle;
        private Toggle apiHeadlessToggle;
        // Local asset library folder, from config.yml at startup; a change applies to this run only
        private InputField assetsPathInput;
        private Button browseAssetsButton;

        // the status line's own color, and the one for problems to fix (no asset library, wrong port, ...)
        private Color statusColor;
        private static readonly Color WarningColor = new Color(1f, 0.78f, 0.34f);
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
            statusColor = statusText.color;
            instance = this;
            statusButton.onClick.AddListener(OnStatusButtonClicked);
            // icons are Font Awesome glyphs, as on the buttons they replace
            startApiButton = CreateApiButton("StartApiButton", "\uF144", "Start API", OnStartApiButtonClicked);
            stopApiButton = CreateApiButton("StopApiButton", "\uF04D", "Stop API", OnStopApiButtonClicked);
            configuredApiHost = Config.ApiHost;
            CreateApiSettings();
            CreateAssetsPathInput();
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

        // "Port [8181]" left of the Start API button, "[ ] Allow remote" and "[ ] Headless" right of it,
        // styled like the dropdown they replace
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
            apiPortInput = CreateInput("ApiPortInput", background, buttonLabel, TextAnchor.MiddleCenter, "Port");
            Place(apiPortInput.gameObject, anchor, inputOffset, inputWidth, height);
            apiPortInput.contentType = InputField.ContentType.IntegerNumber;
            apiPortInput.characterLimit = 5;
            apiPortInput.text = Config.ApiPort.ToString();

            var labelWidth = height * 1.6f;
            var label = DefaultControls.CreateText(resources);
            label.name = "ApiPortLabel";
            Place(label, anchor, inputOffset - inputWidth / 2 - gap / 2 - labelWidth / 2, labelWidth, height);
            var labelText = label.GetComponent<Text>();
            CopyLook(labelText, buttonLabel, TextAnchor.MiddleRight);
            labelText.text = "Port";

            // checkboxes in a column right of the Start API button
            var toggleWidth = height * 4.5f;
            var toggleOffset = anchor.rect.width / 2 + gap + toggleWidth / 2;
            Toggle CreateToggle(string objectName, string text, bool isOn, float offsetY)
            {
                var toggle = DefaultControls.CreateToggle(resources);
                toggle.name = objectName;
                Place(toggle, anchor, toggleOffset, toggleWidth, height, offsetY);
                var component = toggle.GetComponent<Toggle>();
                component.isOn = isOn;
                var box = (RectTransform)component.targetGraphic.transform;
                box.anchorMin = box.anchorMax = new Vector2(0, 0.5f);
                box.pivot = new Vector2(0.5f, 0.5f);
                box.sizeDelta = new Vector2(height * 0.6f, height * 0.6f);
                box.anchoredPosition = new Vector2(height * 0.3f, 0);
                CopyLook((Image)component.targetGraphic, background);
                var check = (RectTransform)component.graphic.transform;
                check.anchorMin = Vector2.zero;
                check.anchorMax = Vector2.one;
                check.offsetMin = check.offsetMax = Vector2.zero;
                check.localScale = Vector3.one * 0.6f;
                component.graphic.color = startApiButton.GetComponent<Image>().color;
                var toggleLabel = toggle.GetComponentInChildren<Text>();
                var toggleLabelRect = (RectTransform)toggleLabel.transform;
                toggleLabelRect.offsetMin = new Vector2(height * 0.8f, 0);
                toggleLabelRect.offsetMax = Vector2.zero;
                CopyLook(toggleLabel, buttonLabel, TextAnchor.MiddleLeft);
                toggleLabel.text = text;
                return component;
            }

            apiRemoteToggle = CreateToggle("ApiRemoteToggle", "Allow remote", Config.ApiHost == "*", 0);
            apiHeadlessToggle = CreateToggle("ApiHeadlessToggle", "Headless", Config.Headless, -height * 0.9f);
        }

        // A KeyInputField (see there why not a plain InputField) looking like the given background and text
        static InputField CreateInput(string objectName, Image background, Text font, TextAnchor alignment, string placeholderText)
        {
            var input = DefaultControls.CreateInputField(new DefaultControls.Resources { standard = background.sprite });
            input.name = objectName;
            CopyLook(input.GetComponent<Image>(), background);
            var stock = input.GetComponent<InputField>();
            var (textComponent, placeholder) = (stock.textComponent, (Text)stock.placeholder);
            DestroyImmediate(stock);
            var field = input.AddComponent<KeyInputField>();
            field.targetGraphic = input.GetComponent<Image>();
            field.textComponent = textComponent;
            field.placeholder = placeholder;
            CopyLook(textComponent, font, alignment);
            CopyLook(placeholder, font, alignment);
            placeholder.text = placeholderText;
            return field;
        }

        // "Assets [path] [folder]" centered below the Visual Editor button, laid out like the "Port [8181]" row:
        // the local asset library, which can be switched for this run. It replaces "Assets: <path>" in the bottom left.
        void CreateAssetsPathInput()
        {
            CloudTypeText.gameObject.SetActive(false);
            var anchor = (RectTransform)VSEButton.transform;
            var height = anchor.rect.height;
            var gap = height * 0.4f;
            var offsetY = -height * 1.6f;
            var width = ((RectTransform)offlineDropdown.transform).rect.width;
            var buttonLabel = startApiButton.GetComponentsInChildren<Text>(true).First(t => t.name != "VSEText");

            assetsPathInput = CreateInput("AssetsPathInput", offlineDropdown.GetComponent<Image>(), buttonLabel, TextAnchor.MiddleLeft, "Asset library folder");
            Place(assetsPathInput.gameObject, anchor, 0, width, height, offsetY);
            // paths are long: a smaller font than the buttons
            var pathFontSize = Mathf.RoundToInt(buttonLabel.fontSize * 0.75f);
            assetsPathInput.textComponent.fontSize = pathFontSize;
            ((Text)assetsPathInput.placeholder).fontSize = pathFontSize;
            assetsPathInput.text = Config.AssetsPath;
            assetsPathInput.onEndEdit.AddListener(OnAssetsPathEdited);

            var labelWidth = height * 2.2f;
            var label = DefaultControls.CreateText(new DefaultControls.Resources());
            label.name = "AssetsPathLabel";
            Place(label, anchor, -(width / 2 + gap / 2 + labelWidth / 2), labelWidth, height, offsetY);
            var labelText = label.GetComponent<Text>();
            CopyLook(labelText, buttonLabel, TextAnchor.MiddleRight);
            labelText.text = "Assets";

            // folder dialog: a copy of the old square start button with a Font Awesome folder icon, right of the field
            if (FolderDialogAvailable)
            {
                browseAssetsButton = Instantiate(offlineStartButton, anchor.parent);
                browseAssetsButton.name = "BrowseAssetsButton";
                browseAssetsButton.onClick = new Button.ButtonClickedEvent();
                browseAssetsButton.onClick.AddListener(OnBrowseAssetsClicked);
                browseAssetsButton.GetComponentInChildren<Text>(true).text = "\uF07C";
                Place(browseAssetsButton.gameObject, anchor, width / 2 + gap / 2 + height / 2, height, height, offsetY);
                browseAssetsButton.gameObject.SetActive(true);
            }
        }

        // Linux desktops show a native folder dialog through zenity
        static bool FolderDialogAvailable =>
            (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor) &&
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(dir => File.Exists(Path.Combine(dir, "zenity")));

        async void OnBrowseAssetsClicked()
        {
            browseAssetsButton.interactable = false;
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("zenity",
                    $"--file-selection --directory --title=\"Local asset library\" --filename=\"{Config.AssetsPath.Replace("\"", "\\\"")}/\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                // zenity prints the chosen folder, or nothing with a non-zero exit code when cancelled
                var folder = await System.Threading.Tasks.Task.Run(() =>
                {
                    using (var process = System.Diagnostics.Process.Start(start))
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        return process.ExitCode == 0 ? output.Trim() : null;
                    }
                });
                if (!string.IsNullOrEmpty(folder))
                {
                    assetsPathInput.text = folder;
                    OnAssetsPathEdited(folder);
                }
            }
            catch (Exception e)
            {
                SetStatus($"Cannot open the folder dialog: {e.Message}", true);
            }
            finally
            {
                browseAssetsButton.interactable = LoaderUIState == LoaderUIStateType.START;
            }
        }

        void OnAssetsPathEdited(string path)
        {
            // Reconfirming the current path must validate and reload it too: a rejected edit keeps
            // Config.AssetsPath unchanged, and a missing library may have been restored on disk.
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Config.AssetsPath;
            }

            try
            {
                var error = Config.SetAssetsPath(path);
                if (error == null)
                {
                    ShowLocalStatus();
                    SetStatus($"Asset library for this run: {LocalAssetLibrary.Describe()}");
                }
                else
                {
                    ShowLocalStatus();
                    SetStatus($"Asset library not changed: {error}", true);
                }
            }
            catch (Exception e)
            {
                // Invalid path syntax and filesystem failures should not break the edit callback.
                ShowLocalStatus();
                SetStatus($"Cannot use asset library: {e.Message}", true);
            }
            finally
            {
                assetsPathInput.text = Config.AssetsPath;
            }
        }

        // Same parent, size and center as the anchor, moved by offsetX and offsetY (in the anchor's units)
        static void Place(GameObject control, RectTransform anchor, float offsetX, float width, float height, float offsetY = 0)
        {
            var rect = (RectTransform)control.transform;
            rect.SetParent(anchor.parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.position = anchor.position + anchor.TransformVector(new Vector3(offsetX, offsetY, 0));
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
            apiHeadlessToggle.interactable = interactable;
            // the asset library cannot change under a running simulation
            assetsPathInput.interactable = interactable;
            if (browseAssetsButton != null)
            {
                browseAssetsButton.interactable = interactable;
            }
        }

        public void UpdateDownloadProgress()
        {
            var text = string.Empty;
            foreach (var item in TaskProgressManager.Instance.Tasks)
            {
                text += $"{item.Description} {Mathf.Floor(item.Progress * 100)}%\n";
            }
            if (statusText != null)
                SetStatus(text);
        }

        // Idle loader screen: start the Python API or the Visual Editor.
        // Linking to a WISE cluster, going online and the list of simulations WISE sent no longer exist.
        public void ShowLocalStatus()
        {
            if (statusText == null || statusButtonIcon == null || statusButtonText == null || statusButton == null)
                return; // fix for editor stop playmode null

            // no Python API and no Visual Editor without an asset library
            var hasLibrary = LocalAssetLibrary.Exists;
            if (hasLibrary)
            {
                SetStatus("Start the API to connect with the Python API, or open the Visual Editor");
            }
            else
            {
                SetStatus($"Asset library not found: {Config.AssetsPath}\nFix assets_path in config.yml, or choose a folder below", true);
            }
            statusButtonText.text = "Local";
            statusButtonIcon.color = onlineColor;
            statusMenuButton.gameObject.SetActive(false);
            unlinkButton.gameObject.SetActive(false);
            linkButton.gameObject.SetActive(false);
            offlineDropdown.gameObject.SetActive(false);
            offlineStartButton.gameObject.SetActive(false);
            offlineStopButton.gameObject.SetActive(false);
            startApiButton.gameObject.SetActive(true);
            startApiButton.interactable = hasLibrary;
            stopApiButton.gameObject.SetActive(false);
            SetApiSettingsInteractable(true);
            VSEButton.gameObject.SetActive(true);
            VSEButton.interactable = hasLibrary;
            assetsPathInput.text = Config.AssetsPath;
        }

        // Loader reports failures here
        public void UpdateStatusText(string text)
        {
            SetStatus(text, true);
        }

        void SetStatus(string text, bool warning = false)
        {
            statusText.text = text;
            statusText.color = warning ? WarningColor : statusColor;
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
            if (!LocalAssetLibrary.Exists)
            {
                ShowLocalStatus();
                return;
            }

            if (!int.TryParse(apiPortInput.text, out var port) || port < 1 || port > 65535)
            {
                SetStatus("Port must be a number from 1 to 65535", true);
                return;
            }

            Config.ApiPort = port;
            Config.ApiHost = apiRemoteToggle.isOn ? "*" : configuredApiHost == "*" ? "localhost" : configuredApiHost;
            Config.Headless = apiHeadlessToggle.isOn;
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
                    SetStatus("Loading...");
                    startApiButton.gameObject.SetActive(false);
                    stopApiButton.gameObject.SetActive(false);
                    SetApiSettingsInteractable(false);
                    break;
                case LoaderUIStateType.READY:
                    statusButtonText.text = "API ready!";
                    var address = Config.ApiHost == "*" ? $"port {Config.ApiPort} of all network interfaces" : $"{Config.ApiHost}:{Config.ApiPort}";
                    SetStatus($"API ready! Python API listens on {address}");
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
