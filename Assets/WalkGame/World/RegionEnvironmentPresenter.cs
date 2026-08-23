using System.Collections.Generic;
using UnityEngine;
using WalkGame.Core;

namespace WalkGame.World
{
    /// <summary>
    /// Lightweight, reusable Ashfall Basin environment kit. It builds a recognizable
    /// region from shared primitive materials and named district anchors so authored
    /// prefabs can replace individual pieces later without changing RegionState or
    /// BuildingActor contracts.
    ///
    /// Every stateful presentation decision is derived from the canonical region state:
    /// stage thresholds control macro atmosphere, while environment flags control the
    /// river, grove, wetland, and gate. The generated geometry is intentionally static
    /// after construction; Refresh only toggles existing objects and updates a few
    /// property blocks, keeping the mobile path allocation-light.
    /// </summary>
    public sealed class RegionEnvironmentPresenter : MonoBehaviour
    {
        private readonly List<GameObject> _firstGrowth = new List<GameObject>();
        private readonly List<GameObject> _recovering = new List<GameObject>();
        private readonly List<GameObject> _rewilded = new List<GameObject>();
        private readonly List<GameObject> _riverWater = new List<GameObject>();
        private readonly List<GameObject> _groveCanopy = new List<GameObject>();
        private readonly List<Renderer> _stageAccentRenderers = new List<Renderer>();

        private RegionDefinition _definition;
        private RegionState _state;
        private Transform _contentRoot;
        private Material _ashMaterial;
        private Material _stoneMaterial;
        private Material _roadMaterial;
        private Material _waterMaterial;
        private Material _plantMaterial;
        private Material _warmLightMaterial;
        private Material _metalMaterial;
        private ParticleSystem _ambientLife;
        private Transform _gateHalo;
        private Transform _waterWheel;
        private bool _built;
        private bool _reducedMotion;
        private int _lastStage = -1;

        public int LastAppliedStage => _lastStage;

        public void Present(RegionDefinition definition, RegionState state)
        {
            _definition = definition;
            _state = state;
            if (!_built)
            {
                BuildEnvironment();
                _built = true;
            }

            Refresh();
        }

        public void SetReducedMotion(bool reducedMotion)
        {
            _reducedMotion = reducedMotion;
            if (_ambientLife != null)
            {
                _ambientLife.gameObject.SetActive(!_reducedMotion && _lastStage >= 2);
            }
        }

        public void Refresh()
        {
            if (_definition == null || _state == null)
            {
                return;
            }

            int stage = Mathf.Clamp(_state.restorationStage, 0, Mathf.Max(0, _definition.visualStageCount - 1));
            SetActiveAtStage(_firstGrowth, stage >= 1);
            SetActiveAtStage(_recovering, stage >= 2);
            SetActiveAtStage(_rewilded, stage >= 3);

            bool riverAlive = _state.HasEnvironmentFlag(WellKnownIds.EnvironmentFlags.RiverFlowing) || stage >= 2;
            bool groveAlive = _state.HasEnvironmentFlag(WellKnownIds.EnvironmentFlags.GroveRevived) || stage >= 3;
            bool wetlandAlive = _state.HasEnvironmentFlag(WellKnownIds.EnvironmentFlags.WetlandAlive) || stage >= 3;
            SetActiveAtStage(_riverWater, riverAlive);
            SetActiveAtStage(_groveCanopy, groveAlive);

            if (_gateHalo != null)
            {
                _gateHalo.gameObject.SetActive(_state.completedProjectIds.Contains("project.ashfall.transit_gate_awaken"));
                _gateHalo.localScale = stage >= 3 ? Vector3.one * 1.15f : Vector3.one;
            }

            if (_waterWheel != null)
            {
                _waterWheel.localRotation = Quaternion.Euler(0f, riverAlive ? 18f : 0f, 0f);
            }

            if (_ambientLife != null)
            {
                _ambientLife.gameObject.SetActive(!_reducedMotion && (wetlandAlive || stage >= 2));
            }

            ApplyAtmosphere(stage, riverAlive, wetlandAlive);
            _lastStage = stage;
        }

