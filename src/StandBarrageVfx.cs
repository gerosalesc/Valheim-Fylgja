using System.Collections.Generic;
using UnityEngine;

namespace StandsOfValheim
{
    /// <summary>
    /// Pooled spectral afterimage fist flurry and punch-impact shockwave rings for Aurvandil's Fylgja's ORA barrage.
    /// Uses Valheim's opaque Custom/Creature shader with high emission and clean scale-in/scale-out lifecycles.
    /// </summary>
    internal sealed class StandBarrageVfx
    {
        public sealed class VfxSlot
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public bool IsRing;
            public bool Active;
            public float Age;
            public float Lifetime;
            public Vector3 Velocity;
            public Vector3 PeakScale;
            public Vector3 EndScale;
        }

        private const int FistPoolLimit = 14;
        private const int RingPoolLimit = 5;

        private static Mesh s_fistMesh;
        private static Mesh s_ringMesh;
        private static Material s_violetFistMat;
        private static Material s_magentaFistMat;
        private static Material s_shockRingMat;

        public static void InvalidateMaterials()
        {
            s_violetFistMat = null;
            s_magentaFistMat = null;
            s_shockRingMat = null;
        }

        private readonly GameObject _poolRoot;
        private readonly List<VfxSlot> _slots = new List<VfxSlot>(FistPoolLimit + RingPoolLimit);
        private int _spawnCounter = 0;

        public StandBarrageVfx()
        {
            EnsureAssets();
            _poolRoot = new GameObject("StandBarrageVfxPool");
        }

