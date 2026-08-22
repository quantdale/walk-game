using System;
using UnityEngine;
using UnityEngine.UI;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.UI
{
    /// <summary>
    /// Developer tools menu (AGENT_EXECUTION_GUIDE 20): add steps, simulate sessions,
    /// grant vitality, advance clock, reset region. Excluded from release behavior by
    /// the App layer only enabling it when debug tools are on in a development build.
    /// </summary>
    public sealed class DebugMenuController : MonoBehaviour
    {
        private Action<long> _addSteps;
        private Action _simulateWalk;
        private Action _simulateRun;
        private Action _simulateVehicleSession;
        private Action<long> _grantVitality;
        private Action _advanceClockOneHour;
        private Action _resetRegion;
        private Action _changed;

        public void Bind(
            Action<long> addSteps,
            Action simulateWalk,
            Action simulateRun,
            Action simulateVehicleSession,
            Action<long> grantVitality,
            Action advanceClockOneHour,
            Action resetRegion,
            Action changed)
        {
            _addSteps = addSteps;
            _simulateWalk = simulateWalk;
            _simulateRun = simulateRun;
            _simulateVehicleSession = simulateVehicleSession;
            _grantVitality = grantVitality;
            _advanceClockOneHour = advanceClockOneHour;
            _resetRegion = resetRegion;
            _changed = changed;

            BuildUi();
        }

        private void BuildUi()
        {
            var canvas = new GameObject("DebugCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            var canvasComponent = canvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var panel = HudController.CreatePanel(canvas.transform, "DebugPanel",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f),
                new Vector2(360f, 520f), new Color(0.1f, 0.1f, 0.15f, 0.85f));
            panel.pivot = new Vector2(1f, 0.5f);

            HudController.CreateLabel(panel.transform, "Title", new Vector2(0f, 230f), 30).text = "DEBUG";

            float y = 170f;
            Add(panel, ref y, "+1,000 steps", () => { _addSteps?.Invoke(1000); _changed?.Invoke(); });
            Add(panel, ref y, "Simulate 5 km walk", () => { _simulateWalk?.Invoke(); _changed?.Invoke(); });
            Add(panel, ref y, "Simulate 5 km run", () => { _simulateRun?.Invoke(); _changed?.Invoke(); });
            Add(panel, ref y, "Simulate vehicle session", () => { _simulateVehicleSession?.Invoke(); _changed?.Invoke(); });
            Add(panel, ref y, "+500 Vitality", () => { _grantVitality?.Invoke(500); _changed?.Invoke(); });
            Add(panel, ref y, "Advance clock +1h", () => { _advanceClockOneHour?.Invoke(); _changed?.Invoke(); });
            Add(panel, ref y, "Reset region (debug)", () => { _resetRegion?.Invoke(); _changed?.Invoke(); });
        }

        private static void Add(Transform parent, ref float y, string label, Action onClick)
        {
            HudController.CreateButton(parent, $"Btn_{label}", new Vector2(0f, y), new Vector2(320f, 52f),
                label, onClick);
            y -= 60f;
        }
    }
}
