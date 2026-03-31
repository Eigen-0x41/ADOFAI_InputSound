using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private int hitSoundBufferCurrentIndex = 0;
        private const double hitSoundbufferdTime = 1.0;

        private PriorityBuffer[] adaptivePriority = new PriorityBuffer[4] { new PriorityBuffer(), new PriorityBuffer(), new PriorityBuffer(), new PriorityBuffer() };

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
                AudioManager.Instance.liveSources.Enqueue(hitSoundBuffer[key].AudioSource, key);
                hitSoundBuffer.Remove(key);
            }
        }

        public void Clear()
        {
            hitSoundBufferCurrentIndex = 0;
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
            double lateTime = double.MinValue / 2;
            double earlyTime = double.MaxValue / 2;
            AudioSourceInformation earlyValue = null;
            AudioSourceInformation lateValue = null;

            int bufferCount = 0;
            lock (hitSoundBufferLockObj)
                bufferCount = hitSoundBuffer.Count();

            if (bufferCount <= 0)
            {
                audioSourceInfomation = null;
                return false;
            }

            hitSoundBufferCurrentIndex -= (hitSoundBufferCurrentIndex > 1) ? hitSoundBufferCurrentIndex - 2 : 0;

            for (; hitSoundBufferCurrentIndex < bufferCount; hitSoundBufferCurrentIndex++)
            {
                lock (hitSoundBufferLockObj)
                {
                    lateTime = hitSoundBuffer.Keys[hitSoundBufferCurrentIndex];
                    lateValue = hitSoundBuffer.Values[hitSoundBufferCurrentIndex];
                }

                if (dspTime < lateTime)
                    break;

                earlyTime = lateTime;
                earlyValue = lateValue;
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
            // もし落ちるようならここにtry-catch文でデバックする。
            var scrCondIns = scrConductor.instance;
            if (scrCondIns is null)
                return;
            if (!TryGetAudioSourceInfomation(scrCondIns.dspTime, out AudioSourceInformation audSrcInfo))
                return;
            if (isReleased)
                return;
            if (audSrcInfo.AudioSource is null)
                return;

            if (await isExecuteLazy)
                audSrcInfo.AudioSource.Play();
        }

        public static AudioSource HoldSoundEnroller(string snd, double time, AudioMixerGroup group, float volume = 1, int priority = 128)
        {
            if (!Main.IsEnabled)
                return AudioManager.Play(snd, time, group, volume, priority);

            if (instance is null)
                return AudioManager.Play(snd, time, group, volume, priority);

            return instance.EnrollHitSound(true, snd, time, group, volume, priority);
        }

        public static AudioSource HitSoundEnroller(string snd, double time, AudioMixerGroup group, float volume = 1, int priority = 128)
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

        internal sealed class AudioSourceInformation
        {
            public AudioSource AudioSource = null;
            public int AdditionalPriority = 0;

            public AudioSourceInformation(AudioSource audioSource, int additionalPriority)
            {
                AudioSource = audioSource;
                AdditionalPriority = additionalPriority;
            }
        }
    }
}
