using System;
using StormLab.Supercell.Rainfall;

static class Program
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Main()
    {
        var s = new RainSpectrum();
        foreach (double rho in new[] { .7, 1.0, 1.225 })
        foreach (double rate in new[] { 0, .01, .1, 1, 10, 50, 150, 300 })
        {
            s.SetRainRate(rate, rho);
            Check(Math.Abs(s.RainMmPerHour-rate) < 1e-9*Math.Max(1,rate), "Rain flux normalization");
            Check(s.NumberDensity >= 0 && s.ExtinctionPerM >= 0, "Positive spectrum");
            double independentFlux = 0, independentMass = 0;
            for (int i = 0; i < RainSpectrum.BinCount; i++)
            {
                double d = s.DiameterMm[i] / 1000;
                double v = Math.PI*d*d*d/6;
                independentFlux += s.NumberPerM3[i]*v*s.SpeedMps[i]*3.6e6;
                independentMass += s.NumberPerM3[i]*v*1000;
            }
            Check(Math.Abs(independentFlux-rate) < 1e-9*Math.Max(1,rate), "Independent flux integration");
            Check(Math.Abs(independentMass-s.WaterKgPerM3) < 1e-12, "Independent mass integration");
        }
        foreach (double rho in new[] { .7, 1.225 })
        foreach (double qr in new[] { 0, 1e-7, 1e-6, 1e-4, .001, .02 })
        {
            s.SetMixingRatio(qr, rho);
            Check(Math.Abs(s.WaterKgPerM3-qr*rho) < 1e-12, "Mixing-ratio mass conservation");
            double still = s.RainMmPerHour;
            s.SetMixingRatio(qr, rho, 8e6, 20);
            Check(s.RainMmPerHour == 0, "Strong updraft suspends rain");
            Check(Math.Abs(s.WaterKgPerM3-qr*rho) < 1e-12, "Updraft does not erase water");
            s.SetMixingRatio(qr, rho, 8e6, -5);
            Check(s.RainMmPerHour >= still, "Downdraft increases downward flux");
        }
        double previous = 0;
        for (double d = .2; d <= 6; d += .1)
        {
            double speed = RainSpectrum.TerminalSpeed(d);
            Check(speed > previous && speed < 10, "Monotonic bounded terminal speed"); previous = speed;
        }
        Check(RainSpectrum.TerminalSpeed(1, .8) > RainSpectrum.TerminalSpeed(1, 1.225), "Thin air accelerates fallout");
        Check(Math.Abs(RainSpectrum.TerminalSpeed(1)-3.9972) < .001, "1 mm terminal-speed benchmark");
        foreach (double bad in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            s.SetRainRate(bad); Check(s.NumberDensity == 0, "Invalid rain input is dry");
        }
        Console.WriteLine($"PASS: {checks} rainfall physics checks (flux, mass, velocity, updrafts, input bounds).");
    }
}