        public void SpawnFlurryFist(Transform standTransform)
        {
            if (_poolRoot == null || standTransform == null) return;

            int sideSign = (_spawnCounter & 1) == 0 ? -1 : 1;
            bool useCustom3D = Plugin.UseCustom3DModel;
            Mesh fistMesh = useCustom3D ? (CustomValheimModelBuilder.GetBarrageFistMesh() ?? s_fistMesh) : s_fistMesh;
            Material defaultMat = (_spawnCounter & 1) == 0 ? s_violetFistMat : s_magentaFistMat;
            Material mat = useCustom3D ? (CustomValheimModelBuilder.GetBarrageFistMaterial() ?? defaultMat) : defaultMat;
            _spawnCounter++;

            VfxSlot slot = AcquireSlot(isRing: false, FistPoolLimit, fistMesh, mat);
            if (slot == null) return;

            Vector3 fwd = standTransform.forward;
            Vector3 right = standTransform.right;
            Vector3 up = standTransform.up;

            Vector3 origin = standTransform.position
                           + up * Random.Range(1.02f, 1.66f)
                           + right * (sideSign * Random.Range(0.24f, 0.64f))
                           + fwd * Random.Range(0.20f, 0.42f);

            Vector3 dir = (fwd + right * Random.Range(-0.14f, 0.14f) + up * Random.Range(-0.10f, 0.12f)).normalized;
            float speed = Random.Range(14.5f, 18.5f);
            float scale = Random.Range(1.05f, 1.42f);

            slot.Active = true;
            slot.Age = 0f;
            slot.Lifetime = Random.Range(0.115f, 0.145f);
            slot.Velocity = dir * speed;
            slot.PeakScale = useCustom3D
                ? new Vector3(scale * 0.92f, scale * 0.92f, scale * 1.15f)
                : new Vector3(scale, scale, scale * 1.45f);
            slot.EndScale = Vector3.zero;

            if (slot.Filter != null) slot.Filter.sharedMesh = fistMesh;
            slot.Renderer.sharedMaterial = mat;
            slot.Go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir, up));
            slot.Go.transform.localScale = slot.PeakScale * 0.35f;
            slot.Go.SetActive(true);
        }

        public void SpawnShockRing(Vector3 position, Vector3 forward, float radiusScale = 1.0f)
        {
            if (_poolRoot == null) return;

            VfxSlot slot = AcquireSlot(isRing: true, RingPoolLimit, s_ringMesh, s_shockRingMat);
            if (slot == null) return;

            Vector3 dir = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            slot.Active = true;
            slot.Age = 0f;
            slot.Lifetime = 0.095f;
            slot.Velocity = dir * 3.5f;
            slot.PeakScale = new Vector3(1.15f * radiusScale, 1.15f * radiusScale, 0.85f * radiusScale);
            slot.EndScale = new Vector3(1.55f * radiusScale, 1.55f * radiusScale, 0.01f);

            slot.Go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir));
            slot.Go.transform.localScale = slot.PeakScale * 0.28f;
            slot.Go.SetActive(true);
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                VfxSlot slot = _slots[i];
                if (!slot.Active || slot.Go == null) continue;

                slot.Age += dt;
                float t = slot.Age / slot.Lifetime;
                if (t >= 1f)
                {
                    slot.Active = false;
                    slot.Go.SetActive(false);
                    continue;
                }

                slot.Go.transform.position += slot.Velocity * dt;

                if (slot.IsRing)
                {
                    float grow = Mathf.Clamp01(t / 0.45f);
                    Vector3 s = t < 0.45f
                        ? Vector3.Lerp(slot.PeakScale * 0.28f, slot.PeakScale, grow)
                        : Vector3.Lerp(slot.PeakScale, slot.EndScale, (t - 0.45f) / 0.55f);
                    slot.Go.transform.localScale = s;
                }
                else
                {
                    Vector3 s;
                    if (t < 0.22f)
                    {
                        s = Vector3.Lerp(slot.PeakScale * 0.35f, slot.PeakScale, t / 0.22f);
                    }
                    else if (t < 0.65f)
                    {
                        s = slot.PeakScale;
                    }
                    else
                    {
                        s = Vector3.Lerp(slot.PeakScale, Vector3.zero, (t - 0.65f) / 0.35f);
                    }
                    slot.Go.transform.localScale = s;
                }
            }
        }

        public void Dispose()
        {
            _slots.Clear();
            if (_poolRoot != null)
            {
                Object.Destroy(_poolRoot);
            }
        }

        private VfxSlot AcquireSlot(bool isRing, int limitOfType, Mesh mesh, Material material)
        {
            VfxSlot oldestActive = null;
            int countOfType = 0;

            for (int i = 0; i < _slots.Count; i++)
            {
                VfxSlot s = _slots[i];
                if (s.IsRing != isRing) continue;
                countOfType++;
                if (!s.Active) return s;
                if (oldestActive == null || s.Age > oldestActive.Age) oldestActive = s;
            }

            if (countOfType < limitOfType)
            {
                GameObject go = StandEquipmentBuilder.CreateGearObject(
                    isRing ? "StandBarrageRing" : "StandBarrageFist",
                    mesh,
                    material);
                go.transform.SetParent(_poolRoot.transform, false);
                go.SetActive(false);

                var created = new VfxSlot
                {
                    Go = go,
                    Filter = go.GetComponent<MeshFilter>(),
                    Renderer = go.GetComponent<MeshRenderer>(),
                    IsRing = isRing
                };
                _slots.Add(created);
                return created;
            }

            return oldestActive;
        }

        private static void EnsureAssets()
        {
            if (s_fistMesh == null) s_fistMesh = BuildFlurryFistMesh();
            if (s_ringMesh == null) s_ringMesh = BuildShockRingMesh();

            float vfxOpacity = Plugin.ModelOpacity < 0.99f
                ? Mathf.Clamp(Plugin.ModelOpacity * 0.82f, 0.30f, 0.85f)
                : 1.0f;

            if (s_violetFistMat == null)
            {
                s_violetFistMat = StandEquipmentBuilder.CreateCrispMaterial(
                    new Color(0.62f, 0.24f, 0.96f, 1.0f),
                    metallic: 0.25f,
                    glossiness: 0.72f,
                    new Color(0.85f, 0.28f, 1.45f),
                    customTexture: null,
                    opacityOverride: vfxOpacity,
                    useDepthPrepass: false);
            }

            if (s_magentaFistMat == null)
            {
                s_magentaFistMat = StandEquipmentBuilder.CreateCrispMaterial(
                    new Color(0.78f, 0.28f, 1.00f, 1.0f),
                    metallic: 0.30f,
                    glossiness: 0.76f,
                    new Color(1.10f, 0.34f, 1.55f),
                    customTexture: null,
                    opacityOverride: vfxOpacity,
                    useDepthPrepass: false);
            }

            if (s_shockRingMat == null)
            {
                s_shockRingMat = StandEquipmentBuilder.CreateCrispMaterial(
                    new Color(0.94f, 0.78f, 1.00f, 1.0f),
                    metallic: 0.15f,
                    glossiness: 0.85f,
                    new Color(1.35f, 0.95f, 1.60f),
                    customTexture: null,
                    opacityOverride: vfxOpacity,
                    useDepthPrepass: false);
            }
        }

        private static Mesh BuildFlurryFistMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int segs = 10;

            // Tapered speed-streak tail lofting seamlessly into a clenched spectral fist facing +Z
            var rings = new List<Vector3[]>
            {
                StandEquipmentBuilder.CreateEllipseRing(new Vector3(0f, 0f, -0.46f), Vector3.right, Vector3.up, 0.007f, 0.007f, segs),
                StandEquipmentBuilder.CreateEllipseRing(new Vector3(0f, 0f, -0.24f), Vector3.right, Vector3.up, 0.024f, 0.022f, segs),
                StandEquipmentBuilder.CreateEllipseRing(new Vector3(0f, 0f, -0.06f), Vector3.right, Vector3.up, 0.048f, 0.044f, segs),
                StandEquipmentBuilder.CreateEllipseRing(new Vector3(0f, 0f,  0.02f), Vector3.right, Vector3.up, 0.076f, 0.068f, segs),
                StandEquipmentBuilder.CreateEllipseRing(new Vector3(0f, 0f,  0.10f), Vector3.right, Vector3.up, 0.088f, 0.078f, segs),
                StandEquipmentBuilder.CreateEllipseRing(new Vector3(0f, 0f,  0.16f), Vector3.right, Vector3.up, 0.074f, 0.066f, segs),
                StandEquipmentBuilder.CreateEllipseRing(new Vector3(0f, 0f,  0.20f), Vector3.right, Vector3.up, 0.044f, 0.040f, segs),
            };
            StandEquipmentBuilder.AppendLoftedRings(verts, tris, rings, capStart: true, capEnd: true);

            return StandEquipmentBuilder.FinalizeMesh("StandBarrageFistMesh", verts, tris, uvScale: 4.0f);
        }

        private static Mesh BuildShockRingMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int loopSteps = 18;
            const int tubeSegs = 6;
            const float majorR = 0.34f;

            var rings = new List<Vector3[]>(loopSteps);
            for (int i = 0; i < loopSteps; i++)
            {
                float a = (i / (float)loopSteps) * Mathf.PI * 2f;
                Vector3 radialOut = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 center = radialOut * majorR;
                rings.Add(StandEquipmentBuilder.CreateEllipseRing(center, radialOut, Vector3.forward, 0.024f, 0.016f, tubeSegs));
            }
            StandEquipmentBuilder.AppendClosedLoopTorus(verts, tris, rings);

            return StandEquipmentBuilder.FinalizeMesh("StandBarrageRingMesh", verts, tris, uvScale: 4.0f);
        }
    }
}
