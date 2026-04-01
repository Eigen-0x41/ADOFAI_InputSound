using UnityModManagerNet;

namespace InputSound
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("選択用のしきい値にヒット音固有の音声オフセット値を考慮する")] public bool IsUseHitSoundOffset = false;
        [Draw("ヒットサウンドの最小値(0.0~1.0)")] public float MinimumVolumeLimit = 0.0f;
        [Draw("常に特定のヒットサウンドを鳴らす")] public bool IsOverrideHitSound = false;
        [Draw("特定のヒットサウンドの音色")] public HitSound OverrideHitSoundType = HitSound.Kick;


        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
            Main.settings = Settings.Load<Settings>(modEntry);
        }

        public void OnChange()
        {
            HitSoundQueue.instance.UpdateOverrideHitSound(OverrideHitSoundType);
        }
    }
}