        private void BuildEnvironment()
        {
            _contentRoot = new GameObject("AshfallEnvironment").transform;
            _contentRoot.SetParent(transform, false);

            _ashMaterial = CreateMaterial("Ashfall Ash", new Color(0.17f, 0.18f, 0.18f));
            _stoneMaterial = CreateMaterial("Ashfall Stone", new Color(0.27f, 0.28f, 0.27f));
            _roadMaterial = CreateMaterial("Ashfall Paths", new Color(0.30f, 0.25f, 0.22f));
            _waterMaterial = CreateMaterial("Ashfall Water", new Color(0.15f, 0.46f, 0.55f));
            _plantMaterial = CreateMaterial("Ashfall Growth", new Color(0.30f, 0.58f, 0.31f));
            _warmLightMaterial = CreateMaterial("Ashfall Warm Lights", new Color(0.95f, 0.61f, 0.24f), 0.1f);
            _metalMaterial = CreateMaterial("Ashfall Metal", new Color(0.34f, 0.38f, 0.39f), 0.65f);

            BuildBoundaryAndRoutes();
            BuildSettlementCore();
            BuildDryRiverAndWaterworks();
            BuildDeadGroveAndWetland();
            BuildDistrictLandmarks();
            BuildTransitGate();
            BuildStorySpaces();
            BuildAmbientLife();
        }

        private void BuildBoundaryAndRoutes()
        {
            // Low boundary markers communicate the contained-region rule in both views.
            for (int i = 0; i <= 32; i += 4)
            {
                CreateCube("Boundary_N_" + i, new Vector3(i, 0.35f, -0.3f), new Vector3(0.18f, 0.7f, 0.18f), _stoneMaterial);
                CreateCube("Boundary_S_" + i, new Vector3(i, 0.35f, 32.3f), new Vector3(0.18f, 0.7f, 0.18f), _stoneMaterial);
                CreateCube("Boundary_W_" + i, new Vector3(-0.3f, 0.35f, i), new Vector3(0.18f, 0.7f, 0.18f), _stoneMaterial);
                CreateCube("Boundary_E_" + i, new Vector3(32.3f, 0.35f, i), new Vector3(0.18f, 0.7f, 0.18f), _stoneMaterial);
            }

            // Authored pedestrian routes keep movable footprints away from the main loop.
            CreateCube("Route_Central", new Vector3(7.5f, 0.03f, 6f), new Vector3(14f, 0.06f, 1.2f), _roadMaterial);
            CreateCube("Route_River", new Vector3(7.5f, 0.035f, 18f), new Vector3(12f, 0.07f, 1f), _roadMaterial);
            CreateCube("Route_Grove", new Vector3(23f, 0.035f, 16f), new Vector3(1f, 0.07f, 11f), _roadMaterial);
            CreateCube("Route_Gate", new Vector3(24f, 0.035f, 27f), new Vector3(10f, 0.07f, 1f), _roadMaterial);
            AddLabel("RegionLabel", "ASHFALL BASIN", new Vector3(16f, 0.05f, 31.2f), _stoneMaterial, 0.065f);
        }

        private void BuildSettlementCore()
        {
            CreateCylinder("SettlementPlaza", new Vector3(7.5f, 0.06f, 6f), 4.2f, 0.12f, _stoneMaterial);
            CreateCylinder("PlazaAsh", new Vector3(7.5f, 0.14f, 6f), 2.8f, 0.05f, _ashMaterial);
            CreateCube("SettlementWall_W", new Vector3(1.2f, 0.65f, 6f), new Vector3(0.5f, 1.3f, 7f), _stoneMaterial);
            CreateCube("SettlementWall_E", new Vector3(13.8f, 0.65f, 6f), new Vector3(0.5f, 1.3f, 7f), _stoneMaterial);
            AddLabel("SettlementSign", "RUINED SETTLEMENT", new Vector3(7.5f, 1.7f, 6f), _stoneMaterial, 0.05f);

            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                Vector3 position = new Vector3(7.5f + Mathf.Cos(angle) * 2.8f, 0.45f, 6f + Mathf.Sin(angle) * 2.8f);
                CreateCube("PlazaRubble_" + i, position, new Vector3(0.5f, 0.8f, 0.35f), _ashMaterial,
                    Quaternion.Euler(0f, i * 31f, 12f));
            }
        }

