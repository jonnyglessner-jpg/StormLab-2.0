// Shared by the skin's compute passes: the parameter block (SkinParams.cs, 51 float4 slots), noise, and the
// near-ground features. Ported from StormLab 1.0's src/storm3d/shaders.ts (WGSL, MIT). Lengths are km in the volume
// frame (origin at the volume centre, x east, y north, z up above ground).
#ifndef STORM_SKIN_COMMON
#define STORM_SKIN_COMMON

float4 _SP[63];
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
#define S_late   _SP[39]
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

float2 tailSpinePoint(float t)
{
    float u = min(t, 0.999999) * 3.0;
    int k = (int)floor(u); u -= k;
    float2 b = _SP[51 + k].xy, c = _SP[52 + k].xy;
    float2 a = k > 0 ? _SP[50 + k].xy : 2.0 * b - c;
    float2 d = k < 2 ? _SP[53 + k].xy : 2.0 * c - b;
    return 0.5 * ((2.0 * b) + (-a + c) * u + (2.0 * a - 5.0 * b + 4.0 * c - d) * u * u
        + (-a + 3.0 * b - 3.0 * c + d) * u * u * u);
}

// Project onto the persistent tail spine. The same coordinates feed coarse and near
// cloud, so the two representations cannot rotate or detach independently.
void tailCoordinates(float2 p, out float along, out float across, out float span)
{
    if (_SP[51].w < 0.5)
    {
        float2 root = S_up.xy + S_f.xy * 2.5 + S_f.zw * 0.5;
        along = dot(p - root, S_tail.xy);
        across = dot(p - root, float2(-S_tail.y, S_tail.x));
        span = max(S_tail.z, 0.1);
        return;
    }
    float best = 1e20, offset = 0.0;
    along = 0.0; across = 0.0; span = 0.0;
    bool designed = _SP[51].w > 1.5;
    uint count = designed ? 12u : 3u;
    [loop] for (uint j = 0; j < count; j++)
    {
        uint legacyKnot = min(j, 2u);
        float2 a = designed ? tailSpinePoint((float)j / count) : _SP[51 + legacyKnot].xy;
        float2 b = designed ? tailSpinePoint((float)(j + 1) / count) : _SP[52 + legacyKnot].xy;
        float2 d = b - a;
        float len = max(length(d), 0.01); float2 dir = d / len;
        float raw = dot(p - a, dir), u = clamp(raw, 0.0, len);
        float distance2 = dot(p - a - dir * u, p - a - dir * u);
        if (distance2 < best)
        {
            best = distance2;
            along = offset + ((j == 0 && raw < 0.0 || j == count - 1 && raw > len) ? raw : u);
            across = dot(p - a, float2(-dir.y, dir.x));
        }
        offset += len;
    }
    span = offset;
}

// The light grids (storm and near) store the sun's optical depth in r, not its transmittance, so the march can light
// each multiple-scattering octave from it. sunT is 1.0's two-lobe transmittance of that depth, for what is not lit as
// cloud: rain, the light shafts, the ground shadow and the tornado.
float sunT(float tau) { return 0.72 * exp(-0.38 * tau) + 0.28 * exp(-0.07 * tau); }
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
float3 sampledWindAt(float3 p)
{
    float2 gm = _VolC.xy + p.xy * 1000.0;
    float3 uvw = float3(gm.x / _WindP.y, gm.y / _WindP.z, (p.z + 0.5) / _WindP.w);
    float4 a = _WindA.SampleLevel(sampler_LinearClamp, uvw, 0);
    float4 b = _WindB.SampleLevel(sampler_LinearClamp, uvw, 0);
    float3 e = lerp(a.xyz, b.xyz, _WindP.x);
    return -51.0 + e * 255.0 * 0.4;
}
float3 windAt(float3 p) { return _VolC.w < 0.5 ? float3(0.0, 0.0, 0.0) : sampledWindAt(p); }

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

