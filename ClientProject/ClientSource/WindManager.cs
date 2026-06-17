using Barotrauma.Lights;
using Barotrauma.Sounds;

namespace SoundproofWalls
{
    public static class WindManager
    {
        private static Sound? _windSound;
        private static SoundChannel? _windChannel;
        private static double _windStartTime; // The longer the sound is active the higher the pitch.
        private static float _windMaxDuration = 10; // The time at which the wind sound is at _maxPitch.

        private static float _minSpeed = 5;
        private static float _maxSpeed = 45; // Max falling speed is 64.16373

        private static float _minPitch = 0.9f;
        private static float _maxPitch = 4.0f;

        public static void Update()
        {
            if (!ConfigManager.Config.WindSounds || _windSound == null || LightManager.ViewTarget == null) { return; }

            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.WindUpdate);

            float speed = 0;
            bool isEligible = false;
            if (LightManager.ViewTarget is Character character)
            {
                speed = character.CurrentSpeed;
                isEligible = !character.InWater;
            }
            else if (LightManager.ViewTarget is Item item)
            {
                speed = item.Speed;
                isEligible = !item.InWater && item.PhysicsBodyActive;
            }

            bool isPlaying = _windChannel != null && _windChannel.IsPlaying;
            bool shouldPlay = isEligible && speed >= _minSpeed;

            if (!shouldPlay)
            {
                StopWind();
            }
            else
            {
                if (isPlaying)
                {
                    _windChannel!.Gain = GetWindGain(speed);
                    _windChannel.FrequencyMultiplier = GetWindPitch();
                }
                else
                {
                    float targetGain = GetWindGain(speed);
                    SoundChannel channel = _windSound.Play(position: null, gain: targetGain, muffle: true);
                    if (channel != null && channel.Sound != null)
                    {
                        channel.Gain = targetGain;
                        channel.FrequencyMultiplier = GetWindPitch();
                        channel.Looping = true;
                        _windChannel = channel;
                    }
                    _windStartTime = Timing.TotalTime;
                }
            }

            PerformanceProfiler.Instance.StopTimingEvent();
        }

        public static void StopWind()
        {
            if (_windChannel != null)
            {
                ChannelInfoManager.RemovePitchedChannel(_windChannel);
                _windChannel.Looping = false;
                _windChannel.Gain = 0;
                _windChannel.Dispose();
                _windChannel = null;
            }
        }

        public static void Dispose()
        {
            StopWind();
            _windSound?.Dispose();
        }

        public static void Setup()
        {
            try
            {
                _windSound = GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.Wind]);
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Failed to load wind sound: {ex.Message}");
            }
        }

        private static float GetWindGain(float speed)
        {
            return Math.Min(MathHelper.Lerp(0, 1, (speed - _minSpeed) / _maxSpeed) * ConfigManager.Config.WindSoundsVolumeMuliplier, 1);
        }

        private static float GetWindPitch()
        {
            float windTime = (float)(Timing.TotalTime - _windStartTime);
            return Math.Min(MathHelper.Lerp(_minPitch, _maxPitch, windTime / _windMaxDuration), _maxPitch);
        }
    }
}
