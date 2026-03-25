using UnityModManagerNet;

namespace InputSound
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("選択用のしきい値にヒット音固有の音声オフセット値を考慮する")] public bool IsUseHitSoundOffset = false;

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