// The designed feeder band condenses along the FFD boundary. Its cloud texture travels
// inward on model time; the boundary itself stays attached to the precipitation gradient.
float designedTailCloud(float3 p, float fine)
{
    float s, q, span;
    tailCoordinates(p.xy, s, q, span);
    float zb = S_base.x;
    if (s < -2.0 || s > span + 1.0 || abs(q) > 3.5 || p.z < zb - 0.3 || p.z > zb + 2.5) return 0.0;
    float fr = saturate(s / max(span, 0.1)), head = (1.0 - fr) * (1.0 - fr);
    float flow = s + S_inflow.z * 0.001 * S_base.z;
    float3 uv = float3(flow * 0.12, q * 0.45, (p.z - zb) * 0.7);
    float n = 0.65 * n3k(uv, 1.0, float3(0.2, 0.6, 0.1))
        + 0.35 * lerp(0.5, n3k(uv, 3.0, float3(0.6, 0.1, 0.4)), fine);
    float billow = n - 0.5;
    float width = lerp(1.6, 0.45, pow(fr, 0.7)) * (0.85 + 0.3 * n);
    float bottom = zb + 0.04 + 0.15 * (1.0 - head) + billow * 0.13;
    float thickness = lerp(0.25, 1.25, head) * (0.85 + 0.3 * n);
    float top = bottom + thickness;
    float band = ss(width + 0.25, width * 0.5, abs(q) + billow * 0.3)
        * ss(bottom - 0.12, bottom + 0.08, p.z) * ss(top + 0.2, top - 0.15, p.z)
        * ss(-2.0, 0.0, s) * (1.0 - ss(span * 0.8, span, s + billow * 1.2));
    return S_tail.w * band * (0.75 + 0.3 * n);
}
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
float mesoExtra(float zr) { return S_rfdb.z * 0.3 * S_rfdb.w * exp(-max(zr, 0.0) / 2.5); }

