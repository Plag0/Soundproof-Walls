using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent;

namespace SoundproofWalls
{
    public static class ProjectileManager
    {
        private class ProjectileSound
        {
            public List<Sound> SoundsList;
            public uint BaseSoundRange;
            public float ChanceToPlay;
            public SoundChannel? Channel;
            public double SpawnTime;
            public bool Activated;
            public bool PlayIncomingEffects;

            public ProjectileSound(List<Sound> soundsList, uint soundRange, float chanceToPlay, SoundChannel? channel = null, bool activated = false, bool playIncomingEffects = false)
            {
                SoundsList = soundsList;
                BaseSoundRange = soundRange;
                ChanceToPlay = chanceToPlay;
                Channel = channel;
                Activated = activated; // Once a sound is activated (by coming into activation range), outgoing effects will apply.
                PlayIncomingEffects = playIncomingEffects; // Incoming effects don't require the sound to be "activated", like outgoing effects do.
                SpawnTime = Timing.TotalTime;
            }
        }

        private const uint HeavyProjectileSoundActivationRange = 1000;
        private const uint MediumProjectileSoundActivationRange = 300;
        private const uint LightProjectileSoundActivationRange = 210;
        private const uint OutgoingRangeMultiplier = 8;
        private const uint IncomingRangeMultiplier = 7;
        private const float HeavyProjectileSoundChance = 1.0f;
        private const float MediumProjectileSoundChance = 0.45f;
        private const float LightProjectileSoundChance = 0.3f;
        private const float HighestPitch = 2.0f;
        private const float LowestPitch = 0.6f;

        private static Random _random = new Random();
        private static ConcurrentDictionary<Item, ProjectileSound> _projectileChannels = new ConcurrentDictionary<Item, ProjectileSound>();
        private static List<Sound> _heavyProjectileSounds = new List<Sound>();
        private static List<Sound> _mediumProjectileSounds = new List<Sound>();
        private static List<Sound> _lightProjectileSounds = new List<Sound>();

        public static void Update()
        {
            if (_projectileChannels.IsEmpty) { return; }

            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.ProjectileUpdate);

            foreach (var kvp in _projectileChannels)
            {
                Item projectile = kvp.Key;
                ProjectileSound projectileSound = kvp.Value;
                SoundChannel? channel = projectileSound.Channel;

                bool projectileActive = projectile.body != null && projectile.body.isEnabled;
                if (!projectileActive)
                {
                    channel?.Dispose();
                    channel = null;
                    _projectileChannels.TryRemove(projectile, out _);
                    continue;
                }

                uint range = projectileSound.BaseSoundRange;
                if (projectileSound.Activated)
                {
                    //range *= 4;
                }
                float pitchMult = 1;

                if (projectileSound.Activated || projectileSound.PlayIncomingEffects)
                {
                    Vector2 angle = Listener.WorldPos - projectile.WorldPosition;
                    Vector2 velocity = projectile.body!.LinearVelocity;
                    angle.Normalize();
                    velocity.Normalize();
                    float dot = Vector2.Dot(velocity, angle);
                    const float threshold = 0.5f; // 90-degree threshold
                    if (dot > threshold) // projectile is coming towards listener
                    {
                        float centerRatio = (dot - threshold) / (1.0f - threshold);
                        pitchMult = MathHelper.Lerp(LowestPitch, HighestPitch, centerRatio);
                        if (projectileSound.PlayIncomingEffects)
                        {
                            range *= IncomingRangeMultiplier;
                        }
                    }
                    else if (dot < -threshold) // projectile is going away from listener
                    {
                        float centerRatio = (Math.Abs(dot) - threshold) / (1.0f - threshold);
                        pitchMult = MathHelper.Lerp(HighestPitch, LowestPitch, centerRatio);
                        range *= OutgoingRangeMultiplier; // When a projectile near-misses the player, its range expands so they can hear it fly away.
                    }
                    else
                    {
                        pitchMult = LowestPitch;
                    }
                }

                bool shouldPlay = Timing.TotalTime > projectileSound.SpawnTime + 0.15f && 
                    Vector2.Distance(projectile.WorldPosition, Listener.WorldPos) < range;
                bool channelExists = channel != null && channel.IsPlaying;

                if (!shouldPlay)
                {
                    channel?.Dispose();
                    channel = null;
                }
                else
                {
                    if (channelExists)
                    {
                        float transitionFactor = 4000;
                        float maxStep = (float)(transitionFactor * Timing.Step);
                        if (channel!.Far != range) { channel.Far = Util.SmoothStep(channel.Far, range, maxStep); }
                        channel.Position = new Vector3(projectile.WorldPosition, 0);
                        ChannelInfoManager.EnsureUpdateChannelInfo(channel, itemComp: projectile.components[0]);
                        channel.FrequencyMultiplier *= pitchMult;
                        channel.Gain *= Math.Clamp((float)(Timing.TotalTime - projectileSound.SpawnTime) / 2, 0.0f, 1.0f);
                    }
                    else
                    {
                        PlayProjectileSound(projectile, projectileSound, range);
                    }
                }
            }

