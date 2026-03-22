using System;
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

        private SortedList<double, AudioSourceInfomation> hitSoundBuffer = new SortedList<double, AudioSourceInfomation>();
        private SortedSet<double> keyReleaseDelay = new SortedSet<double>();

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
            AudioSourceInfomation audioSourceInfo = null;
            if (hitSoundBuffer.TryGetValue(time, out audioSourceInfo))
            {
                if (priority != audioSourceInfo.AudioSource.priority)
                    return false;

                if (additionalPriority <= audioSourceInfo.AdditionalPriority)
                    return false;
            }
            return true;
        }

        const long NOT_RUNNING_RESOURCE_RELEASER = 0;
        const long RUNNING_RESOURCE_RELEASER = 1;
        private long interLockResourceRelease = NOT_RUNNING_RESOURCE_RELEASER;
        private async void ResourceReleserAsync()
        {
            const double bufferTime = 1.0;

            double dspTime = scrConductor.instance.dspTime - bufferTime;
            await Task.Run(() =>
                {
                    if (Interlocked.Exchange(ref interLockResourceRelease, RUNNING_RESOURCE_RELEASER) != NOT_RUNNING_RESOURCE_RELEASER)
                        return;

                    var deleteKeys = new List<double>(hitSoundBuffer.Keys);
                    foreach (var key in deleteKeys)
                    {
                        if (key > dspTime)
                            break;
                        hitSoundBuffer.Remove(key);
                    }

                    deleteKeys = new List<double>(keyReleaseDelay);
                    foreach (var key in deleteKeys)
                    {
                        if (key > dspTime)
                            break;
                        keyReleaseDelay.Remove(key);
                    }

                    Interlocked.Exchange(ref interLockResourceRelease, NOT_RUNNING_RESOURCE_RELEASER);
                });
        }

        public void Clear()
        {
            hitSoundBuffer.Clear();
            keyReleaseDelay.Clear();
        }

        public static AudioSource HitSoundEnroller(string snd, double time, AudioMixerGroup group, float volume = 1, int priority = 128)
        {
            if (!Main.IsEnabled)
                return AudioManager.Play(snd, time, group, volume, priority);

            if (instance is null)
                return AudioManager.Play(snd, time, group, volume, priority);

            return instance.EnrollHitSound(snd, time, group, volume, priority);
        }

        public AudioSource EnrollHitSound(string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            AudioSource audioSource = null;

            int additionalPriority = GenAdditionalPriority(snd, priority);
            if (IsDoingAddValue(time, priority, additionalPriority))
            {

                var __instance = AudioManager.Instance;

                audioSource = __instance.MakeSource(snd);
                audioSource.pitch = 1f;

                if (!(group is null))
                    audioSource.outputAudioMixerGroup = group;
                else
                    audioSource.outputAudioMixerGroup = __instance.fallbackMixerGroup;

                audioSource.volume = volume;
                audioSource.priority = priority;
                float num = (audioSource.clip ? audioSource.clip.length : float.PositiveInfinity);

                hitSoundBuffer[time] = new AudioSourceInfomation(audioSource, additionalPriority);
            }
            else
            {
                audioSource = hitSoundBuffer[time].AudioSource;
            }

            ResourceReleserAsync();
            return audioSource;
        }
        public void EnrollReleaseHitSound(double endTime)
        {
            keyReleaseDelay.Add(endTime);
            ResourceReleserAsync();
        }

        private bool IsDoingReleaseHitSound(double late, double early)
        {
            double average = (late + early) / 2.0;
            return keyReleaseDelay.Any((a) =>
            {
                double locAverage = (a + average) / 2.0;
                return (late < locAverage) && (locAverage < early);
            });
        }

        internal bool TryGetHitSound(double dspTime, bool isKeyPressed, out AudioSource audioSource)
        {
            double currentHitSoundDelay = dspTime;

            var lateHitSoundPair = hitSoundBuffer.LastOrDefault((a) => a.Key < dspTime);
            var earlyHitSoundPair = hitSoundBuffer.FirstOrDefault((a) => dspTime < a.Key);
            if (!hitSoundBuffer.TryGetValue(dspTime, out AudioSourceInfomation audioSourceInfo))
            {
                double average = (lateHitSoundPair.Key + earlyHitSoundPair.Key) / 2.0;
                if (dspTime < average)
                {
                    audioSourceInfo = lateHitSoundPair.Value;
                }
                else
                {
                    audioSourceInfo = earlyHitSoundPair.Value;
                }

            }

            audioSource = null;
            if (audioSourceInfo is null)
                return false;
            if (!isKeyPressed)
                if (!IsDoingReleaseHitSound(lateHitSoundPair.Key, earlyHitSoundPair.Key))
                    return false;

            audioSource = audioSourceInfo.AudioSource;
            return !(audioSource is null);
        }
        public async void PlayHitSoundAsync(bool isKeyPressed, Task<bool> isExecuteLazy)
        {
            try
            {
                var scrCondIns = scrConductor.instance;
                if (scrCondIns is null)
                    return;
                if (TryGetHitSound(scrCondIns.dspTime, isKeyPressed, out AudioSource audSrc) && await isExecuteLazy)
                    audSrc.Play();
            }
            catch (Exception e)
            {
                Main.Logger.LogException(e);
            }
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
        private sealed class AudioSourceInfomation
        {
            public AudioSource AudioSource = null;
            public int AdditionalPriority = 0;

            public AudioSourceInfomation(AudioSource audioSource, int additionalPriority = 0)
            {
                AudioSource = audioSource;
                AdditionalPriority = additionalPriority;
            }
        }
    }
}
