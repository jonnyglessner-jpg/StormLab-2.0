// Shared by the skin's compute passes: the parameter block (SkinParams.cs, 51 float4 slots), noise, and the
// near-ground features. Ported from StormLab 1.0's src/storm3d/shaders.ts (WGSL, MIT). Lengths are km in the volume
// frame (origin at the volume centre, x east, y north, z up above ground).
#ifndef STORM_SKIN_COMMON
#define STORM_SKIN_COMMON

float4 _SP[51];
#define S_f      _SP[0]
#define S_base   _SP[1]
#define S_tower  _SP[2]
#define S_tilt   _SP[3]
#define S_anvil  _SP[4]
#define S_anvil2 _SP[5]
#define S_wall   _SP[6]
#define S_wall2  _SP[7]
#define S_tail   _SP[8]
#define S_ffd    _SP[9]
#define S_hail   _SP[10]
#define S_rfd    _SP[11]
#define S_flank  _SP[12]
#define S_tor    _SP[13]
#define S_tor2   _SP[14]
#define S_tor3   _SP[15]
#define S_up     _SP[16]
#define S_grid   _SP[17]
#define S_rfdb   _SP[18]
#define S_anti   _SP[19]
#define S_slab   _SP[20]
#define S_meso1  _SP[21]
#define S_meso4  _SP[22]
#define S_mesov  _SP[23]
#define S_ot     _SP[24]
#define S_anvil3 _SP[25]
#define S_mamm   _SP[26]
#define S_inflow _SP[27]
#define S_tail2  _SP[28]
#define S_vault  _SP[29]
#define S_slot2  _SP[30]
#define S_ffd2   _SP[31]
#define S_rcore  _SP[32]
#define S_gust   _SP[33]
#define S_flank2 _SP[34]
#define S_plates _SP[40]
#define S_curt   _SP[41]
#define S_near   _SP[42]
#define S_tor4   _SP[43]
#define S_tor5   _SP[44]
#define S_tor6   _SP[45]
#define S_tor7   _SP[46]
#define S_tor8   _SP[47]
#define S_tor9   _SP[48]
#define S_tor10  _SP[49]
#define S_tor11  _SP[50]

Texture3D<float> _Noise;
SamplerState sampler_LinearRepeat;
SamplerState sampler_LinearClamp;

// smoothstep that also works with reversed edges (1.0 relies on that; HLSL leaves it undefined)
float ss(float a, float b, float x) { float t = saturate((x - a) / (b - a)); return t * t * (3.0 - 2.0 * t); }
float n3(float3 p) { return _Noise.SampleLevel(sampler_LinearRepeat, p, 0); }

// Filtered noise for the march: g_fp is the pixel footprint (km) at the current sample. n3k(q, k, o) samples the noise
// at q * k + o from the mip level whose texels are about the footprint, so octaves finer than a pixel average out
// instead of sparkling (the grain on distant towers). 0 = unfiltered, as in the volume passes
static float g_fp = 0.0;
float nlod(float k) { return max(0.0, log2(max(g_fp * 72.0 * k, 1e-6))); }   // 48 texels per tile, x 1.5
// bilinear from the nearest mip (half the cost of trilinear in the march); an octave filtered down to its 3rd mip is
// close to its mean and is not fetched at all
float n3k(float3 q, float k, float3 o) { float l = nlod(k); return l > 3.0 ? 0.5 : _Noise.SampleLevel(sampler_LinearRepeat, q * k + o, l); }
// one ridged (cauliflower) octave, faded to its mean as it is filtered away
float ridge(float3 q, float k, float3 o) { float l = nlod(k); if (l > 2.0) return 0.5; float r = 1.0 - abs(2.0 * _Noise.SampleLevel(sampler_LinearRepeat, q * k + o, l) - 1.0); return lerp(r, 0.5, saturate(l * 0.5)); }

// ---- the simulated wind (WindHistory: 2 km blocks x 1 km layers, two snapshots blended on the GPU)
Texture3D<float4> _WindA;
Texture3D<float4> _WindB;
// x: wind blend A->B, yz: domain size m, w: wind layers
float4 _WindP;
// xy: volume centre in grid m, z: rain fall speed m/s, w: 1 = use the wind
float4 _VolC;
float3 windAt(float3 p)
{
    if (_VolC.w < 0.5) return 0.0;
    float2 gm = _VolC.xy + p.xy * 1000.0;
    float3 uvw = float3(gm.x / _WindP.y, gm.y / _WindP.z, (p.z + 0.5) / _WindP.w);
    float4 a = _WindA.SampleLevel(sampler_LinearClamp, uvw, 0);
    float4 b = _WindB.SampleLevel(sampler_LinearClamp, uvw, 0);
    float3 e = lerp(a.xyz, b.xyz, _WindP.x);
    return -51.0 + e * 255.0 * 0.4;
}

