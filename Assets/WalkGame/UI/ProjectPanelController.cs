using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.UI
{
    /// <summary>
    /// Scrollable restoration progression. Each project exposes its cost, category,
    /// lock reason, prerequisites, and world change without showing the entire graph at
    /// once. Rows are reused between refreshes to avoid rebuilding the hierarchy every
    /// activity tick.
    /// </summary>
    public sealed class ProjectPanelController : MonoBehaviour
    {
        private Func<PlayerProfile> _getProfile;
        private Func<IReadOnlyList<RestorationProjectView>> _getProjects;
        private Func<string, RestorationFailure> _evaluate;
        private Func<string, bool> _tryComplete;
        private Action _changed;
        private RectTransform _content;
        private Text _details;
        private string _selectedProjectId;
        private readonly Dictionary<string, ProjectRow> _rows = new Dictionary<string, ProjectRow>();

        private sealed class ProjectRow
        {
            public GameObject root;
            public Image background;
            public Text text;
            public Button restore;
        }

        public void Bind(
            Func<PlayerProfile> getProfile,
            Func<IReadOnlyList<RestorationProjectView>> getProjects,
            Func<string, RestorationFailure> evaluate,
            Func<string, bool> tryComplete,
            Action changed)
        {
            _getProfile = getProfile;
            _getProjects = getProjects;
            _evaluate = evaluate;
            _tryComplete = tryComplete;
            _changed = changed;
            BuildUi();
        }

        public void Refresh()
        {
            if (_getProjects == null || _content == null)
            {
                return;
            }

            var projects = _getProjects();
            var seen = new HashSet<string>();
            for (int i = 0; i < projects.Count; i++)
            {
                var view = projects[i];
                seen.Add(view.ProjectId);
                if (!_rows.TryGetValue(view.ProjectId, out var row))
                {
                    row = CreateRow(view.ProjectId);
                    _rows.Add(view.ProjectId, row);
                }

                UpdateRow(row, view);
            }

            foreach (var pair in _rows)
            {
                if (!seen.Contains(pair.Key))
                {
                    pair.Value.root.SetActive(false);
                }
            }

            var selected = FindView(_selectedProjectId, projects);
            _details.text = selected == null
                ? "Select a project to inspect its chain and world impact."
                : BuildDetails(selected, _evaluate?.Invoke(selected.ProjectId) ?? RestorationFailure.UnknownProject);
        }

        private void BuildUi()
        {
            var canvas = new GameObject("ProjectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            var canvasComponent = canvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComponent.sortingOrder = 19;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            safe.transform.SetParent(canvas.transform, false);
            var safeRect = safe.GetComponent<RectTransform>();
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;

            var panel = HudController.CreatePanel(safe.transform, "RestorationPanel",
                new Vector2(0.015f, 0.28f), new Vector2(0.31f, 0.82f), Vector2.zero, Vector2.zero,
                new Color(0.055f, 0.075f, 0.085f, 0.94f));
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            var title = HudController.CreateLabel(panel, "Title", Vector2.zero, 20);
            title.text = "RESTORATION PROJECTS";
            title.rectTransform.anchorMin = new Vector2(0.05f, 0.92f);
            title.rectTransform.anchorMax = new Vector2(0.95f, 0.99f);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;
            var subtitle = HudController.CreateLabel(panel, "Subtitle", Vector2.zero, 12);
            subtitle.text = "Every project changes the basin. Walk to power the next step.";
            subtitle.color = new Color(0.66f, 0.74f, 0.71f);
            subtitle.rectTransform.anchorMin = new Vector2(0.05f, 0.84f);
            subtitle.rectTransform.anchorMax = new Vector2(0.95f, 0.92f);
            subtitle.rectTransform.offsetMin = Vector2.zero;
            subtitle.rectTransform.offsetMax = Vector2.zero;

            var scrollGo = new GameObject("ProjectScroll", typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(panel, false);
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.04f, 0.27f);
            scrollRect.anchorMax = new Vector2(0.96f, 0.83f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.025f);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            _content = contentGo.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 5f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 28f;

            var detailPanel = HudController.CreatePanel(panel, "Details", new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.24f),
                Vector2.zero, Vector2.zero, new Color(0.12f, 0.16f, 0.16f, 1f));
            detailPanel.offsetMin = Vector2.zero;
            detailPanel.offsetMax = Vector2.zero;
            _details = HudController.CreateLabel(detailPanel, "Text", Vector2.zero, 12);
            _details.alignment = TextAnchor.UpperLeft;
            _details.rectTransform.anchorMin = new Vector2(0.04f, 0.05f);
            _details.rectTransform.anchorMax = new Vector2(0.96f, 0.95f);
            _details.rectTransform.offsetMin = Vector2.zero;
            _details.rectTransform.offsetMax = Vector2.zero;
        }

        private ProjectRow CreateRow(string projectId)
        {
            var rowGo = new GameObject("Project_" + projectId, typeof(Image), typeof(LayoutElement), typeof(Button));
            rowGo.transform.SetParent(_content, false);
            var layout = rowGo.GetComponent<LayoutElement>();
            layout.minHeight = 86f;
            layout.preferredHeight = 86f;
            var background = rowGo.GetComponent<Image>();
            var rowButton = rowGo.GetComponent<Button>();
            rowButton.targetGraphic = background;
            var row = new ProjectRow { root = rowGo, background = background };
            rowButton.onClick.AddListener(() =>
            {
                _selectedProjectId = projectId;
                Refresh();
            });

            var text = HudController.CreateLabel(rowGo.transform, "Info", Vector2.zero, 12);
            text.alignment = TextAnchor.UpperLeft;
            text.rectTransform.anchorMin = new Vector2(0.03f, 0.08f);
            text.rectTransform.anchorMax = new Vector2(0.69f, 0.92f);
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            row.text = text;

            var restore = HudController.CreateButton(rowGo.transform, "Action", Vector2.zero, new Vector2(88f, 40f), "LOCKED", () =>
            {
                if (_tryComplete != null && _tryComplete(projectId))
                {
                    _changed?.Invoke();
                }
            });
            restore.GetComponent<RectTransform>().anchorMin = new Vector2(0.72f, 0.2f);
            restore.GetComponent<RectTransform>().anchorMax = new Vector2(0.98f, 0.8f);
            restore.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            restore.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            row.restore = restore;
            return row;
        }

        private void UpdateRow(ProjectRow row, RestorationProjectView view)
        {
            row.root.SetActive(true);
            var failure = _evaluate?.Invoke(view.ProjectId) ?? RestorationFailure.UnknownProject;
            bool selected = _selectedProjectId == view.ProjectId;
            bool completed = view.Completed || failure == RestorationFailure.AlreadyCompleted;
            bool available = failure == RestorationFailure.None;
            bool affordable = view.Affordable && !IsResourceFailure(failure);
            row.background.color = completed
                ? new Color(0.19f, 0.34f, 0.27f, 1f)
                : available
                    ? new Color(0.14f, 0.32f, 0.24f, 1f)
                    : selected
                        ? new Color(0.25f, 0.25f, 0.21f, 1f)
                        : new Color(1f, 1f, 1f, 0.07f);
            row.text.text = $"{view.Title}  ·  {view.Category}\\n{view.Description}\\n{view.VitalityCost:N0} Vitality" +
                            (view.ResourceCost.Length > 0 ? $"  +  {view.ResourceCost}" : string.Empty) +
                            $"\\n{LockCopy(view, failure)}";
            row.restore.interactable = available;
            SetButtonText(row.restore, completed ? "DONE" : available ? (affordable ? "RESTORE" : "NEED") : "LOCKED");
        }

        private static string BuildDetails(RestorationProjectView view, RestorationFailure failure)
        {
            return $"{view.Title}\\n{view.Description}\\n\\n" +
                   $"Category: {view.Category}   Cost: {view.VitalityCost:N0} Vitality" +
                   (view.ResourceCost.Length > 0 ? $" + {view.ResourceCost}" : string.Empty) +
                   $"\\nPrerequisite: {(view.Prerequisites.Length > 0 ? view.Prerequisites : "None")}" +
                   $"\\nChanges: {view.RewardSummary}" +
                   $"\\n{LockCopy(view, failure)}";
        }

        private static string LockCopy(RestorationProjectView view, RestorationFailure failure)
        {
            if (view.Completed || failure == RestorationFailure.AlreadyCompleted) return "Completed · the change is permanent";
            switch (failure)
            {
                case RestorationFailure.MissingPrerequisite: return "Locked · complete the prerequisite first";
                case RestorationFailure.RegionStageTooLow: return "Locked · restore more of the basin first";
                case RestorationFailure.LifetimeStepsTooLow: return "Locked · keep walking to reach the movement milestone";
                case RestorationFailure.InsufficientVitality: return "Available · earn more Vitality through movement";
                case RestorationFailure.InsufficientResources: return "Available · collect the required materials";
                case RestorationFailure.None: return "Ready · tap Restore to change the basin";
                default: return "Locked · inspect this project for its next step";
            }
        }

        private static bool IsResourceFailure(RestorationFailure failure)
        {
            return failure == RestorationFailure.InsufficientVitality || failure == RestorationFailure.InsufficientResources;
        }

        private static RestorationProjectView FindView(string id, IReadOnlyList<RestorationProjectView> views)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].ProjectId == id) return views[i];
            }
            return null;
        }

        private static void SetButtonText(Button button, string label)
        {
            var text = button?.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }
    }

    /// <summary>Flattened project row for UI binding; no Unity types or domain mutation.</summary>
    public sealed class RestorationProjectView
    {
        public string ProjectId;
        public string Title;
        public string Category;
        public string Description;
        public long VitalityCost;
        public string ResourceCost;
        public string Prerequisites;
        public string RewardSummary;
        public bool Completed;
        public bool Affordable;
    }
}
