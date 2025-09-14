using Barotrauma;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using OpenAL;
using System.Collections.Concurrent;

namespace SoundproofWalls
{
    public class EfxAudioManager : IDisposable
    {
        private const uint INVALID_ID = 0;
        private const uint REVERB_SEND = 0;
        private const uint DISTORTION_SEND = 1;

        private uint reverbSlotId = INVALID_ID;
        private uint reverbEffectId = INVALID_ID;
        
        private uint distortionSlotId = INVALID_ID;
        private uint distortionEffectId = INVALID_ID;

        private enum ReverbType
        {
            None,
            Inside,
            Outside,
            Hydrophone
        }
        private ReverbType currentReverb = ReverbType.None;

        private enum DistortionType
        {
            None,
            Inside,
            Outside,
            Hydrophone
        }
        private DistortionType currentDistortion = DistortionType.None;

        public struct ReverbConfiguration
        {
            public float Density;
            public float Diffusion;
            public float Gain;
            public float GainHf;
            public float DecayTime;
            public float DecayHfRatio;
            public float ReflectionsGain;
            public float ReflectionsDelay;
            public float LateReverbGain;
            public float LateReverbDelay;
            public float AirAbsorptionGainHf;
            public float RoomRolloffFactor;
            public int DecayHfLimit;
        }

        public struct DistortionConfiguration
        {
            public float Edge;
            public float Gain;
            public int LowpassCutoff;
            public int EqCenter;
            public int EqBandwidth;
        }


        // Maps source IDs to their current filter ID
        private ConcurrentDictionary<uint, uint> _sourceFilters = new ConcurrentDictionary<uint, uint>();
        private ConcurrentDictionary<uint, bool> _reverbRoutedSources = new ConcurrentDictionary<uint, bool>();

        private double LastEffectUpdateTime = 0;
        private bool ListenerIsCharacter = true;

        private float trailingAirReverbGain = ConfigManager.Config.DynamicReverbAirTargetGain;
        private float trailingHydrophoneReverbGain = ConfigManager.Config.HydrophoneReverbTargetGain;

        private bool AirReverbGainBounceActive = false;
        private bool AirReverbGainBounceGainSet = false;
        private float AirReverbGainBounceFinishValue = 0;
        
        private static Config Config { get { return ConfigManager.Config; } }

        public bool IsInitialized { get; private set; } = false;


        public EfxAudioManager()
        {
            if (!AlEffects.IsInitialized)
            {
                DebugConsole.LogError("[SoundproofWalls] Error: AlEffects wrapper not initialized.");
                return;
            }

            if (!InitEffects()) { Dispose(); return; }

            _sourceFilters = new ConcurrentDictionary<uint, uint>();

            IsInitialized = true;
            DebugConsole.NewMessage("[SoundproofWalls] DynamicFx initialization complete.");
        }

        private bool InitEffects()
        {
            int numEffects = 2;
            uint[] effectSlots = new uint[numEffects];
            uint[] effects = new uint[numEffects];

            int alError;
            Al.GetError();

            AlEffects.GenAuxiliaryEffectSlots(numEffects, effectSlots);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.LogError($"[SoundproofWalls] Failed to generate effect slots, {Al.GetErrorString(alError)}");
                return false;
            }
            reverbSlotId = effectSlots[0];
            distortionSlotId = effectSlots[1];

            AlEffects.GenEffects(numEffects, effects);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.LogError($"[SoundproofWalls] Failed to generate effects, {Al.GetErrorString(alError)}");
                return false;
            }
            reverbEffectId = effects[0];
            distortionEffectId = effects[1];

