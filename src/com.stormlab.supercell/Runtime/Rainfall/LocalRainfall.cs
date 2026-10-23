using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Rainfall
{
    /// <summary>Bounded CPU near-camera drops, with inflow through all six faces of a moving
    /// volume. Diagnostic water flux is independent of the visual representative-drop budget.</summary>
    [DefaultExecutionOrder(140)]
    public sealed class LocalRainfall : MonoBehaviour
    {
        public Camera targetCamera;
        [Tooltip("Optional component implementing IRainWeatherSource. Empty uses the values below.")]
        public MonoBehaviour weatherSource;
        [Min(0)] public float rainMmPerHour = 15;
        public Vector3 windMps = new Vector3(4, 0, 1);
        [Range(.4f, 1.5f)] public float airDensity = 1.225f;
        [Header("Local volume (metres)")]
        public Vector3 volumeSize = new Vector3(40, 24, 40);
        [Range(64, 8192)] public int maxDrops = 2048;
        [Range(16, 512)] public int maxSpawnsPerFrame = 128;
        [Header("Collisions")]
        public bool collideWithWorld = true;
        public LayerMask collisionMask = ~0;
        [Tooltip("Upward shelter check for newly created drops. Roofs must have colliders. Metres.")]
        [Min(1)] public float shelterProbeHeight = 500;
        public float groundHeight;
        [Header("Presentation")]
        [Range(.001f, .04f)] public float shutterSeconds = .012f;
        [Range(1, 12)] public float widthMultiplier = 3;
        public Color tint = new Color(.65f, .73f, .8f, .4f);
        [Tooltip("Optional material using vertex colour and UVs. Default shader is included in Resources.")]
        public Material material;
        public bool advanceClock = true;
        public float timeSeconds;
        public RainSpectrum Spectrum { get; } = new RainSpectrum();
        public int ActiveDrops => count;
        public double RepresentativeFraction { get; private set; }
        public IRainWeatherSource SourceOverride { get; set; }

        struct Drop { public Vector3 position; public int bin; }
        Drop[] drops;
        Vector3[] vertices;
        Color[] colours;
        Vector2[] uv;
        int[] triangles;
        readonly double[] fluxCdf = new double[RainSpectrum.BinCount];
        readonly System.Random random = new System.Random(81723);
        Mesh mesh;
        Material ownedMaterial;
        int count;
        double carry;
        Vector3 previousCentre;
        bool started;

        void OnEnable() { started = false; count = 0; carry = 0; }
        public void ResetDrops() { started = false; count = 0; carry = 0; }

        void EnsureResources()
        {
            int capacity = Mathf.Clamp(maxDrops, 64, 8192);
            if (drops == null || drops.Length != capacity)
            {
                drops = new Drop[capacity]; vertices = new Vector3[capacity * 4];
                colours = new Color[capacity * 4]; uv = new Vector2[capacity * 4]; triangles = new int[capacity * 6];
                for (int i = 0; i < capacity; i++)
                {
                    int v = i * 4, t = i * 6;
                    uv[v] = new Vector2(0, 0); uv[v+1] = new Vector2(1, 0);
                    uv[v+2] = new Vector2(1, 1); uv[v+3] = new Vector2(0, 1);
                    triangles[t] = v; triangles[t+1] = v+1; triangles[t+2] = v+2;
                    triangles[t+3] = v; triangles[t+4] = v+2; triangles[t+5] = v+3;
                }
                count = 0; started = false;
            }
            if (!mesh) { mesh = new Mesh { name = "Local rainfall streaks", hideFlags = HideFlags.DontSave }; mesh.MarkDynamic(); }
            if (!material && !ownedMaterial)
            {
                var shader = Resources.Load<Shader>("StormRainStreak");
                if (shader) ownedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                else { Debug.LogError("StormRainStreak shader is missing. Import Rainfall/Resources with the scripts.", this); enabled = false; }
            }
        }

        float Unit() => (float)random.NextDouble();
        Vector3 RandomPoint(Vector3 centre, Vector3 size) => centre + new Vector3((Unit()-.5f)*size.x, (Unit()-.5f)*size.y, (Unit()-.5f)*size.z);
        bool Sheltered(Vector3 p) => collideWithWorld && Physics.Raycast(p, Vector3.up, shelterProbeHeight, collisionMask, QueryTriggerInteraction.Ignore);
        int Choose(double[] cdf, double total)
        {
            double u = random.NextDouble() * total;
            for (int i = 0; i < cdf.Length - 1; i++) if (u < cdf[i]) return i;
            return cdf.Length - 1;
        }

        void LateUpdate() { Tick(Time.deltaTime); }

        /// <summary>Advance local drops in real seconds; exposed for deterministic validation.
        /// Normal use calls this from LateUpdate. Large frame stalls are capped at 0.1 s.</summary>
        public void Tick(float deltaSeconds)
        {
            if (!targetCamera) targetCamera = Camera.main;
            if (!targetCamera) return;
            EnsureResources();
            if (!enabled) return;
            float dt = Mathf.Clamp(deltaSeconds, 0, .1f);
            if (advanceClock) timeSeconds += dt;
            var source = SourceOverride ?? weatherSource as IRainWeatherSource;
            Vector3 centre = targetCamera.transform.position;
            Vector3 size = new Vector3(Mathf.Max(2, volumeSize.x), Mathf.Max(2, volumeSize.y), Mathf.Max(2, volumeSize.z));
            var weather = source != null ? source.Sample(centre, timeSeconds) : new RainWeather { rateMmPerHour = rainMmPerHour, airDensity = airDensity, windMps = windMps };
            weather.Fill(Spectrum);
            bool reset = !started || (centre - previousCentre).sqrMagnitude > size.sqrMagnitude * .25f;
            Vector3 cameraVelocity = !reset && dt > 0 ? (centre - previousCentre) / dt : Vector3.zero;
            previousCentre = centre;
            double expected = Spectrum.NumberDensity * size.x * size.y * size.z;
            // Smoothly increase visible count with concentration, then approach the fixed budget.
            RepresentativeFraction = drops.Length / (drops.Length + expected);
            if (reset)
            {
                count = 0; carry = 0; started = true;
                double total = 0;
                for (int i = 0; i < fluxCdf.Length; i++) fluxCdf[i] = total += Spectrum.NumberPerM3[i];
                int initial = Math.Min(maxSpawnsPerFrame, (int)(expected * RepresentativeFraction));
                for (int i = 0; i < initial && total > 0; i++)
                {
                    Vector3 p = RandomPoint(centre, size);
                    if (p.y > groundHeight && !Sheltered(p)) drops[count++] = new Drop { position = p, bin = Choose(fluxCdf, total) };
                }
            }
            for (int i = count - 1; i >= 0; i--)
            {
                var drop = drops[i];
                Vector3 velocity = weather.windMps - Vector3.up * (float)Spectrum.SpeedMps[drop.bin];
                Vector3 next = drop.position + velocity * dt;
                Vector3 offset = next - centre;
                bool remove = Mathf.Abs(offset.x) > size.x*.5f || Mathf.Abs(offset.y) > size.y*.5f || Mathf.Abs(offset.z) > size.z*.5f || next.y <= groundHeight;
                if (!remove && collideWithWorld && dt > 0)
                    remove = Physics.Linecast(drop.position, next, collisionMask, QueryTriggerInteraction.Ignore);
                if (remove) { drops[i] = drops[--count]; continue; }
                drop.position = next; drops[i] = drop;
            }
            double inflow = 0;
            Vector3 relativeWind = weather.windMps - cameraVelocity;
            for (int i = 0; i < fluxCdf.Length; i++)
            {
                double faceFlux = Math.Abs(relativeWind.x)*size.y*size.z + Math.Abs(relativeWind.y-Spectrum.SpeedMps[i])*size.x*size.z + Math.Abs(relativeWind.z)*size.x*size.y;
                fluxCdf[i] = inflow += Spectrum.NumberPerM3[i] * faceFlux * RepresentativeFraction;
            }
            carry = Math.Min(carry + inflow * dt, maxSpawnsPerFrame);
            int attempts = (int)carry; carry -= attempts;
            for (int n = 0; n < attempts && count < drops.Length && inflow > 0; n++)
            {
                int bin = Choose(fluxCdf, inflow);
                Vector3 v = relativeWind - Vector3.up * (float)Spectrum.SpeedMps[bin];
                float fx = Mathf.Abs(v.x)*size.y*size.z, fy = Mathf.Abs(v.y)*size.x*size.z, fz = Mathf.Abs(v.z)*size.x*size.y;
                float face = Unit() * (fx + fy + fz);
                Vector3 p = RandomPoint(centre, size);
                if (face < fx) p.x = centre.x - Mathf.Sign(v.x)*size.x*.4999f;
                else if (face < fx+fy) p.y = centre.y - Mathf.Sign(v.y)*size.y*.4999f;
                else p.z = centre.z - Mathf.Sign(v.z)*size.z*.4999f;
                if (p.y > groundHeight && !Sheltered(p)) drops[count++] = new Drop { position = p, bin = bin };
            }
            Draw(centre, size, weather.windMps);
        }

        void Draw(Vector3 centre, Vector3 size, Vector3 wind)
        {
            for (int i = 0; i < count; i++)
            {
                var d = drops[i];
                Vector3 velocity = wind - Vector3.up * (float)Spectrum.SpeedMps[d.bin];
                Vector3 tail = velocity * Mathf.Max(.001f, shutterSeconds);
                Vector3 view = targetCamera.transform.position - d.position;
                Vector3 side = Vector3.Cross(view, tail).normalized;
                if (side.sqrMagnitude < .01f) side = targetCamera.transform.right;
                side *= (float)Spectrum.DiameterMm[d.bin] * .0005f * Mathf.Max(1, widthMultiplier);
                int at = i*4;
                vertices[at] = d.position - centre - side;
                vertices[at+1] = d.position - centre + side;
                vertices[at+2] = d.position - centre - tail + side;
                vertices[at+3] = d.position - centre - tail - side;
                float fade = Mathf.Clamp01((1 - Mathf.Max(Mathf.Abs(view.x)/(size.x*.5f), Mathf.Abs(view.z)/(size.z*.5f))) * 5);
                Color c = tint; c.a *= fade;
                for (int k = 0; k < 4; k++) colours[at+k] = c;
            }
            // Clear unused quads so a shrinking population leaves no stale streaks.
            Array.Clear(vertices, count*4, vertices.Length-count*4);
            mesh.Clear(false); mesh.vertices = vertices; mesh.colors = colours; mesh.uv = uv;
            mesh.SetTriangles(triangles, 0, count*6, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, size + Vector3.one*20);
            Graphics.DrawMesh(mesh, Matrix4x4.Translate(centre), material ? material : ownedMaterial,
                gameObject.layer, targetCamera, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }

        void OnDisable()
        {
            if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            if (ownedMaterial) { if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial); }
            mesh = null; ownedMaterial = null; count = 0; started = false;
        }
    }
}
