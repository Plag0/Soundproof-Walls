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

            UpdateReverbEffectForRoom(_primaryReverbEffectId, 10f, 3f, true); // Arbitrary default values.

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

                    UpdateReverbEffectForRoom(_secondaryReverbEffectId, 10f, 3f, true); // Arbitrary default values.

                    AlEffects.AuxiliaryEffectSloti(_secondaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _secondaryReverbEffectId);
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach initial reverb effect {_secondaryReverbEffectId} to secondary slot ID: {_secondaryReverbSlotId}, {Al.GetErrorString(alError)}");

                    DebugConsole.NewMessage($"[SoundproofWalls] Initialized Secondary Auxiliary Slot: {_secondaryReverbSlotId}, Effect: {_secondaryReverbEffectId}");
                }
            }

            _sourceFilters = new Dictionary<uint, uint>();

            IsInitialized = true;
            DebugConsole.NewMessage("[SoundproofWalls] Initialization Complete.");
        }

        /// <summary>
        /// Updates the primary reverb effect based on new room dimensions.
        /// </summary>
        public void UpdatePrimaryEnvironment(float roomWidth, float roomHeight)
        {
            if (Timing.TotalTime < LastReverbUpdateTime + ReverbUpdateInterval || 
                !IsInitialized || 
                _primaryReverbEffectId == INVALID_ID || 
                _primaryReverbSlotId == INVALID_ID)
                return;

            LastReverbUpdateTime = Timing.TotalTime;

            UpdateReverbEffectForRoom(_primaryReverbEffectId, roomWidth, roomHeight, true);

            int alError;
            Al.GetError();

            AlEffects.AuxiliaryEffectSloti(_primaryReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _primaryReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach reverb effect {_primaryReverbEffectId} to main slot ID: {_primaryReverbSlotId}, {Al.GetErrorString(alError)}");
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

        public void UpdateSourceLowpass(uint sourceId, float gainHf)
        {
            if (!IsInitialized || _sourceFilters.Count == 0 || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) return;

            int alError;
            Al.GetError();

            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAIN, 1);
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
        public void RouteSourceToEnvironment(uint sourceId, bool isInListenerEnvironment)
        {
            if (!IsInitialized) return;

            uint targetSlot = isInListenerEnvironment ? _primaryReverbSlotId : (_secondaryReverbSlotId != INVALID_ID ? _secondaryReverbSlotId : INVALID_ID);

            Al.GetError();
            int alError;

            if (targetSlot != INVALID_ID)
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

        /// <summary>
        /// Updates an existing Reverb Effect object's parameters based on room properties.
        /// </summary>
        /// <param name="reverbEffectId">The ID of the AL_EFFECT_REVERB object to modify.</param>
        /// <param name="roomWidthMeters">Approximate room width in meters.</param>
        /// <param name="roomHeightMeters">Approximate room height in meters.</param>
        /// <param name="isMetal">Set to true for metal rooms (affects reflections/decay).</param>
        public static void UpdateReverbEffectForRoom(uint reverbEffectId, float roomWidthMeters, float roomHeightMeters, bool isMetal = true)
        {
            if (!AlEffects.IsInitialized) return;

            //TODO all of these values suck, it sounds terrible lol

            float roomLengthMeters = 3;
            float volume = roomWidthMeters * roomLengthMeters * roomHeightMeters;
            volume = Math.Max(1.0f, volume);
            float surfaceArea = 2 * (roomWidthMeters * roomLengthMeters + roomWidthMeters * roomHeightMeters + roomLengthMeters * roomHeightMeters);
            surfaceArea = Math.Max(1.0f, surfaceArea); // Avoid division by zero
            float meanFreePath = (4 * volume / surfaceArea);

            // 1. Delay Times (based on size / mean free path in METERS)
            float reflectionsDelay = Math.Clamp(meanFreePath / 343.0f * 0.7f, 0.001f, 0.3f); // Slightly longer delay based on MFP
            float lateReverbDelay = Math.Clamp(reflectionsDelay * 0.6f, 0.001f, 0.1f);

            // 2. Decay Time (longer in bigger rooms, longer with reflective surfaces)
            float decayTimeBase = Math.Clamp(meanFreePath * 0.05f, 0.1f, 20.0f); // Simpler scaling based on MFP
            float decayTime = isMetal ? decayTimeBase * 1.8f : decayTimeBase * 0.8f; // Metal increases decay significantly
            decayTime = Math.Clamp(decayTime, 0.1f, 20.0f);

            // 3. Decay HF Ratio (Metal reflects HF well)
            float decayHfRatio = isMetal ? Math.Clamp(0.9f, 0.1f, 2.0f) : Math.Clamp(0.5f, 0.1f, 2.0f);

            // 4. Gain levels
            float targetBaseGain = 0.8f;
            // Reduce gain slightly if volume is very large (e.g., > 500 m^3)
            if (volume > 500) targetBaseGain *= (500.0f / volume); // Simple inverse scaling for large volumes
            targetBaseGain = Math.Clamp(targetBaseGain, 0.1f, 1.0f);

            float reflectionsGain = isMetal ? targetBaseGain * 1.5f : targetBaseGain * 0.8f; // Metal has stronger reflections
            reflectionsGain = Math.Clamp(reflectionsGain, 0.0f, 3.16f);

            float reverbGain = targetBaseGain;
            reverbGain = Math.Clamp(reverbGain, 0.0f, 1.0f);

            float lateReverbGain = isMetal ? targetBaseGain * 1.2f : targetBaseGain; // Also based on target, metal boosts slightly
            lateReverbGain = Math.Clamp(lateReverbGain, 0.0f, 10.0f);

            // Master Gain HF (Metal reflects HF well)
            float gainHf = isMetal ? Math.Clamp(0.95f, 0.0f, 1.0f) : Math.Clamp(0.7f, 0.0f, 1.0f);

            // 5. Diffusion & Density (Metal rooms less diffuse, maybe more coloration if small)
            float diffusion = isMetal ? Math.Clamp(0.7f, 0.0f, 1.0f) : Math.Clamp(1.0f, 0.0f, 1.0f);
            float density = (volume < 50.0f && isMetal) ? Math.Clamp(0.8f, 0.0f, 1.0f) : Math.Clamp(1.0f, 0.0f, 1.0f);

            // --- Apply Parameters to the Effect Object ---
            Al.GetError();
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DENSITY, density);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DIFFUSION, diffusion);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAIN, reverbGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAINHF, gainHf);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_TIME, decayTime);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFRATIO, decayHfRatio);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_GAIN, reflectionsGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_REFLECTIONS_DELAY, reflectionsDelay);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_GAIN, lateReverbGain);
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_LATE_REVERB_DELAY, lateReverbDelay);
            AlEffects.Effecti(reverbEffectId, AlEffects.AL_REVERB_DECAY_HFLIMIT, Al.True);
            int alError;
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to apply parameters for reverb effect ID: {reverbEffectId}, {Al.GetErrorString(alError)}");
        }
    }
}