// The skin's parameter block (DESIGN.md 2.3): StormLab 1.0's 21-slot block (design.ts, StormDesign.cs), grown to 47
// slots. Every slot is a float4. Slots 0-20 keep 1.0's meaning so the ported shaders read them unchanged; 21-39 are new
// and carry what the simulation can see that 1.0 had to invent; 40-42 drive the close-range look (near-field volume). Positions are km in the skin's volume frame (origin at
// the volume centre, x east, y north), heights are km above ground, winds m/s, rotation rad/s (counter-clockwise > 0).
// Shader twin: Runtime/Rendering/Shaders/StormSkinCommon.hlsl (the S_* defines).
using System;
using UnityEngine;

namespace StormLab.Supercell.Designer
{
    public enum Slot
    {
        // ---- 1.0's block
        /// <summary>xy forward (storm motion) unit, zw left unit</summary>
        F = 0,
        /// <summary>x cloud base km, y storm top (anvil top) km, z clock s, w storm age min</summary>
        Base = 1,
        /// <summary>x tower top km, y radius km, z amount, w striation</summary>
        Tower = 2,
        /// <summary>xy updraft tilt km/km, z overshooting top (0..1, 1.0 units), w mid-level meso spin rad/s</summary>
        Tilt = 3,
        /// <summary>xy anvil direction, z length km, w growth 0..1</summary>
        Anvil = 4,
        /// <summary>x anvil bottom km, y anvil top km, z mammatus 0..1, w backshear km</summary>
        Anvil2 = 5,
        /// <summary>xy wall cloud centre, z radius km, w amount</summary>
        Wall = 6,
        /// <summary>x lowering km, y spin rad/s, z tail cloud, w scud</summary>
        Wall2 = 7,
        /// <summary>xy inflow tail direction (outward from the updraft), z length km, w amount</summary>
        Tail = 8,
        /// <summary>xy forward-flank core, z size scale, w amount</summary>
        Ffd = 9,
        /// <summary>xy hail core, z radius km, w amount</summary>
        Hail = 10,
        /// <summary>x clear slot, y slot radius km, z rear-flank rain, w HP wrap</summary>
        Rfd = 11,
        /// <summary>xy flanking-line direction, z growth, w tower count</summary>
        Flank = 12,
        /// <summary>Phase 3 (tornado model): xy tornado at the ground, z amount, w type</summary>
        Tor = 13,
        /// <summary>Phase 3: x radius at ground, y radius at cloud base, z condensation reach, w rope-out</summary>
        Tor2 = 14,
        /// <summary>Phase 3: xy tornado top, z debris</summary>
        Tor3 = 15,
        /// <summary>xy updraft centre at cloud base</summary>
        Up = 16,
        /// <summary>renderer: x cells across, y levels, z half-width km, w top km</summary>
        Grid = 17,
        /// <summary>rear-flank downdraft surge: xy centre, z radius km, w clear slot / horseshoe amount</summary>
        Rfdb = 18,
        /// <summary>southern curl of the surge: xy position, z anti-mesocyclone, w anticyclonic tornado (Phase 3)</summary>
        Anti = 19,
        /// <summary>renderer: x first z level of this build slab</summary>
        Slab = 20,

