using System.Collections.Generic;
using UnityEngine;
using WalkGame.Core;
using WalkGame.UI;

namespace WalkGame.App
{
    /// <summary>
    /// Small, optional feedback layer. Gameplay remains comprehensible without audio
    /// or haptics. Final art/audio does not exist yet, so every cue falls back to a
    /// short procedurally synthesized stand-in (clearly marked, never shipped as
    /// final production audio); assigning a real clip through the inspector replaces
    /// the fallback one-for-one. All playback honors the persisted master/music/
    /// effects volumes and the haptics setting.
    /// </summary>
    public sealed class FeedbackController : MonoBehaviour
    {
        private const float AmbientLoopGain = 0.18f;

        [SerializeField] private AudioClip buttonClip;
        [SerializeField] private AudioClip restorationClip;
        [SerializeField] private AudioClip collectionClip;
        [SerializeField] private AudioClip milestoneClip;
        [SerializeField] private AudioClip placementClip;
        [SerializeField] private AudioClip invalidClip;
        [SerializeField] private AudioClip modeClip;
        [SerializeField] private AudioClip expeditionClip;

        private PlayerProfile _profile;
        private AudioSource _audioSource;
        private AudioSource _ambienceSource;
        private readonly Dictionary<FeedbackCue, AudioClip> _fallbackClips = new Dictionary<FeedbackCue, AudioClip>();

        public void Bind(PlayerProfile profile)
        {
            _profile = profile;
            _audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.volume = 1f;
            EnsureAmbience();
            ApplyAudioSettings();
        }

        public void Play(FeedbackCue cue)
        {
            if (_profile == null || _profile.settings == null)
            {
                return;
            }

            var clip = ClipFor(cue);
            if (clip != null && _profile.settings.effectsVolume > 0f)
            {
                _audioSource.PlayOneShot(clip, _profile.settings.masterAudioVolume * _profile.settings.effectsVolume);
            }

#if UNITY_ANDROID || UNITY_IOS
            if (_profile.settings.hapticsEnabled && cue != FeedbackCue.Button)
            {
                Handheld.Vibrate();
            }
#endif
        }

        public void ToggleHaptics()
        {
            if (_profile?.settings == null) return;
            _profile.settings.hapticsEnabled = !_profile.settings.hapticsEnabled;
        }

        public void AdjustMaster(float delta)
        {
            if (_profile?.settings == null) return;
            Adjust(ref _profile.settings.masterAudioVolume, delta);
            ApplyAudioSettings();
        }

        public void AdjustMusic(float delta)
        {
            if (_profile?.settings == null) return;
            Adjust(ref _profile.settings.musicVolume, delta);
            ApplyAudioSettings();
        }

        public void AdjustEffects(float delta)
        {
            if (_profile?.settings == null) return;
            Adjust(ref _profile.settings.effectsVolume, delta);
        }

        public string GetSettingsSummary()
        {
            if (_profile?.settings == null)
            {
                return "Audio settings unavailable";
            }

            return $"Master {Percent(_profile.settings.masterAudioVolume)}  ·  Music {Percent(_profile.settings.musicVolume)}\n" +
                   $"Effects {Percent(_profile.settings.effectsVolume)}  ·  Haptics {(_profile.settings.hapticsEnabled ? "On" : "Off")}";
        }

        private void EnsureAmbience()
        {
            if (_ambienceSource != null)
            {
                return;
            }

            // Procedural placeholder ambience so the music setting has an honest,
            // audible effect until a real soundtrack exists. One tiny looping source
            // keeps the mobile cost negligible.
            _ambienceSource = gameObject.AddComponent<AudioSource>();
            _ambienceSource.playOnAwake = false;
            _ambienceSource.loop = true;
            _ambienceSource.clip = BuildAmbientLoop();
            if (_ambienceSource.clip != null)
            {
                _ambienceSource.Play();
            }
        }

        private void ApplyAudioSettings()
        {
            if (_ambienceSource != null && _profile?.settings != null)
            {
                _ambienceSource.volume =
                    _profile.settings.masterAudioVolume * _profile.settings.musicVolume * AmbientLoopGain;
            }
        }

