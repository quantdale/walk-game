using UnityEngine;
using WalkGame.Core;

namespace WalkGame.World
{
    /// <summary>Interactable plaque/record hook; raw lore is authored content, not save state.</summary>
    public sealed class LoreActor : MonoBehaviour
    {
        public string LoreId { get; private set; }
        public string Title { get; private set; }
        public string Body { get; private set; }
        public bool Discovered { get; private set; }

        private Renderer _markerRenderer;

        public void Bind(LoreDefinition definition)
        {
            LoreId = definition.loreId;
            Title = definition.titleKey;
            Body = definition.bodyKey;
            name = "Lore_" + LoreId;

            if (transform.childCount == 0)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "RecordMarker";
                marker.transform.SetParent(transform, false);
                marker.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                marker.transform.localScale = new Vector3(0.55f, 0.9f, 0.14f);
                _markerRenderer = marker.GetComponent<Renderer>();
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    _markerRenderer.sharedMaterial = new Material(shader) { color = new Color(0.82f, 0.64f, 0.30f) };
                }
                if (collider != null)
                {
                    collider.isTrigger = true;
                }
            }
        }

        public void SetDiscovered(bool discovered)
        {
            Discovered = discovered;
            if (_markerRenderer == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            _markerRenderer.GetPropertyBlock(block);
            var color = discovered ? new Color(0.39f, 0.74f, 0.48f) : new Color(0.82f, 0.64f, 0.30f);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            _markerRenderer.SetPropertyBlock(block);
        }
    }
}