        private void BuildDryRiverAndWaterworks()
        {
            // The corridor is intentionally broad and legible from the builder camera.
            CreateCube("DryRiverBed", new Vector3(16f, -0.02f, 16f), new Vector3(4f, 0.08f, 32f), _ashMaterial);
            CreateCube("RiverBank_W", new Vector3(13.65f, 0.22f, 16f), new Vector3(0.5f, 0.45f, 32f), _stoneMaterial);
            CreateCube("RiverBank_E", new Vector3(18.35f, 0.22f, 16f), new Vector3(0.5f, 0.45f, 32f), _stoneMaterial);
            for (int z = 2; z < 32; z += 5)
            {
                CreateCube("DryRiverStone_" + z, new Vector3(16f + (z % 3) - 1f, 0.12f, z),
                    new Vector3(0.7f, 0.25f, 1.2f), _stoneMaterial, Quaternion.Euler(0f, z * 13f, 0f));
            }

            for (int z = 2; z < 31; z += 4)
            {
                _riverWater.Add(CreateCube("RiverWater_" + z, new Vector3(16f, 0.07f, z), new Vector3(3.1f, 0.05f, 3.5f), _waterMaterial));
            }

            for (int z = 2; z < 31; z += 4)
            {
                CreateCube("AqueductPillar_W_" + z, new Vector3(12.5f, 1.15f, z), new Vector3(0.7f, 2.3f, 0.7f), _stoneMaterial);
                CreateCube("AqueductPillar_E_" + z, new Vector3(19.5f, 1.15f, z), new Vector3(0.7f, 2.3f, 0.7f), _stoneMaterial);
            }

            var wheel = CreateCylinder("PumpWheel", new Vector3(11.8f, 1.2f, 10f), 1.0f, 0.25f, _metalMaterial, Quaternion.Euler(90f, 0f, 0f));
            _waterWheel = wheel.transform;
            AddLabel("WaterworksSign", "DRY RIVER / WATERWORKS", new Vector3(11.3f, 2.5f, 10f), _stoneMaterial, 0.04f);
        }

