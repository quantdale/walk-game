using UnityEngine;
using UnityEngine.EventSystems;

namespace WalkGame.UI
{
    /// <summary>
    /// One-time uGUI input bring-up for programmatically composed UI. Every HUD button
    /// and the Explore joystick depend on an EventSystem; scenes here stay content-only,
    /// so UI composition must provide one itself (M8 first-import defect: without it the
    /// whole interface rendered but never responded to touch).
    ///
    /// The project's committed setup pins activeInputHandler to "Both"; under that (and
    /// under legacy-only defaults) StandaloneInputModule drives uGUI. Switching the
    /// project to new-input-only would additionally require InputSystemUIInputModule and
    /// an Input System assembly reference on this asmdef - do that as a paired change.
    /// </summary>
    public static class UiRuntime
    {
        public static void EnsureEventSystem(Transform parent = null)
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.AddComponent<StandaloneInputModule>();
        }
    }
}