            // Set type
            AlEffects.Effecti(reverbEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_REVERB);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.LogError($"[SoundproofWalls] Failed to set effect ID {reverbEffectId} type to reverb for slot ID: {reverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            // Set type
            AlEffects.Effecti(distortionEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_DISTORTION);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.LogError($"[SoundproofWalls] Failed to set effect ID {distortionEffectId} type to distortion for slot ID: {distortionSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            return true;
        }

        private void DisableReverb()
        {
            if (currentReverb == ReverbType.None) return;
            AlEffects.AuxiliaryEffectSloti(reverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            currentReverb = ReverbType.None;
        }

        private void DisableDistortion()
        {
            if (currentDistortion == DistortionType.None) return;
            AlEffects.AuxiliaryEffectSloti(distortionSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            currentDistortion = DistortionType.None;
        }

        public void Update()
        {
            if (!Config.DynamicFx) { return; }

            if (Timing.TotalTime < LastEffectUpdateTime + Config.OpenALEffectsUpdateInterval) { return; }
            LastEffectUpdateTime = Timing.TotalTime;

            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.EffectsManagerUpdate);

            if (Listener.IsUsingHydrophones)
            {
                UpdateHydrophoneEffects();
            }
            else // Not using hydrophones. Normal gameplay.
            {
                UpdateNormalEffects();
            }

            PerformanceProfiler.Instance.StopTimingEvent();
        }

        private void UpdateNormalEffects()
        {
            // --- Distortion ---
            if (Config.LoudSoundDistortionAirEnabled && !Listener.IsSubmerged)
            {

                ApplyDistortionConfiguration(CalculateAirDistortionConfiguration());
                currentDistortion = DistortionType.Inside;
            }
            else if (Config.LoudSoundDistortionWaterEnabled && Listener.IsSubmerged)
            {
                ApplyDistortionConfiguration(CalculateWaterDistortionConfiguration());
                currentDistortion = DistortionType.Outside;
            }
            else
            {
                DisableDistortion();
            }

            // --- Reverb ---
            if (Config.DynamicReverbEnabled)
            {
                if (Listener.FocusedHull != null)
                {
                    // Reset trailing gain when switching reverb type.
                    if (currentReverb == ReverbType.Hydrophone) { trailingAirReverbGain = 0; }
                    ApplyReverbConfiguration(CalculateInsideReverbConfiguration(), flushReverb: Listener.IsCharacter != ListenerIsCharacter);
                    currentReverb = ReverbType.Inside;
                    ListenerIsCharacter = Listener.IsCharacter;
                }
                else
                {
                    ApplyReverbConfiguration(CalculateOutsideReverbConfiguration(), flushReverb: Listener.IsCharacter != ListenerIsCharacter);
                    currentReverb = ReverbType.Outside;
                    ListenerIsCharacter = Listener.IsCharacter;
                }
            }
            else
            {
                DisableReverb();
            }
        }

        private void UpdateHydrophoneEffects()
        {
            // --- Reverb ---
            if (Config.HydrophoneReverbEnabled)
            {
                ApplyReverbConfiguration(CalculateHydrophoneReverbConfiguration(), flushReverb: currentReverb != ReverbType.Hydrophone);
                currentReverb = ReverbType.Hydrophone;
            }
            else
            {
                DisableReverb();
            }

            // --- Distortion ---
            if (Config.HydrophoneDistortionEnabled)
            {
                ApplyDistortionConfiguration(CalculateHydrophoneDistortionConfiguration());
                currentDistortion = DistortionType.Hydrophone;
            }
            else
            {
                DisableDistortion();
            }
        }

        private void ApplyReverbConfiguration(ReverbConfiguration config, bool flushReverb = false)
        {
            uint effectId = reverbEffectId;
            uint slotId = reverbSlotId;

            int alError;
            Al.GetError();

            if (flushReverb)
            {
                AlEffects.AuxiliaryEffectSloti(slotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECTSLOT_NULL);
                if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.AddWarning($"[SoundproofWalls] Failed to detach reverb effect ID {effectId} to slot ID: {slotId} while flushing, {Al.GetErrorString(alError)}"); }
            }

            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_DENSITY, Math.Clamp(config.Density, 0.0f, 1.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_DIFFUSION, Math.Clamp(config.Diffusion, 0.0f, 1.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_GAIN, Math.Clamp(config.Gain, 0.0f, 1.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_GAINHF, Math.Clamp(config.GainHf, 0.0f, 1.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_DECAY_TIME, Math.Clamp(config.DecayTime, 0.1f, 20.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_DECAY_HFRATIO, Math.Clamp(config.DecayHfRatio, 0.1f, 2.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_REFLECTIONS_GAIN, Math.Clamp(config.ReflectionsGain, 0.0f, 3.16f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_REFLECTIONS_DELAY, Math.Clamp(config.ReflectionsDelay, 0.0f, 0.3f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_LATE_REVERB_GAIN, Math.Clamp(config.LateReverbGain, 0.0f, 10.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_LATE_REVERB_DELAY, Math.Clamp(config.LateReverbDelay, 0.0f, 0.1f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_AIR_ABSORPTION_GAINHF, Math.Clamp(config.AirAbsorptionGainHf, 0.892f, 1.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_REVERB_ROOM_ROLLOFF_FACTOR, Math.Clamp(config.RoomRolloffFactor, 0.0f, 10.0f));
            AlEffects.Effecti(effectId, AlEffects.AL_REVERB_DECAY_HFLIMIT, Math.Clamp(config.DecayHfLimit, Al.False, Al.True));
            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.AddWarning($"[SoundproofWalls] Error applying reverb params for effect ID: {effectId}, {Al.GetErrorString(alError)}"); }

            AlEffects.AuxiliaryEffectSloti(slotId, AlEffects.AL_EFFECTSLOT_EFFECT, effectId);
            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.AddWarning($"[SoundproofWalls] Failed to attach reverb effect ID {effectId} to slot ID: {slotId}, {Al.GetErrorString(alError)}"); }
        }

        private void ApplyDistortionConfiguration(DistortionConfiguration config)
        {
            uint effectId = distortionEffectId;
            uint slotId = distortionSlotId;

            int alError;
            Al.GetError();

            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EDGE, Math.Clamp(config.Edge, 0.0f, 1.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_GAIN, Math.Clamp(config.Gain, 0.01f, 1.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_LOWPASS_CUTOFF, Math.Clamp(config.LowpassCutoff, 80.0f, 24_000.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EQCENTER, Math.Clamp(config.EqCenter, 80.0f, 24_000.0f));
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EQBANDWIDTH, Math.Clamp(config.EqBandwidth, 80.0f, 24_000.0f));
            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.AddWarning($"[SoundproofWalls] Error applying distortion params for effect ID: {effectId}, {Al.GetErrorString(alError)}"); }
            
            AlEffects.AuxiliaryEffectSloti(slotId, AlEffects.AL_EFFECTSLOT_EFFECT, effectId);
            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.AddWarning($"[SoundproofWalls] Failed to attach distortion effect ID {effectId} to slot ID: {slotId}, {Al.GetErrorString(alError)}"); }
        }

        private ReverbConfiguration CalculateInsideReverbConfiguration()
        {
            // Calculate the strength of the reverb effect based on aggregated looping sound amplitude, gain, and player submersion.
            SoundChannel[] playingChannels = GameMain.SoundManager.playingChannels[0]; // Index 0 is sounds while 1 is voice (cite SourcePoolIndex enum) yeah I guess I'm citing stuff in code comments now...

            // For reference: roomSizeFactor == 2 inside the Orca main floor.

            float roomSizeFactor = (Listener.ConnectedArea * ConfigManager.Config.DynamicReverbAreaSizeMultiplier) / 200_000; // Arbitrary magic number that seems to work well.
            float totalAudioAmplitude = 0;
            float loudSoundGain = 0;
            for (int i = 0; i < playingChannels.Length; i++)
            {
                if (playingChannels[i] != null && playingChannels[i].IsPlaying && 
                    ChannelInfoManager.TryGetChannelInfo(playingChannels[i], out ChannelInfo? info) && info != null && !info.Ignored)
                {
                    // Reduce reverb gain when near loud looping sounds.
                    bool isLooping = playingChannels[i].Looping;
                    if (isLooping && !AirReverbGainBounceActive)
                    {
                        float gain = playingChannels[i].Gain;
                        float amplitude = playingChannels[i].CurrentAmplitude;
                        float muffleMult = 1 - info.MuffleStrength;

                        //LuaCsLogger.Log($"{Path.GetFileName(playingChannels[i].Sound.Filename)} gain: {gain} * amplitude {amplitude} * muffleMult {muffleMult} = totalAudioAmplitude {totalAudioAmplitude}");
                        totalAudioAmplitude += amplitude * gain * muffleMult;
                    }
                    // Linearly increase reverb gain when loud non-looping sounds play.
                    else if (!isLooping && info.IsLoud)
                    {
                        loudSoundGain += 0.028f; // Arbitrary magic number
                    }

                    // Dramatically hike the reverb gain if a loud sound is triggered in a small area.
                    if (ConfigManager.Config.DynamicReverbBloom && !isLooping && roomSizeFactor <= 1 && info.IsLoud && !info.ReverbBounceTriggered && 
                        info.PlaybackPosition / (info.SoundInfo.Sound.DurationSeconds * info.SoundInfo.Sound.SampleRate) < 0.05f)
                    {
                        loudSoundGain = (1 - roomSizeFactor) * 2;
                        AirReverbGainBounceActive = true;
                        AirReverbGainBounceGainSet = false;
                        AirReverbGainBounceFinishValue = trailingAirReverbGain;
                    }
                }
            }
            float loudRoomPenalty = 1f - MathUtils.InverseLerp(0.0f, 1.4f, totalAudioAmplitude);
            float submersionBonus = Listener.IsSubmerged ? 1.4f : 1.0f;
            float smallRoomPenalty = Math.Min(Listener.ConnectedArea * ConfigManager.Config.DynamicReverbAreaSizeMultiplier / 180_000, 1.0f);
            float targetReverbGain = loudSoundGain + (loudRoomPenalty * smallRoomPenalty * submersionBonus * ConfigManager.Config.DynamicReverbAirTargetGain);
            targetReverbGain = Math.Clamp(targetReverbGain, 0.0f, 1.0f);

            float transitionFactor = ConfigManager.Config.AirReverbGainTransitionFactor;
            if (transitionFactor > 0 && AirReverbGainBounceGainSet)
            {
                if (AirReverbGainBounceActive) { transitionFactor *= 0.5f; }
                float maxStep = (float)(transitionFactor * ConfigManager.Config.OpenALEffectsUpdateInterval);
                trailingAirReverbGain = Util.SmoothStep(trailingAirReverbGain, targetReverbGain, maxStep);

                if (trailingAirReverbGain <= AirReverbGainBounceFinishValue)
                {
                    AirReverbGainBounceActive = false;
                }
            }
            else
            {
                trailingAirReverbGain = targetReverbGain;
                if (AirReverbGainBounceActive) { AirReverbGainBounceGainSet = true; }
            }

            float durationMult = ConfigManager.Config.DynamicReverbAirDurationMultiplier;
            float decayTime = (0.8f + (roomSizeFactor * 1.9f)) * durationMult * (AirReverbGainBounceActive ? trailingAirReverbGain * 2.6f : 1);
            float reflectionsDelay = (0.025f + (roomSizeFactor * 0.010f)) * durationMult;
            float lateReverbDelay = (0.040f + (roomSizeFactor * 0.015f)) * durationMult;
             
            float reflectionsBaseGain = 5.1f - (roomSizeFactor * 0.95f);
            float reflectionsMult = loudRoomPenalty * submersionBonus * 0.3f;
            float reflectionsTargetGain = reflectionsBaseGain * reflectionsMult;


            float diffusion = 0.55f + (roomSizeFactor * 0.15f);
            diffusion = Math.Clamp(diffusion, 0.0f, 1.0f);

            float decayHfRatio = 0.5f - (roomSizeFactor * 0.15f);
            decayHfRatio = Math.Clamp(decayHfRatio, 0.1f, 2.0f);


            float lateReverbGain = trailingAirReverbGain * 1.8f;

            /*
            LuaCsLogger.Log($"\n\n\nConnectedArea: {Listener.ConnectedArea}\nareaMult: {smallRoomPenalty}\nroomSizeFactor: " +
                $"{roomSizeFactor}\ndiffusion: {diffusion}\nGain: {trailingAirReverbGain}" +
                $"\ndecayTime: {decayTime}\nDecayHfRatio: {decayHfRatio}\nreflectionsGain: {reflectionsTargetGain}");
            */

            return new ReverbConfiguration
            {
                Density = 1.0f,
                Diffusion = diffusion,
                Gain = trailingAirReverbGain,
                GainHf = ConfigManager.Config.DynamicReverbAirGainHf,
                DecayTime = decayTime,
                DecayHfRatio = decayHfRatio,
                ReflectionsGain = reflectionsTargetGain,
                ReflectionsDelay = reflectionsDelay,
                LateReverbGain = lateReverbGain,
                LateReverbDelay = lateReverbDelay,
                AirAbsorptionGainHf = 0.994f, // Default value
                RoomRolloffFactor = 1.1f, // Make sure the reverb effect dies out before we reach the border of the sound's range.
                DecayHfLimit = Al.True // Default value
            };
        }

        private ReverbConfiguration CalculateOutsideReverbConfiguration()
        {
            float targetReverbGain = ConfigManager.Config.DynamicReverbWaterTargetGain;

            // Must be strong enough to support the very long decay time.
            float lateReverbGain = targetReverbGain * 1.5f;

            float durationMult = ConfigManager.Config.DynamicReverbWaterDurationMultiplier;
            float decayTime = 18.0f * durationMult;
            float reflectionsDelay = 0.3f * durationMult;
            float lateReverbDelay = 0.1f * durationMult;

            return new ReverbConfiguration()
            {
                Density = 1.0f,
                Diffusion = Config.DynamicReverbWaterDiffusion,
                Gain = targetReverbGain,
                GainHf = Config.DynamicReverbWaterGainHf,
                DecayTime = decayTime,
                DecayHfRatio = 0.1f,
                ReflectionsGain = 0.42f,
                ReflectionsDelay = reflectionsDelay,
                LateReverbGain = lateReverbGain,
                LateReverbDelay = lateReverbDelay,
                AirAbsorptionGainHf = 0.994f, // Default value
                RoomRolloffFactor = 0.0f, // Default value
                DecayHfLimit = Al.True // Default value
            };
        }

        private ReverbConfiguration CalculateHydrophoneReverbConfiguration()
        {
            Vector2 submarinePosition = Listener.CurrentHull?.Submarine?.WorldPosition ?? Vector2.Zero;
            float gainMult = 1;
            float maxRange = Math.Abs(ConfigManager.Config.HydrophoneSoundRange) + 1; // Avoid division by zero.
            if (ChannelInfoManager.HydrophonedChannels.Count > 0)
            {
                Vector2 sumOfPositions = Vector2.Zero;
                foreach (var channelInfo in ChannelInfoManager.HydrophonedChannels)
                {
                    sumOfPositions += Util.GetSoundChannelWorldPos(channelInfo.Channel); // Using channelInfo.WorldPos can be outdated.
                }
                Vector2 averagePosition = sumOfPositions / ChannelInfoManager.HydrophonedChannels.Count;
                
                // Increase reverb strength for distant sounds
                float distance = Vector2.Distance(averagePosition, submarinePosition);
                gainMult = Math.Clamp(distance / maxRange, 0f, 1f);
            }

            bool muteReverb = !Listener.IsUsingHydrophones || submarinePosition == Vector2.Zero;

            float targetReverbGain = muteReverb ? 0 : ConfigManager.Config.HydrophoneReverbTargetGain * gainMult;
            targetReverbGain = Math.Clamp(targetReverbGain, 0.0f, 1.0f);

            float transitionFactor = ConfigManager.Config.HydrophoneReverbGainTransitionFactor;
            if (transitionFactor > 0 && !muteReverb)
            {
                float maxStep = (float)(transitionFactor * ConfigManager.Config.OpenALEffectsUpdateInterval);
                trailingHydrophoneReverbGain = Util.SmoothStep(trailingHydrophoneReverbGain, targetReverbGain, maxStep);
            }
            else
            {
                trailingHydrophoneReverbGain = targetReverbGain;
            }

            trailingHydrophoneReverbGain = Math.Clamp(trailingHydrophoneReverbGain, 0, 1);

            //LuaCsLogger.Log($"HYDROPHONE REVERB: averagePosition {averagePosition} sub position {submarinePosition} distance {Vector2.Distance(averagePosition, submarinePosition)} gainMult {gainMult} targetReverbGain {targetReverbGain} trailingHydrophoneReverbGain {trailingHydrophoneReverbGain} muteReverb {muteReverb} channelCount: {ChannelInfoManager.HydrophonedChannels.Count}");

            float lateReverbGain = trailingHydrophoneReverbGain * 2f; // Boost relative to main gain

            return new ReverbConfiguration()
            {
                Density = 1.0f,
                Diffusion = 0.45f,
                Gain = trailingHydrophoneReverbGain,
                GainHf = 1.0f,
                DecayTime = 12.0f,
                DecayHfRatio = 0.1f,
                ReflectionsGain = 0.2f,
                ReflectionsDelay = 0.0f,
                LateReverbGain = lateReverbGain,
                LateReverbDelay = 0.8f,
                AirAbsorptionGainHf = 1.0f,
                RoomRolloffFactor = 0.0f, // Default value
                DecayHfLimit = Al.False
            };
        }

        private DistortionConfiguration CalculateAirDistortionConfiguration()
        {
            float edge = Math.Clamp(ConfigManager.Config.LoudSoundDistortionAirTargetEdge, 0.0f, 1.0f);
            float gain = Math.Clamp(ConfigManager.Config.LoudSoundDistortionAirTargetGain, 0.01f, 1f);
            int lowpass = Math.Clamp(ConfigManager.Config.LoudSoundDistortionAirLowpassFrequency, 80, 24000);
            int eqCenter = Math.Clamp(ConfigManager.Config.LoudSoundDistortionAirTargetFrequency, 80, 24000);
            int eqBandwidth = 1500;

            return new DistortionConfiguration()
            {
                Edge = edge,
                Gain = gain,
                LowpassCutoff = lowpass,
                EqCenter = eqCenter,
                EqBandwidth = eqBandwidth
            };
        }

        private DistortionConfiguration CalculateWaterDistortionConfiguration()
        {
            float edge = Math.Clamp(ConfigManager.Config.LoudSoundDistortionWaterTargetEdge, 0.0f, 1.0f);
            float gain = Math.Clamp(ConfigManager.Config.LoudSoundDistortionWaterTargetGain, 0.01f, 1f);
            int lowpass = Math.Clamp(ConfigManager.Config.LoudSoundDistortionWaterLowpassFrequency, 80, 24000);
            int eqCenter = Math.Clamp(ConfigManager.Config.LoudSoundDistortionWaterTargetFrequency, 80, 24000);
            int eqBandwidth = 1500;

            return new DistortionConfiguration()
            {
                Edge = edge,
                Gain = gain,
                LowpassCutoff = lowpass,
                EqCenter = eqCenter,
                EqBandwidth = eqBandwidth
            };
        }

        private DistortionConfiguration CalculateHydrophoneDistortionConfiguration()
        {
            // Each sound playing through hydrophones linearly increases the gain/edge mult.
            SoundChannel[] playingChannels = GameMain.SoundManager.playingChannels[0];
            float multiSoundMult = 0;
            float soundStrength = 0.02f;
            for (int i = 0; i < playingChannels.Length; i++)
            {
                if (playingChannels[i] != null && playingChannels[i].IsPlaying && ChannelInfoManager.TryGetChannelInfo(playingChannels[i], out ChannelInfo? info) && info != null && !info.Ignored && info.Hydrophoned)
                {
                    if (info.IsLoud) { multiSoundMult += soundStrength * 2; }
                    else { multiSoundMult += soundStrength; }
                }
            }
            float targetGain = Math.Clamp(ConfigManager.Config.HydrophoneDistortionTargetGain * HydrophoneManager.HydrophoneEfficiency * (1f + multiSoundMult), 0.01f, 1f);
            float edge = Math.Clamp(ConfigManager.Config.HydrophoneDistortionTargetEdge * (1f + (multiSoundMult / 2f)), 0.0f, 1.0f);
            int lowpassCutoff = 24000;
            int eqCenter = 3600;
            int eqBandwidth = 3600;

            if (!Listener.IsUsingHydrophones) // Instantly cuts off the distortion when exiting the hydrophones.
            {
                targetGain = 0.01f;
                edge = 0;
            }

            return new DistortionConfiguration()
            {
                Edge = edge,
                Gain = targetGain,
                LowpassCutoff = lowpassCutoff,
                EqCenter = eqCenter,
                EqBandwidth = eqBandwidth
            };
        }

        public void RegisterSource(uint sourceId)
        {
            if (!IsInitialized || sourceId <= 0 || _sourceFilters.ContainsKey(sourceId)) return;

            int alError;
            Al.GetError();

            uint[] filters = new uint[1];

            AlEffects.GenFilters(1, filters);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.AddWarning($"[SoundproofWalls] Failed to generate filter for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                return;
            }

            uint filterId = filters[0];

            // Set type to Lowpass
            AlEffects.Filteri(filterId, AlEffects.AL_FILTER_TYPE, AlEffects.AL_FILTER_LOWPASS);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.AddWarning($"[SoundproofWalls] Failed to set filter {filterId} type to lowpass for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                // Clean up failed filter
                AlEffects.DeleteFilters(1, filters);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to clean up failed filter ID: {filterId}, {Al.GetErrorString(alError)}");
                return;
            }

            // Set initial neutral parameters
            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAIN, 1.0f);
            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAINHF, 1.0f);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.AddWarning($"[SoundproofWalls] Failed to set initial values for filter ID: {filterId}, {Al.GetErrorString(alError)}");
                // Clean up failed filter
                AlEffects.DeleteFilters(1, filters);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to clean up failed filter ID: {filterId}, {Al.GetErrorString(alError)}");
                return;
            }

            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.AddWarning($"[SoundproofWalls] Failed to attach initial filter {filterId} to source ID: {sourceId}, {Al.GetErrorString(alError)}");
                // Clean up failed filter
                AlEffects.DeleteFilters(1, filters);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to clean up failed filter ID: {filterId}, {Al.GetErrorString(alError)}");
                return;
            }

            _sourceFilters[sourceId] = filterId;
        }

        public void UnregisterSource(uint sourceId)
        {
            if (!IsInitialized || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) return;

            int alError;
            Al.GetError();

            if (sourceId <= 0)
            {
                if (filterId != INVALID_ID)
                {
                    AlEffects.DeleteFilters(1, new[] { filterId });
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to delete filter ID: {filterId}, {Al.GetErrorString(alError)}");
                }
                _sourceFilters.TryRemove(sourceId, out uint _);
                return;
            }

            _reverbRoutedSources.TryRemove(sourceId, out bool _);

            // Disconnect auxiliary sends 0 and 1
            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, AlEffects.AL_EFFECTSLOT_NULL, 0, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to disconnect send 0 for source ID: {sourceId}, {Al.GetErrorString(alError)}");
            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, AlEffects.AL_EFFECTSLOT_NULL, 1, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to disconnect send 1 for source ID: {sourceId}, {Al.GetErrorString(alError)}");

            // Detach direct filter
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to detach direct filter from source ID: {sourceId}, {Al.GetErrorString(alError)}");

            // Delete the filter
            if (filterId != INVALID_ID)
            {
                AlEffects.DeleteFilters(1, new[] { filterId });
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to delete filter ID: {filterId}, {Al.GetErrorString(alError)}");
            }

            _sourceFilters.TryRemove(sourceId, out uint _);
        }

        public void UpdateSource(ChannelInfo channelInfo, float gainHf, float gainLf = 1)
        {

            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.EffectsManagerUpdate); 

            uint sourceId = channelInfo.Channel.Sound.Owner.GetSourceFromIndex(channelInfo.Channel.Sound.SourcePoolIndex, channelInfo.Channel.ALSourceIndex);

            if (!IsInitialized || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) { return; }

            if (sourceId <= 0)
            {
                UnregisterSource(sourceId);
                return;
            }

            UpdateSourceFilter(sourceId, filterId, gainHf, gainLf, channelInfo);
            RouteSourceToEffectSlot(REVERB_SEND, sourceId, DetermineReverbEffectSlot(channelInfo), filterId);
            RouteSourceToEffectSlot(DISTORTION_SEND, sourceId, DetermineDistortionEffectSlot(channelInfo), filterId);

            PerformanceProfiler.Instance.StopTimingEvent();
        }

        private void UpdateSourceFilter(uint sourceId, uint filterId, float gainHf, float gainLf, ChannelInfo channelInfo)
        {
            int alError;
            Al.GetError();
           
            bool useBandpass = channelInfo.Hydrophoned && ConfigManager.Config.HydrophoneBandpassFilterEnabled;
            if (useBandpass)
            {
                AlEffects.Filteri(filterId, AlEffects.AL_FILTER_TYPE, AlEffects.AL_FILTER_BANDPASS);
            }
            else
            {
                AlEffects.Filteri(filterId, AlEffects.AL_FILTER_TYPE, AlEffects.AL_FILTER_LOWPASS);
            }
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.AddWarning($"[SoundproofWalls] Failed to set filter ID {filterId}'s type for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                return;
            }

            if (useBandpass)
            {
                gainHf = ConfigManager.Config.HydrophoneBandpassFilterHfGain;
                gainLf = ConfigManager.Config.HydrophoneBandpassFilterLfGain;
                AlEffects.Filterf(filterId, AlEffects.AL_BANDPASS_GAINHF, gainHf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to update filter {filterId} param AL_BANDPASS_GAINHF for source {sourceId}, {Al.GetErrorString(alError)}");
                AlEffects.Filterf(filterId, AlEffects.AL_BANDPASS_GAINLF, gainLf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to update filter {filterId} param AL_BANDPASS_GAINLF for source {sourceId}, {Al.GetErrorString(alError)}");
            }
            else
            {
                // Don't muffle voip sounds with OpenAL.
                if (channelInfo.AudioIsVoice) 
                {
                    gainHf = 1;
                    gainLf = 1;
                }

                AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAIN, gainLf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAIN for source {sourceId}, {Al.GetErrorString(alError)}");
                AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAINHF, gainHf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAINHF for source {sourceId}, {Al.GetErrorString(alError)}");
            }

            // Re-attach filter.
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to re-attach filter {filterId} to source {sourceId}, {Al.GetErrorString(alError)}");
        }

        private uint DetermineReverbEffectSlot(ChannelInfo channelInfo)
        {
            if (currentReverb == ReverbType.None) { return INVALID_ID; }

            bool sourceInHull = channelInfo.ChannelHull != null;
            float amplitude = channelInfo.Channel.CurrentAmplitude;
            if (currentReverb == ReverbType.Inside)
            {
                bool shouldReverbAir = sourceInHull &&
                                       !channelInfo.IgnoreAirReverb &&
                                       (channelInfo.IsLoud || channelInfo.SoundInfo.ForceReverb || amplitude >= ConfigManager.Config.DyanmicReverbAirAmplitudeThreshold && Listener.ConnectedArea >= ConfigManager.Config.DynamicReverbMinArea);
                return shouldReverbAir ? reverbSlotId : INVALID_ID;
            }
            else if (currentReverb == ReverbType.Outside)
            {
                bool shouldReverbWater = !sourceInHull &&
                                         !channelInfo.IgnoreWaterReverb &&
                                         (channelInfo.IsLoud || channelInfo.SoundInfo.ForceReverb || amplitude >= ConfigManager.Config.DyanmicReverbWaterAmplitudeThreshold);
                return shouldReverbWater ? reverbSlotId : INVALID_ID;
            }
            else if (currentReverb == ReverbType.Hydrophone)
            {
                return channelInfo.Hydrophoned ? reverbSlotId : INVALID_ID;
            }

            return INVALID_ID;
        }

        private uint DetermineDistortionEffectSlot(ChannelInfo channelInfo)
        {
            if (currentDistortion == DistortionType.None) { return INVALID_ID; }

            if (currentDistortion == DistortionType.Inside)
            {
                bool shouldDistort = !channelInfo.Hydrophoned &&
                                      channelInfo.SoundInfo.Distortion &&
                                      channelInfo.MuffleStrength <= Config.LoudSoundDistortionAirMaxMuffleThreshold 
                                      ||
                                      Config.DrowningRadioDistortion && channelInfo.AudioIsRadioVoice && BubbleManager.IsClientDrowning(channelInfo.SpeakingClient);
                return shouldDistort ? distortionSlotId : INVALID_ID;
            }
            else if (currentDistortion == DistortionType.Outside)
            {
                bool shouldDistort = !channelInfo.Hydrophoned &&
                                      channelInfo.SoundInfo.Distortion &&
                                      channelInfo.MuffleStrength <= Config.LoudSoundDistortionWaterMaxMuffleThreshold
                                      ||
                                      Config.DrowningRadioDistortion && channelInfo.AudioIsRadioVoice && BubbleManager.IsClientDrowning(channelInfo.SpeakingClient);
                return shouldDistort ? distortionSlotId : INVALID_ID;
            }
            else if (currentDistortion == DistortionType.Hydrophone)
            {
                return channelInfo.Hydrophoned ? distortionSlotId : INVALID_ID;
            }

            return INVALID_ID;
        }

        private void RouteSourceToEffectSlot(uint send, uint sourceId, uint targetSlot, uint filterId)
        {
            if (!IsInitialized || send > AlEffects.MaxAuxiliarySends) return;

            Al.GetError();
            int alError;

            // Disable auto gain when a source is sending to a reverb effect slot.
            bool sourceHasReverb = _reverbRoutedSources.ContainsKey(sourceId) || targetSlot == reverbSlotId;
            Al.Sourcei(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER_GAIN_AUTO, sourceHasReverb ? Al.False : Al.True);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to set AL_AUXILIARY_SEND_FILTER_GAIN_AUTO for source ID: {sourceId}, {Al.GetErrorString(alError)}");
            if (sourceHasReverb) { _reverbRoutedSources.TryAdd(sourceId, true); }

            // Send source to the specified effect slot via the specified send.
            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, (int)targetSlot, (int)send, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.AddWarning($"[SoundproofWalls] Failed to route source ID {sourceId} to target slot ID {targetSlot} via send {send}, {Al.GetErrorString(alError)}");
        }

        public void Dispose()
        {
            if (!IsInitialized) return;

            DebugConsole.NewMessage($"[SoundproofWalls] Disposing EfxAudioManager...");
            IsInitialized = false;

            Al.GetError();
            int alError;

            // Cleanup filters and disconnect sources from sends.
            var sourceIdsToClean = new List<uint>(_sourceFilters.Keys);
            foreach (uint sourceId in sourceIdsToClean) { UnregisterSource(sourceId); }
            _sourceFilters.Clear();
            _reverbRoutedSources.Clear();

            // Cleanup reverb slots and effects.
            // Detach effects from slots
            if (reverbSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(reverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.LogError($"[SoundproofWalls] Failed to detach reverb effect from slot ID: {reverbSlotId}, {Al.GetErrorString(alError)}");

            if (distortionSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(distortionSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.LogError($"[SoundproofWalls] Failed to detach distortion effect from slot ID: {distortionSlotId}, {Al.GetErrorString(alError)}");

            // Delete Slots
            if (reverbSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { reverbSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.LogError($"[SoundproofWalls] Failed to delete reverb slot ID: {reverbSlotId}, {Al.GetErrorString(alError)}");

            if (distortionSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { distortionSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.LogError($"[SoundproofWalls] Failed to delete distortion slot ID: {distortionSlotId}, {Al.GetErrorString(alError)}");

            // Delete Effects
            if (reverbEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { reverbEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.LogError($"[SoundproofWalls] Failed to delete reverb effect ID: {reverbEffectId}, {Al.GetErrorString(alError)}");

            if (distortionEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { distortionEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.LogError($"[SoundproofWalls] Failed to delete distortion effect ID: {distortionEffectId}, {Al.GetErrorString(alError)}");


            // Reset IDs
            reverbSlotId = INVALID_ID;
            reverbEffectId = INVALID_ID;
            
            distortionSlotId = INVALID_ID;
            distortionEffectId = INVALID_ID;

            DebugConsole.NewMessage($"[SoundproofWalls] EfxAudioManager disposed successfully");
        }
    }
}