// Authored precipitation curtains: keep the ground footprint, then bend and narrow
// the upper shaft into the tilted tower/anvil root. These are presentation coordinates,
// not liquid-water transport. Recorded brain rain never uses this deformation.
float designedRainSourceHeight()
{
    float zb = S_base.x;
    float canopy = lerp(zb + 1.2, max(zb + 1.2, S_anvil2.y - 0.55), saturate(S_anvil.w));
    return max(zb + 0.4, min(S_tower.x - 0.35, canopy));
}
float3 designedRainCoordinates(float3 p)
{
    if (S_tower.z <= 0.0) return p;
    float source = designedRainSourceHeight();
    float bend = ss(max(0.15, S_base.x * 0.35), source, p.z);
    float2 root = mesoCentre(source - S_base.x) - S_up.xy;
    root += S_anvil.xy * min(S_anvil.z * 0.12, S_tower.y * 1.5) * saturate(S_anvil.w);
    float width = lerp(1.0, 0.45, bend);
    return float3(S_up.xy + (p.xy - S_up.xy - root * bend) / width, p.z);
}
float designedRainTop(float z)
{
    float source = designedRainSourceHeight();
    return ss(source + 0.35, source - 0.35, z)
        * lerp(1.0, 0.42, ss(S_base.x, source, z));
}
// ---- the updraft's shape, from StormLab 1.0 as it is live on the website (2030265, 2026-10-19), shared by the volume,
// the near field and the march so geometry and surface detail agree
// a restrained lower flare with a tucked underside
float baseFlare(float zr)
{
    float mature = saturate(S_anvil.w); float h = max(zr, 0.0);
    return (0.22 * S_tower.w + 0.06 * mature) * exp(-h / 1.8) - 0.06 * mature * (1.0 - ss(0.0, 0.6, h));
}
// a fuller middle barrel round the whole circulation, with a subtle rear spiral shoulder (S_up.z: how far the
// middle-level widening has gone)
float mesoShoulder(float2 d, float zr)
{
    float H = max(S_tower.x - S_base.x, 0.5); float middle = H * 0.45;
    float wrap = 0.65 * ss(0.0, 0.45, S_rfd.x) * ss(0.0, middle, max(zr, 0.0));
    float2 rear = cos(2.18 + wrap) * S_f.xy + sin(2.18 + wrap) * S_f.zw;
    float side = ss(-0.2, 0.7, dot(d, rear) / max(length(d), 0.01));
    float e = (zr - middle) / max(H * 0.24, 0.5);
    return 0.5 * S_tower.y * S_up.z * (0.85 + 0.15 * side) * exp(-e * e);
}
// a broad curved underside: level near the core, rolling upward toward the outer wall
float underbellyRise(float2 d)
{
    float baseRadius = S_tower.y * (1.0 + baseFlare(0.0)) + mesoExtra(0.0) + mesoShoulder(d, 0.0);
    float rim = saturate((length(d) / max(baseRadius, 0.5) - 0.55) / 0.55);
    return 0.75 * rim * rim;
}
// angle round the updraft measured from the inflow vault (the side facing the inflow tail), -pi..pi
float vaultAngle(float2 d) { float2 v = S_tail.xy; return atan2(v.x * d.y - v.y * d.x, dot(v, d)); }
// The rotating updraft's own base sits a little above the simulated LCL, exposing the lowered rim (wall cloud and
// horseshoe lip, which hang down to and below the LCL) without detaching their upper connection. Only while it rotates.
float baseLift() { return 0.62 * ss(0.0, 0.02, S_tilt.w); }
// Shared water-cloud texture for the underside, exposed slot and low cloud. Cartesian
// coordinates keep it continuous across the back of the circulation (no atan2 seam).
float3 baseCloudFrame(float3 p)
{
    float zr = p.z - S_base.x;
    float2 c = mesoCentre(zr);
    float spin = S_mesov.z != 0.0 ? S_mesov.z : S_tilt.w;
    return float3(rot2(p.xy - c, -spin * S_base.z), zr);
}
float baseCloudTexture(float3 p, float fine)
{
    float3 q = baseCloudFrame(p);
    float broad = n3k(q * float3(1.0, 1.0, 1.6), 0.42, float3(0.21, 0.67, 0.43));
    float small = lerp(0.5, ridge(q * float3(1.0, 1.0, 1.35), 1.25, float3(0.2, 0.6, 0.3)), fine);
    return 0.65 * broad + 0.35 * small;
}
// how much of the horseshoe's arc is drawn at an angle th round the surge (grows round from the east as it matures)
float horseshoeArc(float th) { return ss(lerp(0.92, -0.55, S_rfdb.w), lerp(0.99, -0.33, S_rfdb.w), cos(th + 0.96)); }
// One rear-opening cut for the whole cloud, including the apron, low cloud and the near field: a pocket inside the
// horseshoe and a channel out through its open rear side, twisting with height into the barrel as one corkscrew and
// pinching shut half way up the tower. 2.0: a warm, dry RFD (the simulated slot2.z near 1) opens it further.
float clearSlot(float3 p)
{
    float halfway = (S_tower.x - S_base.x) * 0.45;
    if (S_rfd.x <= 0.0 || S_rfdb.z <= 0.0 || p.z > S_base.x + halfway) return 0.0;
    // nothing to cut beyond the updraft's reach (keeps the march from evaluating it everywhere)
    if (length(p.xy - S_rfdb.xy) > 2.0 * S_rfdb.z + 2.0 * S_tower.y + 4.0) return 0.0;
    float zr = p.z - S_base.x;
    float dry = S_slot2.w > 0.0 ? S_slot2.z : 1.0;
    float growth = ss(0.0, 0.45, S_rfd.x) * (0.75 + 0.25 * dry);
    float depth = lerp(0.5, halfway, growth);
    float middle = ss(depth * 0.46, depth, zr);
    float wrap = 0.65 * growth * ss(0.0, halfway, max(zr, 0.0));
    float2 rear = cos(2.18 + wrap) * S_f.xy + sin(2.18 + wrap) * S_f.zw;
    float2 db = p.xy - S_rfdb.xy - S_tilt.xy * max(zr, 0.0) - rear * (S_tower.y * 0.45 * middle);
    float u = dot(db, rear); float v = abs(dot(db, float2(-rear.y, rear.x)));
    float Rs = S_rfdb.z;
    // Rounded billows continue up the inside wall and ceiling; the cut is not a polished tube.
    float n = (baseCloudTexture(p, 0.0) - 0.5) * lerp(0.28, 0.16, middle);
    float arch = 1.0 - middle;
    float feather = lerp(0.25, 0.85, middle);
    float pocket = ss(Rs * 0.82 * arch + feather, max(Rs * 0.6 * arch - feather * 0.25, 0.03), length(db) + n);
    float width = (Rs * (0.3 + 0.28 * growth) + max(u, 0.0) * (0.08 + 0.25 * growth)) * arch;
    float channel = ss(-0.35, 0.45, u) * ss(width + feather + 0.15, max(width - feather, 0.03), v + n);
    float roof = 1.0 - ss(depth * 0.6, depth, zr);
    // keep only the narrow rising attachment above the wall cloud, not the whole barrel
    float attachment = ss(S_wall.z * 1.2, S_wall.z * 0.95, length(p.xy - S_wall.xy)) * S_wall.w * (1.0 - ss(2.0, 4.0, zr));
    float opening = pocket + channel - pocket * channel;
    return growth * opening * roof * (1.0 - attachment);
}

