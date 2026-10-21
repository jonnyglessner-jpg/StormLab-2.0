using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StormLab.Supercell.Lightning.EditorTools
{
    public static class LightningCloudReview
    {
        [MenuItem("Supercell Lab/Lightning/Capture Cloud Integration Review")]
        public static void Capture()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before the cloud integration review.");
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../validation/lightning/out/clouds"));
            Directory.CreateDirectory(folder);
            var oldSkin=StormSkin.Active;
            var root=new GameObject("Temporary lightning cloud review"); root.layer=31;
            var results=new List<string>();
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                var skin=root.AddComponent<StormSkin>();
                skin.playing=false; skin.tier=SkinTier.Low; skin.nearField=false;
                skin.renderScale=.5f; skin.checkerboard=true; skin.temporalBlend=.15f; skin.maxSteps=384;
                skin.sunSource=SunSource.Fixed; skin.sunElevation=-18; skin.moonPhase=0; skin.moonElevation=-20;
                skin.nightFloor=.012f; skin.lightPollution=0; skin.drawBackdrop=false;
                var env=Environment.Make(StormPreset.All.First(p=>p.key=="plains").ToEnv());
                var storm=new DesignedStorm(new FixedEnvironment(env),new Vec2(0,0),1,ClimoRegion.Plains);
                var wind=new KinematicWind(storm);
                skin.BindDesigned(storm,wind); skin.groundOrigin=wind.OriginKm*1000;
                for(float t=1710;t<=1800;t+=10) skin.StepTo(t,t);
                skin.Activate();
                var cameraObject=new GameObject("Cloud integration camera"); cameraObject.transform.SetParent(root.transform);
                var camera=cameraObject.AddComponent<Camera>(); camera.enabled=false; camera.cullingMask=1<<31;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.003f,.004f,.007f);
                camera.nearClipPlane=1; camera.farClipPlane=160000; camera.fieldOfView=48; camera.allowHDR=true;
                var bridge=root.AddComponent<LightningCloudBridge>(); bridge.skin=skin;
                bridge.externalSources=Array.Empty<LightningIllumination>();
                // Assigning after AddComponent is intentional: explicit registration also supports editor tooling.
                skin.TransientLighting=bridge;
                ProbeChecks(skin,bridge,results);
                ChannelChecks(skin,bridge,camera,root,folder,results);
                skin.ResetVolume();
                for(float t=1710;t<=1800;t+=10) skin.StepTo(t,t);
                AnchorChecks(skin,bridge,results);
                CaptureAnchoredStrike(skin,bridge,camera,root,folder,results);
                var up=skin.Params[Slot.Up];
                var aim=new Vector3(up.x,up.y,4.5f);
                camera.transform.position=skin.VolToWorld(aim+new Vector3(12,-20,-4.3f));
                camera.transform.LookAt(skin.VolToWorld(aim));
                var source=skin.VolToWorld(new Vector3(up.x+1,up.y-2.5f,5.5f));
                using(var off=CaptureImage(camera,8))
                {
                    File.WriteAllBytes(Path.Combine(folder,"cloud_flash_off.png"),off.image.EncodeToPNG());
                    bridge.externalSources=new[] { new LightningIllumination(source,1,FlashKind.InCloud) };
                    using(var on=CaptureImage(camera,1))
                    {
                        File.WriteAllBytes(Path.Combine(folder,"cloud_flash_on.png"),on.image.EncodeToPNG());
                        float pulse=Difference(off.image,on.image);
                        Require(pulse>.0001f,"Local flash must brighten the cloud",results);
                        bridge.externalSources=Array.Empty<LightningIllumination>();
                        using(var after=CaptureImage(camera,1))
                        {
                            File.WriteAllBytes(Path.Combine(folder,"cloud_flash_after.png"),after.image.EncodeToPNG());
                            float residual=Difference(off.image,after.image);
                            Require(residual<Math.Max(.003f,pulse*.35f),"No lingering temporal flash after switch-off",results);
                            results.Add($"Mean pixel change: pulse={pulse:0.000000}, after={residual:0.000000}");
                        }
                    }
                }
                File.WriteAllLines(Path.Combine(folder,"checks.txt"),results);
                Debug.Log("PASS: lightning cloud review. " + string.Join("; ",results) + " Images: " + folder);
            }
            catch(Exception e) { File.WriteAllText(Path.Combine(folder,"failure.txt"),e.ToString()); throw; }
            finally
            {
                var skin=root.GetComponent<StormSkin>(); if(skin) skin.Release();
                Object.DestroyImmediate(root);
                if(oldSkin) oldSkin.Activate();
            }
        }

        static void ProbeChecks(StormSkin skin,LightningCloudBridge bridge,List<string> results)
        {
            var shader=Resources.Load<ComputeShader>("StormLightningLight"); int kernel=shader.FindKernel("Probe");
            var v=skin.Volume;
                float Test(Color32 value,Vector3 a,Vector3 b)
                {
                    Fill(skin,(Color)value);
                    using(var output=new ComputeBuffer(1,4))
                    using(var cmd=new CommandBuffer())
                    {
                        bridge.BindMedium(cmd,shader,kernel);
                        var block=new Vector4[51]; block[17]=new Vector4(v.NV,v.NZ,v.L,v.ZT);
                        cmd.SetComputeVectorArrayParam(shader,"_SP",block);
                        cmd.SetComputeVectorParam(shader,"_SLProbeA",a); cmd.SetComputeVectorParam(shader,"_SLProbeB",b);
                        cmd.SetComputeBufferParam(shader,kernel,"_SLProbeOut",output); cmd.DispatchCompute(shader,kernel,1,1,1);
                        Graphics.ExecuteCommandBuffer(cmd);
                        var data=new float[1]; output.GetData(data); return data[0];
                    }
                }
                var a=new Vector3(0,0,5); var b=new Vector3(0,0,6);
                float clear=Test(new Color32(0,0,0,255),a,b);
                float cloud=Test(new Color32(255,0,0,255),a,b);
                float reverse=Test(new Color32(255,0,0,255),b,a);
                float rain=Test(new Color32(0,204,0,255),a,b);
                float foreground=Test(new Color32(255,0,0,255),new Vector3(0,-40,5),new Vector3(0,-31,5));
                Require(Math.Abs(clear-1)<.0001f,"Empty air transmits the channel",results);
                Require(cloud<.001f,"Opaque cloud hides the channel",results);
                Require(Math.Abs(cloud-reverse)<.0001f,"Reverse and parallel rays remain valid",results);
                Require(rain>0 && rain<.8f,"Rain attenuates channel radiance",results);
                Require(Math.Abs(foreground-1)<.0001f,"Cloud behind a foreground channel does not hide it",results);
                results.Add($"GPU transmittance: clear={clear}, cloud={cloud}, rain={rain}, foreground={foreground}");
        }

        static void Fill(StormSkin skin,Color value)
        {
            var shader=Resources.Load<ComputeShader>("StormLightningLight"); int kernel=shader.FindKernel("FillMedium");
            shader.SetVector("_SLSolid",value); shader.SetTexture(kernel,"_SLTestDens",skin.Volume.Dens);
            shader.Dispatch(kernel,(skin.Volume.NV+3)/4,(skin.Volume.NV+3)/4,(skin.Volume.NZ+3)/4);
        }

        static void AnchorChecks(StormSkin skin,LightningCloudBridge bridge,List<string> results)
        {
            var anvil=skin.Params[Slot.Anvil];
            skin.Params[Slot.Anvil]=Vector4.zero;
            Fill(skin,new Color(0,.9f,0,1)); // Heavy rain without cloud must not count as an origin.
            foreach(FlashKind kind in Enum.GetValues(typeof(FlashKind)))
                Require(!bridge.ResolveAnchors(new FlashEvent(0,0,42,kind)).HasValue,"Reject rain-only/clear origin: "+kind,results);
            skin.Params[Slot.Anvil]=anvil;
            skin.ResetVolume();
            for(float t=1710;t<=1800;t+=10) skin.StepTo(t,t);
            var bas=skin.Params[Slot.Base];
            foreach(FlashKind kind in Enum.GetValues(typeof(FlashKind)))
            {
                int accepted=0;
                for(uint seed=1;seed<=12;seed++)
                {
                    var e=new FlashEvent(seed,0,seed,kind);
                    var anchors=bridge.ResolveAnchors(e);
                    if(!anchors.HasValue) continue;
                    accepted++;
                    var again=bridge.ResolveAnchors(e);
                    Require(again.HasValue && anchors.Value.origin==again.Value.origin && anchors.Value.inCloudTarget==again.Value.inCloudTarget,
                        "Cloud anchors replay: "+kind+" seed "+seed,results);
                    var flash=LightningFlash.Create(e,new LightningSettings(),System.Numerics.Vector3.Zero,skin.groundHeight,null,anchors);
                    Require(flash.Channels[0].points[0]==anchors.Value.origin,"Actual channel retains cloud origin",results);
                    if(kind==FlashKind.InCloud)
                        Require(flash.Channels[0].points[128]==anchors.Value.inCloudTarget,"In-cloud target stays anchored",results);
                    else
                        Require(flash.Channels[0].points[128].Y==skin.groundHeight,"Cloud-origin channel reaches ground",results);
                    if(kind==FlashKind.PositiveGround)
                        Require(skin.WorldToVol(new Vector3(anchors.Value.origin.X,anchors.Value.origin.Y,anchors.Value.origin.Z)).z>bas.x+(bas.y-bas.x)*.55f,
                            "Positive source is in upper cloud/anvil",results);
                }
                Require(accepted>0,"Real storm supplies cloud anchors: "+kind,results);
                results.Add($"Cloud anchors accepted: {kind} {accepted}/12");
            }
        }

        static void CaptureAnchoredStrike(StormSkin skin,LightningCloudBridge bridge,Camera camera,GameObject parent,string folder,List<string> results)
        {
            var go=new GameObject("Anvil strike fixture"); go.layer=31; go.transform.SetParent(parent.transform);
            var rig=go.AddComponent<LightningSystem>(); rig.advanceClock=false; rig.thunder=false;
            rig.settings.flashesPerMinute=24; rig.settings.groundFraction=1; rig.settings.positiveGroundFraction=1;
            rig.settings.cellRadiusM=3500; rig.groundY=skin.groundHeight;
            float oldFraction=bridge.positiveAnvilFraction;
            bool oldBackdrop=skin.drawBackdrop;
            try
            {
                bridge.lightning=rig; bridge.positiveAnvilFraction=1; bridge.externalSources=null; bridge.Connect();
                var events=new List<FlashEvent>(); LightningSchedule.Query(rig.settings,0,60,events);
                var e=events[0]; rig.SampleAt(e.time+.036);
                Require(rig.TryGetFlash(e.id,out var flash),"Cloud-connected system accepts real anvil strike",results);
                var a=flash.Channels[0].points[0]; var b=flash.Channels[0].points[128];
                var origin=new Vector3(a.X,a.Y,a.Z); var end=new Vector3(b.X,b.Y,b.Z);
                var up=skin.Params[Slot.Up]; var bas=skin.Params[Slot.Base]; var tilt=skin.Params[Slot.Tilt];
                var tower=skin.Params[Slot.Tower]; var p=skin.WorldToVol(origin);
                var projected=skin.VolToWorld(new Vector3(up.x+tilt.x*(p.z-bas.x),up.y+tilt.y*(p.z-bas.x),0));
                Vector3 outward=origin-projected; outward.y=0;
                Require(outward.magnitude>tower.y*1200,"Positive strike can originate in anvil beside the tower",results);
                outward.Normalize();
                skin.drawBackdrop=true;
                camera.transform.position=end+outward*26000+Vector3.up*150;
                camera.transform.LookAt((origin+end)*.5f);
                bridge.RefreshChannelOcclusion(); rig.PrepareForCamera(camera);
                using(var image=CaptureImage(camera,2))
                    File.WriteAllBytes(Path.Combine(folder,"positive_anvil_to_ground.png"),image.image.EncodeToPNG());
                results.Add($"Anvil strike origin={origin}; ground={end}; origin height={a.Y-skin.groundHeight:0}m");
            }
            finally
            {
                rig.ResetPlayback(); bridge.lightning=null; bridge.positiveAnvilFraction=oldFraction;
                skin.drawBackdrop=oldBackdrop;
                bridge.externalSources=Array.Empty<LightningIllumination>(); bridge.Connect(); Object.DestroyImmediate(go);
            }
        }

        static void ChannelChecks(StormSkin skin,LightningCloudBridge bridge,Camera camera,GameObject parent,string folder,List<string> results)
        {
            skin.enabled=false; // isolate the actual bolt shader from the full-screen cloud composite
            var go=new GameObject("Occlusion channel fixture"); go.layer=31; go.transform.SetParent(parent.transform);
            var rig=go.AddComponent<LightningSystem>(); rig.advanceClock=false; rig.thunder=false; rig.showInCloudChannels=true;
            rig.settings.groundFraction=0; rig.settings.cellRadiusM=1000;
            bridge.lightning=rig;
            var events=new List<FlashEvent>(); LightningSchedule.Query(rig.settings,-1e-6,60,events);
            var e=events[0];
            try
            {
                void Prepare(float northKm)
                {
                    rig.CellCentreAtTime=_=>skin.VolToWorld(new Vector3(0,northKm,0));
                    rig.ResetPlayback(); rig.SampleAt(e.time+.036);
                    bridge.RefreshChannelOcclusion();
                    var block=new Vector4[51]; block[17]=new Vector4(skin.Volume.NV,skin.Volume.NZ,skin.Volume.L,skin.Volume.ZT);
                    rig.ChannelMaterial.SetVectorArray("_SP",block);
                    rig.PrepareForCamera(camera);
                }
                camera.transform.position=skin.VolToWorld(new Vector3(0,-40,7));
                camera.transform.LookAt(skin.VolToWorld(new Vector3(0,0,7)));
                Fill(skin,new Color(0,0,0,1)); Prepare(0);
                using(var clear=CaptureImage(camera,1))
                {
                    File.WriteAllBytes(Path.Combine(folder,"channel_clear.png"),clear.image.EncodeToPNG());
                    int visible=BrightPixels(clear.image);
                    Require(visible>50,"Actual channel shader renders through empty air",results);
                    Fill(skin,new Color(1,0,0,1)); Prepare(0);
                    using(var hidden=CaptureImage(camera,1))
                    {
                        File.WriteAllBytes(Path.Combine(folder,"channel_cloud_hidden.png"),hidden.image.EncodeToPNG());
                        Require(BrightPixels(hidden.image)<visible*.05f,"Actual channel shader hides a buried channel",results);
                    }
                }
                camera.transform.position=skin.VolToWorld(new Vector3(0,-50,7));
                camera.transform.LookAt(skin.VolToWorld(new Vector3(0,-36,7)));
                Prepare(-36);
                using(var foreground=CaptureImage(camera,1))
                {
                    File.WriteAllBytes(Path.Combine(folder,"channel_foreground.png"),foreground.image.EncodeToPNG());
                    Require(BrightPixels(foreground.image)>50,"Actual foreground channel remains visible with cloud behind it",results);
                }
            }
            finally { bridge.lightning=null; Object.DestroyImmediate(go); skin.enabled=true; skin.Activate(); }
        }
        static int BrightPixels(Texture2D image) => image.GetPixels32().Count(p=>p.r>120 || p.g>120 || p.b>120);

        sealed class ImageResult:IDisposable
        {
            public Texture2D image;
            public void Dispose() { if(image) Object.DestroyImmediate(image); }
        }
        static ImageResult CaptureImage(Camera camera,int frames)
        {
            var rt=new RenderTexture(1200,750,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB); rt.Create();
            try
            {
                for(int i=0;i<frames;i++)
                {
                    var request=new RenderPipeline.StandardRequest { destination=rt };
                    if(RenderPipeline.SupportsRenderRequest(camera,request)) RenderPipeline.SubmitRenderRequest(camera,request);
                    else { camera.targetTexture=rt; camera.Render(); camera.targetTexture=null; }
                }
                var previous=RenderTexture.active; RenderTexture.active=rt;
                var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply(); RenderTexture.active=previous;
                return new ImageResult { image=image };
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); }
        }
        static float Difference(Texture2D a,Texture2D b)
        {
            var x=a.GetPixels32(); var y=b.GetPixels32(); double sum=0;
            for(int i=0;i<x.Length;i++) sum+=(Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b))/(3*255.0);
            return (float)(sum/x.Length);
        }
        static void Require(bool condition,string message,List<string> results)
        { if(!condition) throw new Exception(message); results.Add("PASS "+message); }
    }
}
