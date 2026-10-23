using System;
using System.IO;
using System.Linq;
using System.Reflection;
using StormLab.Supercell;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Targeted presentation checks, not a meteorological calibration.
public static class StormStructureChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    [MenuItem("Supercell Lab/Run Storm Structure Checks")]
    public static void Run()
    {
        try { Motion(); Rain(); RainInterpolation(); RainTransport(); RainObservationOwnership(); }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Application.dataPath, "../Library/StormStructureChecks.last-run.txt"), e.ToString());
            throw;
        }
        File.WriteAllText(Path.Combine(Application.dataPath, "../Library/StormStructureChecks.last-run.txt"),
            "PASS: section speeds <=20.1 m/s, nonrigid tail, seek order and recenter; GPU Qr above LCL, temporal interpolation, moving crop, surface rain, empty sky, and stale binding reset; coarse Qc/Qi do not add cloud geometry; motion-compensated rain interpolation, signed wind/fall transport, pause and recenter; hail-only rain is zero, vanished Qr leaves no stale slab/history, missing brain record is dry.");
        Debug.Log("STORM STRUCTURE PASS: persistent feature geometry, seek determinism, recorded rain above LCL, moving crop, time interpolation and stale-data reset.");
    }

    public static void RunFromCommandLine()
    {
        try
        {
            // Reuse the transport/clock regression suite when present in the test project.
            var baseline = typeof(StormStructureChecks).Assembly.GetType("CloudMotionChecks");
            baseline?.GetMethod("RunChecks", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            Run(); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static Timeline MakeTimeline(int capacity = 64) => new Timeline(SimGrid.Make(40, 40, 20, 1000, 18000), 40, 40, 19, 1000, Vector2.zero, capacity);
    static TimelineFrame Frame(int index)
    {
        var f = new TimelineFrame { index = index, t = index * 10, valid = true, wMax = 40, cloudTop = 12000,
            updraftPos = new Vector2(20000, 20000), updraftCentroid5km = new Vector2(20000, 20000),
            updraftArea5km = 20, coldPoolArea = 150, coldPoolCentroid = new Vector2(20000, 20000), coldPoolMin1km = -3,
            stormMotion = index < 15 ? new Vector2(15, 5) : new Vector2(-15, -5),
            meso1 = new MesoState { present = true, pos = new Vector2(20000, 20000), radius = 2000, zeta = .01f },
            meso4 = new MesoState { present = true, pos = new Vector2(20000, 20000), radius = 3000, zeta = .01f },
            map2D = new byte[Map2D.Count * 40 * 40] };
        for (int k = 0; k < f.gustFront.Length; k++) f.gustFront[k] = 15000;
        for (int field = 0; field < Map2D.Count; field++)
            for (int i = 0; i < 1600; i++)
            {
                float value = field == (int)Map2D.Field.Dbz1km || field == (int)Map2D.Field.CompositeDbz ? -20 :
                    field == (int)Map2D.Field.USfc ? -12 : field == (int)Map2D.Field.VSfc ? -8 : 0;
                f.map2D[field * 1600 + i] = Map2D.Encode((Map2D.Field)field, value);
            }
        return f;
    }
    static void Motion()
    {
        var tl = MakeTimeline(); for (int i = 0; i < 40; i++) tl.Store(Frame(i));
        var env = StormLab.Supercell.Environment.Make(StormPreset.All.First(p => p.key == "plains").ToEnv());
        var designer = new FeatureDesigner(tl, env); var fresh = new FeatureDesigner(tl, env);
        var a = new SkinParams(); var b = new SkinParams();
        designer.Evaluate(0, 0, Vector2.zero, a);
        float maxSpeed = 0;
        for (int t = 1; t <= 390; t++)
        {
            designer.Evaluate(t, t, Vector2.zero, b);
            for (int k = 51; k < SkinParams.NSLOT; k++)
            {
                float speed = Vector2.Distance(a[(Slot)k], b[(Slot)k]) * 1000;
                maxSpeed = Mathf.Max(maxSpeed, speed);
                Require(speed <= 20.1f, "Rigid feature jump at " + t + ": " + speed);
            }
            var swap = a; a = b; b = swap;
        }
        foreach (float t in new[] { 390f, 150f, 30f, 310f, 150f })
        {
            designer.Evaluate(t, t, Vector2.zero, a); fresh.Evaluate(t, t, Vector2.zero, b);
            for (int k = 51; k < SkinParams.NSLOT; k++) Require((a[(Slot)k] - b[(Slot)k]).magnitude < 1e-5f, "Seek-dependent geometry");
        }
        designer.Evaluate(150, 150, Vector2.zero, a); designer.Evaluate(200, 200, Vector2.zero, b);
        Vector2 rootMove = (Vector2)(b[Slot.TailPath0] - a[Slot.TailPath0]);
        Vector2 endMove = (Vector2)(b[Slot.TailPath3] - a[Slot.TailPath3]);
        Require((rootMove - endMove).magnitude > .01f, "Tail still moves as a rigid body");
        designer.Evaluate(200, 200, new Vector2(3000, -2000), a);
        for (int k = 51; k < SkinParams.NSLOT; k++)
            Require(((Vector2)a[(Slot)k] + new Vector2(3, -2) - (Vector2)b[(Slot)k]).magnitude < 1e-4f, "Recenter changed geometry");
        Debug.Log("Structure motion PASS: max section speed " + maxSpeed + " m/s under a reversing storm-motion estimate.");
    }

    static void Rain()
    {
        var tl = MakeTimeline();
        for (int i = 0; i < 2; i++)
        {
            var f = Frame(i); f.winI0 = 10 + i; f.winJ0 = 10; f.winNX = f.winNY = 12;
            f.q3D = new byte[Q3D.Count * tl.NL * 144];
            // Fixed grid column despite a moving crop. A source at 5 km and a
            // shaft extending through the LCL; the second snapshot has twice the Qr.
            for (int z = 0; z <= 6; z++)
                for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++)
                {
                    int cell = (z * 12 + y) * 12 + x;
                    if (x + f.winI0 >= 14 && x + f.winI0 <= 17 && y + 10 >= 14 && y + 10 <= 17)
                    {
                        f.q3D[tl.NL * 144 + cell] = Q3D.Encode(.001f * (i + 1));
                        if (z >= 4)
                        {
                            f.q3D[cell] = Q3D.Encode(.001f);
                            f.q3D[2 * tl.NL * 144 + cell] = Q3D.Encode(.005f);
                        }
                    }
                }
            tl.Store(f);
        }
        var shader = Resources.Load<ComputeShader>("StormSkinVolume");
        Require(shader && !UnityEditor.ShaderUtil.GetComputeShaderMessages(shader).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), "Volume shader errors");
        foreach (var name in new[] { "StormSkinNear", "StormSkinMarch" })
        {
            var cs = Resources.Load<ComputeShader>(name);
            Require(cs && !UnityEditor.ShaderUtil.GetComputeShaderMessages(cs).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), name + " errors");
        }
        using (var hydro = new HydrometeorHistory(tl))
        {
            var volume = new SkinVolume(SkinTier.Low, shader) { Precipitation = hydro, PrecipitationCentre = new Vector2(16000, 16000) };
            var noise = CloudNoise.Make(); var cmd = new CommandBuffer(); var p = new SkinParams();
            p.Set(Slot.Base, 1, 12, 0, 30);
            try
            {
                float Sample(float t, float lcl, float height, Vector2 centre, bool cloud = false)
                {
                    p.Set(Slot.Base, lcl, 12, t, 30); volume.PrecipitationCentre = centre;
                    cmd.Clear(); volume.Build(cmd, p, noise, 0, volume.NZ); Graphics.ExecuteCommandBuffer(cmd);
                    var req = AsyncGPUReadback.Request(volume.Target, 0, TextureFormat.RGBAFloat); req.WaitForCompletion();
                    Require(!req.hasError, "Rain readback failed");
                    int x = Mathf.Clamp(Mathf.FloorToInt((16 - centre.x / 1000 + volume.L) / (2 * volume.L) * volume.NV), 0, volume.NV - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt((16 - centre.y / 1000 + volume.L) / (2 * volume.L) * volume.NV), 0, volume.NV - 1);
                    int z = Mathf.Clamp(Mathf.FloorToInt(height / volume.ZT * volume.NZ), 0, volume.NZ - 1);
                    var c = req.GetData<Color>(z)[y * volume.NV + x]; return cloud ? c.r * c.r : c.g * c.g;
                }
                float first = Sample(0, 1, 5, new Vector2(16000, 16000));
                float middle = Sample(5, 1, 5, new Vector2(16000, 16000));
                float last = Sample(10, 1, 5, new Vector2(16000, 16000));
                Require(first > .02f && middle > first && last > middle, "Rain fails above LCL or does not interpolate");
                Require(Mathf.Abs(middle - Sample(5, 2.6f, 5, new Vector2(16000, 16000))) < .01f, "Rain source is tied to LCL");
                Require(Mathf.Abs(middle - Sample(5, 1, 5, new Vector2(18000, 15000))) < .03f, "Crop/recenter shifted recorded rain");
                Require(Sample(5, 1, .2f, new Vector2(16000, 16000)) > .02f, "Shaft fails to reach surface");
                Require(Sample(5, 1, 9, new Vector2(16000, 16000)) < .01f, "Rain extends beyond recorded source");
                Require(Sample(5, 1, 5, new Vector2(16000, 16000), true) < .001f,
                    "Coarse Qc/Qi diagnostics created unsupported cloud geometry");
                volume.Precipitation = null;
                Require(Sample(5, 1, 5, new Vector2(16000, 16000)) < .01f, "Stale hydro binding leaks into designed mode");
                Debug.Log("Recorded rain GPU PASS: Qr at 5 km, LCL-independent, temporal blend, moving crop, recenter, surface and empty sky.");
            }
            finally { volume.Dispose(); cmd.Release(); UnityEngine.Object.DestroyImmediate(noise); }
        }
    }

    static void RainInterpolation()
    {
        var tl = new Timeline(SimGrid.Make(40, 40, 20, 1000, 18000), 40, 40, 19, 1000, Vector2.zero, 4, 100);
        for (int i = 0; i < 2; i++)
        {
            var f = Frame(i); f.t = i * 100;
            f.winI0 = f.winJ0 = 10; f.winNX = f.winNY = 12;
            f.q3D = new byte[Q3D.Count * tl.NL * 144];
            for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++)
                f.q3D[tl.NL * 144 + ((6-i)*12+y)*12+x] = Q3D.Encode(.001f);
            tl.Store(f);
        }
        using (var hydro = new HydrometeorHistory(tl))
        {
            var volume = new SkinVolume(SkinTier.Low, Resources.Load<ComputeShader>("StormSkinVolume"))
                { Precipitation = hydro, PrecipitationCentre = new Vector2(16000, 16000), rainFall = 10 };
            var noise = CloudNoise.Make(); var cmd = new CommandBuffer(); var p = new SkinParams();
            try
            {
                float At(float time, float height)
                {
                    p.Set(Slot.Base, 1, 12, time, 30);
                    cmd.Clear(); volume.Build(cmd, p, noise, 0, volume.NZ); Graphics.ExecuteCommandBuffer(cmd);
                    var req = AsyncGPUReadback.Request(volume.Target, 0, TextureFormat.RGBAFloat); req.WaitForCompletion();
                    Require(!req.hasError, "Interpolation readback failed");
                    int z = Mathf.Clamp(Mathf.FloorToInt(height / volume.ZT * volume.NZ), 0, volume.NZ-1);
                    float g = req.GetData<Color>(z)[(volume.NV/2)*volume.NV+volume.NV/2].g; return g*g;
                }
                float initial = At(0,6), halfway = At(50,5.5f), final = At(100,5);
                Require(initial > .02f && halfway > .85f * initial && final > .85f * initial,
                    "Falling rain crossfades between fixed layers instead of following its trajectory: " + initial + ", " + halfway + ", " + final);
                Debug.Log("Rain interpolation PASS: a 10 m/s falling source remains coherent halfway between snapshots.");
            }
            finally { volume.Dispose(); cmd.Release(); UnityEngine.Object.DestroyImmediate(noise); }
        }
    }

    static void RainTransport()
    {
        const int n = 64;
        var input = new Texture3D(n,n,n,TextureFormat.RGBAFloat,false)
            { filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp };
        var target = new Texture3D(n,n,n,TextureFormat.RGBAFloat,false);
        var wind = new Texture3D(1,1,1,TextureFormat.RGBAFloat,false);
        var output = new RenderTexture(n,n,0,RenderTextureFormat.ARGBFloat)
            { dimension=TextureDimension.Tex3D, volumeDepth=n, enableRandomWrite=true };
        var shader = Resources.Load<ComputeShader>("StormSkinVolume");
        try
        {
            var pixels = new Color[n*n*n];
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                var p = new Vector3(-1+(x+.5f)*2/n,-1+(y+.5f)*2/n,-1+(z+.5f)*2/n);
                pixels[(z*n+y)*n+x] = new Color(0,Mathf.Exp(-p.sqrMagnitude/.0256f),0,1);
            }
            input.SetPixels(pixels); input.Apply();
            target.SetPixels(new Color[pixels.Length]); target.Apply(); output.Create();
            int kernel=shader.FindKernel("Advect");
            var block=new Vector4[SkinParams.NSLOT]; block[(int)Slot.Grid]=new Vector4(n,n,1,2);
            shader.SetVectorArray("_SP",block);
            shader.SetFloat("_HydroEnabled",1);
            shader.SetVector("_WindP",new Vector4(0,2000,2000,2));
            shader.SetVector("_VolC",new Vector4(1000,1000,7,1));
            shader.SetTexture(kernel,"_DensIn",input); shader.SetTexture(kernel,"_Target",target);
            shader.SetTexture(kernel,"_WindA",wind); shader.SetTexture(kernel,"_WindB",wind);
            shader.SetTexture(kernel,"_OutD",output);
            shader.SetTexture(kernel,"_HydroA",target); shader.SetTexture(kernel,"_HydroB",target);
            void Check(Vector3 air, float dt, Vector2 shift)
            {
                wind.SetPixelData(new Color[] { new Color((air.x+51)/102,(air.y+51)/102,(air.z+51)/102,1) },0); wind.Apply();
                shader.SetVector("_AdvP",new Vector4(dt,0,shift.x,shift.y));
                shader.Dispatch(kernel,n/4,n/4,n/4);
                var req=AsyncGPUReadback.Request(output,0,TextureFormat.RGBAFloat); req.WaitForCompletion();
                Require(!req.hasError,"Rain transport readback failed");
                double mass=0; Vector3 sum=Vector3.zero;
                var first=req.GetData<Color>(0); bool packed=req.layerCount==1 && first.Length==n*n*n;
                for(int z=0;z<n;z++)
                {
                    var data=packed ? first : req.GetData<Color>(z); int start=packed ? z*n*n : 0;
                    for(int y=0;y<n;y++) for(int x=0;x<n;x++)
                    {
                        float d=data[start+y*n+x].g;
                        mass+=d; sum+=d*new Vector3(-1+(x+.5f)*2/n,-1+(y+.5f)*2/n,-1+(z+.5f)*2/n);
                    }
                }
                Require(mass>1,"Recorded-rain path replaced the transported rain with an empty target");
                var actual=sum/(float)mass;
                var expected=(air-new Vector3(0,0,7))*dt/1000-new Vector3(shift.x,shift.y,0);
                Require((actual-expected).magnitude<.002f,"Rain displacement differs from wind minus fall speed: "+actual+" expected "+expected);
            }
            Check(new Vector3(20.2f,-9.8f,.2f),10,Vector2.zero);
            Check(new Vector3(-19.8f,10.2f,14.2f),10,Vector2.zero);
            Check(new Vector3(20.2f,-9.8f,.2f),0,Vector2.zero);
            Check(new Vector3(20.2f,-9.8f,.2f),10,new Vector2(.2f,-.1f));
            Debug.Log("Rain transport PASS: signed winds, gravity-relative fallout, strong updraft, pause and recenter within 2 m.");
        }
        finally
        {
            shader.SetFloat("_HydroEnabled",0);
            output.Release(); UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(wind);
        }
    }

    static void RainObservationOwnership()
    {
        var tl=MakeTimeline();
        for(int index=0;index<2;index++)
        {
            var f=Frame(index); f.winI0=f.winJ0=10; f.winNX=f.winNY=12;
            int plane=144, field=plane*tl.NL;
            f.q3D=new byte[Q3D.Count*field];
            for(int z=0;z<7;z++) for(int i=0;i<plane;i++)
            {
                f.q3D[field+z*plane+i]=Q3D.Encode(index==0?.001f:0);
                f.q3D[3*field+z*plane+i]=Q3D.Encode(.02f);
            }
            tl.Store(f);
        }
        using(var hydro=new HydrometeorHistory(tl))
        using(var volume=new SkinVolume(SkinTier.Low,Resources.Load<ComputeShader>("StormSkinVolume")))
        {
            var noise=CloudNoise.Make(); var cmd=new CommandBuffer(); var p=new SkinParams();
            volume.Precipitation=hydro; volume.PrecipitationCentre=new Vector2(16000,16000); volume.advectEvery=1;
            float Sample()
            {
                var req=AsyncGPUReadback.Request(volume.Dens,0,TextureFormat.RGBAFloat); req.WaitForCompletion();
                Require(!req.hasError,"Live rain observation readback failed");
                int z=Mathf.FloorToInt(5/volume.ZT*volume.NZ);
                return req.GetData<Color>(z)[(volume.NV/2)*volume.NV+volume.NV/2].g;
            }
            try
            {
                p.Set(Slot.Base,1,12,0,30);
                volume.Frame(cmd,p,noise,null,false,volume.PrecipitationCentre,Vector2.zero,0,60,Vector3.up,true,12,4);
                Graphics.ExecuteCommandBuffer(cmd); Require(Sample()>.1f,"Initial live Qr missing"); cmd.Clear();
                p.Set(Slot.Base,1,12,10,30);
                // All but a small cloud-target slab still belongs to the earlier time. Live
                // rain must use the current observed mass, not that stale target or old density.
                volume.Frame(cmd,p,noise,null,false,volume.PrecipitationCentre,Vector2.zero,10,60,Vector3.up,false,12,4);
                Graphics.ExecuteCommandBuffer(cmd);
                Require(Sample()<.001f,"Hail or a stale transported rain copy survives after Qr vanishes"); cmd.Clear();
                var missing=Frame(2); missing.q3D=null; tl.Store(missing); p.Set(Slot.Base,1,12,20,30);
                volume.Frame(cmd,p,noise,null,false,volume.PrecipitationCentre,Vector2.zero,10,60,Vector3.up,false,12,4);
                Graphics.ExecuteCommandBuffer(cmd);
                Require(Sample()<.001f,"Missing brain record creates fallback rain");
                Debug.Log("Rain ownership PASS: Qr-only, no hail shroud, no stale slab/history ghost, missing record dry.");
            }
            finally { cmd.Release(); UnityEngine.Object.DestroyImmediate(noise); }
        }
    }
}