            PerformanceProfiler.Instance.StopTimingEvent();
        }

        public static void AddProjectile(Item projectile)
        {
            if (_projectileChannels.TryGetValue(projectile, out var _))
            {
                return;
            }

            if (projectile?.body?.Mass == null)
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Invalid projectile!");
                return;
            }

            var soundsList = _lightProjectileSounds;
            uint soundRange = LightProjectileSoundActivationRange;
            float chanceToPlay = LightProjectileSoundChance;
            bool playIncomingEffects = false;
            if (projectile.body.Mass > 3.0f)
            {
                soundsList = _heavyProjectileSounds;
                soundRange = HeavyProjectileSoundActivationRange;
                chanceToPlay = HeavyProjectileSoundChance;
                playIncomingEffects = true;
            }
            else if (projectile.body.Mass > 0.8f)
            {
                soundsList = _mediumProjectileSounds;
                soundRange = MediumProjectileSoundActivationRange;
                chanceToPlay = MediumProjectileSoundChance;
            }

            _projectileChannels[projectile] = new ProjectileSound(soundsList, soundRange, chanceToPlay, playIncomingEffects: playIncomingEffects);
        }

        private static void PlayProjectileSound(Item projectile, ProjectileSound projectileSound, uint soundRange)
        {
            if (_random.NextDouble() > projectileSound.ChanceToPlay)
            {
                return;
            }

            int randomIndex = _random.Next(projectileSound.SoundsList.Count);
            Sound sound = projectileSound.SoundsList[randomIndex];
            SoundChannel channel = sound.Play(1, soundRange, projectile.WorldPosition, muffle: true);
            if (channel != null && channel.Sound != null)
            {
                channel.Looping = true;
                projectileSound.Channel = channel;
                projectileSound.Activated = true;
            }
        }

        public static void Dispose()
        {
            foreach (var kvp in _projectileChannels)
            {
                StopProjectileSound(kvp.Key);
            }
            _projectileChannels.Clear();

            for (int i = 0; i < _heavyProjectileSounds.Count(); i++)
            {
                _heavyProjectileSounds[i]?.Dispose();
                _heavyProjectileSounds[i] = null;
            }
            _heavyProjectileSounds.Clear();

            for (int i = 0; i < _mediumProjectileSounds.Count(); i++)
            {
                _mediumProjectileSounds[i]?.Dispose();
                _mediumProjectileSounds[i] = null;
            }
            _mediumProjectileSounds.Clear();

            for (int i = 0; i < _lightProjectileSounds.Count(); i++)
            {
                _lightProjectileSounds[i]?.Dispose();
                _lightProjectileSounds[i] = null;
            }
            _lightProjectileSounds.Clear();
        }

        // Overkill method to make sure the sounds stop for cleanup
        private static void StopProjectileSound(Item item)
        {
            if (_projectileChannels.TryGetValue(item, out ProjectileSound? projectileSound) && projectileSound?.Channel != null)
            {
                SoundChannel channel = projectileSound.Channel;
                ChannelInfoManager.RemovePitchedChannel(channel);
                channel.Looping = false;
                channel.Gain = 0;
                channel.Dispose();
                _projectileChannels.Remove(item, out _);
            }
        }

        public static void Setup()
        {
            try
            {
                _heavyProjectileSounds.Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.HeavyProjectileCavitation]));
                
                _mediumProjectileSounds.Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.MediumProjectileCavitation]));

                _lightProjectileSounds.Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.LightProjectileCavitation]));
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Failed to load projectile sounds: {ex.Message}");
            }
        }
    }
}