// Flow-mapped detail: the fine noise is carried with the simulated wind in two phases of FLOW_T clock-seconds, each
// restarting where the other is at full weight, so detail drifts and rises with the air and never stretches.
// o0, o1: offsets to add to the noise coordinate (km); w0: weight of phase 0 (phase 1 gets 1 - w0)
#define FLOW_T 40.0
void flowPhases(float3 p, out float3 o0, out float3 o1, out float w0)
{
    float3 u = windAt(p) * 0.001;
    float ph = S_base.z / FLOW_T;
    float f0 = frac(ph), f1 = frac(ph + 0.5);
    o0 = -u * (f0 * FLOW_T) + floor(ph) * float3(0.273, 0.581, 0.137);
    o1 = -u * (f1 * FLOW_T) + floor(ph + 0.5) * float3(0.419, 0.127, 0.733) + float3(0.5, 0.21, 0.37);
    w0 = 1.0 - abs(2.0 * f0 - 1.0);
}
// three octaves, about 11, 4.3 and 1.7 km
float fbm(float3 p) { return 0.5 * n3(p * 0.09) + 0.3 * n3(p * 0.23 + float3(0.31, 0.17, 0.53)) + 0.2 * n3(p * 0.6 + float3(0.7, 0.2, 0.4)); }
// rotate a horizontal vector counter-clockwise (x east, y north) by angle a
float2 rot2(float2 v, float a) { float c = cos(a); float s = sin(a); return float2(c * v.x - s * v.y, s * v.x + c * v.y); }
float hash1(float x) { return frac(sin(x * 127.1 + 311.7) * 43758.5453); }
float2 safeNorm(float2 v, float2 fallback) { float l = length(v); return l > 1e-4 ? v / l : fallback; }

// The low-level mesocyclone: once the rear-flank downdraft surges, the rotating updraft's lower few km broaden over the
// horseshoe, so its rim rises from the horseshoe's lip into the updraft. Centre and extra radius at height zr above base
float2 mesoCentre(float zr)
{
    float lo = exp(-max(zr, 0.0) / 2.5);
    return S_up.xy + S_tilt.xy * max(zr, 0.0) + (S_rfdb.xy - S_up.xy) * 0.55 * S_rfdb.w * lo;
}
float mesoExtra(float zr) { return S_rfdb.z * 0.55 * S_rfdb.w * exp(-max(zr, 0.0) / 2.5); }
// the vault: warm, moist inflow streams in under a raised, arched base on the inflow side. 2.0: the side comes from the
// simulated inflow, and the arch rises with the depth of the simulated echo overhang (BWER). d: offset from the
// updraft centre at the base, km; returns the base lift, km
float vaultLift(float2 d)
{
    float r = length(d); float R = S_tower.y;
    float2 inDir = length(S_vault.xy) > 0.1 ? normalize(S_vault.xy) : normalize(S_f.xy * 0.45 - S_f.zw * 0.9);
    float ca = dot(d, inDir) / max(r, 0.01);
    float vaultH = (0.7 + 0.25 * clamp(S_vault.z, 0.0, 4.0)) * (0.5 + 0.5 * S_vault.w);
    return vaultH * S_tower.w * ss(0.15, 0.85, ca) * ss(R * 0.15, R * 0.9, r);
}
// angle round the updraft measured from the inflow vault (the side facing the inflow tail), -pi..pi
float vaultAngle(float2 d) { float2 v = S_tail.xy; return atan2(v.x * d.y - v.y * d.x, dot(v, d)); }

