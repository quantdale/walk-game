using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.UI
{
    /// <summary>
    /// Player-facing HUD for the vertical slice. It is composed from reusable cards,
    /// uses a safe-area root, and reads only derived domain/UI view data. Technical
    /// provider errors never cross into player copy.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        private UiContext _context;
        private RectTransform _safeRoot;
        private Text _vitalityLabel;
        private Text _resourceLabel;
        private Text _modeLabel;
        private Text _regionLabel;
        private Text _stepsLabel;
        private Text _goalLabel;
        private Text _permissionLabel;
        private Text _interactionLabel;
        private Text _expeditionLabel;
        private Text _expeditionProgress;
        private Text _onboardingLabel;
        private Text _builderLabel;
        private Text _builderHint;
        private Text _settingsLabel;
        private Button _toggleButton;
        private Button _settingsButton;
        private Button _permissionButton;
        private Button _expeditionWalkButton;
        private Button _expeditionRunButton;
        private Button _expeditionStopButton;
        private Button _builderMoveButton;
        private Button _builderRotateButton;
        private Button _builderConfirmButton;
        private Button _builderCancelButton;
        private Button _builderResetButton;
        private GameObject _permissionBanner;
        private GameObject _productionPanel;
        private GameObject _builderPanel;
        private GameObject _interactionPanel;
        private GameObject _expeditionPanel;
        private GameObject _onboardingPanel;
        private GameObject _settingsPanel;
        private GameObject _joystickPanel;
        private MobileJoystick _joystick;
        private RectTransform _producerRowsRoot;
        private RectTransform _onboardingCard;

        private readonly Dictionary<string, ProducerRow> _producerRows = new Dictionary<string, ProducerRow>();

        public bool IsSettingsVisible => _settingsPanel != null && _settingsPanel.activeSelf;

        private sealed class ProducerRow
        {
            public GameObject root;
            public Text label;
            public Button collect;
        }

        public void Bind(UiContext context)
        {
            _context = context;
            Refresh();
        }

        private void Awake()
        {
            BuildUi();
        }

        public void Refresh()
        {
            var profile = _context?.GetProfile?.Invoke();
            if (profile == null || _vitalityLabel == null)
            {
                return;
            }

            _vitalityLabel.text = $"{profile.vitalityBalance:N0}\nVITALITY";
            _resourceLabel.text = BuildResourceText(profile.resources);
            _stepsLabel.text = $"{profile.lifetimeAcceptedSteps:N0} lifetime steps";
            _regionLabel.text = $"ASHFALL BASIN  ·  STAGE {GetStage(profile)}/3";
            _goalLabel.text = _context.GetNextGoal?.Invoke() ?? "Choose a restoration project to begin.";

            bool explore = _context.GetIsExplore?.Invoke() ?? false;
            _modeLabel.text = explore ? "EXPLORE MODE" : "BUILDER MODE";
            SetButtonLabel(_toggleButton, explore ? "Return to Builder" : "Enter Explore");
            RefreshPermissionBanner();
            RefreshProduction();
            RefreshBuilder();
            RefreshExpedition();
            RefreshInteraction();
            RefreshOnboarding();
            RefreshSettings();

            if (_joystickPanel != null)
            {
                _joystickPanel.SetActive(explore);
            }
        }

        private void BuildUi()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            var canvasComponent = canvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComponent.sortingOrder = 20;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var safeGo = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            safeGo.transform.SetParent(canvas.transform, false);
            _safeRoot = safeGo.GetComponent<RectTransform>();
            _safeRoot.anchorMin = Vector2.zero;
            _safeRoot.anchorMax = Vector2.one;
            _safeRoot.offsetMin = Vector2.zero;
            _safeRoot.offsetMax = Vector2.zero;

            BuildTopBar();
            BuildPermissionBanner();
            BuildProductionPanel();
            BuildBuilderPanel();
            BuildExpeditionPanel();
            BuildInteractionPanel();
            BuildOnboardingPanel();
            BuildSettingsPanel();
            BuildExploreJoystick();
        }

        public void ToggleSettings()
        {
            if (_settingsPanel == null)
            {
                return;
            }

            _settingsPanel.SetActive(!_settingsPanel.activeSelf);
            RefreshSettings();
        }

        private void BuildTopBar()
        {
            var top = CreatePanel(_safeRoot, "TopBar", new Vector2(0.015f, 0.84f), new Vector2(0.985f, 0.985f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.075f, 0.085f, 0.94f));
            top.offsetMin = Vector2.zero;
            top.offsetMax = Vector2.zero;

            var vitalityCard = CreatePanel(top, "VitalityCard", new Vector2(0.015f, 0.12f), new Vector2(0.16f, 0.88f),
                Vector2.zero, Vector2.zero, new Color(0.19f, 0.31f, 0.27f, 1f));
            vitalityCard.offsetMin = Vector2.zero;
            vitalityCard.offsetMax = Vector2.zero;
            _vitalityLabel = CreateText(vitalityCard, "Vitality", 28, TextAnchor.MiddleCenter, Color.white);
            _vitalityLabel.rectTransform.anchorMin = Vector2.zero;
            _vitalityLabel.rectTransform.anchorMax = Vector2.one;
            _vitalityLabel.rectTransform.offsetMin = Vector2.zero;
            _vitalityLabel.rectTransform.offsetMax = Vector2.zero;

            _regionLabel = CreateText(top, "Region", 19, TextAnchor.UpperLeft, new Color(0.88f, 0.91f, 0.84f));
            SetAnchors(_regionLabel.rectTransform, new Vector2(0.19f, 0.58f), new Vector2(0.52f, 0.91f), new Vector2(12f, 0f), new Vector2(-8f, 0f));
            _stepsLabel = CreateText(top, "Steps", 16, TextAnchor.LowerLeft, new Color(0.65f, 0.73f, 0.7f));
            SetAnchors(_stepsLabel.rectTransform, new Vector2(0.19f, 0.1f), new Vector2(0.52f, 0.55f), new Vector2(12f, 0f), new Vector2(-8f, 0f));

            _resourceLabel = CreateText(top, "Resources", 15, TextAnchor.MiddleLeft, new Color(0.8f, 0.84f, 0.77f));
            SetAnchors(_resourceLabel.rectTransform, new Vector2(0.54f, 0.1f), new Vector2(0.76f, 0.9f), Vector2.zero, Vector2.zero);

            _modeLabel = CreateText(top, "Mode", 16, TextAnchor.MiddleCenter, new Color(0.94f, 0.76f, 0.42f));
            SetAnchors(_modeLabel.rectTransform, new Vector2(0.77f, 0.58f), new Vector2(0.86f, 0.91f), Vector2.zero, Vector2.zero);
            _toggleButton = CreateButton(top, "ModeToggle", new Vector2(0f, 0f), new Vector2(170f, 48f), "Enter Explore",
                () => _context?.ToggleExploreRequested?.Invoke());
            SetAnchors(_toggleButton.GetComponent<RectTransform>(), new Vector2(0.77f, 0.1f), new Vector2(0.985f, 0.55f), Vector2.zero, Vector2.zero);

            _settingsButton = CreateButton(top, "Settings", Vector2.zero, new Vector2(90f, 36f), "Settings", ToggleSettings);
            SetAnchors(_settingsButton.GetComponent<RectTransform>(), new Vector2(0.64f, 0.05f), new Vector2(0.75f, 0.42f), Vector2.zero, Vector2.zero);

            var goalCard = CreatePanel(_safeRoot, "GoalCard", new Vector2(0.28f, 0.765f), new Vector2(0.72f, 0.84f),
                Vector2.zero, Vector2.zero, new Color(0.08f, 0.12f, 0.13f, 0.92f));
            goalCard.offsetMin = Vector2.zero;
            goalCard.offsetMax = Vector2.zero;
            _goalLabel = CreateText(goalCard, "Goal", 16, TextAnchor.MiddleCenter, new Color(0.92f, 0.9f, 0.78f));
            _goalLabel.rectTransform.anchorMin = Vector2.zero;
            _goalLabel.rectTransform.anchorMax = Vector2.one;
            _goalLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
            _goalLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);
        }

        private void BuildPermissionBanner()
        {
            _permissionBanner = CreatePanel(_safeRoot, "MotionPermissionBanner", new Vector2(0.28f, 0.68f), new Vector2(0.72f, 0.755f),
                Vector2.zero, Vector2.zero, new Color(0.12f, 0.19f, 0.22f, 0.96f)).gameObject;
            var rect = _permissionBanner.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _permissionLabel = CreateText(_permissionBanner.transform, "Copy", 15, TextAnchor.MiddleLeft, Color.white);
            SetAnchors(_permissionLabel.rectTransform, new Vector2(0.02f, 0.05f), new Vector2(0.73f, 0.95f), Vector2.zero, Vector2.zero);
            _permissionButton = CreateButton(_permissionBanner.transform, "Enable", Vector2.zero, new Vector2(150f, 42f), "Enable",
                () => _context?.EnableMotionAccessRequested?.Invoke());
            SetAnchors(_permissionButton.GetComponent<RectTransform>(), new Vector2(0.75f, 0.12f), new Vector2(0.98f, 0.88f), Vector2.zero, Vector2.zero);
        }

        private void BuildProductionPanel()
        {
            _productionPanel = CreatePanel(_safeRoot, "ProductionPanel", new Vector2(0.75f, 0.02f), new Vector2(0.985f, 0.32f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.075f, 0.085f, 0.94f)).gameObject;
            var rect = _productionPanel.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var title = CreateText(_productionPanel.transform, "Title", 17, TextAnchor.MiddleLeft, new Color(0.9f, 0.84f, 0.61f));
            SetAnchors(title.rectTransform, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.98f), Vector2.zero, Vector2.zero);
            title.text = "RESTORED SYSTEMS";
            var rows = new GameObject("Rows", typeof(RectTransform));
            rows.transform.SetParent(_productionPanel.transform, false);
            _producerRowsRoot = rows.GetComponent<RectTransform>();
            SetAnchors(_producerRowsRoot, new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.8f), Vector2.zero, Vector2.zero);
            var collectAll = CreateButton(_productionPanel.transform, "CollectAll", Vector2.zero, new Vector2(130f, 34f), "Collect all",
                () => _context?.CollectAllRequested?.Invoke());
            SetAnchors(collectAll.GetComponent<RectTransform>(), new Vector2(0.56f, 0.02f), new Vector2(0.97f, 0.17f), Vector2.zero, Vector2.zero);
        }

        private void BuildBuilderPanel()
        {
            _builderPanel = CreatePanel(_safeRoot, "BuilderSelection", new Vector2(0.75f, 0.34f), new Vector2(0.985f, 0.57f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.075f, 0.085f, 0.94f)).gameObject;
            var rect = _builderPanel.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _builderLabel = CreateText(_builderPanel.transform, "Selection", 16, TextAnchor.UpperLeft, Color.white);
            SetAnchors(_builderLabel.rectTransform, new Vector2(0.05f, 0.48f), new Vector2(0.95f, 0.96f), Vector2.zero, Vector2.zero);
            _builderHint = CreateText(_builderPanel.transform, "Hint", 13, TextAnchor.LowerLeft, new Color(0.69f, 0.75f, 0.74f));
            SetAnchors(_builderHint.rectTransform, new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.5f), Vector2.zero, Vector2.zero);
            _builderMoveButton = CreateButton(_builderPanel.transform, "Move", Vector2.zero, new Vector2(78f, 34f), "Move",
                () => _context?.BeginBuildingMoveRequested?.Invoke());
            _builderRotateButton = CreateButton(_builderPanel.transform, "Rotate", Vector2.zero, new Vector2(78f, 34f), "Rotate",
                () => _context?.RotateBuildingRequested?.Invoke());
            _builderConfirmButton = CreateButton(_builderPanel.transform, "Confirm", Vector2.zero, new Vector2(78f, 34f), "Place",
                () => _context?.ConfirmBuildingMoveRequested?.Invoke());
            _builderCancelButton = CreateButton(_builderPanel.transform, "Cancel", Vector2.zero, new Vector2(78f, 34f), "Cancel",
                () => _context?.CancelBuildingMoveRequested?.Invoke());
            _builderResetButton = CreateButton(_builderPanel.transform, "Reset", Vector2.zero, new Vector2(78f, 34f), "Reset",
                () => _context?.ResetBuildingPreviewRequested?.Invoke());
            PositionRow(_builderMoveButton, 0.04f, 0.04f, 0.23f, 0.3f);
            PositionRow(_builderRotateButton, 0.25f, 0.04f, 0.44f, 0.3f);
            PositionRow(_builderConfirmButton, 0.46f, 0.04f, 0.67f, 0.3f);
            PositionRow(_builderCancelButton, 0.69f, 0.04f, 0.86f, 0.3f);
            PositionRow(_builderResetButton, 0.87f, 0.04f, 0.98f, 0.3f);
        }

        private void BuildExpeditionPanel()
        {
            _expeditionPanel = CreatePanel(_safeRoot, "ExpeditionPanel", new Vector2(0.32f, 0.02f), new Vector2(0.72f, 0.17f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.075f, 0.085f, 0.94f)).gameObject;
            var rect = _expeditionPanel.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _expeditionLabel = CreateText(_expeditionPanel.transform, "Status", 15, TextAnchor.UpperLeft, Color.white);
            SetAnchors(_expeditionLabel.rectTransform, new Vector2(0.04f, 0.55f), new Vector2(0.58f, 0.94f), Vector2.zero, Vector2.zero);
            _expeditionProgress = CreateText(_expeditionPanel.transform, "Progress", 13, TextAnchor.LowerLeft, new Color(0.68f, 0.77f, 0.74f));
            SetAnchors(_expeditionProgress.rectTransform, new Vector2(0.04f, 0.08f), new Vector2(0.58f, 0.53f), Vector2.zero, Vector2.zero);
            _expeditionWalkButton = CreateButton(_expeditionPanel.transform, "Walk", Vector2.zero, new Vector2(92f, 36f), "Walk",
                () => _context?.StartWalkExpeditionRequested?.Invoke());
            _expeditionRunButton = CreateButton(_expeditionPanel.transform, "Run", Vector2.zero, new Vector2(92f, 36f), "Run",
                () => _context?.StartRunExpeditionRequested?.Invoke());
            _expeditionStopButton = CreateButton(_expeditionPanel.transform, "Stop", Vector2.zero, new Vector2(92f, 36f), "Finish",
                () => _context?.FinishExpeditionRequested?.Invoke());
            PositionRow(_expeditionWalkButton, 0.6f, 0.2f, 0.78f, 0.82f);
            PositionRow(_expeditionRunButton, 0.79f, 0.2f, 0.97f, 0.82f);
            PositionRow(_expeditionStopButton, 0.79f, 0.2f, 0.97f, 0.82f);
        }

        private void BuildInteractionPanel()
        {
            _interactionPanel = CreatePanel(_safeRoot, "InteractionPanel", new Vector2(0.32f, 0.18f), new Vector2(0.72f, 0.27f),
                Vector2.zero, Vector2.zero, new Color(0.08f, 0.12f, 0.13f, 0.96f)).gameObject;
            var rect = _interactionPanel.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _interactionLabel = CreateText(_interactionPanel.transform, "Prompt", 15, TextAnchor.MiddleLeft, Color.white);
            SetAnchors(_interactionLabel.rectTransform, new Vector2(0.04f, 0.05f), new Vector2(0.76f, 0.95f), Vector2.zero, Vector2.zero);
            var interact = CreateButton(_interactionPanel.transform, "Interact", Vector2.zero, new Vector2(130f, 40f), "Inspect",
                () => _context?.InteractRequested?.Invoke());
            SetAnchors(interact.GetComponent<RectTransform>(), new Vector2(0.78f, 0.1f), new Vector2(0.97f, 0.9f), Vector2.zero, Vector2.zero);
        }

        private void BuildOnboardingPanel()
        {
            _onboardingPanel = CreatePanel(_safeRoot, "Onboarding", new Vector2(0.31f, 0.38f), new Vector2(0.69f, 0.65f),
                Vector2.zero, Vector2.zero, new Color(0.07f, 0.1f, 0.11f, 0.98f)).gameObject;
            _onboardingCard = _onboardingPanel.GetComponent<RectTransform>();
            _onboardingCard.offsetMin = Vector2.zero;
            _onboardingCard.offsetMax = Vector2.zero;
            var title = CreateText(_onboardingPanel.transform, "Title", 19, TextAnchor.UpperCenter, new Color(0.94f, 0.76f, 0.42f));
            SetAnchors(title.rectTransform, new Vector2(0.06f, 0.75f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);
            title.text = "THE FIRST SPARK";
            _onboardingLabel = CreateText(_onboardingPanel.transform, "Copy", 17, TextAnchor.MiddleCenter, Color.white);
            SetAnchors(_onboardingLabel.rectTransform, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.75f), Vector2.zero, Vector2.zero);
            var advance = CreateButton(_onboardingPanel.transform, "Continue", Vector2.zero, new Vector2(120f, 40f), "Continue",
                () => _context?.AdvanceOnboardingRequested?.Invoke());
            SetAnchors(advance.GetComponent<RectTransform>(), new Vector2(0.58f, 0.05f), new Vector2(0.94f, 0.22f), Vector2.zero, Vector2.zero);
            var dismiss = CreateButton(_onboardingPanel.transform, "Dismiss", Vector2.zero, new Vector2(90f, 40f), "Later",
                () => _context?.DismissOnboardingRequested?.Invoke());
            SetAnchors(dismiss.GetComponent<RectTransform>(), new Vector2(0.06f, 0.05f), new Vector2(0.38f, 0.22f), Vector2.zero, Vector2.zero);
        }

        private void BuildSettingsPanel()
        {
            _settingsPanel = CreatePanel(_safeRoot, "SettingsPanel", new Vector2(0.35f, 0.3f), new Vector2(0.65f, 0.7f),
                Vector2.zero, Vector2.zero, new Color(0.055f, 0.075f, 0.085f, 0.98f)).gameObject;
            var panelRect = _settingsPanel.GetComponent<RectTransform>();
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var title = CreateText(_settingsPanel.transform, "Title", 19, TextAnchor.UpperCenter,
                new Color(0.94f, 0.76f, 0.42f));
            SetAnchors(title.rectTransform, new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.97f), Vector2.zero, Vector2.zero);
            title.text = "SETTINGS";
            _settingsLabel = CreateText(_settingsPanel.transform, "Values", 15, TextAnchor.UpperCenter, Color.white);
            SetAnchors(_settingsLabel.rectTransform, new Vector2(0.08f, 0.64f), new Vector2(0.92f, 0.83f), Vector2.zero, Vector2.zero);

            CreateSettingAdjuster("Master -", 0.48f, 0.08f, 0.48f, () => _context?.AdjustMasterVolumeRequested?.Invoke(-0.1f));
            CreateSettingAdjuster("Master +", 0.48f, 0.52f, 0.92f, () => _context?.AdjustMasterVolumeRequested?.Invoke(0.1f));
            CreateSettingAdjuster("Music -", 0.34f, 0.08f, 0.48f, () => _context?.AdjustMusicVolumeRequested?.Invoke(-0.1f));
            CreateSettingAdjuster("Music +", 0.34f, 0.52f, 0.92f, () => _context?.AdjustMusicVolumeRequested?.Invoke(0.1f));
            CreateSettingAdjuster("SFX -", 0.20f, 0.08f, 0.48f, () => _context?.AdjustEffectsVolumeRequested?.Invoke(-0.1f));
            CreateSettingAdjuster("SFX +", 0.20f, 0.52f, 0.92f, () => _context?.AdjustEffectsVolumeRequested?.Invoke(0.1f));

            var haptics = CreateButton(_settingsPanel.transform, "Haptics", Vector2.zero, new Vector2(130f, 36f), "Haptics",
                () => _context?.ToggleHapticsRequested?.Invoke());
            SetAnchors(haptics.GetComponent<RectTransform>(), new Vector2(0.08f, 0.02f), new Vector2(0.48f, 0.13f), Vector2.zero, Vector2.zero);
            var close = CreateButton(_settingsPanel.transform, "Close", Vector2.zero, new Vector2(100f, 36f), "Close", ToggleSettings);
            SetAnchors(close.GetComponent<RectTransform>(), new Vector2(0.56f, 0.02f), new Vector2(0.92f, 0.13f), Vector2.zero, Vector2.zero);
            _settingsPanel.SetActive(false);
        }

        private void CreateSettingAdjuster(string label, float y, float xMin, float xMax, Action action)
        {
            var button = CreateButton(_settingsPanel.transform, label, Vector2.zero, new Vector2(112f, 32f), label, action);
            SetAnchors(button.GetComponent<RectTransform>(), new Vector2(xMin, y), new Vector2(xMax, y + 0.1f), Vector2.zero, Vector2.zero);
        }

        private void BuildExploreJoystick()
        {
            _joystickPanel = CreatePanel(_safeRoot, "ExploreJoystick", new Vector2(0.03f, 0.05f), new Vector2(0.2f, 0.27f),
                Vector2.zero, Vector2.zero, new Color(0.08f, 0.12f, 0.12f, 0.62f)).gameObject;
            var rect = _joystickPanel.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var knobGo = new GameObject("Knob", typeof(Image));
            knobGo.transform.SetParent(_joystickPanel.transform, false);
            var knob = knobGo.GetComponent<RectTransform>();
            knob.anchorMin = new Vector2(0.5f, 0.5f);
            knob.anchorMax = new Vector2(0.5f, 0.5f);
            knob.sizeDelta = new Vector2(70f, 70f);
            knobGo.GetComponent<Image>().color = new Color(0.35f, 0.72f, 0.53f, 0.8f);
            _joystick = _joystickPanel.AddComponent<MobileJoystick>();
            _joystick.Configure(knob);
            _joystick.ValueChanged += value => _context?.ExploreMoveInputChanged?.Invoke(value);
        }

        private void RefreshPermissionBanner()
        {
            if (_permissionBanner == null)
            {
                return;
            }

            var state = _context?.GetMotionPermission?.Invoke() ?? ActivityPermissionState.Unavailable;
            bool show = state != ActivityPermissionState.Granted;
            _permissionBanner.SetActive(show);
            if (!show)
            {
                return;
            }

            switch (state)
            {
                case ActivityPermissionState.NotDetermined:
                    _permissionLabel.text = "Walking can restore Ashfall. Turn on motion access when you are ready.";
                    SetButtonLabel(_permissionButton, "Enable");
                    _permissionButton.interactable = true;
                    break;
                case ActivityPermissionState.Denied:
                    _permissionLabel.text = "Motion access is off. Building and Explore still work; new steps wait until access returns.";
                    SetButtonLabel(_permissionButton, "Try again");
                    _permissionButton.interactable = true;
                    break;
                default:
                    _permissionLabel.text = "This device cannot provide motion tracking. You can still build, restore with existing Vitality, and explore.";
                    _permissionButton.interactable = false;
                    break;
            }
        }

        private void RefreshProduction()
        {
            if (_producerRowsRoot == null || _context?.GetProducerStatuses == null)
            {
                return;
            }

            var statuses = _context.GetProducerStatuses();
            var seen = new HashSet<string>();
            for (int i = 0; i < statuses.Count; i++)
            {
                var status = statuses[i];
                seen.Add(status.producerId);
                if (!_producerRows.TryGetValue(status.producerId, out var row))
                {
                    row = CreateProducerRow(status.producerId, i);
                    _producerRows.Add(status.producerId, row);
                }

                row.root.SetActive(true);
                string resource = HumanizeId(status.resourceId);
                string state = !status.isActive ? "RUIN" : status.isFull ? "FULL" : $"{status.ratePerHour:0.#}/h";
                row.label.text = $"{resource}  {status.storedOutput}/{status.storageCap}  T{status.upgradeTier}  {state}";
                row.collect.interactable = status.isActive && status.storedOutput > 0;
                SetButtonLabel(row.collect, status.isFull ? "FULL" : "COLLECT");
            }

            foreach (var pair in _producerRows)
            {
                if (!seen.Contains(pair.Key))
                {
                    pair.Value.root.SetActive(false);
                }
            }
        }

        private ProducerRow CreateProducerRow(string producerId, int index)
        {
            var root = new GameObject("Producer_" + producerId, typeof(Image));
            root.transform.SetParent(_producerRowsRoot, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -index * 30f);
            rect.sizeDelta = new Vector2(0f, 28f);
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            var label = CreateText(root.transform, "Label", 12, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 0.82f));
            SetAnchors(label.rectTransform, new Vector2(0.03f, 0f), new Vector2(0.68f, 1f), Vector2.zero, Vector2.zero);
            var collect = CreateButton(root.transform, "Collect", Vector2.zero, new Vector2(90f, 26f), "COLLECT",
                () => _context?.CollectProducerRequested?.Invoke(producerId));
            SetAnchors(collect.GetComponent<RectTransform>(), new Vector2(0.7f, 0.05f), new Vector2(0.98f, 0.95f), Vector2.zero, Vector2.zero);
            return new ProducerRow { root = root, label = label, collect = collect };
        }

        private void RefreshBuilder()
        {
            var view = _context?.GetBuilderSelection?.Invoke();
            bool visible = (_context?.GetIsExplore?.Invoke() ?? true) == false && view != null && view.hasSelection;
            _builderPanel.SetActive(visible);
            if (!visible)
            {
                return;
            }

            _builderLabel.text = $"{HumanizeId(view.title)}\n{view.status}\n{view.placement}";
            _builderHint.text = view.isMoving ? view.previewStatus + " · tap a clear cell" : "Select a restored building to move or rotate it.";
            _builderMoveButton.interactable = view.canMove && !view.isMoving;
            _builderRotateButton.interactable = view.isMoving;
            _builderConfirmButton.interactable = view.isMoving && view.previewValid;
            _builderCancelButton.interactable = view.isMoving;
            _builderResetButton.interactable = view.isMoving;
        }

        private void RefreshExpedition()
        {
            bool active = _context?.IsExpeditionActive?.Invoke() ?? false;
            _expeditionPanel.SetActive(true);
            _expeditionLabel.text = _context?.GetExpeditionStatus?.Invoke() ?? "Expeditions are optional";
            _expeditionProgress.text = _context?.GetExpeditionProgress?.Invoke() ?? "Base steps count; bonuses are capped.";
            _expeditionWalkButton.interactable = !active;
            _expeditionRunButton.interactable = !active;
            _expeditionStopButton.gameObject.SetActive(active);
            _expeditionWalkButton.gameObject.SetActive(!active);
            _expeditionRunButton.gameObject.SetActive(!active);
        }

        private void RefreshInteraction()
        {
            string prompt = _context?.GetInteractionPrompt?.Invoke();
            bool visible = !string.IsNullOrEmpty(prompt) && (_context?.GetIsExplore?.Invoke() ?? false);
            _interactionPanel.SetActive(visible);
            if (visible)
            {
                _interactionLabel.text = prompt;
            }
        }

        private void RefreshOnboarding()
        {
            bool visible = _context?.IsOnboardingVisible?.Invoke() ?? false;
            _onboardingPanel.SetActive(visible);
            if (visible)
            {
                _onboardingLabel.text = _context.GetOnboardingMessage?.Invoke() ?? string.Empty;
            }
        }

        private void RefreshSettings()
        {
            if (_settingsLabel == null || _context == null)
            {
                return;
            }

            _settingsLabel.text = _context.GetAudioSettings?.Invoke() ?? "Audio settings unavailable";
        }

        private static int GetStage(PlayerProfile profile)
        {
            if (profile?.worldState == null || !profile.worldState.TryGetRegionState(WellKnownIds.StartingRegionId, out var state))
            {
                return 0;
            }

            return Mathf.Clamp(state.restorationStage, 0, 3);
        }

        private static string BuildResourceText(Dictionary<string, long> resources)
        {
            if (resources == null || resources.Count == 0)
            {
                return "No materials yet";
            }

            var entries = new List<string>();
            foreach (var pair in resources)
            {
                if (pair.Value > 0)
                {
                    entries.Add($"{HumanizeId(pair.Key)} {pair.Value:N0}");
                }
            }

            return entries.Count == 0 ? "No materials yet" : string.Join("  ·  ", entries);
        }

        private static string HumanizeId(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int lastDot = value.LastIndexOf('.');
            string text = lastDot >= 0 ? value.Substring(lastDot + 1) : value;
            text = text.Replace('_', ' ').Replace('-', ' ');
            if (text.Length == 0) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static void PositionRow(Button button, float xMin, float yMin, float xMax, float yMax)
        {
            SetAnchors(button.GetComponent<RectTransform>(), new Vector2(xMin, yMin), new Vector2(xMax, yMax), Vector2.zero, Vector2.zero);
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        internal static RectTransform CreatePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return rect;
        }

        internal static Text CreateLabel(Transform parent, string name, Vector2 position, int size)
        {
            var text = CreateText(parent, name, size, TextAnchor.MiddleLeft, Color.white);
            text.rectTransform.sizeDelta = new Vector2(420f, 44f);
            text.rectTransform.anchoredPosition = position;
            return text;
        }

        internal static Button CreateButton(Transform parent, string name, Vector2 position, Vector2 size,
            string label, Action onClick)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.2f, 0.45f, 0.34f, 1f);
            var text = CreateText(go.transform, "Text", 13, TextAnchor.MiddleCenter, Color.white);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4f, 2f);
            text.rectTransform.offsetMax = new Vector2(-4f, -2f);
            text.text = label;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());
            var colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.45f, 0.34f, 1f);
            colors.highlightedColor = new Color(0.32f, 0.62f, 0.46f, 1f);
            colors.pressedColor = new Color(0.13f, 0.28f, 0.22f, 1f);
            colors.disabledColor = new Color(0.2f, 0.24f, 0.24f, 0.7f);
            button.colors = colors;
            return button;
        }

        private static Text CreateText(Transform parent, string name, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            return text;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }
    }
}
