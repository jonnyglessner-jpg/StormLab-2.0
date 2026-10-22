// Diagnostics and playback controls for the brain-driven SupercellSkinTest scene.
// This panel observes recorded simulation frames; it does not prescribe cloud growth or wind speeds.
using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Rendering;
using UnityEngine;

namespace StormLab.Supercell
{
    [DefaultExecutionOrder(200)]
    public sealed class BrainPreviewPanel : MonoBehaviour
    {
        SupercellRunner runner;
        StormSkin skin;
        Rect panel = new Rect(10, 10, 410, 100);
        Vector2 scroll;
        bool follow;
        float sampledAt, sampledTime, actualSpeed;
        static int nextPanelId = 910240;
        int panelId;
        static readonly float[] PlaybackSpeeds = { 1f, 10f, 30f };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachToPreview()
        {
            // Existing copies of the test scene get the panel without needing to rebuild or overwrite the scene.
            foreach (var r in Object.FindObjectsByType<SupercellRunner>())
                if (r.gameObject.scene.name == "SupercellSkinTest" && !r.GetComponent<BrainPreviewPanel>())
                    r.gameObject.AddComponent<BrainPreviewPanel>();
        }

        void Awake()
        {
            runner = GetComponent<SupercellRunner>(); skin = GetComponent<StormSkin>();
            panelId = nextPanelId++;
            sampledAt = Time.realtimeSinceStartup;
        }

        void LateUpdate()
        {
            if (runner == null || runner.Run == null || skin == null) return;
            float now = Time.realtimeSinceStartup;
            if (now - sampledAt >= 1f)
            {
                actualSpeed = (runner.Run.Time - sampledTime) / (now - sampledAt);
                sampledTime = runner.Run.Time; sampledAt = now;
            }
            if (follow) FrameUpdraft();
        }

        TimelineFrame DisplayedFrame => runner?.Timeline?.AtTime(skin != null ? skin.playbackTime : 0);
        static string Clock(float t) => $"{Mathf.FloorToInt(t / 60):00}:{Mathf.FloorToInt(t % 60):00}";

        void FrameUpdraft()
        {
            var frame = DisplayedFrame; var cam = Camera.main;
            if (frame == null || skin == null || cam == null || float.IsNaN(frame.updraftPos.x) || float.IsNaN(frame.updraftPos.y)) return;
            Vector3 ground = skin.GridToWorld(frame.updraftPos, 0);
            float lookHeight = Mathf.Clamp(frame.cloudTop * 0.4f, 1000, 6000);
            cam.transform.position = ground + new Vector3(12000, 2, -12000);
            cam.transform.rotation = Quaternion.LookRotation(ground + Vector3.up * lookHeight - cam.transform.position);
        }

        void OnGUI()
        {
            if (runner == null || skin == null) return;
            panel.width = Mathf.Min(410, Screen.width - 20);
            panel = GUILayout.Window(panelId, panel, Draw, "Brain preview — simulated winds");
        }

        void Draw(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(Mathf.Max(100, Screen.height - 65)));
            if (runner.Run == null)
            {
                GUILayout.Label("Waiting for the brain to initialize. Check Console for errors.");
                GUILayout.EndScrollView(); return;
            }
            var run = runner.Run; var frame = DisplayedFrame;
            GUILayout.Label($"Displayed: {Clock(skin.playbackTime)}    Computed: {Clock(run.Horizon)}");
            GUILayout.Label($"Model: {Clock(run.Time)}    Actual compute speed: {actualSpeed:0.0}x");
            string state = !skin.playing ? "Playback paused; brain continues computing."
                : run.Finished && skin.playbackTime >= run.Horizon ? "End of computed run."
                : skin.playbackTime >= run.Horizon - 0.1f ? "Playback is waiting for the brain to compute ahead."
                : "Playback advancing through recorded simulation frames.";
            GUILayout.Label(state);
            GUILayout.Label(run.plan.ToString());
            if (frame != null)
            {
                GUILayout.Label($"At displayed time: peak updraft {frame.wMax:0.0} m/s; cloud top {frame.cloudTop / 1000:0.00} km");
                GUILayout.Label($"Peak reflectivity {frame.maxDbz:0.0} dBZ; wind snapshots {skin.Wind?.Count ?? 0}");
                if (frame.cloudTop <= 0) GUILayout.Label("No cloud detected in this frame yet. Let the brain advance.");
            }
            else GUILayout.Label("Waiting for the first recorded cloud/wind frame.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(skin.playing ? "Pause playback" : "Play")) skin.playing = !skin.playing;
            if (GUILayout.Button("Latest computed time")) skin.playbackTime = run.Horizon;
            GUILayout.EndHorizontal();
            GUILayout.Label($"Playback speed: {skin.timelapse:0}x (1x = real time)");
            GUILayout.BeginHorizontal();
            foreach (float speed in PlaybackSpeeds) if (GUILayout.Button($"{speed:0}x")) skin.timelapse = speed;
            GUILayout.EndHorizontal();
            GUILayout.Label("Scrub within computed frames:");
            float seek = GUILayout.HorizontalSlider(skin.playbackTime, 0, Mathf.Max(0.01f, run.Horizon));
            if (Mathf.Abs(seek - skin.playbackTime) > 0.01f) skin.playbackTime = seek;
            GUILayout.Label($"Compute-ahead limit: {(runner.maxSpeed <= 0 ? "GPU budget only" : runner.maxSpeed.ToString("0") + "x")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("10x limit")) runner.maxSpeed = 10;
            if (GUILayout.Button("30x limit")) runner.maxSpeed = 30;
            if (GUILayout.Button("GPU budget only")) runner.maxSpeed = 0;
            GUILayout.EndHorizontal();
            GUILayout.Label($"Readbacks pending: {run.PendingReadbacks}; step {runner.Scheduler?.stepMs ?? 0:0.0} ms");
            if (GUILayout.Button("Frame updraft in camera")) FrameUpdraft();
            follow = GUILayout.Toggle(follow, "Keep camera following the updraft");
            GUILayout.Label("Use faster playback to reach cloud development, then switch to 1x to assess motion.");
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, panel.width, 20));
        }
    }
}
