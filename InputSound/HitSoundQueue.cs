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
        private SortedList<double, AudioSourceInfomation> hitSoundBuffer = new SortedList<double, AudioSourceInfomation>();
        private SortedSet<double> keyReleaseDelay = new SortedSet<double>();

        static PriorityBuffer[] adaptivePriority = new PriorityBuffer[4] { new PriorityBuffer(), new PriorityBuffer(), new PriorityBuffer(), new PriorityBuffer() };

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

        public void EnrollHitSound(string snd, double time, AudioMixerGroup group, float volume, int priority)
        {
            int additionalPriority = GenAdditionalPriority(snd, priority);
            if (IsDoingAddValue(time, priority, additionalPriority))
            {

                var __instance = AudioManager.Instance;

                AudioSource audioSource = __instance.MakeSource(snd, 0);
                audioSource.pitch = 1f;

                if (group != null)
                    audioSource.outputAudioMixerGroup = group;
                else
                    audioSource.outputAudioMixerGroup = __instance.fallbackMixerGroup;

                audioSource.volume = volume;
                audioSource.priority = priority;
                float num = (audioSource.clip ? audioSource.clip.length : float.PositiveInfinity);

                hitSoundBuffer[time] = new AudioSourceInfomation(audioSource, additionalPriority);

            }

            UpdateLatest();
        }
        public void EnrollReleaseHitSound(double endTime)
        {
            keyReleaseDelay.Add(endTime);
            UpdateLatest();
        }

        private long interLockRunningUpdate = 0;
        private async void UpdateLatest()
        {
            const long NOT_RUNNING_UPDATE_LATEST = 0;
            const long RUNNING_UPDATE_LATEST = 1;

            if (Interlocked.Exchange(ref interLockRunningUpdate, RUNNING_UPDATE_LATEST) == RUNNING_UPDATE_LATEST)
                return;

            double dspTime = scrConductor.instance.dspTime - 1.0;
            await Task.Run(() =>
                {
                    while (hitSoundBuffer.Count > 0)
                    {
                        double targetDelay = hitSoundBuffer.Keys.First();
                        if (dspTime > targetDelay)
                        {
                            Main.DebugPrinting($"TryUpdateLatest: [Operation: delete], {targetDelay}, {dspTime}");
                            hitSoundBuffer.Remove(targetDelay);
                            continue;
                        }
                        break;
                    }

                    while (keyReleaseDelay.Count > 0)
                    {
                        double targetDelay = keyReleaseDelay.First();
                        if (dspTime > targetDelay)
                        {
                            Main.DebugPrinting($"TryUpdateLatest[KeyRelease]: [Operation: delete], {targetDelay}, {dspTime}");
                            keyReleaseDelay.Remove(targetDelay);
                            continue;
                        }
                        break;
                    }

                    Interlocked.Exchange(ref interLockRunningUpdate, NOT_RUNNING_UPDATE_LATEST);
                });
        }

        private bool IsDoingReleaseHitSound(double late, double early)
        {
            double average = (late + early) / 2;
            return keyReleaseDelay.Any((a) =>
            {
                var locAverage = (a + average) / 2;
                return (late < locAverage) && (locAverage < early);
            });
        }

        public bool TryGetHitSound(double dspTime, bool isKeyPressed, out AudioSource audioSource)
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

            if (audioSourceInfo != null)
                if (IsDoingReleaseHitSound(lateHitSoundPair.Key, earlyHitSoundPair.Key) || isKeyPressed)
                    audioSource = audioSourceInfo.AudioSource;

            return audioSource != null;
        }

        private class PriorityBuffer
        {
            public string Snd = string.Empty;
            public int Priority = 0;

            public void Init(string soundName, int priority)
            {
                Snd = soundName;
                Priority = priority;
            }
        }
        private class AudioSourceInfomation
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
