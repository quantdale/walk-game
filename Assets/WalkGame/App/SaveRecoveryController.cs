using System;
using UnityEngine;
using UnityEngine.UI;
using WalkGame.Persistence;
using WalkGame.UI;

namespace WalkGame.App
{
    /// <summary>
    /// Player-facing fail-closed recovery screen (ADR 0007). Composed INSTEAD of the
    /// playable runtime while persistence health is blocked: it explains what happened
    /// in plain copy, never claims the session can be durably saved, offers an in-place
    /// load retry, and gates "start over" behind an explicit two-tap confirmation that
    /// quarantines the old material instead of deleting it.
    ///
    /// Deliberately minimal: this is reliability communication, not a visual redesign.
    /// </summary>
    public sealed class SaveRecoveryController : MonoBehaviour
    {
        private Text _statusLabel;
        private Button _retryButton;
        private Button _startOverButton;
        private bool _startOverArmed;
        private float _armExpiresAt;

        private void Start()
        {
            var host = GameHost.Current;
            if (host == null || !host.PersistenceBlocked)
            {
                // Recovered in place before composition ran; nothing to show.
                Destroy(gameObject);
                return;
            }

            BuildUi(host);
        }

        private void BuildUi(GameHost host)
        {
            UiRuntime.EnsureEventSystem(transform);

            var canvasGo = new GameObject("RecoveryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var cardGo = new GameObject("Card", typeof(Image));
            cardGo.transform.SetParent(canvasGo.transform, false);
            var card = cardGo.GetComponent<RectTransform>();
            card.anchorMin = new Vector2(0.14f, 0.2f);
            card.anchorMax = new Vector2(0.86f, 0.8f);
            card.offsetMin = Vector2.zero;
            card.offsetMax = Vector2.zero;
            cardGo.GetComponent<Image>().color = new Color(0.07f, 0.1f, 0.11f, 0.98f);

            var title = CreateText(cardGo.transform, "Title", 30, TextAnchor.MiddleCenter,
                new Color(0.94f, 0.76f, 0.42f));
            Stretch(title.rectTransform, new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.95f));
            title.text = "Save needs attention";

            var body = CreateText(cardGo.transform, "Body", 18, TextAnchor.UpperLeft, Color.white);
            Stretch(body.rectTransform, new Vector2(0.06f, 0.34f), new Vector2(0.94f, 0.74f));
            body.text = BuildBlockedCopy(host.LastSaveResult);

            _statusLabel = CreateText(cardGo.transform, "Status", 15, TextAnchor.MiddleCenter,
                new Color(0.65f, 0.73f, 0.7f));
            Stretch(_statusLabel.rectTransform, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.32f));
            _statusLabel.text = $"Checked {host.Clock.UtcNow:yyyy-MM-dd HH:mm} UTC";

            _retryButton = CreateButton(cardGo.transform, "Retry", "Try loading again", TryLoadAgain,
                new Color(0.2f, 0.45f, 0.34f, 1f));
            Stretch(_retryButton.GetComponent<RectTransform>(), new Vector2(0.08f, 0.1f), new Vector2(0.46f, 0.22f));

            _startOverButton = CreateButton(cardGo.transform, "StartOver", "Start over (keep old data)", ArmStartOver,
                new Color(0.45f, 0.26f, 0.16f, 1f));
            Stretch(_startOverButton.GetComponent<RectTransform>(), new Vector2(0.54f, 0.1f), new Vector2(0.92f, 0.22f));
        }

        private static string BuildBlockedCopy(SaveLoadResult reason)
        {
            if (reason == SaveLoadResult.IncompatibleSchema ||
                reason == SaveLoadResult.RecoveredFromBackupForwardSchema)
            {
                return "Your saved world was written by a newer version of the game, " +
                       "so this build cannot open it without risking your progress.\n\n" +
                       "Nothing was erased. Install the newer version and continue there, " +
                       "or start a fresh world below - the older files stay preserved either way.";
            }

            return "We found saved progress but could not load it safely.\n\n" +
                   "Nothing was erased. Restarting the app often fixes a temporary storage " +
                   "problem. You can also try again here, or start a fresh world below - " +
                   "the unreadable files stay preserved either way.";
        }

        private void TryLoadAgain()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return;
            }

            DisarmStartOver();
            if (host.RetryLoadFromDisk())
            {
                return; // Runtime recomposed normally; this controller is destroyed.
            }

            _statusLabel.text = "Still unreadable. Your saved world remains untouched on this device.";
        }

        private void ArmStartOver()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return;
            }

            if (_startOverArmed && Time.unscaledTime < _armExpiresAt)
            {
                DisarmStartOver();
                if (!host.StartOverWithFreshProfile())
                {
                    _statusLabel.text = "Could not start over right now. Try again.";
                }

                return;
            }

            _startOverArmed = true;
            _armExpiresAt = Time.unscaledTime + 6f;
            SetButtonLabel(_startOverButton, "Tap again to confirm");
            _statusLabel.text = "Starting over creates a new world. The old files are kept as backups.";
        }

        private void DisarmStartOver()
        {
            _startOverArmed = false;
            if (_startOverButton != null)
            {
                SetButtonLabel(_startOverButton, "Start over (keep old data)");
            }
        }

        private void Update()
        {
            if (_startOverArmed && Time.unscaledTime >= _armExpiresAt)
            {
                DisarmStartOver();
                if (_statusLabel != null)
                {
                    _statusLabel.text = "Confirmation expired.";
                }
            }
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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

        private static Button CreateButton(Transform parent, string name, string label, Action onClick, Color color)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            var text = CreateText(go.transform, "Text", 17, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one);
            text.offsetMin = new Vector2(4f, 2f);
            text.offsetMax = new Vector2(-4f, -2f);
            text.text = label;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = new Color(color.r * 1.4f, color.g * 1.3f, color.b * 1.3f, 1f);
            colors.pressedColor = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f, 1f);
            button.colors = colors;
            return button;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
            }
        }
    }
}
