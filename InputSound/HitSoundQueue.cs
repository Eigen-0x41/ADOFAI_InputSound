using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

namespace InputSound
{
    internal class HitSoundQueue
    {
        public static HitSoundQueue instance = null;

        private object hitSoundBufferLockObj = new object();
        private SortedList<double, AudioSourceInformation> hitSoundBuffer = new SortedList<double, AudioSourceInformation>();
        private const double hitSoundbufferdTime = 1.0;

        private PriorityBuffer[] adaptivePriority = new PriorityBuffer[4] { new PriorityBuffer(), new PriorityBuffer(), new PriorityBuffer(), new PriorityBuffer() };

        private AudioSourceInformation OverrideHitSound = null;

        private int GenAdditionalPriority(string snd, int priority)
        {
            const int MAX_BUFFER_SIZE = 4;
            int matchCount = 0;
            foreach (var value in adaptivePriority)
            {
                if (value.Snd != snd)
                    break;
                if (value.Priority != priority)
                    break;
                matchCount++;
            }

            if (matchCount == MAX_BUFFER_SIZE)
                return 0;

            adaptivePriority[matchCount].Init(snd, priority);
            return MAX_BUFFER_SIZE - matchCount;
        }

        private bool IsDoingAddValue(double time, int priority, int additionalPriority)
        {
            AudioSourceInformation audioSourceInfo = null;
            bool status = false;
            lock (hitSoundBufferLockObj)
                status = hitSoundBuffer.TryGetValue(time, out audioSourceInfo);
            if (status)
            {
                if (priority != audioSourceInfo.AudioSource.priority)
                    return false;

                if (additionalPriority <= audioSourceInfo.AdditionalPriority)
                    return false;
            }
            return true;
        }

        private void HitSoundBufferRemoveHelper(double key)
        {
            lock (hitSoundBufferLockObj)
            {
                hitSoundBuffer[key].Dispose();
                hitSoundBuffer.Remove(key);
            }
        }

        public void Clear()
        {
            var deleteKeys = new List<double>(hitSoundBuffer.Keys);
            foreach (var key in deleteKeys)
                HitSoundBufferRemoveHelper(key);
        }

        private AudioSource CreateHitSound(string snd, AudioMixerGroup group, float volume, int priority)
        {
            var audMngIns = AudioManager.Instance;

            AudioSource audioSource = audMngIns.MakeSource(snd);
            audioSource.pitch = 1f;

            if (!(group is null))
                audioSource.outputAudioMixerGroup = group;
            else
                audioSource.outputAudioMixerGroup = audMngIns.fallbackMixerGroup;

            audioSource.volume = volume;
            audioSource.priority = priority;

            return audioSource;
        }

        private bool isPreviousEnrolledHoldHitSound = false;
        private AudioSource EnrollHitSound(bool isHold, string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            if (isHold && isPreviousEnrolledHoldHitSound)
            {
                isPreviousEnrolledHoldHitSound = false;
                return AudioManager.Play(snd, time, group, volume, priority);
            }

            AudioSource audioSource = null;

            int additionalPriority = GenAdditionalPriority(snd, priority);
            if (IsDoingAddValue(time, priority, additionalPriority))
            {
                audioSource = CreateHitSound(snd, group, volume, priority);
                lock (hitSoundBufferLockObj)
                    hitSoundBuffer[time] = new AudioSourceInformation(audioSource, additionalPriority);
                isPreviousEnrolledHoldHitSound = isHold;
            }
            else
            {
                lock (hitSoundBufferLockObj)
                    audioSource = hitSoundBuffer[time].AudioSource;
            }

            return audioSource;
        }