        private AudioClip ClipFor(FeedbackCue cue)
        {
            switch (cue)
            {
                case FeedbackCue.Restoration: return restorationClip != null ? restorationClip : Fallback(cue);
                case FeedbackCue.Collection: return collectionClip != null ? collectionClip : Fallback(cue);
                case FeedbackCue.Milestone: return milestoneClip != null ? milestoneClip : Fallback(cue);
                case FeedbackCue.PlacementConfirm: return placementClip != null ? placementClip : Fallback(cue);
                case FeedbackCue.PlacementInvalid: return invalidClip != null ? invalidClip : Fallback(cue);
                case FeedbackCue.ModeSwitch: return modeClip != null ? modeClip : Fallback(cue);
                case FeedbackCue.ExpeditionStart:
                case FeedbackCue.ExpeditionFinish: return expeditionClip != null ? expeditionClip : Fallback(cue);
                case FeedbackCue.Button: return buttonClip != null ? buttonClip : Fallback(cue);
                default: return null;
            }
        }

        private AudioClip Fallback(FeedbackCue cue)
        {
            if (_fallbackClips.TryGetValue(cue, out var cached) && cached != null)
            {
                return cached;
            }

            var clip = BuildFallbackClip(cue);
            _fallbackClips[cue] = clip;
            return clip;
        }

        private static AudioClip BuildFallbackClip(FeedbackCue cue)
        {
            switch (cue)
            {
                case FeedbackCue.Restoration: return Tone("fb.restoration", new[] { 523.25f, 783.99f }, 0.12f, 5f);
                case FeedbackCue.Collection: return Tone("fb.collection", new[] { 659.26f }, 0.09f, 9f);
                case FeedbackCue.Milestone: return Tone("fb.milestone", new[] { 523.25f, 659.26f, 783.99f }, 0.13f, 5f);
                case FeedbackCue.PlacementConfirm: return Tone("fb.place", new[] { 739.99f }, 0.06f, 12f);
                case FeedbackCue.PlacementInvalid: return Tone("fb.invalid", new[] { 155f }, 0.16f, 7f);
                case FeedbackCue.ModeSwitch: return Tone("fb.mode", new[] { 440f, 587.33f }, 0.08f, 8f);
                case FeedbackCue.ExpeditionStart:
                case FeedbackCue.ExpeditionFinish: return Tone("fb.expedition", new[] { 392f, 523.25f, 659.26f }, 0.11f, 6f);
                case FeedbackCue.Button: return Tone("fb.button", new[] { 880f }, 0.05f, 14f);
                default: return null;
            }
        }

        /// <summary>Short decaying sine sequence; deliberately tiny and mono.</summary>
        private static AudioClip Tone(string name, float[] frequencies, float toneSeconds, float decayPerSecond)
        {
            const int sampleRate = 22050;
            int samplesPerTone = Mathf.CeilToInt(toneSeconds * sampleRate);
            int totalSamples = samplesPerTone * frequencies.Length;
            var data = new float[totalSamples];
            int cursor = 0;
            foreach (var frequency in frequencies)
            {
                for (int i = 0; i < samplesPerTone; i++, cursor++)
                {
                    float time = i / (float)sampleRate;
                    data[cursor] = 0.5f * Mathf.Exp(-decayPerSecond * time) *
                                   Mathf.Sin(2f * Mathf.PI * frequency * time);
                }
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Four-second quiet pad loop: layered low sines with a slow swell so
        /// the loop point stays inaudible. Placeholder only - replaced by real music.</summary>
        private static AudioClip BuildAmbientLoop()
        {
            const int sampleRate = 22050;
            const float seconds = 4f;
            int totalSamples = Mathf.CeilToInt(seconds * sampleRate);
            var data = new float[totalSamples];
            for (int i = 0; i < totalSamples; i++)
            {
                float time = i / (float)sampleRate;
                float phase = time / seconds * Mathf.PI * 2f;
                float swell = 0.6f + 0.4f * Mathf.Sin(phase); // matches at loop boundary
                float pad = Mathf.Sin(2f * Mathf.PI * 110f * time) +
                            0.6f * Mathf.Sin(2f * Mathf.PI * 165f * time) +
                            0.35f * Mathf.Sin(2f * Mathf.PI * 220f * time);
                data[i] = 0.28f * swell * pad / 1.95f;
            }

            var clip = AudioClip.Create("fb.ambient-loop", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void Adjust(ref float value, float delta)
        {
            value = Mathf.Clamp01(value + delta);
        }

        private static string Percent(float value)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";
        }
    }
}