        private void BuildDeadGroveAndWetland()
        {
            for (int i = 0; i < 12; i++)
            {
                float x = 22f + (i % 4) * 1.4f;
                float z = 13f + (i / 4) * 1.8f;
                var trunk = CreateCylinder("DeadGroveTrunk_" + i, new Vector3(x, 1.1f, z), 0.22f, 2.2f, _ashMaterial,
                    Quaternion.Euler(0f, i * 17f, (i % 2 == 0 ? 8f : -10f)));
                var branch = CreateCube("DeadGroveBranch_" + i, new Vector3(x + 0.3f, 1.8f, z), new Vector3(0.9f, 0.12f, 0.12f), _ashMaterial,
                    Quaternion.Euler(0f, i * 21f, 18f));

                var canopy = CreateSphere("GroveCanopy_" + i, new Vector3(x, 2.2f, z), 0.65f, _plantMaterial);
                _groveCanopy.Add(canopy);
                var firstLeaf = CreateSphere("FirstGrowth_" + i, new Vector3(x + 0.35f, 0.42f, z + 0.15f), 0.22f, _plantMaterial);
                _firstGrowth.Add(firstLeaf);
            }

            CreateCube("WetlandBed", new Vector3(24.5f, 0.02f, 8.2f), new Vector3(5.5f, 0.06f, 3.6f), _ashMaterial);
            for (int i = 0; i < 8; i++)
            {
                var reed = CreateCylinder("WetlandReed_" + i,
                    new Vector3(22.5f + (i % 4) * 1.3f, 0.6f, 7.2f + (i / 4) * 1.4f),
                    0.04f, 1.2f, _plantMaterial,
                    Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 8f : -8f)));
                _recovering.Add(reed);
            }

            AddLabel("GroveSign", "DEAD GROVE", new Vector3(24.5f, 2.7f, 16.5f), _ashMaterial, 0.05f);
            AddLabel("WetlandSign", "WETLAND BASIN", new Vector3(24.5f, 1.6f, 8.2f), _plantMaterial, 0.04f);
        }

        private void BuildDistrictLandmarks()
        {
            BuildWorkshopDistrict();
            BuildGreenhouseDistrict();
            BuildResearchDistrict();
            BuildResidentialDistrict();
        }

        private void BuildWorkshopDistrict()
        {
            Vector3 origin = new Vector3(8.5f, 0f, 20.5f);
            CreateCube("WorkshopYard", origin + new Vector3(0f, 0.06f, 0f), new Vector3(5f, 0.12f, 4f), _roadMaterial);
            CreateCube("WorkshopStack", origin + new Vector3(-1.7f, 1.2f, 0.9f), new Vector3(0.7f, 2.4f, 0.7f), _metalMaterial);
            CreateCylinder("WorkshopTank", origin + new Vector3(1.6f, 0.9f, -0.8f), 0.8f, 1.8f, _metalMaterial);
            CreateCube("WorkshopFrame", origin + new Vector3(0f, 1.1f, 0f), new Vector3(3.2f, 2.2f, 0.16f), _stoneMaterial);
            AddLabel("WorkshopSign", "WORKSHOP DISTRICT", origin + new Vector3(0f, 2.8f, 0f), _stoneMaterial, 0.04f);
        }

        private void BuildGreenhouseDistrict()
        {
            Vector3 origin = new Vector3(21f, 0f, 22.5f);
            CreateCube("GreenhouseBed", origin + new Vector3(0f, 0.06f, 0f), new Vector3(5.5f, 0.12f, 3.8f), _roadMaterial);
            for (int i = -2; i <= 2; i++)
            {
                CreateCube("GreenhouseFrame_" + i, origin + new Vector3(i, 1.4f, 0f), new Vector3(0.12f, 2.8f, 3.2f), _metalMaterial);
            }
            CreateCube("GreenhouseRoof", origin + new Vector3(0f, 2.7f, 0f), new Vector3(5.2f, 0.12f, 3.2f), _metalMaterial,
                Quaternion.Euler(0f, 0f, 8f));
            for (int i = 0; i < 5; i++)
            {
                var plant = CreateSphere("GreenhousePlant_" + i, origin + new Vector3(-1.6f + i * 0.8f, 0.45f, 0f), 0.28f, _plantMaterial);
                _rewilded.Add(plant);
            }
            AddLabel("GreenhouseSign", "GREENHOUSE / SEED ARCHIVE", origin + new Vector3(0f, 3.3f, 0f), _plantMaterial, 0.035f);
        }

        private void BuildResearchDistrict()
        {
            Vector3 origin = new Vector3(25.3f, 0f, 11.8f);
            CreateCylinder("ResearchTower", origin + new Vector3(0f, 2.3f, 0f), 1.2f, 4.6f, _stoneMaterial);
            CreateCylinder("ResearchDish", origin + new Vector3(0f, 4.8f, 0f), 1.6f, 0.18f, _metalMaterial, Quaternion.Euler(28f, 0f, 0f));
            CreateCube("ResearchLight", origin + new Vector3(0f, 4.1f, 0f), new Vector3(0.18f, 0.6f, 0.18f), _warmLightMaterial);
            AddLabel("ResearchSign", "RESEARCH STRUCTURE", origin + new Vector3(0f, 5.6f, 0f), _stoneMaterial, 0.035f);
        }

        private void BuildResidentialDistrict()
        {
            var homes = new[]
            {
                new Vector3(4.5f, 0f, 25.2f),
                new Vector3(22.5f, 0f, 5.8f),
                new Vector3(27.4f, 0f, 25.7f),
            };
            for (int i = 0; i < homes.Length; i++)
            {
                Vector3 origin = homes[i];
                CreateCube("HomeFoundation_" + i, origin + new Vector3(0f, 0.06f, 0f), new Vector3(3.4f, 0.12f, 3.2f), _roadMaterial);
                CreateCube("HomeWall_" + i, origin + new Vector3(0f, 0.9f, 0f), new Vector3(2.7f, 1.8f, 2.5f), _stoneMaterial);
                CreateCube("HomeRoof_" + i, origin + new Vector3(0f, 2.1f, 0f), new Vector3(3.1f, 0.35f, 2.9f), _ashMaterial,
                    Quaternion.Euler(0f, 45f, 0f));
                AddLabel("HomeSign_" + i, "RESIDENTIAL", origin + new Vector3(0f, 2.9f, 0f), _stoneMaterial, 0.03f);
            }
        }

        private void BuildTransitGate()
        {
            Vector3 origin = new Vector3(20f, 0f, 27f);
            CreateCube("GatePillar_L", origin + new Vector3(-1.4f, 2f, 0f), new Vector3(0.8f, 4f, 1.1f), _stoneMaterial);
            CreateCube("GatePillar_R", origin + new Vector3(1.4f, 2f, 0f), new Vector3(0.8f, 4f, 1.1f), _stoneMaterial);
            CreateCube("GateHeader", origin + new Vector3(0f, 3.8f, 0f), new Vector3(3.6f, 0.8f, 1.1f), _stoneMaterial);
            var halo = CreateCylinder("GateHalo", origin + new Vector3(0f, 2.1f, 0f), 1.15f, 0.16f, _warmLightMaterial, Quaternion.Euler(90f, 0f, 0f));
            _gateHalo = halo.transform;
            AddLabel("GateSign", "TRANSIT GATE", origin + new Vector3(0f, 5f, 0f), _warmLightMaterial, 0.055f);
        }

        private void BuildStorySpaces()
        {
            CreateCube("AqueductPlaque", new Vector3(11.2f, 0.45f, 10f), new Vector3(0.9f, 0.9f, 0.18f), _metalMaterial,
                Quaternion.Euler(0f, 90f, 0f));
            CreateCube("RiversideLetters", new Vector3(12.4f, 0.42f, 22f), new Vector3(0.8f, 0.8f, 0.18f), _metalMaterial);
            CreateCube("GateInscription", new Vector3(18.7f, 1.1f, 27f), new Vector3(0.5f, 0.9f, 0.12f), _metalMaterial,
                Quaternion.Euler(0f, 90f, 0f));
            AddLabel("StoryLabel", "RESTORATION RECORDS", new Vector3(10.7f, 0.9f, 10f), _metalMaterial, 0.032f);
        }

        private void BuildAmbientLife()
        {
            var particleGo = new GameObject("AshfallFireflies");
            particleGo.transform.SetParent(_contentRoot, false);
            particleGo.transform.position = new Vector3(16f, 1.8f, 16f);
            _ambientLife = particleGo.AddComponent<ParticleSystem>();
            var main = _ambientLife.main;
            main.loop = true;
            main.startLifetime = 3.5f;
            main.startSpeed = 0.25f;
            main.startSize = 0.06f;
            main.startColor = new Color(0.78f, 0.93f, 0.57f, 0.75f);
            main.maxParticles = 24;
            var emission = _ambientLife.emission;
            emission.rateOverTime = 8f;
            var shape = _ambientLife.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, 2f, 22f);
            var renderer = particleGo.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                renderer.sharedMaterial = new Material(shader) { color = new Color(0.72f, 0.9f, 0.45f, 0.85f) };
            }
        }

        private void ApplyAtmosphere(int stage, bool riverAlive, bool wetlandAlive)
        {
            Color fog = stage <= 0
                ? new Color(0.13f, 0.15f, 0.16f)
                : stage == 1
                    ? new Color(0.19f, 0.22f, 0.20f)
                    : stage == 2
                        ? new Color(0.26f, 0.33f, 0.29f)
                        : new Color(0.34f, 0.43f, 0.35f);
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = stage <= 0 ? 0.018f : 0.011f;
            RenderSettings.ambientLight = Color.Lerp(new Color(0.18f, 0.19f, 0.20f), new Color(0.48f, 0.58f, 0.45f), stage / 3f);

            var sun = FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(0.72f, 1.2f, stage / 3f);
                sun.color = Color.Lerp(new Color(0.76f, 0.78f, 0.82f), new Color(1f, 0.94f, 0.76f), stage / 3f);
            }

            // Use property blocks for repeated accent meshes; avoid Renderer.material
            // instantiation on every state refresh.
            var block = new MaterialPropertyBlock();
            Color accent = riverAlive || wetlandAlive ? new Color(0.34f, 0.68f, 0.48f) : new Color(0.31f, 0.32f, 0.31f);
            for (int i = 0; i < _stageAccentRenderers.Count; i++)
            {
                var renderer = _stageAccentRenderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", accent);
                block.SetColor("_Color", accent);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void SetActiveAtStage(List<GameObject> objects, bool active)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null && objects[i].activeSelf != active)
                {
                    objects[i].SetActive(active);
                }
            }
        }

        private GameObject CreateCube(string objectName, Vector3 position, Vector3 scale, Material material,
            Quaternion rotation = default, bool collidable = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objectName;
            go.transform.SetParent(_contentRoot, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            go.transform.localScale = scale;
            ApplyMaterial(go.GetComponent<Renderer>(), material);
            if (!collidable)
            {
                RemoveCollider(go);
            }

            return go;
        }

        private GameObject CreateCylinder(string objectName, Vector3 position, float radius, float height, Material material,
            Quaternion rotation = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = objectName;
            go.transform.SetParent(_contentRoot, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            ApplyMaterial(go.GetComponent<Renderer>(), material);
            RemoveCollider(go);
            return go;
        }

        private GameObject CreateSphere(string objectName, Vector3 position, float radius, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = objectName;
            go.transform.SetParent(_contentRoot, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * radius * 2f;
            ApplyMaterial(go.GetComponent<Renderer>(), material);
            RemoveCollider(go);
            return go;
        }

        private GameObject AddLabel(string objectName, string text, Vector3 position, Material unused, float characterSize)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(_contentRoot, false);
            go.transform.localPosition = position;
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 32;
            label.characterSize = characterSize;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.Lerp(Color.white, new Color(0.66f, 0.7f, 0.68f), _lastStage < 1 ? 0.55f : 0.1f);
            return go;
        }

        private static Material CreateMaterial(string name, Color color, float metallic = 0f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", metallic > 0.1f ? 0.55f : 0.18f);
            }

            return material;
        }

        private static void ApplyMaterial(Renderer renderer, Material material)
        {
            if (renderer == null || material == null)
            {
                return;
            }

            renderer.sharedMaterial = material;
        }

        private static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }
    }
}