        private bool isEndedAudioSourceInformation = false;
        internal bool TryGetAudioSourceInfomation(double dspTime, out AudioSourceInformation audioSourceInfomation)
        {
            // オーバーフロー対策。
            double lateTime = float.MinValue;
            double earlyTime = float.MaxValue;
            AudioSourceInformation earlyValue = null;
            AudioSourceInformation lateValue = null;

            lock (hitSoundBufferLockObj)
            {
                if (hitSoundBuffer.Count() <= 0)
                {
                    audioSourceInfomation = null;
                    return false;
                }

                var deleteKeys = new List<double>();
                foreach (var keyValuePair in hitSoundBuffer)
                {
                    lateTime = keyValuePair.Key;
                    lateValue = keyValuePair.Value;

                    if (dspTime < lateTime)
                        break;

                    deleteKeys.Add(earlyTime);

                    earlyTime = lateTime;
                    earlyValue = lateValue;
                }

                for (var i = 1; i < deleteKeys.Count; i++)
                    HitSoundBufferRemoveHelper(deleteKeys[i]);
            }

            audioSourceInfomation = lateValue;
            double averageTime = (earlyTime + lateTime) / 2.0;

            if (earlyValue is null)
                return true;
            if (dspTime < averageTime)
                audioSourceInfomation = earlyValue;

            if (lateTime == earlyTime)
            {
                if (isEndedAudioSourceInformation)
                    return false;
                isEndedAudioSourceInformation = true;
            }
            else
            {
                isEndedAudioSourceInformation = false;
            }
            return true;
        }

        public async void PlayHitSoundAsync(bool isReleased, Task<bool> isExecuteLazy)
        {
            if (isReleased)
                return;

            var scrCondIns = scrConductor.instance;
            if (scrCondIns is null)
                return;

            if (Main.settings.IsOverrideHitSound)
            {
                OverrideHitSound.AudioSource.volume = scrCondIns.hitSoundVolume;
                if (await isExecuteLazy)
                    OverrideHitSound.AudioSource.Play();
                return;
            }

            // もし落ちるようならここにtry-catch文でデバックする。
            if (!TryGetAudioSourceInfomation(scrCondIns.dspTime, out AudioSourceInformation audSrcInfo))
                return;
            if (audSrcInfo.AudioSource is null)
                return;

            if (await isExecuteLazy)
                audSrcInfo.AudioSource.Play();
        }

        public bool UpdateOverrideHitSound(HitSound hitSound)
        {
            if (!(OverrideHitSound is null))
                OverrideHitSound.Dispose();

            OverrideHitSound = new AudioSourceInformation(CreateHitSound("snd" + hitSound, null, 1.0f, 128), 0);
            return true;
        }

        public static AudioSource HoldSoundEnrollHelper(string snd, double time, AudioMixerGroup group, float volume = 1, int priority = 128)
        {
            if (!Main.IsEnabled)
                return AudioManager.Play(snd, time, group, volume, priority);

            if (instance is null)
                return AudioManager.Play(snd, time, group, volume, priority);

            return instance.EnrollHitSound(true, snd, time, group, volume, priority);
        }

        public static AudioSource HitSoundEnrollHelper(string snd, double time, AudioMixerGroup group, float volume = 1, int priority = 128)
        {
            if (!Main.IsEnabled)
                return AudioManager.Play(snd, time, group, volume, priority);

            if (instance is null)
                return AudioManager.Play(snd, time, group, volume, priority);

            return instance.EnrollHitSound(false, snd, time, group, volume, priority);
        }

        private sealed class PriorityBuffer
        {
            public string Snd = string.Empty;
            public int Priority = 0;

            public void Init(string soundName, int priority)
            {
                Snd = soundName;
                Priority = priority;
            }
        }

        internal sealed class AudioSourceInformation : IDisposable
        {
            private bool isDispose = false;
            public AudioSource AudioSource = null;
            public int AdditionalPriority = 0;

            public AudioSourceInformation(AudioSource audioSource, int additionalPriority)
            {
                AudioSource = audioSource;
                AdditionalPriority = additionalPriority;
            }

            public void Dispose()
            {
                if (isDispose)
                    return;
                isDispose = true;
                if (AudioSource is null)
                    return;
                AudioManager.Instance.liveSources.Enqueue(AudioSource, 0);
            }
        }
    }
}
