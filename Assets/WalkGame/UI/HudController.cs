using UnityEngine;
using UnityEngine.UI;
using WalkGame.Gameplay;

namespace WalkGame.UI
{
    /// <summary>
    /// Runtime HUD construction + binding. Built from code so scenes stay content-only.
    /// Shows Vitality, key resources, and the Builder/Explore toggle; dedicated collect
    /// buttons appear whenever producer stores hold output (ROADMAP Phase 5). The debug
    /// menu is opened via UiContext and gated there by profile settings
    /// (AGENT_EXECUTION_GUIDE 20).
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        private Text _vitalityLabel;
        private Text _resourceLabel;
        private Text _modeLabel;
        private Button _toggleButton;
        private RectTransform _collectBar;
        private UiContext _context;

        public void Bind(UiContext context)
        {
            _context = context;
        }

        private void Awake()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            var canvasComponent = canvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var panel = CreatePanel(canvas.transform, "TopBar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f),
                new Vector2(880f, 96f), new Color(0f, 0f, 0f, 0.55f));

            _vitalityLabel = CreateLabel(panel.transform, "Vitality", new Vector2(-360f, 12f), 34);
            _resourceLabel = CreateLabel(panel.transform, "Resources", new Vector2(-120f, -14f), 22);
            _modeLabel = CreateLabel(panel.transform, "Mode", new Vector2(180f, 0f), 26);

            _toggleButton = CreateButton(panel.transform, "ExploreToggle", new Vector2(340f, 0f), new Vector2(190f, 56f), "Explore",
                () => _context?.ToggleExploreRequested?.Invoke());

            var barGo = new GameObject("CollectBar", typeof(RectTransform));
            barGo.transform.SetParent(canvas.transform, false);
            _collectBar = barGo.GetComponent<RectTransform>();
            _collectBar.anchorMin = new Vector2(0.5f, 1f);
            _collectBar.anchorMax = new Vector2(0.5f, 1f);
            _collectBar.pivot = new Vector2(0f, 1f);
            _collectBar.anchoredPosition = new Vector2(-440f, -116f);
            _collectBar.sizeDelta = new Vector2(280f, 0f);

            Refresh();
        }

        public void Refresh()
        {
            var profile = _context?.GetProfile?.Invoke();
            if (profile == null || _vitalityLabel == null)
            {
                return;
            }

            _vitalityLabel.text = $"Vitality {profile.vitalityBalance}";
            if (_modeLabel != null)
            {
                bool explore = _context.GetIsExplore?.Invoke() ?? false;
                _modeLabel.text = explore ? "Explore" : "Builder";
                if (_toggleButton != null)
                {
                    var label = _toggleButton.GetComponentInChildren<Text>();
                    if (label != null)
                    {
                        label.text = explore ? "Builder" : "Explore";
                    }
                }
            }

            string resources = string.Empty;
            foreach (var pair in profile.resources)
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                resources += $"{pair.Key.Replace("resource.", string.Empty)} {pair.Value}   ";
            }

            _resourceLabel.text = $"{resources}Steps {profile.lifetimeAcceptedSteps}";

            RefreshCollectBar();
        }

        private void RefreshCollectBar()
        {
            if (_collectBar == null)
            {
                return;
            }

            for (int i = _collectBar.childCount - 1; i >= 0; i--)
            {
                Destroy(_collectBar.GetChild(i).gameObject);
            }

            var pending = _context?.GetCollectables?.Invoke();
            if (pending == null || pending.Count == 0)
            {
                _collectBar.gameObject.SetActive(false);
                return;
            }

            _collectBar.gameObject.SetActive(true);
            int index = 0;
            foreach (var item in pending)
            {
                string resourceShort = item.resourceId.Replace("resource.", string.Empty);
                CreateButton(_collectBar, $"Collect_{item.producerId}",
                    new Vector2(0f, -22f - index * 52f), new Vector2(280f, 44f),
                    $"Collect {resourceShort} {item.stored}",
                    () => _context?.CollectProducerRequested?.Invoke(item.producerId));
                index++;
            }
        }

        internal static RectTransform CreatePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return rect;
        }

        internal static Text CreateLabel(Transform parent, string name, Vector2 position, int size)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.rectTransform.sizeDelta = new Vector2(420f, 44f);
            text.rectTransform.anchoredPosition = position;
            return text;
        }

        internal static Button CreateButton(Transform parent, string name, Vector2 position, Vector2 size,
            string label, System.Action onClick)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            go.GetComponent<Image>().color = new Color(0.22f, 0.45f, 0.35f);

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 26;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.rectTransform.sizeDelta = size;
            text.text = label;

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => onClick());
            return button;
        }
    }
}