// Material, as the volume stores it in alpha: water cloud 0.3 + 0.7 x turbulence, ice 0.3 x (1 - ice), so 0 is pure ice
float water(float turb) { return 0.3 + 0.7 * saturate(turb); }
float icy(float ice) { return 0.3 * (1.0 - saturate(ice)); }

// ---- the anvil (live 1.0's shape): an oval canopy of ice drawn out along the storm-relative wind aloft, flat-topped,
// backsheared upwind, thinning downstream into streaky cirrus, a broad curved root over the updraft, and late in the
// storm (S_up.w) a thinner veil filling out in every direction. The volume paints it within its 60 km; the march draws
// the rest analytically (farAnvil), so a long anvil is not cut off at the volume's edge.
struct AnvilS { float av; float mat; float inPlan; float botz; float topz; float fr; float lens; };
AnvilS anvilAt(float3 p)
{
    AnvilS o; o.av = 0.0; o.mat = icy(1.0); o.inPlan = 0.0; o.botz = 0.0; o.topz = 0.0; o.fr = 0.0; o.lens = 0.0;
    float zb = S_base.x; float el = S_base.y; float R = S_tower.y;
    if (S_anvil.w <= 0.0 || p.z < el - 9.0 || p.z > el + 3.0) return o;
    float2 a0 = S_up.xy + S_tilt.xy * (el - zb);
    float2 ad = S_anvil.xy; float2 an = float2(-ad.y, ad.x);
    float s = dot(p.xy - a0, ad); float q = dot(p.xy - a0, an);
    float grow = S_anvil.w; float Lz = max(S_anvil.z, 2.0);
    float fr = saturate(s / Lz);
    float thin = S_up.w;
    float back = S_anvil2.w * grow;
    float nb = max(back + R * 0.5, 0.5);
    float A = 0.5 * (Lz + nb); float sc = 0.5 * (Lz - nb);
    float B = R * (1.2 + 0.6 * grow) + A * (0.34 + 0.16 * thin) * grow;
    float su = (s - sc) / A;
    float Bq = B * (1.0 - 0.18 * su);
    float e = length(float2(su, q / max(Bq, 0.5)));
    float outD = min((e - 1.0) * min(Bq, A), length(p.xy - a0) - R * 2.6 * (0.6 + 0.4 * grow) * (1.0 + 3.0 * thin));
    if (outD >= 3.5) return o;
    float n = fbm(p * 0.5 + float3(3.1, 1.7, 0.0)) - 0.5;
    float streak = 0.6 * n3(float3(s * 0.022, q * 0.2, p.z * 0.25) + float3(0.2, 0.5, 0.8)) + 0.4 * n3(float3(s * 0.05, q * 0.45, p.z * 0.4) + float3(0.7, 0.1, 0.5)) - 0.5;
    float inPlan = ss(1.4, -2.0, outD + n * 1.0 + streak * (0.5 + 1.0 * fr));
    float qe = saturate(e);
    float lens = lerp(sqrt(max(1.0 - qe * qe, 0.0)), pow(max(1.0 - qe * qe, 0.0), 0.2), ss(-0.3, -0.8, su));
    float headD = length(float2(max(-s, 0.0) / max(nb, 0.5), q / max(B, 0.5)));
    float bulge = (0.45 + 0.3 * n) * exp(-max(s, 0.0) / max(R * 2.0, 1.0)) * (1.0 - ss(0.6, 1.0, headD)) * grow;
    float topz = S_anvil2.y + 0.35 * exp(-max(s, 0.0) / 8.0) - 0.3 * fr + n * 0.06 + bulge;
    float upw = ss(0.0, -nb, s);
    float thick = lerp(2.7, 0.3, pow(fr, lerp(0.55, 0.4, thin))) * lerp(1.0, 0.45, upw) * (0.55 + 0.45 * grow) * (0.2 + 0.8 * lens)
        * lerp(1.0, 0.6, thin * ss(0.08, 0.5, fr));
    float dA = length(p.xy - a0);
    // a broad curved root: the underside rises continuously from the column into the canopy
    float skirtA = 3.4 * (1.0 - ss(R * 0.9, R * 2.7, dA)) * grow;
    float botz = topz - thick - skirtA + n * 0.5 * thick + streak * 0.2 * fr;
    float vert = ss(botz - 0.2, botz + 0.35, p.z) * ss(topz + 0.25, topz - 0.55, p.z + streak * 0.25);
    float dens = lerp(1.0, lerp(0.3, 0.2, thin), ss(0.05, 0.9, fr)) * (1.0 - ss(lerp(0.7, 0.85, thin), 1.05, fr + streak * 0.2)) * lerp(0.6, 1.0, lens);
    // cauliflower in the head over and round the updraft's root, blending into streaky ice toward the rim
    float rootWater = (1.0 - ss(R * 1.5, R * 2.8, dA)) * (1.0 - ss(R * 1.5, R * 3.5, max(s, 0.0)));
    float knuckle = max(rootWater, (1.0 - ss(R * 1.0, R * 1.0 + 0.18 * Lz, s)) * (1.0 - ss(0.35, 0.85, qe)));
    o.av = inPlan * vert * dens;
    // ice ages as it spreads: fresh near the head, fully glaciated streaky ice downwind or, for the late round canopy,
    // anywhere a few updraft radii from the root
    float aged = max(ss(0.02, 0.3, fr), ss(R * 2.0, R * 5.0, dA));
    o.mat = lerp(icy(0.35 + 0.65 * aged), water(0.9), knuckle);
    o.inPlan = inPlan; o.botz = botz; o.topz = topz; o.fr = fr; o.lens = lens;
    return o;
}

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
        if (rw < Rw + 5.5 && p.z < zb + 1.8 && p.z > max(0.06, zb - lw - 0.85))
        {
            // the wall cloud turns counter-clockwise at the simulated low-level rotation rate
            float2 dr = rot2(dw, -S_wall2.y * clock);
            float n = 0.5 * n3(float3(dr * 0.5, p.z * 1.0)) + 0.5 * lerp(0.5, n3(float3(dr * 1.9, p.z * 2.2) + float3(0.3, 0.7, 0.2)), fine);
            float ang = atan2(dr.y, dr.x);
            float fringe = lerp(0.5, n3k(float3(dr, p.z * 0.6), 1.3, float3(0.1, 0.9, 0.5)), fine);
            // broad, subtle lobes turn with the texture, so its rotation reads even inside the RFD's still outline
            float lobe = 0.07 * cos(3.0 * ang + 0.8) + 0.035 * sin(5.0 * ang - 0.4);
            float contourR = Rw * (1.0 + lobe);
            float bowl = 1.0 - pow(saturate(rw / max(contourR, 0.001)), 2.4);
            // a smooth, solid lowering; the ragged fringe is only round its outer edge
            float rim = ss(0.6 * contourR, 0.95 * contourR, rw);
            float cloudTex = baseCloudTexture(p, fine);
            float bot = zb - lw * (0.15 + 0.85 * bowl + 0.06 * cos(2.0 * ang + 0.5)) + (cloudTex - 0.5) * 0.16 - (fringe - 0.5) * 0.12 * rim;
            // it joins up into the rotating barrel above instead of ending at the base
            float join = ss(zb - 0.1, zb + 1.0, p.z);
            float w = ss(contourR + 0.15 + join * 0.8, contourR - 0.2 + join * 0.8, rw + (n - 0.5) * 0.35) * ss(bot - 0.03, bot + 0.05, p.z) * ss(zb + 1.8, zb + 0.9, p.z);
            if (S_rfdb.w > 0.0)
            {
                float hb = ss(S_rfdb.z * 0.85, S_rfdb.z * 0.55, length(p.xy - S_rfdb.xy) + (n - 0.5) * 0.7) * ss(Rw * 0.9, Rw * 1.2, rw);
                w *= 1.0 - 0.4 * S_rfdb.w * hb;
            }
            v = max(v, S_wall.w * w * (0.9 + 0.2 * cloudTex));
            // tail cloud: aimed at the inner part of the inflow (beaver) tail
            float2 beaverRoot = S_up.xy + S_f.xy * 2.5 + S_f.zw * 0.5;
            float2 beaverNormal = float2(-S_tail.y, S_tail.x);
            float beaverBend = 0.035 * pow(max(S_tail.z - 2.0, 0.0), 2.0) * 0.12 - 0.6 * sin(0.7);
            float2 tailAim = beaverRoot + S_tail.xy * 2.0 + beaverNormal * beaverBend;
            if (_SP[51].w > 1.5) tailAim = lerp(_SP[51].xy, _SP[52].xy, 0.35);
            float2 td = safeNorm(tailAim - wc, S_f.xy); float s = dot(dw, td); float qq = dot(dw, float2(-td.y, td.x));
            float nt = n3(float3(s * 0.3, qq * 1.0, p.z * 1.5) + float3(0.2, 0.6, 0.1));
            float wt = lerp(1.15, 0.45, saturate((s - Rw) / 4.5));
            float zt = zb - lw * 0.6 - 0.24 + 0.15 * saturate((s - Rw) / 4.5);
            float tl = ss(Rw * 0.4, Rw * 0.8, s) * (1.0 - ss(Rw + 3.0, Rw + 4.8, s + (nt - 0.5) * 1.5))
                * ss(wt, wt * 0.4, abs(qq) + (nt - 0.5) * 0.35) * ss(zt - 0.08, zt + 0.08, p.z) * ss(zb + 0.22, zb - 0.04, p.z);
            v = max(v, S_wall2.z * tl * 0.9);
            // scud: a few ragged fragments hugging the wall cloud's edge, more on the inflow side where they are drawn in;
            // the base away from it stays clean
            float phiS = atan2(dot(dw, S_f.zw), dot(dw, S_f.xy));
            float side = 0.4 + 0.6 * ss(-0.2, 0.4, cos(phiS + 0.8));
            float sn2 = n3(float3(dr * 1.6, p.z * 3.5) + float3(0.4, 0.3, 0.8));
            float ring = ss(Rw - 0.35, Rw - 0.05, rw) * ss(Rw + 0.7, Rw + 0.25, rw);
            float sc = side * ring * ss(0.76, 0.82, sn2) * ss(zb - lw * 0.75 - 0.08, zb - lw * 0.75 + 0.05, p.z) * ss(zb - 0.08, zb - 0.22, p.z);
            v = max(v, S_wall2.w * sc * 0.6);
        }
    }
    if (S_rfdb.w > 0.0)
    {
        // the horseshoe: the edge of the base curling round the clear slot, closed on the east and open to the rear
        float2 db = p.xy - S_rfdb.xy; float rb = length(db); float Rs = S_rfdb.z;
        if (abs(rb - Rs) < 1.4 && p.z < zb + 1.4 && p.z > zb - 0.8)
        {
            float th = atan2(dot(db, S_f.zw), dot(db, S_f.xy));
            float am = horseshoeArc(th);
            float lipSpin = S_mesov.z != 0.0 ? S_mesov.z : S_tilt.w;
            float2 dl = rot2(db, -lipSpin * clock);
            float nl = 0.6 * n3(float3(dl * 0.6, p.z * 1.2) + float3(0.2, 0.4, 0.9)) + 0.4 * lerp(0.5, n3(float3(dl * 2.2, p.z * 2.6) + float3(0.8, 0.1, 0.3)), fine);
            float north = ss(0.3, 1.0, cos(th - 0.9)); float south = ss(0.5, 1.0, cos(th + 2.97));
            float D = S_rfdb.w * (0.16 + 0.08 * south + 0.15 * north + 0.25 * S_wall2.x * north) * (0.85 + 0.3 * nl);
            // the base's edge: a wedge of rising cloud whose underside slants up gently toward the slot and more gently
            // outward into the base, smooth, without torn fragments under it
            float cloudTex = baseCloudTexture(p, fine);
            float x = rb - Rs + (cloudTex - 0.5) * 0.4;
            float join = ss(zb - 0.1, zb + 0.9, p.z);
            float xr = x / (x < 0.0 ? 0.65 + 0.25 * join : 0.75 + 0.6 * join);
            float prof = sqrt(saturate(1.0 - xr * xr));
            float bot = zb - D * prof - (cloudTex - 0.5) * 0.14;
            float wisp = lerp(0.5, n3(float3(dl * 3.0, p.z * 6.0) + float3(0.4, 0.9, 0.2)), fine);
            float lip = am * ss(0.0, 0.35, prof) * ss(bot - 0.04 - wisp * 0.03, bot + 0.15, p.z) * ss(zb + 1.4, zb + 0.95, p.z);
            float mR0 = S_tower.y * (1.0 + baseFlare(0.0)) + mesoExtra(0.0);
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
    if (S_tail.w > 0.0 && _SP[51].w > 1.5)
        v = max(v, designedTailCloud(p, fine));
    else if (S_tail.w > 0.0)
    {
        // inflow tail (beaver tail): smooth, laminar bands streaming in along the forward flank's edge. 2.0: the
        // direction and length come from the simulated inflow; the band count (1-3) from its speed and humidity
        float s, qq, tailLength;
        tailCoordinates(p.xy, s, qq, tailLength);
        if (_SP[51].w < 0.5) qq -= max(S_tail2.w, 0.035) * (tailLength - s) * (tailLength - s) * 0.12;
        qq += 0.6 * sin(s * 0.35);
        if (s > -2.0 && s < tailLength + 2.0 && abs(qq) < 5.0 && p.z > zb - 1.0 && p.z < zb + 3.0)
        {
            float fr = saturate(s / tailLength);
            float n = 0.55 * n3(float3(s * 0.05, qq * 0.45, p.z * 0.6)) + 0.45 * lerp(0.5, n3(float3(s * 0.25, qq * 1.6, p.z * 2.5) + float3(0.6, 0.2, 0.4)), fine);
            float nearS = (1.0 - fr) * (1.0 - fr);
            float wt = lerp(2.8, 0.45, pow(fr, 0.7)); float zc = zb + S_tail2.z * 0.5 + 1.1 * nearS; float th = lerp(0.22, 1.25, nearS);
            float inT = ss(-2.0, 0.75, s) * (1.0 - ss(tailLength * 0.8, tailLength, s + (n - 0.5) * 4.0))
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
    return v > 0.0 ? v * (1.0 - clearSlot(p)) : 0.0;
}

#endif
