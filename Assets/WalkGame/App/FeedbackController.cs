using UnityEngine;
using WalkGame.Core;
using WalkGame.UI;

namespace WalkGame.App
{
    /// <summary>
    /// Small, optional feedback layer. Gameplay remains comprehensible without audio
    /// or haptics; clips are injectable later and the mobile-safe haptic hooks are
    /// controlled by the persisted player settings.
    /// </summary>
    public sealed class FeedbackController : MonoBehaviour
    {
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

        public void Bind(PlayerProfile profile)
        {
            _profile = profile;
            _audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            ApplyAudioSettings();
        }

        public void Play(FeedbackCue cue)
        {
            if (_profile == null || _profile.settings == null)
            {
                return;
            }

            ApplyAudioSettings();
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

        private void ApplyAudioSettings()
        {
            if (_audioSource != null && _profile?.settings != null)
            {
                _audioSource.volume = _profile.settings.masterAudioVolume;
            }

            AudioListener.volume = _profile?.settings == null ? 1f : _profile.settings.masterAudioVolume;
        }

        private AudioClip ClipFor(FeedbackCue cue)
        {
            switch (cue)
            {
                case FeedbackCue.Restoration: return restorationClip;
                case FeedbackCue.Collection: return collectionClip;
                case FeedbackCue.Milestone: return milestoneClip;
                case FeedbackCue.PlacementConfirm: return placementClip;
                case FeedbackCue.PlacementInvalid: return invalidClip;
                case FeedbackCue.ModeSwitch: return modeClip;
                case FeedbackCue.ExpeditionStart:
                case FeedbackCue.ExpeditionFinish: return expeditionClip;
                case FeedbackCue.Button: return buttonClip;
                default: return null;
            }
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
