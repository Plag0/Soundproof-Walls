using Barotrauma;
using OpenAL;

namespace SoundproofWalls {
    public class EfxAudioManager : IDisposable
    {
        private const uint INVALID_ID = 0;

        private uint _primaryReverbSlotId = INVALID_ID;
        private uint _primaryReverbEffectId = INVALID_ID;
        private uint _secondaryReverbSlotId = INVALID_ID;
        private uint _secondaryReverbEffectId = INVALID_ID;

        // Maps source IDs to their lowpass filter ID
        private Dictionary<uint, uint> _sourceFilters = new Dictionary<uint, uint>();

        public bool IsInitialized { get; private set; } = false;
        public bool HasTwoSlots { get; private set; } = false;

        private double LastReverbUpdateTime = 0;
        private double ReverbUpdateInterval = 0.2f;

        /// <summary>
        /// Initializes auxiliary effect slots, reverb effects, and the source filter dictionary.
        /// </summary>
        public EfxAudioManager()
        {
            if (!AlEffects.IsInitialized)
            {
                DebugConsole.NewMessage("[SoundproofWalls] Error: AlEffects wrapper not initialized.");
                return;
            }

            // Create Main Slot/Effect

            int alError;
            Al.GetError();

            uint[] slots = new uint[1];
            AlEffects.GenAuxiliaryEffectSlots(1, slots);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate main slot, {Al.GetErrorString(alError)}");
                Dispose();
                return;
            }
            _primaryReverbSlotId = slots[0];

            uint[] effects = new uint[1];
            AlEffects.GenEffects(1, effects);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate effect for main slot ID: {_primaryReverbSlotId}, {Al.GetErrorString(alError)}");
                Dispose();
                return;
            }
            _primaryReverbEffectId = effects[0];

