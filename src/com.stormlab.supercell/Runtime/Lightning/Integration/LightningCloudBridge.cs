using System;
using System.Collections.Generic;
using StormLab.Supercell.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Lightning
{
    /// <summary>Optional adapter between independent lightning and a storm skin. No dynamics or genome dependency.</summary>
    [ExecuteAlways, DefaultExecutionOrder(120)]
    public sealed class LightningCloudBridge : MonoBehaviour, IStormTransientLighting
    {
        public LightningSystem lightning;
        public StormSkin skin;
        [Min(0)] public float brightness = 24;
        [Min(.1f)] public float sourceRadiusKm = 1.5f;
        [Tooltip("Effective extinction for unresolved multiple scattering. 1 = direct extinction only.")]
        [Range(.01f,1)] public float scatteringExtinction = .04f;
        [Range(4,32)] public int lightTraceSamples = 12;
        [Range(24,96)] public int gridWidth = 64;
        [Range(12,48)] public int gridHeight = 24;
        public bool channelOcclusion = true;
        /// <summary>Optional external local-light source list. Null uses the LightningSystem's channel samples.</summary>
        [NonSerialized] public IReadOnlyList<LightningIllumination> externalSources;

        public RenderTexture LightField => lightField;
        ComputeShader compute;
        Texture3D noise;
        RenderTexture lightField;
        StormSkin boundSkin;
        readonly Vector4[] sources = new Vector4[8];

        void OnEnable() { Attach(); }
        void LateUpdate() { Attach(); RefreshChannelOcclusion(); }
        void Attach()
        {
            if (!lightning) lightning = GetComponent<LightningSystem>();
            if (boundSkin == skin) return;
            if (boundSkin && ReferenceEquals(boundSkin.TransientLighting,this)) boundSkin.TransientLighting = null;
            boundSkin = skin;
            if (boundSkin)
            {
                if (boundSkin.TransientLighting != null && !ReferenceEquals(boundSkin.TransientLighting,this))
                    throw new InvalidOperationException("This storm already has a transient lighting adapter.");
                boundSkin.TransientLighting = this;
            }
        }

        public bool RecordLighting(CommandBuffer cmd, StormSkin target, Camera camera, ComputeShader march, int kernel)
        {
            if (!isActiveAndEnabled || target != skin || target.Volume == null) return false;
            EnsureAssets();
            RefreshChannelOcclusion();
            var list = externalSources ?? (lightning ? lightning.Illumination : null);
            Array.Clear(sources,0,sources.Length);
            int count = 0;
            if (list != null)
            foreach (var sample in list)
            {
                if (!(sample.intensity > .0005f)) continue;
                Vector3 p = skin.WorldToVol(sample.position);
                // Keep the strongest eight samples deterministically; no random or camera-dependent selection.
                int at = Math.Min(count,7);
                if (count >= 8 && sample.intensity <= sources[7].w) continue;
                while (at > 0 && sources[at-1].w < sample.intensity) { if(at<8) sources[at]=sources[at-1]; at--; }
                sources[at]=new Vector4(p.x,p.y,p.z,sample.intensity); count=Math.Min(8,count+1);
            }
            if (count == 0 || brightness <= 0) return false;
            int build = compute.FindKernel("BuildLight");
            BindMedium(cmd,compute,build);
            cmd.SetComputeVectorArrayParam(compute,"_SLLights",sources);
            cmd.SetComputeIntParam(compute,"_SLLightCount",count);
            cmd.SetComputeVectorParam(compute,"_SLLightSettings",new Vector4(brightness,Mathf.Max(.1f,sourceRadiusKm),scatteringExtinction,lightTraceSamples));
            cmd.SetComputeVectorParam(compute,"_SLGrid",new Vector4(lightField.width,lightField.height,lightField.volumeDepth,0));
            cmd.SetComputeTextureParam(compute,build,"_SLLightOut",lightField);
            cmd.BeginSample("Lightning cloud light");
            cmd.DispatchCompute(compute,build,(lightField.width+3)/4,(lightField.height+3)/4,(lightField.volumeDepth+3)/4);
            cmd.EndSample("Lightning cloud light");
            cmd.SetComputeTextureParam(march,kernel,"_TransientLightTex",lightField);
            cmd.SetComputeIntParam(march,"_TransientLightOn",1);
            return true;
        }

        void EnsureAssets()
        {
            if (!compute) compute=Resources.Load<ComputeShader>("StormLightningLight");
            if (!compute) throw new InvalidOperationException("Missing StormLightningLight compute shader.");
            if (!noise) noise=CloudNoise.Make();
            int width=Mathf.Clamp(gridWidth,24,96),height=Mathf.Clamp(gridHeight,12,48);
            if (lightField && lightField.width==width && lightField.volumeDepth==height) return;
            if (lightField) { lightField.Release(); Release(lightField); }
            lightField=new RenderTexture(width,width,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
            {
                dimension=TextureDimension.Tex3D,volumeDepth=height,enableRandomWrite=true,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,
                hideFlags=HideFlags.DontSave,name="Lightning local cloud radiance"
            };
            lightField.Create();
        }

        public void RefreshChannelOcclusion()
        {
            if (!lightning || !lightning.ChannelMaterial) return;
            var material=lightning.ChannelMaterial;
            bool valid=channelOcclusion && skin && skin.Volume!=null && skin.ReadyToRender;
            material.SetFloat("_SLActive",valid ? 1 : 0);
            if(!valid) return;
            EnsureAssets();
            var parameters=skin.GpuParams();
            if(!skin.NearActive) parameters[42]=Vector4.zero;
            material.SetVectorArray("_SP",parameters);
            material.SetTexture("_Noise",noise);
            material.SetTexture("_SLDens",skin.Volume.Dens);
            material.SetTexture("_SLNear",skin.NearActive ? (Texture)skin.Near.Field : SkinVolume.Calm);
            material.SetVector("_SLVolume",VolumeSettings());
            material.SetFloat("_SLNearHeight",skin.NearActive ? skin.Near.H : 0);
            material.SetVector("_SLOrigin",skin.VolToWorld(Vector3.zero));
        }

        /// <summary>Shared binding also used by GPU probes. Does not change density or the normal sunlight grids.</summary>
        public void BindMedium(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            EnsureAssets();
            var parameters=skin.GpuParams();
            if(!skin.NearActive) parameters[42]=Vector4.zero;
            cmd.SetComputeVectorArrayParam(shader,"_SP",parameters);
            cmd.SetComputeVectorParam(shader,"_SLVolume",VolumeSettings());
            cmd.SetComputeFloatParam(shader,"_SLNearHeight",skin.NearActive ? skin.Near.H : 0);
            cmd.SetComputeTextureParam(shader,kernel,"_Noise",noise);
            cmd.SetComputeTextureParam(shader,kernel,"_SLDens",skin.Volume.Dens);
            cmd.SetComputeTextureParam(shader,kernel,"_SLNear",skin.NearActive ? (Texture)skin.Near.Field : SkinVolume.Calm);
        }
        Vector4 VolumeSettings() => new Vector4(skin.Volume.L,skin.Volume.ZT,skin.Volume.cloudMax,skin.Volume.rainMax);
        void OnDisable()
        {
            if(boundSkin && ReferenceEquals(boundSkin.TransientLighting,this)) boundSkin.TransientLighting=null;
            boundSkin=null;
            if(lightning && lightning.ChannelMaterial) lightning.ChannelMaterial.SetFloat("_SLActive",0);
            if(lightField) { lightField.Release(); Release(lightField); lightField=null; }
            if(noise) { Release(noise); noise=null; }
        }
        static void Release(UnityEngine.Object obj) { if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
