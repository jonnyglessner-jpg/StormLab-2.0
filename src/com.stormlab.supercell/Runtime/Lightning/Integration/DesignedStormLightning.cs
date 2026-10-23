using UnityEngine;

namespace StormLab.Supercell.Lightning
{
    /// <summary>Connect the optional lightning module to every game/bench designed-storm spawn.</summary>
    public static class DesignedStormLightning
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            // Remove first so entering Play with domain reload disabled cannot duplicate the hook.
            DesignedStormRunner.Spawned -= Connect;
            DesignedStormRunner.Spawned += Connect;
            // The bench's readout of each storm's lightning (the bench cannot see this assembly).
            StormTestBench.LightningReadout = (storm, t) => DesignedLightning.At(storm, t, ReadoutTuning).ToString();
        }

        static readonly StormLightningTuning ReadoutTuning = new StormLightningTuning();

        public static void Connect(DesignedStormRunner runner)
        {
            if (!runner.enableLightning || runner.Storm == null || !runner.skin) return;
            var lightning = runner.GetComponent<LightningSystem>();
            if (!lightning) lightning = runner.gameObject.AddComponent<LightningSystem>();
            lightning.advanceClock = false;
            lightning.settings.seed = runner.seed;
            lightning.groundY = runner.skin.groundHeight;
            lightning.showInCloudChannels = true;
            if (!lightning.observer && Camera.main) lightning.observer = Camera.main.transform;
            lightning.CellCentreAtTime = t =>
            {
                var p = runner.Storm.UpdraftAt(t);
                return new Vector3((float)(p.x - runner.worldOriginKm.x) * 1000f,
                    runner.skin.groundHeight, (float)(p.y - runner.worldOriginKm.y) * 1000f);
            };

            var bridge = runner.GetComponent<LightningCloudBridge>();
            if (!bridge) bridge = runner.gameObject.AddComponent<LightningCloudBridge>();
            bridge.lightning = lightning; bridge.skin = runner.skin;
            bridge.Connect();

            var driver = runner.GetComponent<LightningStormDriver>();
            if (!driver) driver = runner.gameObject.AddComponent<LightningStormDriver>();
            driver.lightning = lightning; driver.skin = runner.skin;
            driver.source = LightningStormDriver.Source.Designed;
            driver.followSkinClock = true;
            driver.Rebind();
        }
    }
}
