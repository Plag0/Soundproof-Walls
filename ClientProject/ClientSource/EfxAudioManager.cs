using Barotrauma;
using Barotrauma.Sounds;
using OpenAL;

namespace SoundproofWalls
{
    public class EfxAudioManager : IDisposable
    {
        private const uint INVALID_ID = 0;
        private const float MIN_EQ_FREQ = 50.0f;    // Min cutoff freq for EQ (Hz)
        private const float MAX_EQ_FREQ = 8000.0f;   // Max useful cutoff freq for EQ (Hz) - Tweak based on testing
        private const float MUFFLE_SHAPE = 2.0f;     // Shape parameter for frequency formula (Higher = faster drop initially)

        // Periodic cleanup timing for unused EQ slots
        private double _lastEqSlotCheckTime = 0;
        private const double EQ_SLOT_CHECK_INTERVAL = 5.0; // Check every 5 seconds
        private const double UNUSED_EQ_SLOT_TIMEOUT = 10.0; // Delete slot if unused for 10 seconds

        // --- State for EQ Muffling ---
        private class MuffleSlotInfo // Renamed from HqMuffleSlotInfo for brevity
        {
            public uint SlotId { get; set; }
            public uint EqEffectId { get; set; } // AL_EFFECT_EQUALIZER used as lowpass
            public HashSet<uint> Users { get; set; } = new HashSet<uint>(); // sources using this slot
            public double LastUsedTime { get; set; }
            public int FrequencyKey { get; set; } // Store the target frequency this slot represents
        }

        private uint _indoorReverbSlotId = INVALID_ID;
        private uint _indoorReverbEffectId = INVALID_ID;
        private uint _outdoorReverbSlotId = INVALID_ID;
        private uint _outdoorReverbEffectId = INVALID_ID;

        // Maps source IDs to their lowpass filter ID
        private Dictionary<uint, uint> _sourceFilters = new Dictionary<uint, uint>();
        // Maps Target Cutoff Frequency (rounded int) to Slot Info
        private Dictionary<int, MuffleSlotInfo> _eqEffectSlots = new Dictionary<int, MuffleSlotInfo>();
        // Maps Source ID to its CURRENT Target Cutoff Frequency Key (0 = direct path active, >0 = EQ slot active)
        private Dictionary<uint, int> _sourceEqFrequencies = new Dictionary<uint, int>();

        public bool IsInitialized { get; private set; } = false;

        private double LastReverbUpdateTime = 0;
        private double ReverbUpdateInterval = 0.2f;

        private float AirReverbGain = ConfigManager.Config.DynamicReverbAirTargetGain;

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

            if (!InitReverbEffects()) { Dispose(); return; }

            _sourceFilters = new Dictionary<uint, uint>();
            _sourceEqFrequencies = new Dictionary<uint, int>();
            _eqEffectSlots = new Dictionary<int, MuffleSlotInfo>();

            IsInitialized = true;
            DebugConsole.NewMessage("[SoundproofWalls] DynamicFx initialization complete.");
        }

        private bool InitReverbEffects()
        {
            int alError;
            Al.GetError();

            // Create inside hull reverb slot.
            uint[] slots = new uint[1];
            AlEffects.GenAuxiliaryEffectSlots(1, slots);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate main slot, {Al.GetErrorString(alError)}");
                return false;
            }
            _indoorReverbSlotId = slots[0];

