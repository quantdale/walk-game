using UnityEngine;
using WalkGame.Core;

namespace WalkGame.World
{
    /// <summary>Small scene-side NPC hook for the vertical slice.</summary>
    public sealed class NpcActor : MonoBehaviour
    {
        public string NpcId { get; private set; }
        public string DisplayName { get; private set; }
        public string Role { get; private set; }
        public string Dialogue { get; private set; }

        private TextMesh _label;

        public void Bind(NpcDefinition definition)
        {
            NpcId = definition.npcId;
            DisplayName = definition.displayNameKey;
            Role = definition.roleKey;
            Dialogue = definition.dialogueKey;
            name = "NPC_" + NpcId;

            if (transform.childCount == 0)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.65f, 0.9f, 0.65f);
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    body.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = new Color(0.67f, 0.49f, 0.32f) };
                }

                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                head.transform.SetParent(transform, false);
                head.transform.localPosition = new Vector3(0f, 1.95f, 0f);
                head.transform.localScale = Vector3.one * 0.42f;
                if (shader != null)
                {
                    head.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = new Color(0.78f, 0.62f, 0.46f) };
                }

                var collider = body.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.isTrigger = true;
                }
            }

            var labelGo = new GameObject("Nameplate");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.55f, 0f);
            _label = labelGo.AddComponent<TextMesh>();
            _label.text = DisplayName;
            _label.fontSize = 28;
            _label.characterSize = 0.045f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(0.96f, 0.84f, 0.52f);
        }

        public void SetPresent(bool present)
        {
            gameObject.SetActive(present);
        }
    }
}
