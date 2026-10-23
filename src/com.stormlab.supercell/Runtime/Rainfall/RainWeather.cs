using UnityEngine;

namespace StormLab.Supercell.Rainfall
{
    public struct RainWeather
    {
        public float rateMmPerHour;
        public Vector3 windMps; // Ground relative, Unity x east, y up, z north.
        public float airDensity;
        public float qrKgPerKg;
        public float interceptPerM4;
        public bool useMixingRatio;

        public void Fill(RainSpectrum spectrum)
        {
            float rho = airDensity > 0 && !float.IsInfinity(airDensity) ? airDensity : 1.225f;
            float intercept = interceptPerM4 > 0 && !float.IsInfinity(interceptPerM4) ? interceptPerM4 : 8e6f;
            if (useMixingRatio) spectrum.SetMixingRatio(qrKgPerKg, rho, intercept, windMps.y);
            else spectrum.SetRainRate(rateMmPerHour, rho);
        }
    }

    /// <summary>Sample once per local rain volume per frame. Clock units are seconds.</summary>
    public interface IRainWeatherSource
    {
        RainWeather Sample(Vector3 worldPosition, float timeSeconds);
    }
}