            AlEffects.Effecti(_primaryReverbEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_REVERB);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect {_primaryReverbEffectId} type to reverb for main slot ID: {_primaryReverbSlotId}, {Al.GetErrorString(alError)}");
                Dispose();
                return;
            }

            UpdateReverbEffectForRoom(_primaryReverbEffectId); // Arbitrary default values.

            AlEffects.AuxiliaryEffectSloti(_primaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _primaryReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach initial reverb effect {_primaryReverbEffectId} to main slot ID: {_primaryReverbSlotId}, {Al.GetErrorString(alError)}");
                Dispose();
                return;
            }

            DebugConsole.NewMessage($"[SoundproofWalls] Initialized Primary Auxiliary Slot: {_secondaryReverbSlotId}, Effect: {_secondaryReverbEffectId}");

            // Create secondary Slot/Effect
            if (AlEffects.MaxAuxiliarySends < 2)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to initialize secondary slot");
            }
            else { 
                HasTwoSlots = true;
                AlEffects.GenAuxiliaryEffectSlots(1, slots);
                if ((alError = Al.GetError()) != Al.NoError)
                {
                    DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate secondary slot, {Al.GetErrorString(alError)}");
                    HasTwoSlots = false;
                }
                else
                {
                    _secondaryReverbSlotId = slots[0];
                }

                if (HasTwoSlots)
                {
                    AlEffects.GenEffects(1, effects);
                    if ((alError = Al.GetError()) != Al.NoError)
                    {
                        DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate effect for secondary slot ID: {_secondaryReverbSlotId}, {Al.GetErrorString(alError)}");
                        HasTwoSlots = false;
                    }
                    else 
                    {
                        _secondaryReverbEffectId = effects[0];
                    }
                }

                if (HasTwoSlots)
                {
                    AlEffects.Effecti(_secondaryReverbEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_REVERB);
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect {_secondaryReverbEffectId} type to reverb for secondary slot ID: {_secondaryReverbSlotId}, {Al.GetErrorString(alError)}");

                    UpdateReverbEffectForWater(_secondaryReverbEffectId); // Arbitrary default values.

                    AlEffects.AuxiliaryEffectSloti(_secondaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _secondaryReverbEffectId);
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach initial reverb effect {_secondaryReverbEffectId} to secondary slot ID: {_secondaryReverbSlotId}, {Al.GetErrorString(alError)}");

                    DebugConsole.NewMessage($"[SoundproofWalls] Initialized Secondary Auxiliary Slot: {_secondaryReverbSlotId}, Effect: {_secondaryReverbEffectId}");
                }
            }

            _sourceFilters = new Dictionary<uint, uint>();

            IsInitialized = true;
            DebugConsole.NewMessage("[SoundproofWalls] Initialization Complete.");
        }

        public void Update()
        {
            if (ConfigManager.Config.DynamicFx != false)
            {
                if (Timing.TotalTime < LastReverbUpdateTime + ReverbUpdateInterval) { return; }

                LastReverbUpdateTime = Timing.TotalTime;

                UpdatePrimaryEnvironment();
                UpdateSecondaryEnvironment();
            }
        }

        public void UpdatePrimaryEnvironment()
        {
            if ( !IsInitialized || 
                _primaryReverbEffectId == INVALID_ID || 
                _primaryReverbSlotId == INVALID_ID)
                return;

            UpdateReverbEffectForRoom(_primaryReverbEffectId);

            int alError;
            Al.GetError();

            AlEffects.AuxiliaryEffectSloti(_primaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _primaryReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach reverb effect {_primaryReverbEffectId} to main slot ID: {_primaryReverbSlotId}, {Al.GetErrorString(alError)}");
        }

        public void UpdateSecondaryEnvironment()
        {
            if (!IsInitialized ||
                _secondaryReverbEffectId == INVALID_ID ||
                _secondaryReverbSlotId == INVALID_ID)
                return;

            UpdateReverbEffectForWater(_secondaryReverbEffectId);

            int alError;
            Al.GetError();

            AlEffects.AuxiliaryEffectSloti(_secondaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _secondaryReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach reverb effect {_secondaryReverbEffectId} to secondary slot ID: {_secondaryReverbSlotId}, {Al.GetErrorString(alError)}");
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
            // Check if initialized and if the source is actually tracked
            if (!IsInitialized || _sourceFilters == null || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) return;

            int alError;
            Al.GetError();

            // --- Disconnect Auxiliary Sends ---
            // Disconnect send 0
            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, AlEffects.AL_EFFECTSLOT_NULL, 0, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to disconnect send 0 for source ID: {sourceId}, {Al.GetErrorString(alError)}");

            // --- Detach Direct Filter ---
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach direct filter from source ID: {sourceId}, {Al.GetErrorString(alError)}");

            // --- Delete the Filter Object ---
            if (filterId != INVALID_ID)
            {
                AlEffects.DeleteFilters(1, new[] { filterId });
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete filter ID: {filterId}, {Al.GetErrorString(alError)}");
            }

            // --- Remove from Dictionary ---
            _sourceFilters.Remove(sourceId);
        }

        public void UpdateSourceLowpass(uint sourceId, float gainHf, float gainLf = 1)
        {
            if (!IsInitialized || _sourceFilters.Count == 0 || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) return;

            int alError;
            Al.GetError();

            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAIN, gainLf);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAIN for source {sourceId}, {Al.GetErrorString(alError)}");
            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAINHF, gainHf);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAINHF for source {sourceId}, {Al.GetErrorString(alError)}");

            // Re-attach filter.
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to re-attach filter {filterId} to source {sourceId}, {Al.GetErrorString(alError)}");
        }


        /// <summary>
        /// Routes a source's auxiliary send 0 to the appropriate reverb slot.
        /// </summary>
        public void RouteSourceToEnvironment(uint sourceId, bool routePrimary, bool routeNull = false)
        {
            if (!IsInitialized) return;

            uint targetSlot = routePrimary ? _primaryReverbSlotId : (_secondaryReverbSlotId != INVALID_ID ? _secondaryReverbSlotId : INVALID_ID);

            Al.GetError();
            int alError;

            if (targetSlot != INVALID_ID && ConfigManager.Config.ReverbEnabled && !routeNull)
            {
                Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, (int)targetSlot, 0, AlEffects.AL_FILTER_NULL);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to route source to target slot ID: {targetSlot}, {Al.GetErrorString(alError)}");
            }
            else
            {
                Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, AlEffects.AL_EFFECTSLOT_NULL, 0, AlEffects.AL_FILTER_NULL);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to route source to null slot, {Al.GetErrorString(alError)}");
            }
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

            // --- Cleanup filters and disconnect sources from sends ---
            var sourceIdsToClean = new List<uint>(_sourceFilters.Keys);
            foreach (uint sourceId in sourceIdsToClean)
            {
                UnregisterSource(sourceId);
            }
            _sourceFilters.Clear();

            // --- Cleanup Reverb ---

            // Detach effects from slots
            if (_primaryReverbSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_primaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach reverb effect from main slot ID: {_primaryReverbSlotId}, {Al.GetErrorString(alError)}");

            if (_secondaryReverbSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_secondaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach reverb effect from secondary slot ID: {_secondaryReverbSlotId}, {Al.GetErrorString(alError)}");

            // Delete Slots
            if (_primaryReverbSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _primaryReverbSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete main slot ID: {_primaryReverbSlotId}, {Al.GetErrorString(alError)}");
            
            if (_secondaryReverbSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _secondaryReverbSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete secondary slot ID: {_secondaryReverbSlotId}, {Al.GetErrorString(alError)}");

            // Delete Effects
            if (_primaryReverbEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _primaryReverbEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete main reverb effect ID: {_primaryReverbEffectId}, {Al.GetErrorString(alError)}");

            if (_secondaryReverbEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _secondaryReverbEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete secondary reverb effect ID: {_secondaryReverbEffectId}, {Al.GetErrorString(alError)}");

            // Reset IDs
            _primaryReverbSlotId = INVALID_ID;
            _primaryReverbEffectId = INVALID_ID;
            _secondaryReverbSlotId = INVALID_ID;
            _secondaryReverbEffectId = INVALID_ID;
            HasTwoSlots = false;

            DebugConsole.NewMessage($"[SoundproofWalls] EfxAudioManager disposed successfully");
        }

        public static void UpdateReverbEffectForRoom(uint reverbEffectId)
        {
            if (!AlEffects.IsInitialized) return;

            float roomSizeFactor = Listener.ConnectedArea / 180000;
            float reverbGainMult = Listener.IsSubmerged ? 1.3f : 1.0f;

            float targetReverbGain = ConfigManager.Config.ReverbAirTargetGain * reverbGainMult;
            targetReverbGain = Math.Clamp(targetReverbGain, 0.0f, 1.0f);

            // Decay Time
            float decayTime = 3.5f + (roomSizeFactor * 1.0f);
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
            float lateReverbGain = targetReverbGain * 1.8f; // Make tail prominent relative to overall gain
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

            Al.GetError();
            int error;

            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DENSITY, density);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DIFFUSION, diffusion);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAIN, targetReverbGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAINHF, gainHf);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_TIME, decayTime);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFRATIO, decayHfRatio);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_GAIN, reflectionsGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_DELAY, reflectionsDelay);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_GAIN, lateReverbGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_DELAY, lateReverbDelay);
            AlEffects.Effecti(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFLIMIT, Al.True); // Keep HF limit ON

            if ((error = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Error applying room reverb params for effect ID: {reverbEffectId}, {Al.GetErrorString(error)}");
            }
        }

        public static void UpdateReverbEffectForWater(uint reverbEffectId)
        {
            if (!AlEffects.IsInitialized) return;

            float targetReverbGain = ConfigManager.Config.ReverbWaterTargetGain;

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

            Al.GetError();
            int error;

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

            if ((error = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Error applying water reverb params for effect ID: {reverbEffectId}, {Al.GetErrorString(error)}");
            }
        }
    }
}