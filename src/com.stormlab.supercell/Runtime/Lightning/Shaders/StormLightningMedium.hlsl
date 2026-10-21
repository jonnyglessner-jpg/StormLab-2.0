#ifndef STORM_LIGHTNING_MEDIUM
#define STORM_LIGHTNING_MEDIUM
// Reuse the skin's analytic anvil and near-feature definitions. Private sampler names avoid URP's global samplers.
#define sampler_LinearClamp sl_LinearClamp
#define sampler_LinearRepeat sl_LinearRepeat
#include "Packages/com.stormlab.supercell/Runtime/Rendering/Shaders/StormSkinCommon.hlsl"
#undef sampler_LinearClamp
#undef sampler_LinearRepeat

Texture3D<float4> _SLDens, _SLNear;
// xy macro half-width and height km; zw cloud and rain extinction 1/km.
float4 _SLVolume;
float _SLNearHeight;
// World origin of the volume frame, in metres, plus independent toggle.
float4 _SLOrigin;
float _SLActive;

float3 slWorldToVolume(float3 world) { return (world.xzy - _SLOrigin.xzy) * .001; }
float3 slUVW(float3 p) { return float3(p.xy / (2 * _SLVolume.x) + .5, p.z / _SLVolume.y); }
float slSmooth(float a, float b, float v) { float t = saturate((v-a)/(b-a)); return t*t*(3-2*t); }

// Coarse extinction matching the skin's water/ice thresholds, near-field replacement and rain curtain.
// It deliberately omits the march's camera-filtered sub-voxel billows, not a second moving cloud field.
float slExtinction(float3 p)
{
    bool inVolume = all(abs(p.xy) < _SLVolume.x) && p.z >= 0 && p.z < _SLVolume.y;
    float4 s = inVolume ? _SLDens.SampleLevel(sl_LinearClamp, slUVW(p), 0) : 0;
    if (S_anvil.w > 0)
    {
        float2 centre = S_up.xy + S_tilt.xy * (S_base.y - S_base.x);
        float blend = inVolume ? ss(3*S_tower.y, 5*S_tower.y, length(p.xy-centre)) : 1;
        if (blend > .01)
        {
            AnvilS a = anvilAt(p);
            float r = sqrt(max(a.av, 0));
            if (s.a < .3 || s.r <= .004 || r > s.r) { s.a = lerp(s.a,a.mat,blend); s.r=lerp(s.r,r,blend); }
        }
    }
    float ice = 1-saturate(s.a/.3);
    float water = slSmooth(.26,.4,s.r) * (.4+.6*slSmooth(.4,.75,s.r));
    float iceDensity = slSmooth(.04,.8,s.r*.65)*.55;
    float cloud = lerp(water,iceDensity,slSmooth(0,1,ice));
    float nearWeight = 0;
    if (_SLNearHeight > 0 && S_near.w > 0 && p.z >= S_near.z)
    {
        float2 edge = S_near.w-abs(p.xy-S_near.xy);
        nearWeight = slSmooth(0,.5,min(edge.x,edge.y)) * slSmooth(0,.3,S_near.z+_SLNearHeight-p.z);
        if (S_near.z > .001) nearWeight *= slSmooth(0,.05,p.z-S_near.z);
        if (nearWeight > 0)
        {
            float3 uv = float3((p.xy-S_near.xy)/(2*S_near.w)+.5,(p.z-S_near.z)/_SLNearHeight);
            float4 n = _SLNear.SampleLevel(sl_LinearClamp,uv,0);
            float edgeWidth = lerp(.035,.14,n.g);
            cloud = lerp(cloud,slSmooth(.5-edgeWidth,.5+edgeWidth,n.r),nearWeight);
        }
    }
    // Analytic low features can extend past their last coarse snapshot.
    if (inVolume && nearWeight < 1 && p.z < S_base.x+1.2 && s.b > .05)
        cloud += (1-nearWeight)*slSmooth(.22,.38,nearField(p,0))*.8;
    float rain = s.g*s.g*slSmooth(.2,.5,s.g)*_SLVolume.w;
    return max(0,cloud*_SLVolume.z+rain);
}

// Slab intersection handles parallel and negative directions without a sign-changing epsilon.
bool slBox(float3 origin,float3 direction,float3 low,float3 high,out float enter,out float leave)
{
    enter=0; leave=1e6;
    [unroll] for(int axis=0;axis<3;axis++)
    {
        if(abs(direction[axis])<1e-7)
        { if(origin[axis]<low[axis] || origin[axis]>high[axis]) return false; }
        else
        {
            float a=(low[axis]-origin[axis])/direction[axis],b=(high[axis]-origin[axis])/direction[axis];
            enter=max(enter,min(a,b)); leave=min(leave,max(a,b));
        }
    }
    return leave>enter;
}

float slOpticalDepth(float3 a,float3 b,int budget,float extinctionScale)
{
    float3 delta=b-a; float distance=length(delta);
    if(distance<1e-5) return 0;
    float3 direction=delta/distance;
    float enter,leave;
    // Include the analytic far anvil, whose canopy can extend beyond the stored 60 km volume.
    float extent=max(_SLVolume.x,S_anvil.w>0 ? S_anvil.z+length(S_up.xy)+20 : 0);
    if(!slBox(a,direction,float3(-extent,-extent,0),float3(extent,extent,max(_SLVolume.y,S_base.y+3)),enter,leave)) return 0;
    leave=min(leave,distance); if(leave<=enter) return 0;
    int count=clamp((int)ceil((leave-enter)/.12),1,budget);
    float stepLength=(leave-enter)/count, tau=0;
    [loop] for(int i=0;i<count;i++)
    {
        tau+=slExtinction(a+direction*(enter+(i+.5)*stepLength))*stepLength*extinctionScale;
        if(tau>14) break;
    }
    return tau;
}
#endif