// Near-ground features, drawn at full detail in the march (fine = 1) and coarsely into the volume for shadows
// (fine = 0): the wall cloud, its tail cloud and scud, the horseshoe, the anti-mesocyclone lowering and the inflow tail.
float nearField(float3 p, float fine)
{
    float zb = S_base.x; float clock = S_base.z;
    float v = 0.0;
    if (S_wall.w > 0.0)
    {
        float2 wc = S_wall.xy; float Rw = S_wall.z; float lw = S_wall2.x;
        float2 dw = p.xy - wc; float rw = length(dw);
        if (rw < Rw + 5.5 && p.z < zb + 0.4 && p.z > zb - lw - 0.4)
        {
            // the wall cloud turns counter-clockwise at the simulated low-level rotation rate
            float2 dr = rot2(dw, -S_wall2.y * clock);
            float n = 0.5 * n3(float3(dr * 0.5, p.z * 1.0)) + 0.5 * lerp(0.5, n3(float3(dr * 1.9, p.z * 2.2) + float3(0.3, 0.7, 0.2)), fine);
            float ang = atan2(dr.y, dr.x);
            float fringe = lerp(0.5, n3(float3(ang * 1.3, rw * 0.8, p.z * 0.6) + float3(0.1, 0.9, 0.5)), fine);
            float bowl = 1.0 - pow(min(rw / Rw, 1.0), 2.4);
            float bot = zb - lw * (0.15 + 0.85 * bowl) + (n - 0.5) * 0.12 - (fringe - 0.5) * 0.18;
            float w = ss(Rw + 0.15, Rw - 0.2, rw + (n - 0.5) * 1.0) * ss(bot - 0.03, bot + 0.05, p.z) * ss(zb + 0.3, zb + 0.05, p.z);
            if (S_rfdb.w > 0.0)
            {
                float hb = ss(S_rfdb.z * 0.85, S_rfdb.z * 0.55, length(p.xy - S_rfdb.xy) + (n - 0.5) * 0.7) * ss(Rw * 0.55, Rw * 1.0, rw);
                w *= 1.0 - 0.8 * S_rfdb.w * hb;
            }
            v = max(v, S_wall.w * w);
            // tail cloud: a ragged band from the wall cloud toward the rain on the forward flank
            float2 td = safeNorm(S_ffd.xy - wc, S_f.xy); float s = dot(dw, td); float qq = dot(dw, float2(-td.y, td.x));
            float nt = n3(float3(s * 0.3, qq * 1.0, p.z * 1.5) + float3(0.2, 0.6, 0.1));
            float wt = lerp(1.0, 0.35, saturate((s - Rw) / 4.5));
            float zt = zb - lw * 0.6 + 0.15 * saturate((s - Rw) / 4.5);
            float tl = ss(Rw * 0.4, Rw * 0.8, s) * (1.0 - ss(Rw + 3.0, Rw + 4.8, s + (nt - 0.5) * 1.5))
                * ss(wt, wt * 0.4, abs(qq) + (nt - 0.5) * 0.6) * ss(zt - 0.05, zt + 0.06, p.z) * ss(zb + 0.05, zb - 0.12, p.z);
            v = max(v, S_wall2.z * tl * 0.9);
            // scud: ragged fragments below the base on the inflow side, being drawn into the wall cloud
            float phiS = atan2(dot(dw, S_f.zw), dot(dw, S_f.xy));
            float side = ss(-0.2, 0.4, cos(phiS + 0.8));
            float sn2 = n3(float3(dr * 1.2, p.z * 3.5) + float3(0.4, 0.3, 0.8));
            float sc = side * ss(0.74, 0.8, sn2) * ss(Rw + 2.6, Rw + 0.6, rw) * ss(zb - lw * 0.8 - 0.08, zb - lw * 0.8 + 0.05, p.z) * ss(zb - 0.08, zb - 0.25, p.z);
            v = max(v, S_wall2.w * sc * 0.85);
        }
    }
    if (S_rfdb.w > 0.0)
    {
        // the horseshoe: the edge of the base curling round the clear slot, closed on the east and open to the rear
        float2 db = p.xy - S_rfdb.xy; float rb = length(db); float Rs = S_rfdb.z;
        if (abs(rb - Rs) < 2.2 && p.z < zb + 1.3 && p.z > zb - 1.0)
        {
            float th = atan2(dot(db, S_f.zw), dot(db, S_f.xy));
            float am = ss(-0.55, -0.33, cos(th + 0.96));
            float2 dl = rot2(db, -0.06 * clock);
            float nl = 0.6 * n3(float3(dl * 0.6, p.z * 1.2) + float3(0.2, 0.4, 0.9)) + 0.4 * lerp(0.5, n3(float3(dl * 2.2, p.z * 2.6) + float3(0.8, 0.1, 0.3)), fine);
            float north = ss(0.3, 1.0, cos(th - 0.9)); float south = ss(0.5, 1.0, cos(th + 2.97));
            float D = (0.3 + 0.15 * south + 0.25 * north + 0.4 * S_wall2.x * north) * (0.75 + 0.5 * nl);
            // no deeper than about a third of the base height, and lifted clear over a tornado on the northern arm, so the
            // funnel hangs exposed below the base as in the photos (data/reference/tornadoes) instead of inside the lip
            D = min(D, 0.33 * zb + 0.05);
            if (S_tor.z > 0.0) D *= lerp(0.25, 1.0, ss(S_tor2.y * 2.5 + 0.25, S_tor2.y * 4.0 + 1.0, length(p.xy - S_tor3.xy)));
            float x = rb - Rs + (nl - 0.5) * 0.7;
            float prof = saturate(1.0 - max(-x, 0.0) / 1.1 - max(x, 0.0) / 0.9);
            float bot = zb - D * prof * prof * (3.0 - 2.0 * prof) - (nl - 0.5) * 0.2;
            float wisp = lerp(0.5, n3(float3(dl * 3.0, p.z * 6.0) + float3(0.4, 0.9, 0.2)), fine);
            float lip = am * step(0.01, prof) * ss(bot - 0.12 - wisp * 0.12, bot + 0.22, p.z) * ss(zb + 1.2, zb + 0.6, p.z);
            float mR0 = S_tower.y * 1.05 + mesoExtra(0.0);
            float cover = ss(mR0 + 0.2, mR0 - 1.2, length(p.xy - mesoCentre(0.0)));
            v = max(v, S_rfdb.w * lip * cover);
        }
    }
    if (S_anti.z > 0.0)
    {
        // anti-mesocyclone: a small lowering at the southern curl, turning clockwise
        float2 da = p.xy - S_anti.xy; float ra = length(da);
        float Ra = 0.45 + 0.4 * S_anti.z;
        if (ra < Ra + 1.0 && p.z < zb + 0.3 && p.z > zb - 0.7)
        {
            float2 dra = rot2(da, 0.16 * clock);
            float na = 0.55 * n3(float3(dra * 0.9, p.z * 1.5) + float3(0.6, 0.2, 0.7)) + 0.45 * lerp(0.5, n3(float3(dra * 2.8, p.z * 3.0) + float3(0.1, 0.5, 0.2)), fine);
            float la = (0.1 + 0.25 * S_anti.z) * (1.0 - 0.5 * pow(min(ra / Ra, 1.2), 4.0));
            float bot = zb - la - (na - 0.5) * 0.12;
            float lw2 = ss(Ra + 0.1, Ra - 0.25, ra + (na - 0.5) * 0.6) * ss(bot - 0.03, bot + 0.05, p.z) * ss(zb + 0.2, zb, p.z);
            v = max(v, S_anti.z * lw2 * 0.9);
        }
    }
    if (S_tail.w > 0.0)
    {
        // inflow tail (beaver tail): smooth, laminar bands streaming in along the forward flank's edge. 2.0: the
        // direction and length come from the simulated inflow; the band count (1-3) from its speed and humidity
        float2 t0 = S_up.xy + S_f.xy * 2.5 + S_f.zw * 0.5;
        float2 td = S_tail.xy; float s = dot(p.xy - t0, td);
        float curv = S_tail2.w > 0.0 ? S_tail2.w : 0.035;
        float qq = dot(p.xy - t0, float2(-td.y, td.x)) - curv * (S_tail.z - s) * (S_tail.z - s) * 0.12 + 0.6 * sin(s * 0.35);
        if (s > -2.0 && s < S_tail.z + 2.0 && abs(qq) < 5.0 && p.z > zb - 1.0 && p.z < zb + 3.0)
        {
            float fr = saturate(s / S_tail.z);
            float n = 0.55 * n3(float3(s * 0.05, qq * 0.45, p.z * 0.6)) + 0.45 * lerp(0.5, n3(float3(s * 0.25, qq * 1.6, p.z * 2.5) + float3(0.6, 0.2, 0.4)), fine);
            float nearS = (1.0 - fr) * (1.0 - fr);
            float wt = lerp(2.6, 0.35, pow(fr, 0.7)); float zc = zb + S_tail2.z * 0.5 + 1.1 * nearS; float th = lerp(0.15, 1.0, nearS);
            float inT = ss(-0.6, 0.6, s) * (1.0 - ss(S_tail.z * 0.8, S_tail.z, s + (n - 0.5) * 4.0))
                * ss(wt, wt * 0.45, abs(qq) + (n - 0.5) * 0.9) * ss(th, th * 0.4, abs(p.z - zc) + (n - 0.5) * 0.12);
            // stacked laminae: more than one band when the inflow is strong and moist
            float nb = max(S_tail2.x, 1.0);
            if (nb > 1.5)
            {
                float sp = max(S_tail2.y, 0.3);
                float lam = 0.5 + 0.5 * cos(6.2832 * (p.z - zc) / sp + (n - 0.5) * 1.5);
                inT *= lerp(1.0, 0.35 + 0.65 * lam, saturate((nb - 1.0) * 0.6) * nearS);
            }
            v = max(v, S_tail.w * inT * 0.85);
        }
    }
    return v;
}

#endif