            uint[] effects = new uint[1];
            AlEffects.GenEffects(1, effects);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate effect for main slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }
            _indoorReverbEffectId = effects[0];

            AlEffects.Effecti(_indoorReverbEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_REVERB);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect ID {_indoorReverbEffectId} type to reverb for main slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            UpdateReverbEffectForRoom(_indoorReverbEffectId);

            AlEffects.AuxiliaryEffectSloti(_indoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _indoorReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach initial reverb effect ID {_indoorReverbEffectId} to main slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }


            // Create outside reverb slot.
            AlEffects.GenAuxiliaryEffectSlots(1, slots);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate secondary slot, {Al.GetErrorString(alError)}");
                return false;
            }
            _outdoorReverbSlotId = slots[0];

            AlEffects.GenEffects(1, effects);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate effect for secondary slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }
            _outdoorReverbEffectId = effects[0];

            AlEffects.Effecti(_outdoorReverbEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_REVERB);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect ID {_outdoorReverbEffectId} type to reverb for secondary slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            UpdateReverbEffectForWater(_outdoorReverbEffectId);

            AlEffects.AuxiliaryEffectSloti(_outdoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _outdoorReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach initial reverb effect ID {_outdoorReverbEffectId} to main slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");
                return false;
            }

            return true;
        }

        public void Update()
        {
            if (!ConfigManager.Config.DynamicFx) { return; }

            UpdatePooledSlots();

            // Update reverb effects.
            if (Timing.TotalTime < LastReverbUpdateTime + ReverbUpdateInterval) { return; }
            LastReverbUpdateTime = Timing.TotalTime;
            UpdatePrimaryEnvironment();
            UpdateSecondaryEnvironment();
        }

        /// <summary>
        /// Periodically checks for unused EQ Muffle slots and cleans them up.
        /// </summary>
        public void UpdatePooledSlots()
        {
            if (!IsInitialized || Timing.TotalTime < _lastEqSlotCheckTime + EQ_SLOT_CHECK_INTERVAL) return;
            _lastEqSlotCheckTime = Timing.TotalTime;

            List<int> keysToDelete = new List<int>();
            foreach (var kvp in _eqEffectSlots)
            {
                if (kvp.Value.Users.Count == 0 && Timing.TotalTime > kvp.Value.LastUsedTime + UNUSED_EQ_SLOT_TIMEOUT)
                {
                    keysToDelete.Add(kvp.Key);
                }
            }
            
            foreach (int freqKey in keysToDelete)
            {
                if (!_eqEffectSlots.TryGetValue(freqKey, out MuffleSlotInfo? slotInfo)) { continue; }

                int alError;
                Al.GetError();
                if (slotInfo.SlotId != INVALID_ID)
                {
                    AlEffects.AuxiliaryEffectSloti(slotInfo.SlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
                    AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { slotInfo.SlotId });
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to cleanup expired EQ slot ID: {slotInfo.SlotId}, {Al.GetErrorString(alError)}");
                }
                if (slotInfo.EqEffectId != INVALID_ID)
                {
                    AlEffects.DeleteEffects(1, new[] { slotInfo.EqEffectId });
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to cleanup expired EQ effect ID: {slotInfo.EqEffectId}, {Al.GetErrorString(alError)}");
                }

                _eqEffectSlots.Remove(freqKey);
            }
        }

        public void UpdatePrimaryEnvironment()
        {
            if (!IsInitialized ||
                _indoorReverbEffectId == INVALID_ID ||
                _indoorReverbSlotId == INVALID_ID)
                return;

            UpdateReverbEffectForRoom(_indoorReverbEffectId);

            int alError;
            Al.GetError();

            AlEffects.AuxiliaryEffectSloti(_indoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _indoorReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach reverb effect {_indoorReverbEffectId} to main slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");
        }

        public void UpdateSecondaryEnvironment()
        {
            if (!IsInitialized ||
                _outdoorReverbEffectId == INVALID_ID ||
                _outdoorReverbSlotId == INVALID_ID)
                return;

            UpdateReverbEffectForWater(_outdoorReverbEffectId);

            int alError;
            Al.GetError();

            AlEffects.AuxiliaryEffectSloti(_outdoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, _outdoorReverbEffectId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach reverb effect {_outdoorReverbEffectId} to secondary slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");
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

            _sourceEqFrequencies[sourceId] = 0;
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
                
            // Disconnect from EQ effect slot
            DisconnectSourceFromEqSlot(sourceId);

            // Detach direct filter
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach direct filter from source ID: {sourceId}, {Al.GetErrorString(alError)}");

            // Delete the filter
            if (filterId != INVALID_ID)
            {
                AlEffects.DeleteFilters(1, new[] { filterId });
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete filter ID: {filterId}, {Al.GetErrorString(alError)}");
            }

            // Remove from dictionaries
            _sourceFilters.Remove(sourceId);
            _sourceEqFrequencies.Remove(sourceId);
        }

        private void DisconnectSourceFromEqSlot(uint sourceId)
        {
            if (!IsInitialized) return;

            if (_sourceEqFrequencies.TryGetValue(sourceId, out int eqFrequencyKey) && eqFrequencyKey > 0)
            {
                // Remove source from the user list of that slot
                if (_eqEffectSlots.TryGetValue(eqFrequencyKey, out MuffleSlotInfo? slotInfo))
                {
                    slotInfo.Users.Remove(sourceId);
                    slotInfo.LastUsedTime = Timing.TotalTime; // Mark slot as recently active
                }

                int alError;
                Al.GetError();

                Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, AlEffects.AL_EFFECTSLOT_NULL, 0, AlEffects.AL_FILTER_NULL);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to disconnect send 0 for source ID: {sourceId}, {Al.GetErrorString(alError)}");

                _sourceEqFrequencies[sourceId] = 0;
            }
        }

        /// <summary>
        /// Calculates the target frequency key (rounded integer Hz) based on muffle strength.
        /// Returns 0 if muffleStrength is negligible.
        /// </summary>
        private int CalculateFrequencyKey(float muffleStrength)
        {
            if (muffleStrength < 0.01f || !ConfigManager.Config.HighFidelityMuffling) return 0;
            muffleStrength = Math.Clamp(muffleStrength, 0.0f, 1.0f);
            // f_c = f_min * (f_max / f_min)^(1 - (1 - muffleStrength)^k)
            float ratio = MAX_EQ_FREQ / MIN_EQ_FREQ;
            if (ratio < 1.0f) ratio = 1.0f;
            float exponentTerm = 1.0f - (float)Math.Pow(muffleStrength, MUFFLE_SHAPE);
            float cutoffHz = MIN_EQ_FREQ * (float)Math.Pow(ratio, exponentTerm);
            return (int)Math.Round(Math.Clamp(cutoffHz, MIN_EQ_FREQ, MAX_EQ_FREQ));
        }

        public void UpdateSource(uint sourceId, bool inHull, bool ignoreReverb, float muffleStrength, float gainHf, float gainLf = 1)
        {
            if (!IsInitialized || !_sourceFilters.TryGetValue(sourceId, out uint filterId)) { return; }
            
            // Select which reverb environment to route to.
            RouteSourceToReverbSlot(sourceId, DetermineTargetReverbSlot(inHull, ignoreReverb));

            int targetFreq = CalculateFrequencyKey(muffleStrength);

            bool useEqMuffling = targetFreq > 0;
            if (!useEqMuffling) 
            {
                DisconnectSourceFromEqSlot(sourceId);
                UpdateSourceLowpassFilter(sourceId, filterId, gainHf, gainLf);
                return; 
            }

            _sourceEqFrequencies.TryGetValue(sourceId, out int currentFreq);
            if (targetFreq == currentFreq) { return; }

            DisconnectSourceFromEqSlot(sourceId); // Disconnect Send 0, remove from Users set, sets state to 0

            MuffleSlotInfo? slotInfo = GetOrCreateEqSlot(targetFreq);
            if (slotInfo != null)
            {
                UpdateSourceLowpassFilter(sourceId, filterId, 1, 0); // Silence Direct Path

                int alError;
                Al.GetError();
                Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, (int)slotInfo.SlotId, 0, AlEffects.AL_FILTER_NULL); // Route Send 0 to EQ
                if ((alError = Al.GetError()) != Al.NoError)
                {
                    DebugConsole.NewMessage($"[SoundproofWalls] Failed to route source to target EQ slot ID: {slotInfo.SlotId}, {Al.GetErrorString(alError)}");
                    UpdateSourceLowpassFilter(sourceId, filterId, gainHf, gainLf); // Restore direct path
                    return;
                }
                slotInfo.Users.Add(sourceId);
                slotInfo.LastUsedTime = Timing.TotalTime;
                _sourceEqFrequencies[sourceId] = targetFreq;
            }
            else
            {
                // Failed to get/create slot
                DebugConsole.NewMessage($"[SoundproofWalls] Failed get/create EQ slot for freq {targetFreq}. Falling back to lowpass filters for source ID: {sourceId}");
                UpdateSourceLowpassFilter(sourceId, filterId, gainHf, gainLf); // Restore direct path
            }
        }

        private void UpdateSourceLowpassFilter(uint sourceId, uint filterId, float gainHf, float gainLf, bool disableFilter = false)
        {
            int alError;
            Al.GetError();

            if (disableFilter)
            {
                Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, AlEffects.AL_FILTER_NULL);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to remove filter from source ID {sourceId}, {Al.GetErrorString(alError)}");
                return;
            }

            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAIN, gainLf);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAIN for source {sourceId}, {Al.GetErrorString(alError)}");
            AlEffects.Filterf(filterId, AlEffects.AL_LOWPASS_GAINHF, gainHf);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to update filter {filterId} param AL_LOWPASS_GAINHF for source {sourceId}, {Al.GetErrorString(alError)}");

            // Re-attach filter.
            Al.Sourcei(sourceId, AlEffects.AL_DIRECT_FILTER, (int)filterId);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to re-attach filter {filterId} to source {sourceId}, {Al.GetErrorString(alError)}");
        }

        /// <summary>
        /// Routes a source's auxiliary send 1 to the appropriate reverb slot.
        /// </summary>
        private void RouteSourceToReverbSlot(uint sourceId, uint targetSlot)
        {
            if (!IsInitialized) return;

            Al.GetError();
            int alError;

            Al.Source3i(sourceId, AlEffects.AL_AUXILIARY_SEND_FILTER, (int)targetSlot, 1, AlEffects.AL_FILTER_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to route source to target slot ID: {targetSlot}, {Al.GetErrorString(alError)}");
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
            _sourceEqFrequencies.Clear();

            // Cleanup EQ slots and effects.
            var keysToClean = new List<int>(_eqEffectSlots.Keys);
            foreach (int freqKey in keysToClean)
            {
                if (!_eqEffectSlots.TryGetValue(freqKey, out MuffleSlotInfo? slotInfo)) { continue; }

                if (slotInfo.SlotId != INVALID_ID)
                {
                    AlEffects.AuxiliaryEffectSloti(slotInfo.SlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
                    AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { slotInfo.SlotId });
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to cleanup expired EQ slot ID: {slotInfo.SlotId}, {Al.GetErrorString(alError)}");
                }
                if (slotInfo.EqEffectId != INVALID_ID)
                {
                    AlEffects.DeleteEffects(1, new[] { slotInfo.EqEffectId });
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to cleanup expired EQ effect ID: {slotInfo.EqEffectId}, {Al.GetErrorString(alError)}");
                }
            }
            _eqEffectSlots.Clear();

            // Cleanup reverb slots and effects.
            // Detach effects from slots
            if (_indoorReverbSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_indoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach reverb effect from main slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");

            if (_outdoorReverbSlotId != INVALID_ID) AlEffects.AuxiliaryEffectSloti(_outdoorReverbSlotId, AlEffects.AL_EFFECTSLOT_EFFECT, AlEffects.AL_EFFECT_NULL);
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to detach reverb effect from secondary slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");

            // Delete Slots
            if (_indoorReverbSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _indoorReverbSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete main slot ID: {_indoorReverbSlotId}, {Al.GetErrorString(alError)}");

            if (_outdoorReverbSlotId != INVALID_ID) AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { _outdoorReverbSlotId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete secondary slot ID: {_outdoorReverbSlotId}, {Al.GetErrorString(alError)}");

            // Delete Effects
            if (_indoorReverbEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _indoorReverbEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete main reverb effect ID: {_indoorReverbEffectId}, {Al.GetErrorString(alError)}");

            if (_outdoorReverbEffectId != INVALID_ID) AlEffects.DeleteEffects(1, new[] { _outdoorReverbEffectId });
            if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to delete secondary reverb effect ID: {_outdoorReverbEffectId}, {Al.GetErrorString(alError)}");

            // Reset IDs
            _indoorReverbSlotId = INVALID_ID;
            _indoorReverbEffectId = INVALID_ID;
            _outdoorReverbSlotId = INVALID_ID;
            _outdoorReverbEffectId = INVALID_ID;

            DebugConsole.NewMessage($"[SoundproofWalls] EfxAudioManager disposed successfully");
        }

        public void UpdateReverbEffectForRoom(uint reverbEffectId)
        {
            if (!AlEffects.IsInitialized) return;

            // Calculate the strength of the reverb effect based on aggregated looping sound amplitude, gain, and player submersion.
            SoundChannel[] playingChannels = GameMain.SoundManager.playingChannels[0]; // Index 0 is sounds while 1 is voice (cite SourcePoolIndex enum) yeah I guess I'm citing stuff in code comments now...
            float totalAudioAmplitude = 0;
            for (int i = 0; i < playingChannels.Length; i++)
            {
                if (playingChannels[i] != null && playingChannels[i].IsPlaying)
                {
                    if (playingChannels[i].Looping && SoundInfoManager.TryGetSoundInfo(playingChannels[i], out SoundInfo? info) && info != null && !info.Ignored)
                    {
                        float sidechainMult = Plugin.Sidechain.SidechainMultiplier;
                        if (sidechainMult >= 1) { sidechainMult = 0.99f; }

                        float gain = playingChannels[i].Gain;
                        if (gain <= 0) { gain = 0.01f; }

                        gain /= 1 - sidechainMult; // Undo gain adjustments from sidechain multiplier for audio amplitude calculation.

                        float amplitude = playingChannels[i].CurrentAmplitude;

                        float muffleMult = 1 - info.MuffleStrength;

                        //LuaCsLogger.Log($"{Path.GetFileName(playingChannels[i].Sound.Filename)} gain: {gain} * amplitude {amplitude} * muffleMult {muffleMult} = totalAudioAmplitude {totalAudioAmplitude}");
                        totalAudioAmplitude += amplitude * gain * muffleMult;
                    }
                }
            }
            float amplitudeMult = 1f - MathUtils.InverseLerp(0.0f, 1.4f, totalAudioAmplitude);
            float submersionMult = Listener.IsSubmerged ? 1.3f : 1.0f;
            float targetReverbGain = ConfigManager.Config.DynamicReverbAirTargetGain * amplitudeMult * submersionMult;

            targetReverbGain = Math.Clamp(targetReverbGain, 0.0f, 1.0f);

            float gainDiff = targetReverbGain - AirReverbGain;
            AirReverbGain += Math.Abs(gainDiff) < 0.01f ? gainDiff : Math.Sign(gainDiff) * 0.01f;

            // Influences decay and delay times.
            float roomSizeFactor = Listener.ConnectedArea / 180000; // Arbitrary magic number that seems to work well.

            //LuaCsLogger.Log($" targetReverbGain: {targetReverbGain} actualReverbGain: {ReverbGain} playingChannelMult: {playingChannelMult} totalAudioAmplitude: {totalAudioAmplitude}");

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
            float lateReverbGain = AirReverbGain * 1.8f; // Make tail prominent relative to overall gain
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
            AlEffects.Effectf(reverbEffectId, AlEffects.AL_REVERB_GAIN, AirReverbGain);
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

        public void UpdateReverbEffectForWater(uint reverbEffectId)
        {
            if (!AlEffects.IsInitialized) return;

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

        /// <summary>
        /// Determines the correct reverb slot ID based on environment and config flags.
        /// </summary>
        private uint DetermineTargetReverbSlot(bool sourceInHull, bool ignoreReverb)
        {
            if (ignoreReverb || !ConfigManager.Config.DynamicReverbEnabled) { return INVALID_ID; }
            
            return sourceInHull ? _indoorReverbSlotId : _outdoorReverbSlotId;
        }

        /// <summary>
        /// Configures the EQ effect to simulate a low-pass filter with a target cutoff frequency (targetFrequency).
        /// </summary>
        /// <param name="eqEffectId">The ID of the AL_EFFECT_EQUALIZER object to modify.</param>
        /// <param name="targetFrequency">The target cutoff frequency (Hz), rounded to the nearest integer.</param>
        private void ConfigureEqForFrequency(uint eqEffectId, int targetFrequency)
        {
            if (eqEffectId == INVALID_ID || targetFrequency <= 0) return;

            // Make sure we're in the required range.
            int freq = Math.Clamp(targetFrequency, 50, 8000);

            // Constants.
            const float minGain = 0.126f; // Minimum gain (~ -18dB) according to EFX Guide
            const float neutralGain = 1.0f;

            // Synthetic range that fits internal limts.
            const float mid1StartFreq = 200f;
            const float mid2StartFreq = 2000f;

            // Cutoffs.
            float targetLowCutoff = 50f;
            float targetHighCutoff = 8000f;

            // Centers.
            float mid1CenterFreq = 500;
            float mid1Width = 1.0f;
            float mid2CenterFreq = 3000f;
            float mid2Width = 1.0f;

            // Determine gain for each band based on where the frequency falls.
            float lowGain = neutralGain;
            float mid1Gain = neutralGain;
            float mid2Gain = neutralGain;
            float highGain = minGain;
            if (freq < mid1StartFreq)
            {
                mid1CenterFreq = 200f;
                mid1Gain = minGain;
                mid2Gain = minGain;
            }
            else if (freq < mid2StartFreq)
            {
                mid1CenterFreq = freq;
                mid1Gain = minGain;
                mid2Gain = minGain;
            }
            else
            {
                mid2CenterFreq = freq;
                mid2Gain = minGain;
            }

            // Apply EQ parameters
            Al.GetError();
            int alError;

            // Low Band
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_LOW_GAIN, lowGain);
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_LOW_CUTOFF, targetLowCutoff);

            // Mid1 Band
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_MID1_GAIN, mid1Gain);
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_MID1_CENTER, mid1CenterFreq);
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_MID1_WIDTH, mid1Width);

            // Mid2 Band
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_MID2_GAIN, mid2Gain);
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_MID2_CENTER, mid2CenterFreq);
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_MID2_WIDTH, mid2Width);

            // High Band
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_HIGH_GAIN, highGain);
            AlEffects.Effectf(eqEffectId, AlEffects.AL_EQUALIZER_HIGH_CUTOFF, targetHighCutoff);

            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to edit properties for EQ effect ID: {eqEffectId}, {Al.GetErrorString(alError)}");
            }
        }

        /// <summary>
        /// Gets existing or tries to create a new Muffle Slot/Effect for a given frequency key.
        /// Returns null if creation fails.
        /// </summary>
        private MuffleSlotInfo? GetOrCreateEqSlot(int frequencyKey)
        {
            if (frequencyKey <= 0 || !ConfigManager.Config.HighFidelityMuffling) return null;

            if (_eqEffectSlots.TryGetValue(frequencyKey, out MuffleSlotInfo? slotInfo))
            {
                slotInfo.LastUsedTime = Timing.TotalTime;
                return slotInfo; // Return existing
            }

            int alError;
            Al.GetError();

            uint[] slots = new uint[1];
            AlEffects.GenAuxiliaryEffectSlots(1, slots);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate EQ effect slot, {Al.GetErrorString(alError)}");
                return null;
            }
            uint slotId = slots[0];

            uint[] effects = new uint[1];
            AlEffects.GenEffects(1, effects);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to generate effect for EQ slot ID: {slotId}, {Al.GetErrorString(alError)}");
                AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { slotId });
                return null;
            }
            uint eqEffectId = effects[0];
            
            AlEffects.Effecti(eqEffectId, AlEffects.AL_EFFECT_TYPE, AlEffects.AL_EFFECT_EQUALIZER);
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to set effect ID {eqEffectId} type to EQ for slot ID: {slotId}, {Al.GetErrorString(alError)}");
                AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { slotId });
                AlEffects.DeleteEffects(1, new[] { eqEffectId });
                return null;
            }

            ConfigureEqForFrequency(eqEffectId, frequencyKey);

            AlEffects.AuxiliaryEffectSloti(slotId, AlEffects.AL_EFFECTSLOT_EFFECT, eqEffectId); // CRASH HERE
            if ((alError = Al.GetError()) != Al.NoError)
            {
                DebugConsole.NewMessage($"[SoundproofWalls] Failed to attach EQ effect ID {eqEffectId} to EQ slot ID: {slotId}, {Al.GetErrorString(alError)}");
                AlEffects.DeleteAuxiliaryEffectSlots(1, new[] { slotId });
                AlEffects.DeleteEffects(1, new[] { eqEffectId });
                return null;
            }

            slotInfo = new MuffleSlotInfo { SlotId = slotId, EqEffectId = eqEffectId, FrequencyKey = frequencyKey, LastUsedTime = Timing.TotalTime };
            _eqEffectSlots[frequencyKey] = slotInfo;
            return slotInfo;
        }
    }
}