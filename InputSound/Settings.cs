using UnityModManagerNet;

namespace InputSound
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Enable release feedback(Please adjust according to the map.)")] public bool IsEnableReleaseHitSound = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
            Main.settings = Settings.Load<Settings>(modEntry);
        }

        public void OnChange()
        {
        }
    }
}
