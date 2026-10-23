using System;
using System.Linq;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEngine;

namespace StormLab.Supercell.Lightning.EditorTools
{
    public static class LightningCloudPreview
    {
        [MenuItem("Supercell Lab/Lightning/Open Cloud Lightning Preview")]
        public static void Open()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before creating the cloud preview.");
            var existing=UnityEngine.Object.FindObjectsByType<LightningPreviewControls>().FirstOrDefault(c=>c.cloudConnected);
            if(existing)
            {
                var owner=existing.transform.parent.gameObject;
                var setup=owner.GetComponent<LightningCloudPreviewSetup>();
                if(!setup) setup=owner.AddComponent<LightningCloudPreviewSetup>();
                setup.skin=owner.GetComponent<StormSkin>(); setup.lightning=existing.lightning; setup.Initialize();
                Selection.activeGameObject=existing.gameObject; return;
            }
            // Unsaved preview objects only. Existing scenes, renderer assets and case studies are not rewritten.
            var root=new GameObject("Lightning Cloud Preview"); root.layer=31;
            var skin=root.AddComponent<StormSkin>();
            skin.playing=false; skin.tier=SkinTier.Low; skin.nearField=false;
            skin.renderScale=.5f; skin.maxSteps=384;
            skin.sunSource=SunSource.Fixed; skin.sunElevation=-18; skin.moonPhase=0; skin.moonElevation=-20;
            skin.UseStandaloneLook(); skin.nightFloor=.012f; skin.lightPollution=0; skin.drawBackdrop=true;
            var env=Environment.Make(StormPreset.All.First(p=>p.key=="plains").ToEnv());
            var storm=new DesignedStorm(new FixedEnvironment(env),new Vec2(0,0),1,ClimoRegion.Plains);
            var wind=new KinematicWind(storm); skin.BindDesigned(storm,wind); skin.groundOrigin=wind.OriginKm*1000;
            for(float t=1710;t<=1800;t+=10) skin.StepTo(t,t);
            var up=skin.Params[Slot.Up];
            var rigObject=new GameObject("Lightning in the storm"); rigObject.layer=31; rigObject.transform.SetParent(root.transform);
            rigObject.transform.position=skin.VolToWorld(new Vector3(up.x,up.y,0));
            var lightning=rigObject.AddComponent<LightningSystem>();
            lightning.advanceClock=false; lightning.showInCloudChannels=true;
            lightning.settings.flashesPerMinute=24; lightning.settings.groundFraction=.65f; lightning.settings.cellRadiusM=3500;
            lightning.groundY=skin.groundHeight;
            var cameraObject=new GameObject("Cloud lightning camera"); cameraObject.layer=31; cameraObject.transform.SetParent(root.transform);
            var camera=cameraObject.AddComponent<Camera>(); camera.tag="MainCamera";
            camera.cullingMask=1<<31; camera.depth=200; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.003f,.004f,.007f); camera.fieldOfView=58;
            camera.nearClipPlane=1; camera.farClipPlane=160000; camera.allowHDR=true;
            cameraObject.AddComponent<AudioListener>(); lightning.observer=camera.transform;
            var controls=rigObject.AddComponent<LightningPreviewControls>(); controls.lightning=lightning;
            controls.previewCamera=camera; controls.cloudConnected=true;
            var bridge=rigObject.AddComponent<LightningCloudBridge>(); bridge.skin=skin; bridge.lightning=lightning;
            skin.TransientLighting=bridge;
            var runtimeSetup=root.AddComponent<LightningCloudPreviewSetup>(); runtimeSetup.skin=skin; runtimeSetup.lightning=lightning;
            runtimeSetup.Initialize();
            controls.Hold(1);
            Selection.activeGameObject=rigObject;
            Debug.Log("Cloud lightning preview ready. Play for hold/replay/type/distance controls. The designed cloud is held at 30 minutes; lightning has its own preview clock.");
        }
    }
}
