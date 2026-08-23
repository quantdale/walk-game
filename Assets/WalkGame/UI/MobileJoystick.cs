using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WalkGame.UI
{
    /// <summary>Reusable touch joystick used only by Explore mode; no screen-wide drag capture.</summary>
    public sealed class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public event Action<Vector2> ValueChanged;

        [SerializeField] private float radius = 70f;
        private RectTransform _rect;
        private RectTransform _knob;

        public void Configure(RectTransform knob)
        {
            _rect = GetComponent<RectTransform>();
            _knob = knob;
            ResetValue();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_rect == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position,
                    eventData.pressEventCamera, out var local))
            {
                return;
            }

            Vector2 value = Vector2.ClampMagnitude(local / Mathf.Max(1f, radius), 1f);
            if (_knob != null)
            {
                _knob.anchoredPosition = value * radius;
            }

            ValueChanged?.Invoke(value);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ResetValue();
        }

        private void ResetValue()
        {
            if (_knob != null)
            {
                _knob.anchoredPosition = Vector2.zero;
            }

            ValueChanged?.Invoke(Vector2.zero);
        }
    }
}
