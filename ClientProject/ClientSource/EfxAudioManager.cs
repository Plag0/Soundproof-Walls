using Barotrauma;
using Barotrauma.Sounds;
using OpenAL;

namespace SoundproofWalls
{
    public class EfxAudioManager : IDisposable
    {
        private const uint INVALID_ID = 0;

        private uint _indoorReverbSlotId = INVALID_ID;
        private uint _indoorReverbEffectId = INVALID_ID;
        private uint _outdoorReverbSlotId = INVALID_ID;
        private uint _outdoorReverbEffectId = INVALID_ID;
        private uint _hydrophoneDistortionSlotId = INVALID_ID;
        private uint _hydrophoneDistortionEffectId = INVALID_ID;
        private uint _loudDistortionSlotId = INVALID_ID;
        private uint _loudDistortionEffectId = INVALID_ID;

        // Maps source IDs to their current filter ID
        private Dictionary<uint, uint> _sourceFilters = new Dictionary<uint, uint>();

        public bool IsInitialized { get; private set; } = false;

        private double LastEffectUpdateTime = 0;

        private float trailingAirReverbGain = ConfigManager.Config.DynamicReverbAirTargetGain;

        public EfxAudioManager()
        {
            if (!AlEffects.IsInitialized)
            {
                DebugConsole.NewMessage("[SoundproofWalls] Error: AlEffects wrapper not initialized.");
                return;
            }

            if (!InitEffects()) { Dispose(); return; }

            _sourceFilters = new Dictionary<uint, uint>();

            IsInitialized = true;
            DebugConsole.NewMessage("[SoundproofWalls] DynamicFx initialization complete.");
        }

        private bool InitEffects()
        {
            int numEffects = 4;
            uint[] effectSlots = new uint[numEffects];
            uint[] effects = new uint[numEffects];

            int alError;
            Al.GetError();

            AlEffects.GenAuxiliaryEffectSlots(numEffects, effectSlots);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate effect slots, {Al.GetErrorString(alError)}");
                return false;
            }
            _indoorReverbSlotId = effectSlots[0];
            _outdoorReverbSlotId = effectSlots[1];
            _hydrophoneDistortionSlotId = effectSlots[2];
            _loudDistortionSlotId = effectSlots[3];

            AlEffects.GenEffects(numEffects, effects);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate effects, {Al.GetErrorString(alError)}");
                return false;
            }
            _indoorReverbEffectId = effects[0];
            _outdoorReverbEffectId= effects[1];
            _hydrophoneDistortionEffectId = effects[2];
            _loudDistortionEffectId = effects[3];

