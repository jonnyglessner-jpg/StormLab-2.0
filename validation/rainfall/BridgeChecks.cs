using System;
using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Rainfall;
using UnityEngine;

static class BridgeChecks
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Main()
    {
        var grid = SimGrid.Make(4,4,4,1000,4000,1000,1000,true);
        var tl = new Timeline(grid,4,4,4,1000,Vector2.zero,8);
        var f = new TimelineFrame { valid = true, winNX = 2, winNY = 2, winI0 = 1, winJ0 = 1, q3D = new byte[4*4*2*2] };
        int field = 4*2*2;
        for (int i = field; i < 2*field; i++) f.q3D[i] = Q3D.Encode(.001f);
        float q = Q3D.Decode(Q3D.Encode(.001f));
        Check(Math.Abs(BrainRainSource.SampleQr(f,tl,new Vector2(1500,1500),0)-q) < 1e-9, "Cell-centre qr and surface layer");
        Check(Math.Abs(BrainRainSource.SampleQr(f,tl,new Vector2(2000,2000),500)-q) < 1e-9, "Trilinear constant field");
        Check(Math.Abs(BrainRainSource.SampleQr(f,tl,new Vector2(1000,1500),0)-q*.5f) < 1e-9, "Window edge interpolates to zero");
        Check(BrainRainSource.SampleQr(f,tl,new Vector2(-1000,-1000),0)==0, "Outside window stays dry");
        Check(BrainRainSource.SampleQr(f,tl,new Vector2(1500,1500),5000)==0, "Above recorded layers stays dry");
        f.q3D[field+1] = Q3D.Encode(.002f);
        float qr2 = Q3D.Decode(f.q3D[field+1]);
        Check(Math.Abs(BrainRainSource.SampleQr(f,tl,new Vector2(2000,1500),0)-(q+qr2)*.5f) < 1e-9, "Interpolates decoded mass rather than log bytes");
        f.valid = false;
        Check(BrainRainSource.SampleQr(f,tl,new Vector2(1500,1500),0)==0,"Invalid frame is dry");
        f.valid = true; f.q3D = new byte[3];
        Check(BrainRainSource.SampleQr(f,tl,new Vector2(1500,1500),0)==0,"Malformed frame is dry");
        Check(BrainRainSource.SampleQr(null,tl,new Vector2(1500,1500),0)==0,"Missing frame is dry");
        Check(!BrainRainSource.PlaybackJump(100,103,3), "60x at 20 fps retains rain population");
        Check(!BrainRainSource.PlaybackJump(100,100.016f,.016f), "Normal 1x retains rain population");
        Check(BrainRainSource.PlaybackJump(100,2700,0), "Paused forward seek resets rain");
        Check(BrainRainSource.PlaybackJump(100,50,1), "Reverse seek resets rain");
        Console.WriteLine($"PASS: {checks} bridge sampling checks against the real Unity and StormLab assemblies.");
    }
}