        // ---- new in 2.0
        /// <summary>low-level (1 km) mesocyclone: xy centre, z radius km, w vertical vorticity 1/s (0 when absent)</summary>
        Meso1 = 21,
        /// <summary>mid-level (4 km) mesocyclone: xy centre, z radius km, w vertical vorticity 1/s</summary>
        Meso4 = 22,
        /// <summary>x meso base km, y meso top km, z low-level spin rad/s, w updraft-to-gust-front km (negative = occluded)</summary>
        MesoV = 23,
        /// <summary>overshooting top: xy centre, z height above the anvil top km, w radius km</summary>
        Ot = 24,
        /// <summary>xy storm-relative anvil-level wind m/s, z anvil age min, w ice coverage 0..1</summary>
        Anvil3 = 25,
        /// <summary>mammatus band: xy centre, z half-length km, w pouch depth km</summary>
        Mamm = 26,
        /// <summary>xy storm-relative surface inflow m/s, z inflow speed m/s, w boundary-layer RH 0..1</summary>
        Inflow = 27,
        /// <summary>inflow tail: x band count (1-3), y band spacing km, z height above base km, w curvature</summary>
        Tail2 = 28,
        /// <summary>inflow vault (BWER): xy direction toward the inflow, z depth above cloud base km, w amount</summary>
        Vault = 29,
        /// <summary>RFD clear slot: xy centre, z dryness 0..1 (warm, dry RFD = 1), w reach km</summary>
        Slot2 = 30,
        /// <summary>forward-flank core shape: xy long axis unit, z half-length km, w half-width km (0 = use 1.0's shape)</summary>
        Ffd2 = 31,
        /// <summary>RFD / hook rain core: xy centre, z radius km, w amount</summary>
        RfdCore = 32,
        /// <summary>cold pool: xy centroid, z equivalent radius km, w coldest theta' deficit K (positive)</summary>
        Gust = 33,
        /// <summary>flanking line: xy start, z length km, w convergence 0..1</summary>
        Flank2 = 34,
        /// <summary>xy storm motion over the ground m/s (smoothed), zw grid (frame) motion m/s</summary>
        Motion = 35,
        /// <summary>x LCL km, y EL km, z MLCAPE / 1000, w precipitation mode (-1 ordinary, 0 LP, 1 classic, 2 HP)</summary>
        Env = 36,
        /// <summary>x playback time s, y computed horizon s, z first-echo time s, w stage index</summary>
        Time = 37,
        /// <summary>x features from the simulation (bit mask, Feature), y features from 1.0's fallback, z smoothing s, w frames blended</summary>
        Source = 38,
        /// <summary>reserved for the Phase 3 tornado model</summary>
        Reserved = 39,

        // ---- the close-range look (near-field volume)
        /// <summary>stacked plates on the low updraft barrel: x count (0-8), y spacing km, z barrel height above base km, w amount 0..1</summary>
        Plates = 40,
        /// <summary>precipitation curtain: x opacity scale (thin LP 0.3 .. dense HP 1.7), y fall-streak contrast 0..1, z back-lit forward scatter, w hpness</summary>
        Curtain = 41,
        /// <summary>renderer: near-field box, xy centre km (volume frame), z bottom km, w half-width km (0 = no near field)</summary>
        Near = 42,

        // ---- the tornado's shape (designed storm, Runtime/Design/DesignedStorm.cs; 13-15 keep 1.0's meaning)
        /// <summary>x central vortex radius km, y suction-vortex amount 0..1, z breakdown into a collar 0..1, w mode (0 plain funnel, 1 + envelope amount for wedge / multi-vortex)</summary>
        Tor4 = 43,
        /// <summary>x suction-vortex orbit (fraction of the funnel radius), y suction-vortex radius km, z count, w sinuosity 0..1</summary>
        Tor5 = 44,
        /// <summary>x spin at the funnel's edge rad/s (counter-clockwise > 0), y peak wind m/s, z radius of maximum wind km, w dust-cloud radius / ground radius</summary>
        Tor6 = 45,
        /// <summary>x rain wrap 0..1, y shape (TornadoShape), z EF rating, w fraction of its life gone</summary>
        Tor7 = 46,
    }

    /// <summary>Feature groups, for the Source slot (which ones the sim drove and which fell back to 1.0's script).</summary>
    [Flags]
    public enum Feature
    {
        None = 0, Tower = 1, Overshoot = 2, Anvil = 4, Mammatus = 8, Striation = 16, Wall = 32, Tail = 64, Vault = 128,
        Rfd = 256, Ffd = 512, Hail = 1024, Flank = 2048, Motion = 4096, Anti = 8192, Meso = 16384, ColdPool = 32768, Tornado = 65536,
        /// <summary>the whole block came from the designed storm (DesignedStorm), not the simulation</summary>
        Designed = 131072,
    }

    public sealed class SkinParams
    {
        public const int NSLOT = 47;
        public readonly Vector4[] v = new Vector4[NSLOT];
        public string stage = "", caption = "";
        /// <summary>suggested volume centre (grid metres): the updraft, moved downwind so the anvil fits</summary>
        public Vector2 suggestedCentre;
        public Feature fromSim, fromFallback;

        public Vector4 this[Slot s] { get => v[(int)s]; set => v[(int)s] = value; }
        public void Set(Slot s, float x, float y, float z, float w) => v[(int)s] = new Vector4(x, y, z, w);
        public void Clear() { Array.Clear(v, 0, NSLOT); stage = caption = ""; fromSim = fromFallback = Feature.None; }
        public void CopyTo(SkinParams o) { Array.Copy(v, o.v, NSLOT); o.stage = stage; o.caption = caption; o.suggestedCentre = suggestedCentre; o.fromSim = fromSim; o.fromFallback = fromFallback; }
    }
}