            // Set type
            AlEffects.Effecti(_indoorReverbEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_REVERB);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect ID {_indoorReverbEffectId} type to reverb for slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            // Set type
            AlEffects.Effecti(_outdoorReverbEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_REVERB);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect ID {_outdoorReverbEffectId} type to reverb for slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            // Set type
            AlEffects.Effecti(_hydrophoneDistortionEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_DISTORTION);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect ID {_hydrophoneDistortionEffectId} type to distortion for slot ID: {_hydrophoneDistortionSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            // Set type
            AlEffects.Effecti(_loudDistortionEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_DISTORTION);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect ID {_loudDistortionEffectId} type to distortion for slot ID: {_loudDistortionSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            return true;
        }

        public void Update()
        {
            if (!ConfigManager.Config.DynamicFx) { return; }

            if (Timing.TotalTime < LastEffectUpdateTime + ConfigManager.Config.OpenALEffectsUpdateInterval) { return; }
            LastEffectUpdateTime = Timing.TotalTime;

            UpdateInsideReverbEffect();
            UpdateOutsideReverbEffect();
            UpdateHydrophoneDistortionEffect();
            UpdateLoudSoundDistortionEffect();
        }

        private void UpdateLoudSoundDistortionEffect()
        {
            if (!IsInitialized ||
                _loudDistortionEffectId == INVALID_ID ||
                _loudDistortionSlotId == INVALID_ID)
                return;

            uint effectId = _loudDistortionEffectId;

            // Each loud sound playing linearly increases the gain/edge mult based on their custom set gain mult.
            SoundChannel[] playingChannels = GameMain.SoundManager.playingChannels[0];
            float combinedGainMult = 1;
            for (int i = 0; i < playingChannels.Length; i++)
            {
                if (playingChannels[i] != null && playingChannels[i].IsPlaying && ChannelInfoManager.TryGetChannelInfo(playingChannels[i], out ChannelInfo? info) && info != null && !info.Ignored && info.IsLoud && info.SoundInfo.GainMult > 1)
                {
                    combinedGainMult += (info.SoundInfo.GainMult - 1) / 75; // Divide by x to make less impactful
                }
            }

            float gain = Math.Clamp(ConfigManager.Config.LoudSoundDistortionTargetGain, 0.01f, 1f);
            float edge = Math.Clamp(ConfigManager.Config.LoudSoundDistortionTargetEdge, 0.0f, 1.0f);
            int lowpass = Math.Clamp(ConfigManager.Config.LoudSoundDistortionLowpassFrequency, 80, 24000);
            int eqCenter = Math.Clamp(ConfigManager.Config.LoudSoundDistortionTargetFrequency, 80, 24000);
            int eqBandwidth = 1500;

            int alError;
            Al.GetError();

            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EDGE, edge);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_GAIN, gain);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_LOWPASS_CUTOFF, lowpass);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EQCENTER, eqCenter);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EQBANDWIDTH, eqBandwidth);

            AlEffects.AuxiliaryEffectSloti(_loudDistortionSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, effectId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach loud distortion effect ID {effectId} to slot ID: {_loudDistortionSlotId}, {Al.GetErrorString(alError)}");
        }

        private void UpdateHydrophoneDistortionEffect()
        {
            if (!IsInitialized ||
                _hydrophoneDistortionEffectId == INVALID_ID ||
                _hydrophoneDistortionSlotId == INVALID_ID)
                return;

            uint effectId = _hydrophoneDistortionEffectId;

            // Each sound playing through hydrophones linearly increases the gain/edge mult.
            SoundChannel[] playingChannels = GameMain.SoundManager.playingChannels[0];
            float multiSoundMult = 0;
            float soundStrength = 0.02f;
            for (int i = 0; i < playingChannels.Length; i++)
            {
                if (playingChannels[i] != null && playingChannels[i].IsPlaying && ChannelInfoManager.TryGetChannelInfo(playingChannels[i], out ChannelInfo? info) && info != null && !info.Ignored && info.Hydrophoned)
                {
                    if (info.IsLoud) { multiSoundMult += soundStrength * 2; }
                    else             { multiSoundMult += soundStrength; }
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

            int alError;
            Al.GetError();

            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EDGE, edge);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_GAIN, targetGain);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_LOWPASS_CUTOFF, lowpassCutoff);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EQCENTER, eqCenter);
            AlEffects.Effectf(effectId, AlEffects.AL_DISTORTION_EQBANDWIDTH, eqBandwidth);

            AlEffects.AuxiliaryEffectSloti(_hydrophoneDistortionSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, effectId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach distortion effect ID {effectId} to slot ID: {_hydrophoneDistortionSlotId}, {Al.GetErrorString(alError)}");
        }

        public void UpdateInsideReverbEffect()
        {
            if (!IsInitialized ||
                _indoorReverbEffectId == INVALID_ID ||
                _indoorReverbSlotId == INVALID_ID)
                return;

            uint reverbEffectId = _indoorReverbEffectId;

            // Calculate the strength of the reverb effect based on aggregated looping sound amplitude, gain, and player submersion.
            SoundChannel[] playingChannels = GameMain.SoundManager.playingChannels[0]; // Index 0 is sounds while 1 is voice (cite SourcePoolIndex enum) yeah I guess I'm citing stuff in code comments now...
            float totalAudioAmplitude = 0;
            float loudSoundGain = 0;
            for (int i = 0; i < playingChannels.Length; i++)
            {
                if (playingChannels[i] != null && playingChannels[i].IsPlaying)
                {
                    if (ChannelInfoManager.TryGetChannelInfo(playingChannels[i], out ChannelInfo? info) && info != null && !info.Ignored)
                    {
                        if (playingChannels[i].Looping)
                        {
                            float gain = playingChannels[i].Gain;
                            if (!info.IsLoud)
                            {
                                float sidechainMult = Plugin.Sidechain.SidechainMultiplier;
                                if (sidechainMult >= 1) { sidechainMult = 0.99f; }
                                if (gain <= 0) { gain = 0.01f; }
                                gain /= 1 - sidechainMult; // Undo gain adjustments from sidechain multiplier for audio amplitude calculation.
                            }
                            float amplitude = playingChannels[i].CurrentAmplitude;

                            float muffleMult = 1 - info.MuffleStrength;

                            //LuaCsLogger.Log($"{Path.GetFileName(playingChannels[i].Sound.Filename)} gain: {gain} * amplitude {amplitude} * muffleMult {muffleMult} = totalAudioAmplitude {totalAudioAmplitude}");
                            totalAudioAmplitude += amplitude * gain * muffleMult;
                        }
                        else
                        {
                            if (info.IsLoud)
                            {
                                loudSoundGain += 0.2f;
                            }
                        }
                    }
                }
            }
            float amplitudeMult = 1f - MathUtils.InverseLerp(0.0f, 1.4f, totalAudioAmplitude);
            float submersionMult = Listener.IsSubmerged ? 1.3f : 1.0f;
            float targetReverbGain = (ConfigManager.Config.DynamicReverbAirTargetGain * amplitudeMult * submersionMult)  +  (loudSoundGain * ConfigManager.Config.DynamicReverbAirTargetGain);

            targetReverbGain = Math.Clamp(targetReverbGain, 0.0f, 1.0f);

            float gainDiff = targetReverbGain - trailingAirReverbGain;
            trailingAirReverbGain += Math.Abs(gainDiff) < 0.01f ? gainDiff : Math.Sign(gainDiff) * 0.01f;

            // Influences decay and delay times.
            float roomSizeFactor = (Listener.ConnectedArea * ConfigManager.Config.DynamicReverbAreaSizeMultiplier) / 180000; // Arbitrary magic number that seems to work well.

            //LuaCsLogger.Log($" targetReverbGain: {targetReverbGain} actualReverbGain: {AirReverbGain} playingChannelMult: {amplitudeMult} totalAudioAmplitude: {totalAudioAmplitude} loudSoundGain {loudSoundGain}");

            // Decay Time
            float decayTime = 3.1f + (roomSizeFactor * 1.0f);
            decayTime = Math.Clamp(decayTime, 0.1f, 20.0f);

            // Decay HF Ratio
            float decayHfRatio = 0.2f;
            decayHfRatio = Math.Clamp(decayHfRatio, 0.1f, 2.0f);

            // Reflections Gain: Strong initial pings off metal walls.
            float reflectionsGain = 0.9f;
            reflectionsGain = Math.Clamp(reflectionsGain, 0.0f, 3.16f);

            // Reflections Delay
            float reflectionsDelay = 0.025f + (roomSizeFactor * 0.010f); // Base 25ms + up to 20ms more
            reflectionsDelay = Math.Clamp(reflectionsDelay, 0.005f, 0.1f); // Sensible bounds

            // Late Reverb Gain: Tail of the reverb
            float lateReverbGain = trailingAirReverbGain * 1.8f; // Make tail prominent relative to overall gain
            lateReverbGain = Math.Clamp(lateReverbGain, 0.0f, 10.0f);

            // Late Reverb Delay
            float lateReverbDelay = 0.040f + (roomSizeFactor * 0.015f); // Base 40ms + up to 30ms more
            lateReverbDelay = Math.Clamp(lateReverbDelay, 0.01f, 0.1f);

            // Diffusion: Lower value for metallic, distinct reflections, less "smooth wash".
            float diffusion = 0.9f;
            diffusion = Math.Clamp(diffusion, 0.0f, 1.0f);

            // Density: Standard density
            float density = 1.0f;
            density = Math.Clamp(density, 0.0f, 1.0f);

            // Gain HF: Metal is bright, minimal HF damping in the effect itself.
            float gainHf = 0.3f;
            gainHf = Math.Clamp(gainHf, 0.0f, 1.0f);

            int alError;
            Al.GetError();

            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DENSITY, density);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DIFFUSION, diffusion);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAIN, trailingAirReverbGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAINHF, gainHf);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_TIME, decayTime);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFRATIO, decayHfRatio);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_GAIN, reflectionsGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_DELAY, reflectionsDelay);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_GAIN, lateReverbGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_DELAY, lateReverbDelay);
            AlEffects.Effecti(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFLIMIT, Al.True); // Keep HF limit ON
            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.NewMessage($"[SoundproofWalls] Error applying inside reverb params for effect ID: {reverbEffectId}, {Al.GetErrorString(alError)}"); }

            AlEffects.AuxiliaryEffectSloti(_indoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _indoorReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach reverb effect ID {_indoorReverbEffectId} to main slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}"); }
        }

        public void UpdateOutsideReverbEffect()
        {
            if (!IsInitialized ||
                _outdoorReverbEffectId == INVALID_ID ||
                _outdoorReverbSlotId == INVALID_ID)
                return;

            uint reverbEffectId = _outdoorReverbEffectId;

            float targetReverbGain = ConfigManager.Config.DynamicReverbWaterTargetGain;

            // Decay Time
            float decayTime = 20.0f;
            decayTime = Math.Clamp(decayTime, 0.1f, 20.0f);

            // Decay HF Ratio
            float decayHfRatio = 0.1f; // Make high frequencies die out very quickly
            decayHfRatio = Math.Clamp(decayHfRatio, 0.1f, 1.0f);

            // Reflections Gain: Strong reflection from the ice ceiling or seabed.
            float reflectionsGain = 0.42f;
            reflectionsGain = Math.Clamp(reflectionsGain, 0.0f, 3.16f);

            // Reflections Delay: Represents distance to the nearest large reflector (ice/seabed).
            float reflectionsDelay = 0.3f;
            reflectionsDelay = Math.Clamp(reflectionsDelay, 0.05f, 0.3f);

            // Late Reverb Gain: Must be strong enough to support the very long decay time.
            float lateReverbGain = targetReverbGain * 1.5f; // Boost relative to main gain
            lateReverbGain = Math.Clamp(lateReverbGain, 0.0f, 10.0f);

            // Late Reverb Delay: Long delay after initial reflection = vastness.
            float lateReverbDelay = 0.1f;
            lateReverbDelay = Math.Clamp(lateReverbDelay, 0.05f, 0.1f);

            // Diffusion: Very low for open water, less "smooth", more echo-like.
            float diffusion = 0.25f;
            diffusion = Math.Clamp(diffusion, 0.0f, 1.0f);

            // Density: Below 1 for potential resonant "booming" or "hollowness" of deep water.
            float density = 0.9f;
            density = Math.Clamp(density, 0.0f, 1.0f);

            // Gain HF: Overall reduction in high frequencies due to water absorption effect.
            float gainHf = 0.04f; // Cut highs significantly
            gainHf = Math.Clamp(gainHf, 0.0f, 1.0f);

            int alError;
            Al.GetError();

            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DENSITY, density);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DIFFUSION, diffusion);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAIN, targetReverbGain); // Wet/Dry
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAINHF, gainHf);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_TIME, decayTime);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFRATIO, decayHfRatio);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_GAIN, reflectionsGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_DELAY, reflectionsDelay);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_GAIN, lateReverbGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_DELAY, lateReverbDelay);
            AlEffects.Effecti(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFLIMIT, Al.True); // Keep HF limit ON

            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.NewMessage($"[SoundproofWalls] Error applying outside reverb params for effect ID: {reverbEffectId}, {Al.GetErrorString(alError)}"); }

            AlEffects.AuxiliaryEffectSloti(_outdoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _outdoorReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach reverb effect ID {_outdoorReverbEffectId} to secondary slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}"); }
        }

        /// <summary>
        /// Creates and registers a dedicated AL_FILTER_LOWPASS filter for a new source.
        /// Initializes the filter to a neutral state and attaches it via AL_DIRECT_FILTER.
        /// </summary>
        public void RegisterSource(uint sourceId)
        {
            if (!IsInitialized || _sourceFilters.ContainsKey(sourceId)) return;

            int alError;
            Al.GetError();

            uint[] filters = new uint[1];

            AlEffects.GenFilters(1, filters);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate filter for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                return;
            }

            uint filterId = filters[0];

            // Set type to Lowpass
            AlEffects.Filteri(filterId, AlEffects.AL_FILTER_TYPE, AlEffects.AL_FILTER_LOWPASS);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set filter {filterId} type to lowpass for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                // Clean up failed filter
                AlEffects.DeleteFilters(1, filters);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to clean up failed filter ID: {filterId}, {Al.GetErrorString(alError)}");
                return;
            }

            // Set initial neutral parameters
            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAIN, 1.0f);
            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAINHF, 1.0f);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set initial values for filter ID: {filterId}, {Al.GetErrorString(alError)}");
                // Clean up failed filter
                AlEffects.DeleteFilters(1, filters);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to clean up failed filter ID: {filterId}, {Al.GetErrorString(alError)}");
                return;
            }

            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach initial filter {filterId} to source ID: {sourceId}, {Al.GetErrorString(alError)}");
                // Clean up failed filter
                AlEffects.DeleteFilters(1, filters);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to clean up failed filter ID: {filterId}, {Al.GetErrorString(alError)}");
                return;
            }

            _sourceFilters[sourceId] = filterId;
        }

        /// <summary>
        /// Detaches direct filter, disconnects auxiliary sends, deletes the filter object,
        /// and removes the source from tracking. Call this when the source is disposed.
        /// </summary>
        public void UnregisterSource(uint sourceId)
        {
            if (!IsInitialized || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) return;

            int alError;
            Al.GetError();

            // Disconnect auxiliary sends 0 and 1
            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, AlEffects.AL_EFFECTSLOT_NULL, 0, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to disconnect send 0 for source ID: {sourceId}, {Al.GetErrorString(alError)}");
            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, AlEffects.AL_EFFECTSLOT_NULL, 1, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to disconnect send 1 for source ID: {sourceId}, {Al.GetErrorString(alError)}");

            // Detach direct filter
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach direct filter from source ID: {sourceId}, {Al.GetErrorString(alError)}");

            // Delete the filter
            if (filterId != INVALID_ID)
            {
                AlEffects.DeleteFilters(1, new[] { filterId });
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete filter ID: {filterId}, {Al.GetErrorString(alError)}");
            }

            _sourceFilters.Remove(sourceId);
        }

        public void UpdateSource(ChannelInfo channelInfo, float gainHf, float gainLf = 1)
        {
            uint sourceId = channelInfo.Channel.Sound.Owner.GetSourceFromIndex(channelInfo.Channel.Sound.SourcePoolIndex, channelInfo.Channel.ALSourceIndex);

            if (!IsInitialized || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) { return; }

            // Select which effect slot to route to.
            uint send1_targetSlot = Send1_DetermineTargetEffectSlot(channelInfo);
            uint send0_targetSlot = Send0_DetermineTargetEffectSlot(channelInfo);

            UpdateSourceFilter(sourceId, filterId, gainHf, gainLf, channelInfo);
            RouteSourceToEffectSlot(sourceId, send1_targetSlot, filterId, send: 1);
            RouteSourceToEffectSlot(sourceId, send0_targetSlot, filterId, send: 0);
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
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set filter ID {filterId}'s type for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                return;
            }

            if (useBandpass)
            {
                gainHf = ConfigManager.Config.HydrophoneBandpassFilterHfGain;
                gainLf = ConfigManager.Config.HydrophoneBandpassFilterLfGain;
                AlEffects.Filterf(filterId, AlEffects.AL_BANDPASS_GAINHF, gainHf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_BANDPASS_GAINHF for source {sourceId}, {Al.GetErrorString(alError)}");
                AlEffects.Filterf(filterId, AlEffects.AL_BANDPASS_GAINLF, gainLf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_BANDPASS_GAINLF for source {sourceId}, {Al.GetErrorString(alError)}");
            }
            else
            {
                AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAIN, gainLf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAIN for source {sourceId}, {Al.GetErrorString(alError)}");
                AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAINHF, gainHf);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAINHF for source {sourceId}, {Al.GetErrorString(alError)}");
            }

            // Re-attach filter.
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to re-attach filter {filterId} to source {sourceId}, {Al.GetErrorString(alError)}");
        }

        /// <summary>
        /// Routes a source's auxiliary send to the appropriate reverb slot.
        /// </summary>
        private void RouteSourceToEffectSlot(uint sourceId, uint targetSlot, uint filterId, uint send = 1)
        {
            if (!IsInitialized || send > AlEffects.MaxAuxiliarySends) return;

            Al.GetError();
            int alError;

            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, (int)targetSlot, (int)send, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to route source to target slot ID: {targetSlot} via send {send}, {Al.GetErrorString(alError)}");
        }

        /// <summary>
        /// Cleans up all generated EFX objects.
        /// </summary>
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

            // Cleanup reverb slots and effects.
            // Detach effects from slots
            if (_indoorReverbSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_indoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach indoor reverb effect from slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");

            if (_outdoorReverbSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_outdoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach outdoor reverb effect from slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");

            if (_hydrophoneDistortionSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_hydrophoneDistortionSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach hydrophone distortion effect from slot ID: {_hydrophoneDistortionSlotId}, {Al.GetErrorString(alError)}");

            if (_loudDistortionSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_loudDistortionSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach loud distortion effect from slot ID: {_loudDistortionSlotId}, {Al.GetErrorString(alError)}");


            // Delete Slots
            if (_indoorReverbSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _indoorReverbSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete indoor slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");

            if (_outdoorReverbSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _outdoorReverbSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete outdoor slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");

            if (_hydrophoneDistortionSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _hydrophoneDistortionSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete hydrophone distortion slot ID: {_hydrophoneDistortionSlotId}, {Al.GetErrorString(alError)}");

            if (_loudDistortionSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _loudDistortionSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete loud distortion slot ID: {_loudDistortionSlotId}, {Al.GetErrorString(alError)}");

            // Delete Effects
            if (_indoorReverbEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _indoorReverbEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete indoor reverb effect ID: {_indoorReverbEffectId}, {Al.GetErrorString(alError)}");

            if (_outdoorReverbEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _outdoorReverbEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete outdoor reverb effect ID: {_outdoorReverbEffectId}, {Al.GetErrorString(alError)}");

            if (_hydrophoneDistortionEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _hydrophoneDistortionEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete hydrophone distortion effect ID: {_hydrophoneDistortionEffectId}, {Al.GetErrorString(alError)}");

            if (_loudDistortionEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _loudDistortionEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete loud distortion effect ID: {_loudDistortionEffectId}, {Al.GetErrorString(alError)}");


            // Reset IDs
            _indoorReverbSlotId = INVALID_ID;
            _indoorReverbEffectId = INVALID_ID;
            _outdoorReverbSlotId = INVALID_ID;
            _outdoorReverbEffectId = INVALID_ID;
            _hydrophoneDistortionSlotId = INVALID_ID;
            _hydrophoneDistortionEffectId = INVALID_ID;
            _loudDistortionSlotId = INVALID_ID;
            _loudDistortionEffectId = INVALID_ID;

            DebugConsole.NewMessage($"[SoundproofWalls] EfxAudioManager disposed successfully");
        }

        /// <summary>
        /// Determines the correct reverb slot ID based on environment and config flags.
        /// </summary>
        private uint Send1_DetermineTargetEffectSlot(ChannelInfo channelInfo)
        {
            bool inHull = channelInfo.ChannelHull != null;
            bool hydrophoned = channelInfo.Hydrophoned;
            bool ingoreReverb = channelInfo.IgnoreReverb;
            float amplitude = channelInfo.Channel.CurrentAmplitude * channelInfo.Gain; // TODO this may be unreliable if a user changes the submerged gain mult
            bool shouldReverbWater = channelInfo.IsLoud || amplitude >= ConfigManager.Config.DyanmicReverbWaterAmplitudeThreshold;

            if (hydrophoned && ConfigManager.Config.HydrophoneDistortionEnabled)
            { 
                return _hydrophoneDistortionSlotId; 
            }

            if (ingoreReverb || !ConfigManager.Config.DynamicReverbEnabled)
            {
                return INVALID_ID;
            }

            if (inHull)
            {
                return Listener.ConnectedArea >= ConfigManager.Config.DynamicReverbMinArea
                    ? _indoorReverbSlotId
                    : INVALID_ID;
            }
            else // Not in hull
            {
                return shouldReverbWater
                    ? _outdoorReverbSlotId
                    : INVALID_ID;
            }
        }

        private uint Send0_DetermineTargetEffectSlot(ChannelInfo channelInfo)
        {
            if (channelInfo.SoundInfo.Distortion && 
                channelInfo.MuffleStrength <= 0.5f && 
                ConfigManager.Config.LoudSoundDistortionEnabled) 
            { return _loudDistortionSlotId; }

            return INVALID_ID;
        }
    }
}