using System.Linq;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Rendering;
using UnityEngine;

namespace StormLab.Supercell.Lightning
{
    /// <summary>Recreates nonserialized designed/GPU state after Unity enters Play. Preview-only; no simulation brain.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class LightningCloudPreviewSetup : MonoBehaviour
    {
        public StormSkin skin;
        public LightningSystem lightning;
        public float stormSeconds=1800;
        void Start() { Initialize(); }
        public void Initialize()
        {
            if(!skin || !lightning) return;
            if(skin.Designed==null || skin.Volume==null)
            {
                var env=Environment.Make(StormPreset.All.First(p=>p.key=="plains").ToEnv());
                var storm=new DesignedStorm(new FixedEnvironment(env),new Vec2(0,0),1,ClimoRegion.Plains);
                var wind=new KinematicWind(storm);
                skin.BindDesigned(storm,wind); skin.groundOrigin=wind.OriginKm*1000;
                for(float t=Mathf.Max(0,stormSeconds-90);t<=stormSeconds;t+=10) skin.StepTo(t,t);
            }
            skin.playing=false; skin.playbackTime=stormSeconds; skin.Activate();
            var up=skin.Params[Slot.Up]; lightning.transform.position=skin.VolToWorld(new Vector3(up.x,up.y,0));
            lightning.groundY=skin.groundHeight;
        }
    }
}
