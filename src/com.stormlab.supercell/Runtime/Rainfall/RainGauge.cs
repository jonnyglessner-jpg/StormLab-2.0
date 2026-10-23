using UnityEngine;

namespace StormLab.Supercell.Rainfall
{
    /// <summary>Stationary point gauge. Accumulates diagnostic liquid rain, independently of rendered drops.</summary>
    public sealed class RainGauge : MonoBehaviour
    {
        public MonoBehaviour weatherSource;
        public LocalRainfall fallbackRainfall;
        public bool checkShelter = true;
        public LayerMask shelterMask = ~0;
        public float probeHeight = 500;
        public double AccumulationMm { get; private set; }
        public double CurrentMmPerHour { get; private set; }
        readonly RainSpectrum spectrum = new RainSpectrum();
        float clock;
        public void Clear() { AccumulationMm = 0; }
        void Update() { Tick(Time.deltaTime); }
        public void Tick(float deltaSeconds)
        {
            float dt = Mathf.Max(0, deltaSeconds); clock += dt;
            var source = weatherSource as IRainWeatherSource;
            if (source == null && fallbackRainfall) source = fallbackRainfall.SourceOverride ?? fallbackRainfall.weatherSource as IRainWeatherSource;
            RainWeather weather = source != null ? source.Sample(transform.position, clock) :
                new RainWeather { rateMmPerHour = fallbackRainfall ? fallbackRainfall.rainMmPerHour : 0,
                    airDensity = fallbackRainfall ? fallbackRainfall.airDensity : 1.225f };
            weather.Fill(spectrum);
            bool roof = checkShelter && Physics.Raycast(transform.position, Vector3.up, probeHeight, shelterMask, QueryTriggerInteraction.Ignore);
            CurrentMmPerHour = roof ? 0 : spectrum.RainMmPerHour;
            AccumulationMm += CurrentMmPerHour * dt / 3600;
        }
    }
}
