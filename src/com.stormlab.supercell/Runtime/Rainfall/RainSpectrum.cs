using System;

namespace StormLab.Supercell.Rainfall
{
    /// <summary>Finite exponential drop spectrum. Diameters in mm, concentrations in m^-3.
    /// Independent of Unity so water/flux conservation can be checked offline.</summary>
    public sealed class RainSpectrum
    {
        public const int BinCount = 96;
        public const double MinDiameterMm = .2, MaxDiameterMm = 6;
        public readonly double[] DiameterMm = new double[BinCount];
        public readonly double[] NumberPerM3 = new double[BinCount];
        public readonly double[] SpeedMps = new double[BinCount];
        public double NumberDensity { get; private set; }
        public double WaterKgPerM3 { get; private set; }
        public double RainMmPerHour { get; private set; }
        public double ExtinctionPerM { get; private set; }

        static double Positive(double x) => double.IsNaN(x) || double.IsInfinity(x) ? 0 : Math.Max(0, x);
        public static double TerminalSpeed(double diameterMm, double airDensity = 1.225)
        {
            double d = Math.Min(MaxDiameterMm, Positive(diameterMm));
            double rho = Math.Max(.2, Positive(airDensity));
            // Atlas et al. (1973), with Foote/du Toit density exponent approximation.
            return Math.Max(0, 9.65 - 10.3 * Math.Exp(-.6 * d)) * Math.Pow(1.225 / rho, .4);
        }

        /// <summary>MP slope sets shape; normalize the truncated spectrum to the requested
        /// still-air water flux, rather than allowing truncation/velocity fits to change R.</summary>
        public void SetRainRate(double mmPerHour, double airDensity = 1.225, double slopeMultiplier = 1)
        {
            double r = Positive(mmPerHour);
            double lambda = 4.1 * Math.Pow(Math.Max(.01, r), -.21) * Math.Max(.25, Positive(slopeMultiplier));
            Build(lambda, airDensity);
            double flux = Moment(true, 0);
            Scale(flux > 0 ? r / flux : 0, 0);
        }

        /// <summary>Infer a single-moment exponential slope from rho_air*qr and N0 (m^-4),
        /// then preserve the input liquid mass over the represented 0.2..6 mm range.</summary>
        public void SetMixingRatio(double qrKgPerKg, double airDensity, double interceptPerM4 = 8e6, double upwardWindMps = 0)
        {
            double water = Positive(qrKgPerKg) * Math.Max(.2, Positive(airDensity));
            double lambdaPerM = Math.Pow(Math.PI * 1000 * Math.Max(1, Positive(interceptPerM4)) / Math.Max(1e-16, water), .25);
            Build(lambdaPerM / 1000, airDensity);
            double mass = Moment(false, 0);
            Scale(mass > 0 ? water / mass : 0, upwardWindMps);
        }

        void Build(double lambdaPerMm, double rho)
        {
            double step = (MaxDiameterMm - MinDiameterMm) / BinCount;
            for (int i = 0; i < BinCount; i++)
            {
                double d = MinDiameterMm + (i + .5) * step;
                DiameterMm[i] = d; SpeedMps[i] = TerminalSpeed(d, rho);
                // Unit intercept; Scale determines the actual intercept.
                NumberPerM3[i] = Math.Exp(-lambdaPerMm * d) * step;
            }
        }

        double Moment(bool flux, double upwardWind)
        {
            double sum = 0;
            for (int i = 0; i < BinCount; i++)
            {
                double d = DiameterMm[i] * .001;
                double volume = Math.PI / 6 * d * d * d;
                sum += NumberPerM3[i] * volume * (flux ? Math.Max(0, SpeedMps[i] - upwardWind) * 3.6e6 : 1000);
            }
            return sum;
        }

        void Scale(double factor, double upwardWind)
        {
            NumberDensity = ExtinctionPerM = 0;
            for (int i = 0; i < BinCount; i++)
            {
                NumberPerM3[i] *= factor;
                NumberDensity += NumberPerM3[i];
                double d = DiameterMm[i] * .001;
                ExtinctionPerM += NumberPerM3[i] * 2 * Math.PI * d * d / 4;
            }
            WaterKgPerM3 = Moment(false, 0);
            RainMmPerHour = Moment(true, upwardWind);
        }
    }
}
