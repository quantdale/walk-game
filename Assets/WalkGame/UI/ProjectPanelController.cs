using System;
using UnityEngine;
using UnityEngine.UI;
using WalkGame.Core;

namespace WalkGame.UI
{
    /// <summary>
    /// Restoration project list: shows what is available now, why things are locked
    /// (prerequisites/stage/steps), and completes transactions through the domain service.
    /// Refreshes on domain events so the list reflects the world immediately.
    /// </summary>
    public sealed class ProjectPanelController : MonoBehaviour
    {
        private Func<PlayerProfile> _getProfile;
        private Func<System.Collections.Generic.IReadOnlyList<RestorationProjectView>> _getProjects;
        private Func<string, RestorationFailure> _evaluate;
        private Func<string, bool> _tryComplete;
        private Action _changed;
        private RectTransform _listRoot;
        private readonly System.Collections.Generic.List<GameObject> _rows = new System.Collections.Generic.List<GameObject>();

        public void Bind(
            Func<PlayerProfile> getProfile,
            Func<System.Collections.Generic.IReadOnlyList<RestorationProjectView>> getProjects,
            Func<string, RestorationFailure> evaluate,
            Func<string, bool> tryComplete,
            Action changed)
        {
            _getProfile = getProfile;
            _getProjects = getProjects;
            _evaluate = evaluate;
            _tryComplete = tryComplete;
            _changed = changed;

            var canvas = new GameObject("ProjectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            var canvasComponent = canvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var panel = HudController.CreatePanel(canvas.transform, "ProjectPanel",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f),
                new Vector2(420f, 560f), new Color(0f, 0f, 0f, 0.6f));
            panel.pivot = new Vector2(0f, 0.5f);
            panel.anchorMin = new Vector2(0f, 0.5f);
            panel.anchorMax = new Vector2(0f, 0.5f);
            panel.anchoredPosition = new Vector2(12f, -60f);

            HudController.CreateLabel(panel.transform, "Title", new Vector2(0f, 250f), 30).text = "Restoration";
            _listRoot = panel;
        }

        public void Refresh()
        {
            if (_getProjects == null || _listRoot == null)
            {
                return;
            }

            foreach (var row in _rows)
            {
                Destroy(row);
            }

            _rows.Clear();

            float y = 200f;
            foreach (var view in _getProjects())
            {
                var failure = _evaluate?.Invoke(view.ProjectId) ?? RestorationFailure.UnknownProject;
                bool available = failure == RestorationFailure.None;
                string status = available ? "RESTORE" : Explain(failure);

                var rowGo = new GameObject($"Row_{view.ProjectId}", typeof(Image));
                rowGo.transform.SetParent(_listRoot, false);
                rowGo.GetComponent<Image>().color = available
                    ? new Color(0.15f, 0.35f, 0.25f, 0.9f)
                    : new Color(1f, 1f, 1f, 0.08f);
                var rect = rowGo.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(392f, 64f);
                rect.anchoredPosition = new Vector2(0f, y);
                y -= 70f;

                var textGo = new GameObject("Info", typeof(Text));
                textGo.transform.SetParent(rowGo.transform, false);
                var infoText = textGo.GetComponent<Text>();
                infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                infoText.fontSize = 20;
                infoText.alignment = TextAnchor.MiddleLeft;
                infoText.color = Color.white;
                infoText.rectTransform.sizeDelta = new Vector2(280f, 56f);
                infoText.rectTransform.anchoredPosition = new Vector2(10f, 0f);
                string costSuffix = view.ResourceCost.Length > 0 ? $" +{view.ResourceCost}" : string.Empty;
                infoText.text = $"{view.Title}\n{view.VitalityCost}V{costSuffix}";

                var button = HudController.CreateButton(rowGo.transform, "Do", new Vector2(150f, 0f),
                    new Vector2(110f, 44f), status, () =>
                {
                    if (_tryComplete != null && _tryComplete(view.ProjectId))
                    {
                        _changed?.Invoke();
                    }
                });

                button.interactable = available;
                _rows.Add(rowGo);

                if (y < -240f)
                {
                    break; // gray-box MVP: single page of projects
                }
            }
        }

        private static string Explain(RestorationFailure failure)
        {
            switch (failure)
            {
                case RestorationFailure.MissingPrerequisite: return "needs earlier work";
                case RestorationFailure.RegionStageTooLow: return "region not ready";
                case RestorationFailure.LifetimeStepsTooLow: return "walk more";
                case RestorationFailure.InsufficientVitality: return "need Vitality";
                case RestorationFailure.InsufficientResources: return "need materials";
                case RestorationFailure.AlreadyCompleted: return "done";
                default: return "locked";
            }
        }
    }

    /// <summary>Flattened project row for UI binding (no engine types).</summary>
    public sealed class RestorationProjectView
    {
        public string ProjectId;
        public string Title;
        public long VitalityCost;
        public string ResourceCost;
    }
}
