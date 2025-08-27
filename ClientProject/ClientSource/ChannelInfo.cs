using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using OpenAL;

namespace SoundproofWalls
{
    public enum Obstruction
    {
        WaterSurface,
        WaterBody,
        WallThick,
        WallThin,
        DoorThick,
        DoorThin,
        Suit,
        Drowning
    }

    public class ChannelInfo
    {
        private enum MuffleType
        {
            None,
            Light,
            Medium,
            Heavy
        }

        private enum DistanceModel
        {
            OpenAL,
            Approximate,
            Euclidean
        }

        // The sound being played.
        public SoundChannel Channel;

        public string LongName;
        public string ShortName;

        public int DebugObstructionsCount = 0;
        public string DebugObstructionsList = "          Ignored\n";

        public float MuffleStrength;

        private float startGain;
        private float startPitch;

        private float gainMultHF;
        private float trailingGainMultHF;
        private float gainMultLF;
        private float trailingGainMultLF;

        private bool isFirstIteration;
        private bool isLooseSound; // Sound that is not attached to anything. Has no item or character parent. We don't want these sounds updating their hull because they have no way to move.
        private Gap? ignoredGap = null;
        private List<Obstruction> obstructions = new List<Obstruction>();

        // Get this information on the main thread for voice channels.
        public IEnumerable<Body> VoiceOcclusions = new List<Body>();
        public readonly object VoiceOcclusionsLock = new object();
        public List<SoundPathfinder.PathfindingResult> VoicePathResults = new List<SoundPathfinder.PathfindingResult>();
        public readonly object VoicePathResultsLock = new object();
        public double LastVoiceMainThreadUpdateTime = 0;
        public double VoiceMainThreadUpdateInterval = config.VoiceMuffleUpdateInterval;

        public float EuclideanDistance;
        private float? approximateDistance;
        public float Distance => approximateDistance ?? EuclideanDistance;

        public Vector2 WorldPos;
        public Vector2 LocalPos;
        public Vector2 SimPos;

        private bool inWater;
        private bool bothInWater;
        private bool inContainer;
        private bool noPath;

        private DistanceModel distanceModel = DistanceModel.OpenAL;

        private MuffleType muffleType;
        private MuffleType lastMuffleType;
        public bool NoMuffle => muffleType != MuffleType.None;
        public bool LightMuffle => muffleType == MuffleType.Light;
        public bool MediumMuffle => muffleType == MuffleType.Medium;
        public bool HeavyMuffle => muffleType == MuffleType.Heavy;

        public SoundInfo.AudioType DynamicType;
        public bool AudioIsFromDoor => DynamicType == SoundInfo.AudioType.DoorSound;
        public bool AudioIsLocalVoice => DynamicType == SoundInfo.AudioType.LocalVoice;
        public bool AudioIsRadioVoice => DynamicType == SoundInfo.AudioType.RadioVoice;
        public bool AudioIsVoice => AudioIsLocalVoice || AudioIsRadioVoice;
        public bool AudioIsAmbience => DynamicType == SoundInfo.AudioType.AmbientSound;
        public bool AudioIsFlow => DynamicType == SoundInfo.AudioType.FlowSound;
        public bool AudioIsFire => DynamicType == SoundInfo.AudioType.FireSound;

        private readonly bool dontMuffleRecommendation = false; // Passed in via constructor. i.e., if a sound has dontMuffle in their xml file

        // For assigning more specific/niche rules dynamically without including them all in the ignoreX conditions below
        private bool skipMuffle = false;
        private bool skipPitch = false;
        private bool skipReverb = false;

        private bool ignoreLowpass => SoundInfo.IgnoreLowpass || (dontMuffleRecommendation && !SoundInfo.IgnoreXML) || skipMuffle;
        private bool ignorePitch => SoundInfo.IgnorePitch || skipPitch;
        public bool IgnoreWaterReverb => (SoundInfo.IgnoreWaterReverb && !SoundInfo.ForceReverb) || Listener.IsSpectating || skipReverb;
        public bool IgnoreAirReverb => (SoundInfo.IgnoreAirReverb && !SoundInfo.ForceReverb) || Listener.IsSpectating || skipReverb;
        public bool Ignored => SoundInfo.IgnoreAll || (ignoreLowpass && ignorePitch);
        public bool IsLoud => SoundInfo.IsLoud;
        public bool IsInUpdateLoop => Channel.Looping || AudioIsVoice || config.UpdateNonLoopingSounds;

        private int flowFireChannelIndex;

        public bool StaticShouldUseReverbBuffer = false; // For extended sounds only.
        public bool StaticIsUsingReverbBuffer = false; // For extended sounds only.

        private bool sidechainTriggered = false; // Flag for non looping sounds.
        private bool propagated = false;
        public bool Eavesdropped = false;
        public bool Hydrophoned = false;

        private double muffleUpdateInterval = Timing.Step;
        private double lastMuffleCheckTime = 0;

        public Hull? ChannelHull = null;
        public SoundInfo SoundInfo;
        public Character? ChannelCharacter = null; // Mostly for footsteps.
        public StatusEffect? StatusEffect = null;
        public ItemComponent? ItemComp = null;
        public Item? Item = null;
        public Client? SpeakingClient = null;
        public ChatMessageType? MessageType = null;

        private bool rolloffFactorModified = false;
        private float rolloffFactor
        {
            get
            {
                try
                {
                    if (Channel.mutex != null) { Monitor.Enter(Channel.mutex); }

                    float rolloff = 1;
                    if (Channel.ALSourceIndex >= 0)
                    {
                        uint sourceId = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
                        int alError;
                        Al.GetError();
                        Al.GetSourcef(sourceId, Al.RolloffFactor, out rolloff);
                        if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to get rolloff factor for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                    }
                    return rolloff;
                }
                finally
                {
                    if (Channel.mutex != null) { Monitor.Exit(Channel.mutex); }
                }
            }
            set
            {
                try
                {
                    if (Channel.mutex != null) { Monitor.Enter(Channel.mutex); }

                    if (Channel.ALSourceIndex >= 0)
                    {
                        uint sourceId = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
                        int alError;
                        Al.GetError();
                        Al.Sourcef(sourceId, Al.RolloffFactor, value);
                        if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to set rolloff factor of {value} for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                    }
                }
                finally
                {
                    if (Channel.mutex != null) { Monitor.Exit(Channel.mutex); }
                }
            }
        }

        public int PlaybackPosition
        {
            get
            {
                int playbackPos = 0;
                if (Channel.ALSourceIndex >= 0 && !Channel.IsStream)
                {
                    uint sourceId = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
                    int alError;
                    Al.GetError();
                    Al.GetSourcei(sourceId, Al.SampleOffset, out playbackPos);
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to get playback position for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                }
                return playbackPos;
            }
        }

        public float Gain
        {
            get { return Channel.gain; }
            set
            {
                try
                {
                    if (Channel.mutex != null) { Monitor.Enter(Channel.mutex); }
                    float currentGain = Channel.gain;
                    float targetGain = value;

                    if (targetGain != currentGain)
                    {
                        Channel.Gain = value;
                    }
                }
                finally
                {
                    if (Channel.mutex != null) { Monitor.Exit(Channel.mutex); }
                }
            }
        }
        public float Pitch
        {
            get { return Channel.frequencyMultiplier; }
            set
            {
                try
                {
                    if (Channel.mutex != null) { Monitor.Enter(Channel.mutex); }

                    float currentFrequency = Pitch;
                    float targetFrequency = Math.Clamp(value, 0.25f, 4f);

                    if (targetFrequency != currentFrequency)
                    {
                        Channel.FrequencyMultiplier = targetFrequency;

                        if (targetFrequency == 1)
                        {
                            ChannelInfoManager.RemovePitchedChannel(Channel);
                        }
                        else
                        {
                            ChannelInfoManager.AddPitchedChannel(Channel);
                        }
                    }
                }
                finally
                {
                    if (Channel.mutex != null) { Monitor.Exit(Channel.mutex); }
                }
            }
        }
        public bool Muffled
        {
            get { return Channel.muffled; }
            set
            {
                try
                {
                    if (Channel.mutex != null) { Monitor.Enter(Channel.mutex); }

                    bool currentMuffle = Muffled;
                    bool targetMuffle = value && !ignoreLowpass;

                    if (targetMuffle != currentMuffle || muffleType != lastMuffleType || (config.StaticFx && StaticIsUsingReverbBuffer != StaticShouldUseReverbBuffer))
                    {
                        Channel.Muffled = targetMuffle;
                        lastMuffleType = muffleType;
                    }
                }
                finally
                {
                    if (Channel.mutex != null) { Monitor.Exit(Channel.mutex); }
                }
            }
        }

        private bool voiceUseMuffleFilter 
        { 
            set 
            { 
                if (SpeakingClient != null && SpeakingClient.VoipSound != null) 
                { 
                    SpeakingClient.VoipSound.UseMuffleFilter = value; 
                } 
            } 
        }

        private bool voiceUseRadioFilter
        {
            set
            {
                if (SpeakingClient != null && SpeakingClient.VoipSound != null)
                {
                    SpeakingClient.VoipSound.UseRadioFilter = value;
                }
            }
        }

        private static Config config => ConfigManager.Config;

        public ChannelInfo(SoundChannel channel, Hull? channelHull = null, StatusEffect? statusEffect = null, ItemComponent? itemComp = null, Client? speakingClient = null, ChatMessageType? messageType = null, bool dontMuffle = false)
        {
            if (channel.Sound is not VoipSound) { PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.ChannelInfoUpdate); }

            if (speakingClient != null && messageType == null) { messageType = Util.GetMessageType(speakingClient); }
            else if (itemComp != null && Item == null) { Item = itemComp.Item; }
            else if (statusEffect != null && Item == null) { Item = statusEffect.soundEmitter as Item; }

            if (Item != null && channelHull == null) { channelHull = Item.CurrentHull; }
            else if (speakingClient?.Character != null && channelHull == null) { channelHull = speakingClient.Character.CurrentHull; }

            dontMuffleRecommendation = dontMuffle;

            Channel = channel;
            LongName = channel.Sound.Filename.ToLower();
            ShortName = Path.GetFileName(LongName);
            startGain = channel.Gain;
            startPitch = channel.FrequencyMultiplier;
            isFirstIteration = true;
            ChannelHull = channelHull;
            ItemComp = itemComp;
            StatusEffect = statusEffect;
            SpeakingClient = speakingClient;
            MessageType = messageType;

            SoundInfo = SoundInfoManager.EnsureGetSoundInfo(channel.Sound);

            lastMuffleType = MuffleType.None;

            if (speakingClient != null)
            {
                ChannelCharacter = speakingClient.Character;
            }
            else if (ShortName.ToLower().Contains("footstep"))
            {
                Character? closestCharacter = null;
                float closestDist = float.PositiveInfinity;
                Vector2 worldPos = Util.GetSoundChannelWorldPos(Channel);
                foreach (Character c in Character.CharacterList)
                {
                    if (c == Character.Controlled || c.IsDead || !c.Enabled || c.Removed) { continue; }

                    float dist = Vector2.DistanceSquared(worldPos, c.WorldPosition);
                    if (dist < closestDist)
                    {
                        closestCharacter = c;
                        closestDist = dist;
                    }
                }

                ChannelCharacter = closestCharacter;
            }

            if (!SoundInfo.IgnoreAll)
            {

                if (config.DynamicFx && Plugin.EffectsManager != null)
                {
                    uint sourceId = this.Channel.Sound.Owner.GetSourceFromIndex(this.Channel.Sound.SourcePoolIndex, this.Channel.ALSourceIndex);
                    Plugin.EffectsManager.RegisterSource(sourceId);
                }
            }

            // Kinda just easier to leave this here.
            channel.Sound.MaxSimultaneousInstances = ConfigManager.Config.MaxSimultaneousInstances;

            Update(channelHull, statusEffect, itemComp, speakingClient, messageType);
            isFirstIteration = false;

            ModStateManager.State.LifetimeSoundsPlayed++;

            if (channel.Sound is not VoipSound) { PerformanceProfiler.Instance.StopTimingEvent(); }
        }

        // This method takes arguments because sometimes the constructor is called without any of this info available (created from soundchannel ctor with no contex)
        public void Update(Hull? soundHull = null, StatusEffect? statusEffect = null, ItemComponent? itemComp = null, Client? speakingClient = null, ChatMessageType? messageType = null)
        {
            if (!AudioIsVoice) { PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.ChannelInfoUpdate); }

            if (Channel == null) { return; }

            if (speakingClient != null && messageType == null) { messageType = Util.GetMessageType(speakingClient); }
            else if (itemComp != null && Item == null) { Item = itemComp.Item; }
            else if (statusEffect != null && Item == null) { Item = statusEffect.soundEmitter as Item; }
            
            if (Item != null && soundHull == null) { soundHull = Item.CurrentHull; }
            else if (speakingClient?.Character != null && soundHull == null) { soundHull = speakingClient.Character.CurrentHull; }

            // Only update the channel hull for sounds that have a parent like an item or character
            isLooseSound = !isFirstIteration && Item == null && SpeakingClient == null;
            if (!isLooseSound) { ChannelHull = soundHull; }

            ItemComp = itemComp;
            StatusEffect = statusEffect;
            SpeakingClient = speakingClient;
            MessageType = messageType;

            UpdateProperties();

            if (Ignored)
            {
                EuclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos);
                Muffled = false;
                Pitch = 1;
                UpdateGain();
                UpdateVoice();
                return;
            }

            if (!ShouldSkipMuffleUpdate() || isFirstIteration)
            {
                UpdateMuffle();
            }

            UpdateDynamicFx();
            UpdateGain();
            UpdatePitch();
            UpdateVoice();

            if (!AudioIsVoice) { PerformanceProfiler.Instance.StopTimingEvent(); }
        }

        private void UpdateVoice()
        {
            if (SpeakingClient != null)
            {
                voiceUseMuffleFilter = Muffled && SpeakingClient.Character != null && !SpeakingClient.Character.IsDead;
                voiceUseRadioFilter = MessageType == ChatMessageType.Radio && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters && SpeakingClient.Character != null && !SpeakingClient.Character.IsDead;
            }
        }

        private void UpdateProperties()
        {
            skipMuffle = false;
            skipPitch = false;
            skipReverb = false;

            // We have to set some types here at the Channel level because some data is inaccessible on the Sound level.
            // We try to get these types every update because the data is not available from the first 1-2 updates.
            // TODO if more types are added, use !IsFirstIteration and a flag to only run the code once. Right now it doesn't matter
            DynamicType = SoundInfo.StaticType;
            if (SpeakingClient != null && MessageType != null)
            {
                DynamicType = (MessageType == ChatMessageType.Radio) ? SoundInfo.AudioType.RadioVoice : SoundInfo.AudioType.LocalVoice;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.flowSoundChannels, Channel)) >= 0)
            {
                DynamicType = SoundInfo.AudioType.FlowSound;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.fireSoundChannels, Channel)) >= 0)
            {
                DynamicType = SoundInfo.AudioType.FireSound;
            }
            
            if (StatusEffect != null && !config.PitchStatusEffectSounds)
            {
                skipPitch = true;
            }

            Character? speakerCharacter = null;
            Limb? speakerMouth = null;
            if (AudioIsVoice)
            {
                speakerCharacter = SpeakingClient?.Character;
                speakerMouth = speakerCharacter?.AnimController?.GetLimb(LimbType.Head) ?? speakerCharacter?.AnimController?.GetLimb(LimbType.Torso);
                bool speakerIsAlive = speakerCharacter != null && !speakerCharacter.IsDead;

                if (AudioIsRadioVoice) { skipReverb = !config.VoiceRadioReverb; }
                else if (AudioIsLocalVoice) { skipReverb = !config.VoiceLocalReverb; }

                // Early return. No point updating anything for dead (spectating/menu) speakers.
                if (!speakerIsAlive) { inWater = false; bothInWater = false; skipMuffle = true; skipPitch = true; skipReverb = true; return; }
            }

            // Don't update position for non looping sounds because they shouldn't be moving.
            // Note: all looping sounds are considered non-looping for their first update if their ChannelInfo instance was created early enough, e.g, from the SoundChannel ctor. That's why I have the default comparison.
            if (Channel.Looping || LocalPos == default || speakerCharacter != null)
            {

                WorldPos = speakerMouth?.WorldPosition ?? Util.GetSoundChannelWorldPos(Channel);
                ChannelHull = ChannelHull ?? Hull.FindHull(WorldPos, speakerCharacter?.CurrentHull ?? Listener.CurrentHull);
                LocalPos = Util.LocalizePosition(WorldPos, ChannelHull);
                SimPos = LocalPos / 100;
            }

            // Put door sounds at the top of the gap instead of the middle
            if (AudioIsFromDoor && ignoredGap == null)
            {
                ignoredGap = Util.GetClosestGapToPos(ChannelHull, LocalPos);
                if (ignoredGap != null && ignoredGap.ConnectedDoor != null)
                {
                    Item door = ignoredGap.ConnectedDoor.Item;
                    WorldPos = new Vector2(door.WorldPosition.X, door.WorldPosition.Y + door.WorldRect.Height * 0.4f);
                    LocalPos = Util.LocalizePosition(WorldPos, ChannelHull);
                    SimPos = LocalPos / 100;
                }
            }
            
            inWater = Util.SoundInWater(LocalPos, ChannelHull);
            inContainer = Item != null && !SoundInfo.IgnoreContainer && IsContainedWithinContainer(Item);
            EuclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos); // euclidean distance

            // Solves rare issue where channels can get stuck not disposing when spectating looping sounds and panning away.
            if (!AudioIsVoice && !isFirstIteration && EuclideanDistance > Channel.Far * 1.1f)
            {
                Channel.Dispose();
            }
        }

        private void UpdateMuffle()
        {
            bool shouldMuffle = false;
            bool shouldUseReverbBuffer = false; // Extended sounds only.
            MuffleType type = MuffleType.None;

            bool ambienceWhileUsingHydrophones = AudioIsAmbience && Listener.IsUsingHydrophones;
            if (ignoreLowpass && !ambienceWhileUsingHydrophones) // Always muffle ambience while using hydrophones.
            { 
                Muffled = false; MuffleStrength = 0; gainMultHF = 1; gainMultLF = 1;
                return; 
            };

            // Start debug log.
            DebugObstructionsList = "";
            DebugObstructionsCount = 0;
            if (ConfigManager.LocalConfig.ShowChannelInfo && !ConfigManager.LocalConfig.ShowPlayingSounds)
            {
                string firstIterText = isFirstIteration ? " (NEW)" : "";
                LuaCsLogger.Log($"Info for \"{ShortName}\"{firstIterText}:", color: isFirstIteration ? Color.LightSeaGreen : Color.LightSkyBlue);
            }

            UpdateObstructions();

            // Finish debug log.
            if (ConfigManager.LocalConfig.ShowChannelInfo)
            {
                if (DebugObstructionsCount <= 0 || DebugObstructionsList.IsNullOrEmpty()) { DebugObstructionsList += "          None\n"; }
                if (!ConfigManager.LocalConfig.ShowPlayingSounds) { LuaCsLogger.Log($"Gain: {MathF.Round(Gain, 2)}    Pitch: {MathF.Round(Pitch, 2)}    Range: {MathF.Round(Channel.Far)}    Distance: {MathF.Round(Distance)}    Muffle: {MathF.Round(MuffleStrength, 3)}    Obstructions:\n{DebugObstructionsList}"); }
            }
            
            // Calculate muffle strength based on obstructions.
            float gainMultHf = 1;
            float gainMultLf = 1;
            float linearTotal = 0;
            float mult = 1;
            if (config.DynamicFx)
            {
                mult = AudioIsVoice ? config.VoiceDynamicMuffleMultiplier : config.DynamicMuffleStrengthMultiplier;
            }
            mult *= SoundInfo.MuffleInfluence;

            for (int i = 0; i < obstructions.Count; i++)
            {
                float strength = obstructions[i].GetStrength() * mult;
                linearTotal += strength;
                gainMultHf *= 1 - strength; // Exponential decay
            }

            if (config.OverMuffle) { gainMultLf -= (linearTotal - 1) * config.OverMuffleStrengthMultiplier; }

            MuffleStrength = Math.Clamp(1 - gainMultHf, 0, 1);
            gainMultHF = Math.Clamp(gainMultHf, 0, 1);
            gainMultLF = Math.Clamp(gainMultLf, 0, 1);

            if (!config.DynamicFx && (config.StaticFx || AudioIsVoice) && MuffleStrength >= config.StaticMinLightMuffleThreshold)
            {
                type = MuffleType.Light;
                shouldMuffle = true;
                if (MuffleStrength >= config.StaticMinMediumMuffleThreshold)
                {
                    type = MuffleType.Medium;
                }
                if (MuffleStrength >= config.StaticMinHeavyMuffleThreshold)
                {
                    type = MuffleType.Heavy;
                }
            }
            else if (config.ClassicFx && MuffleStrength >= config.ClassicMinMuffleThreshold)
            {
                type = MuffleType.Heavy;
                shouldMuffle = true;
            }
            else if (config.DynamicFx && MuffleStrength > 0 && AudioIsVoice)
            {
                shouldMuffle = true;
            }

            bool isLooping = Channel.Looping || ItemComp?.loopingSoundChannel == Channel;

            // Enable reverb buffers for extended sounds. Is not relevant to the DynamicFx reverb.
            if (config.StaticFx && config.StaticReverbEnabled && !shouldMuffle && !isLooping && !IgnoreAirReverb &&
               (Listener.ConnectedArea >= config.StaticReverbMinArea || IsLoud && config.StaticReverbAlwaysOnLoudSounds || SoundInfo.ForceReverb))
            {
                shouldUseReverbBuffer = true;
            }

            StaticShouldUseReverbBuffer = shouldUseReverbBuffer;
            muffleType = type;
            Muffled = shouldMuffle;
        }

        private void UpdateDynamicFx()
        {
            if (!config.DynamicFx || Plugin.EffectsManager == null) return;

            float transitionFactor = (AudioIsFlow || AudioIsFire) ? config.DynamicMuffleFlowFireTransitionFactor : config.DynamicMuffleTransitionFactor;
            if (IsInUpdateLoop && !isFirstIteration && transitionFactor > 0)
            {
                float maxStep = (float)(transitionFactor * Timing.Step);
                trailingGainMultHF = Util.SmoothStep(trailingGainMultHF, gainMultHF, maxStep);
                trailingGainMultLF = Util.SmoothStep(trailingGainMultLF, gainMultLF, maxStep);
            }
            else
            {
                trailingGainMultHF = gainMultHF;
                trailingGainMultLF = gainMultLF;
            }

            Plugin.EffectsManager.UpdateSource(this, trailingGainMultHF, trailingGainMultLF);
        }

        private void UpdateObstructions()
        {
            // Reset obstruction properties.
            Eavesdropped = false;
            Hydrophoned = false;
            propagated = false;
            noPath = false;
            approximateDistance = null;
            obstructions.Clear();

            // Extra muffle when drowning.
            if (BubbleManager.IsClientDrowning(SpeakingClient)) {
                if (AudioIsRadioVoice)
                {
                    AddObstruction(Obstruction.Drowning, "Drowning (Radio)");
                    return;
                }
                else if (AudioIsLocalVoice)
                {
                    AddObstruction(Obstruction.Drowning, "Drowning (Local)");
                }
            }

            if (AudioIsRadioVoice)
            {
                return; // Early return because radios can't be muffled by anything else.
            }

            // Flow/fire sounds are handled differently.
            if (AudioIsFlow || AudioIsFire)
            {
                UpdateFlowFireObstructions();
                return;
            }

            // Mildly muffle sounds in containers. Don't return yet because the container could be submerged with the sound inside.
            if (inContainer)
            {
                AddObstruction(Obstruction.DoorThin, "In Container");
            }

            // Early return for spectators.
            if (Listener.IsSpectating)
            {
                if (inWater && !SoundInfo.IgnoreSurface)
                {
                    AddObstruction(Obstruction.WaterSurface, "Air-water barrier");
                }
                // Return because nothing else can muffle sounds for spectators.
                return;
            }

            if (Listener.IsWearingDivingSuit && config.MuffleDivingSuits)
            {
                AddObstruction(Obstruction.Suit, "Wearing suit");
            }

            // Hydrophone check. Muffle sounds inside your own sub while still hearing sounds in other subs/structures.
            if (Listener.IsUsingHydrophonesNow)
            {
                // Sound can be heard clearly outside in the water.
                if (ChannelHull == null && 
                    (!AudioIsAmbience || !config.HydrophoneMuffleAmbience) || // Ambient sounds
                    config.HydrophoneHearEngine && ItemComp as Engine != null) // Engine sounds (these have a hull)
                {
                    Hydrophoned = true;
                    if (!SoundInfo.IgnoreHydrophoneMuffle)
                    {
                        AddObstruction(Obstruction.Suit, "Hydrophones - outside");
                    }
                    return; // No path obstructions
                }
                // Sound is in a station or other submarine.
                else if (config.HydrophoneHearIntoStructures && ChannelHull != null && !Channel.Looping && ChannelHull.Submarine != LightManager.ViewTarget?.Submarine)
                {
                    Hydrophoned = true;
                    if (!SoundInfo.IgnoreHydrophoneMuffle)
                    {
                        AddObstruction(Obstruction.WallThin, "Hydrophones - other structure");
                    }
                    return; // No path obstructions
                }
                // Sound is in own submarine.
                else if (ChannelHull != null && ChannelHull.Submarine == LightManager.ViewTarget?.Submarine)
                {
                    if (!SoundInfo.IgnoreHydrophoneMuffle)
                    {
                        AddObstruction(config.HydrophoneMuffleOwnSub ? Obstruction.WaterSurface : Obstruction.Suit, "Hydrophones - own submarine");
                    }
                }
                else
                {
                    if (!SoundInfo.IgnoreHydrophoneMuffle)
                    {
                        AddObstruction(Obstruction.WaterSurface, "Hydrophones - other");
                    }
                }
            }

            // Do simple on-thread obstruction checks until we receive the first advanced obstruction check from the main thread.
            if (config.DynamicFx && AudioIsVoice && LastVoiceMainThreadUpdateTime == 0)
            {
                UpdatePathObstructionsSimple();
                return;
            }

            if (config.DynamicFx) { UpdatePathObstructionsAdvanced(); }
            else                  { UpdatePathObstructionsSimple(); }
        }

        private void UpdateWaterObstructions()
        {
            bool earsInWater = Listener.IsSubmerged;
            bool soundInWater = inWater;
            bool ignoreSubmersion = SoundInfo.IgnoreSubmersion || (Listener.IsCharacter ? !config.MuffleSubmergedPlayer : !config.MuffleSubmergedViewTarget);
            bool ignoreSurface = SoundInfo.IgnoreSurface;

            // Neither in water.
            if (!earsInWater && !soundInWater) { return; }
            else if (noPath) // The listener and sound are in separate rooms or one of them is outside the sub.
            {                // In this case, it feels more natural for ignoreSubmersion and ignoreSurface to be ignored.
                if (earsInWater) { AddObstruction(Obstruction.WaterBody, "No path - listener submerged"); }
                if (soundInWater != earsInWater) { AddObstruction(Obstruction.WaterSurface, "No path - air-water barrier"); }
                return;
            }

            // From here it is assumed there is a path to the sound,
            // usually meaning the listener and the water share the same hull.

            // Ears in water.
            if (earsInWater)
            {
                if (!ignoreSubmersion) { AddObstruction(Obstruction.WaterBody, "With path - listener submerged"); }
                // Sound in same body of water, return because no need to apply water surface.
                if (soundInWater) { bothInWater = true; return; }
            }

            // Separated by water surface.
            if (soundInWater != earsInWater && !SoundInfo.IgnoreBarriers)
            {
                Obstruction obstruction = Obstruction.WaterSurface;
                if (SoundInfo.PropagateWalls && !propagated) { obstruction = Obstruction.WallThin; }
                if (!ignoreSurface) { AddObstruction(obstruction, "With path - air-water barrier"); }
            }
        }

        private void UpdatePathObstructionsAdvanced()
        {
            if (Channel == null) { return; } // Just in case.

            Hull? soundHull = ChannelHull;
            Hull? listenerHull = Listener.FocusedHull;
            HashSet<Hull> listenerConnectedHulls = Listener.ConnectedHulls;

            noPath = !SoundInfo.IgnoreBarriers && listenerHull != soundHull && (listenerHull == null || soundHull == null || !listenerConnectedHulls.Contains(soundHull!));

            int numWallObstructions = 0;
            int numDoorObstructions = 0;
            int numWaterSurfaceObstructions = 0;

            // 1. Get occlusion obstructions. Ray cast to check how many walls are between the sound and the listener.
            if (config.OccludeSounds && !SoundInfo.IgnoreBarriers)
            {
                IEnumerable<Body> bodies;
                // Voice ray casts are performed on the main thread and sent to VoiceOcclusions.
                if (AudioIsVoice) { lock (VoiceOcclusionsLock) { bodies = VoiceOcclusions; } }
                else { bodies = Submarine.PickBodies(Listener.SimPos, SimPos, collisionCategory: Physics.CollisionWall); }
                // Only count collisions for unique wall orientations.
                List<float> yWalls = new List<float>();
                List<float> xWalls = new List<float>();
                float epsilon = 200.0f;
                foreach (var body in bodies)
                {
                    if (body.UserData is Structure structure)
                    {
                        bool closeX = xWalls.Any(x => Math.Abs(x - structure.Position.X) < epsilon);
                        bool closeY = yWalls.Any(y => Math.Abs(y - structure.Position.Y) < epsilon);

                        if (!closeX && !closeY)
                        {
                            xWalls.Add(structure.Position.X);
                            yWalls.Add(structure.Position.Y);
                            numWallObstructions++;
                        }
                    }
                }
                if (config.MaxOcclusions > 0)
                {
                    numWallObstructions = Math.Min(numWallObstructions, config.MaxOcclusions);
                }
                // If both outside, treat any number of obstructions as just one - because the sound is going around
                if (ChannelHull == null && Listener.FocusedHull == null)
                {
                    numWallObstructions = Math.Min(numWallObstructions, 1);
                }
            }

            // 2. Propagate to the correct hull, updating wall obstructions. Propagation allows certain sounds near a wall to propagate to the opposite side.
            if (SoundInfo.PropagateWalls && noPath)
            {
                foreach (Hull hull in listenerConnectedHulls)
                {
                    if (SoundPropagatesToHull(hull, WorldPos))
                    {
                        propagated = true;
                        ChannelHull = hull; // Update the sound's hull.
                        inWater = Util.SoundInWater(LocalPos, ChannelHull); // Update for water obstruction check later.
                        break;
                    }
                }
            }

            // 3. Get path obstructions.
            List<SoundPathfinder.PathfindingResult> topResults;
            if (AudioIsVoice) { lock (VoicePathResultsLock) { topResults = VoicePathResults; } }
            else { topResults = SoundPathfinder.FindShortestPaths(WorldPos, soundHull, Listener.WorldPos, listenerHull, listenerHull?.Submarine, maxRawDistance: Channel.Far * 2, ignoredGap: ignoredGap); }
            if (topResults.Count > 0)
            {
                SoundPathfinder.PathfindingResult bestResult = topResults[0];
                numDoorObstructions = bestResult.ClosedDoorCount;
                numWaterSurfaceObstructions = bestResult.WaterSurfaceCrossings;
                // Assign approximate distance if there's an unobstructed path to the listener. Approximate distance is used for gain attenuation.
                if (numDoorObstructions <= 0 && numWaterSurfaceObstructions <= 0)
                {
                    approximateDistance = bestResult.RawDistance;
                }
            }

            // 4. Update obstructions and remove approximate distance for sounds that ignore pathing.
            if (SoundInfo.IgnoreBarriers)
            {
                approximateDistance = null;
                numWallObstructions = 0;
                numDoorObstructions = 0;
                numWaterSurfaceObstructions = 0;
            }

            // 5. Assign 'eavesdropped' variable and add obstruction.
            if (Listener.IsEavesdropping &&
                ChannelHull != null &&
                ChannelHull != Listener.CurrentHull &&
                listenerConnectedHulls.Contains(ChannelHull))
            {
                Eavesdropped = true;
                if (config.EavesdroppingMuffle) { AddObstruction(Obstruction.DoorThin, "Eavesdropped"); }
            }

            // 6. Add water obstructions.
            //    TODO how does this play with A* water crossing checks?
            UpdateWaterObstructions(); // Must be ran after 'propagated' and 'noPath' have been resolved.

            // Saves a snapshot of the current obstructions to be applied to any clones.
            List<Obstruction> clonedObstructions = new List<Obstruction>(obstructions);

            // 7. Add obstructions. TODO this does what I want it to do but is it's hideous
            float totalDoorPathResistance = 0;
            float totalWallPathResistance = 0;
            for (int i = 0; i < numWallObstructions; i++) { totalWallPathResistance += Obstruction.WallThick.GetStrength(); }
            for (int i = 0; i < obstructions.Count; i++) { if (obstructions[i] == Obstruction.WaterSurface) { totalWallPathResistance += Obstruction.WaterSurface.GetStrength(); } }
            for (int i = 0; i < numDoorObstructions; i++) { totalDoorPathResistance += Obstruction.DoorThick.GetStrength(); }
            for (int i = 0; i < numWaterSurfaceObstructions; i++) { totalDoorPathResistance += Obstruction.WaterSurface.GetStrength(); }

            if (numWallObstructions <= 0) { totalWallPathResistance = float.MaxValue; }
            if (numDoorObstructions <= 0) { totalDoorPathResistance = float.MaxValue; }

            bool pathLeastResistanceIsWalls = totalWallPathResistance < totalDoorPathResistance;

            if (pathLeastResistanceIsWalls)
            {
                if (propagated) { numWallObstructions--; AddObstruction(Obstruction.WallThin, "Propagated"); } // Replacing thick wall with thin.

                for (int i = 0; i < numWallObstructions; i++)
                {
                    if (!bothInWater && (noPath || Channel?.Far < approximateDistance || topResults.Count <= 0)) { AddObstruction(Obstruction.WallThick, "Occlusion - no path"); }
                    else { AddObstruction(Obstruction.WallThin, "Occlusion - with path"); } // Wall occlusion isn't as strong if there's a path to the listener or they are both in the same body of water.
                }
            }
            else
            {
                if (propagated) { numDoorObstructions--; AddObstruction(Obstruction.DoorThin, "Propagated"); }
                for (int i = 0; i < numDoorObstructions; i++) { AddObstruction(Obstruction.DoorThick, "Passing through door"); }
                for (int j = 0; j < numWaterSurfaceObstructions; j++) { AddObstruction(Obstruction.WaterSurface, "Passing through air-water barrier"); }
            }
        }

        private void UpdatePathObstructionsSimple()
        {
            Hull? soundHull = ChannelHull;
            Hull? listenerHull = Listener.FocusedHull;
            HashSet<Hull> listenerConnectedHulls = Listener.ConnectedHulls;

            if (!AudioIsFromDoor) // Check if there's a path to the sound.
            {
                noPath = !SoundInfo.IgnoreBarriers && listenerHull != soundHull && (listenerHull == null || soundHull == null || !listenerConnectedHulls.Contains(soundHull!));
            }
            else // Exception for door opening/closing sounds - must check hulls while ignorning the doorway gap.
            {
                float distance = GetApproximateDistance(Listener.LocalPos, LocalPos, listenerHull, soundHull, ignoredGap: ignoredGap);
                noPath = distance >= float.MaxValue;
                if (!noPath)
                {
                    approximateDistance = distance;
                }
            }

            if (!noPath && soundHull != null && approximateDistance == null) // There is a valid path - get the approx distance if not already (door sounds get earlier).
            {
                approximateDistance = GetApproximateDistance(Listener.LocalPos, LocalPos, listenerHull, soundHull, ignoredGap: ignoredGap);
            }

            bool exceedsRange = Distance >= Channel.Far; // Refers to the range being greater than the approx dist NOT the euclidean distance because then the sound wouldn't be playing.

            if (SoundInfo.IgnoreBarriers)
            {
                approximateDistance = null; // Null this so the Distance property defaults to euclidean dist.
            }
            else if (noPath || exceedsRange) // One extra feature the simple obstruction function has is allowing sounds to propagate when out of range.
            {
                if (SoundInfo.PropagateWalls)
                {
                    foreach (Hull hull in Listener.ConnectedHulls)
                    {
                        if (SoundPropagatesToHull(hull, WorldPos))
                        {
                            AddObstruction(Obstruction.WallThin, "Propagated");
                            ChannelHull = hull;
                            inWater = Util.SoundInWater(LocalPos, ChannelHull); // Update for water obstruction check later.
                            propagated = true;
                            break;
                        }
                    }
                }

                if (!propagated)
                {
                    AddObstruction(Obstruction.WallThick, "No path");
                    if (ShortName.Contains("shot"))
                    {
                        LuaCsLogger.Log($"exceedsRange: {exceedsRange} Distance: {Distance} approx {approximateDistance} euc: {EuclideanDistance}");
                    }
                }
            }

            // Assign 'eavesdropped' variable and add obstruction.
            // Make sure to use the 'ChannelHull' variable and not 'soundHull' as 'ChannelHull' may have been updated via propagation
            if (Listener.IsEavesdropping &&
                ChannelHull != null &&
                ChannelHull != Listener.CurrentHull &&
                listenerConnectedHulls.Contains(ChannelHull))
            {
                Eavesdropped = true;
                if (config.EavesdroppingMuffle) { AddObstruction(Obstruction.DoorThin, "Eavesdropped"); }
            }

            UpdateWaterObstructions();
        }

        private void UpdateGain()
        {
            if (Channel.FadingOutAndDisposing) { return; }

            // Flow/fire sounds are handled differently.
            if (AudioIsFlow || AudioIsFire)
            {
                UpdateFlowFireGain();
                return;
            }

            float mult = 1;
            float currentGain = ItemComp?.GetSoundVolume(ItemComp.loopingSound) ?? startGain;

            mult += SoundInfo.GainMult - 1;

            // Radio can only be muffled when the sender is drowning.
            if (AudioIsRadioVoice)
            {
                mult += MathHelper.Lerp(config.UnmuffledVoiceVolumeMultiplier, config.MuffledVoiceVolumeMultiplier, MuffleStrength) - 1;
            }
            else if (AudioIsLocalVoice)
            {
                mult += MathHelper.Lerp(config.UnmuffledVoiceVolumeMultiplier, config.MuffledVoiceVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (Eavesdropped) mult += config.EavesdroppingVoiceVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }
            // Either a component or status effect sound.
            else if (Channel.Looping)
            {
                mult += MathHelper.Lerp(config.UnmuffledLoopingVolumeMultiplier, config.MuffledLoopingVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (Eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }

                // Tweak for vanilla exosuit volume.
                if (Item != null && Item.HasTag(new Identifier("deepdivinglarge")) && LongName.EndsWith("barotrauma/content/items/weapons/weapons_chargeup.ogg")) { mult *= config.VanillaExosuitVolumeMultiplier; }
                // Disable diving suit ambience when not in water.
                if (!inWater && Item != null && Item.HasTag(new Identifier("deepdiving")) && ShortName.Contains("divingsuitloop")) { mult *= 0; }
            }
            // Single (non-looping sound).
            else
            {
                mult += MathHelper.Lerp(config.UnmuffledSoundVolumeMultiplier, config.MuffledSoundVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (Eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }

            // Note that the following multipliers may also apply to radio channels.

            // Fade transition between two hull soundscapes when eavesdropping.
            float eavesdropEfficiency = EavesdropManager.Efficiency;
            if (eavesdropEfficiency > 0 && (!AudioIsRadioVoice || config.EavesdroppingDucksRadio))
            {
                if (!Eavesdropped) { mult *= Math.Clamp(1 - eavesdropEfficiency * (1 / config.EavesdroppingThreshold), 0.1f, 1); }
                else if (Eavesdropped) { mult *= Math.Clamp(eavesdropEfficiency * (1 / config.EavesdroppingThreshold) - 1, 0.1f, 1); }
            }

            float sidechainMult = 1;
            float thisSidechainRelease = SoundInfo.SidechainRelease + config.SidechainReleaseMaster;
            float thisSidechainStartingStrength = SoundInfo.SidechainMult * config.SidechainIntensityMaster * (1 - (float)Math.Pow(MuffleStrength, config.SidechainMufflePower));
            float globalSidechainStartingStrength = Plugin.Sidechain.SidechainRawStartValue;
            // If a more powerful loud sound is playing than the current loud sound.
            if (IsLoud && Plugin.Sidechain.ActiveSoundGroup != SoundInfo.CustomSound && globalSidechainStartingStrength > thisSidechainStartingStrength)
            {
                float overpowerMult = Math.Clamp(1 - (globalSidechainStartingStrength - thisSidechainStartingStrength), 0f, 1f);
                sidechainMult *= MathHelper.Lerp(1, overpowerMult, Plugin.Sidechain.CompletionRatio);
                //LuaCsLogger.Log($"{ShortName} overpowered by {Plugin.Sidechain.ActiveSoundGroup.Keyword} overpowerMult {overpowerMult} globalSidechainStartingStrength {globalSidechainStartingStrength} thisSidechainStartingStrength {thisSidechainStartingStrength}");
            }

            // Start sidechain release for loud sounds.
            if (IsLoud && (Channel.Looping || AudioIsVoice || !Channel.Looping && !sidechainTriggered))
            {
                // The strength of the sidechaining slightly decreases the more muffled the loud sound is.
                Plugin.Sidechain.StartRelease(thisSidechainStartingStrength, thisSidechainRelease, SoundInfo.CustomSound);
                sidechainTriggered = true; // Only trigger sidechaining once for non-looping sounds in case UpdateNonLoopingSounds is enabled.
            }
            else if (!IsLoud) { sidechainMult *= 1 - Plugin.Sidechain.SidechainMultiplier; }

            if (!AudioIsRadioVoice || config.SidechainRadio) { mult *= sidechainMult; }

            // Replace OpenAL's distance attenuation using the approximate distance gathered from traversing gaps.
            bool ignoreGainFade = false;
            if (config.AttenuateWithApproximateDistance && approximateDistance != null)
            {
                rolloffFactor = 0; // Disable OpenAL distance attenuation.
                rolloffFactorModified = true;

                bool shouldUseEuclideanDistance = Distance >= Channel.Far;
                DistanceModel newModel = shouldUseEuclideanDistance ? DistanceModel.Euclidean : DistanceModel.Approximate;
                
                if (newModel != distanceModel) // Used to set flag to skip the audio fade
                {
                    ignoreGainFade = newModel == DistanceModel.Approximate;
                    distanceModel = newModel;
                }

                float distance = newModel == DistanceModel.Euclidean ? EuclideanDistance : Distance;
                float distMult = CalculateLinearDistanceClampedMult(MathF.Round(distance), Channel.Near, Channel.Far);

                if (shouldUseEuclideanDistance) // Fade to euclidean distance
                {
                    float distanceOver = Distance - Channel.Far;
                    float fadeDistance = 200; // Arbitrary.
                    float fadeMult = Math.Clamp(distanceOver / fadeDistance, config.MinDistanceFalloffVolume, 1);
                    distMult *= fadeMult;
                }

                mult *= distMult;
            }
            else if (rolloffFactorModified || distanceModel == DistanceModel.OpenAL && rolloffFactor != 1)
            {
                rolloffFactor = 1;
                distanceModel = DistanceModel.OpenAL;
            }

            float targetGain = currentGain * mult;

            if (SoundInfo.IsTurretMovementSound || SoundInfo.IsChargeSound)
            {
                targetGain = Math.Clamp(targetGain, 0, 1);
            }

            // Only transition audio levels if there's no active sidechaining, the sound is consistently updated, and this isn't its first update.
            float transitionFactor = config.GainTransitionFactor;
            if (transitionFactor > 0 && !ignoreGainFade && IsInUpdateLoop && !isFirstIteration && Plugin.Sidechain.SidechainMultiplier <= 0)
            {
                float maxStep = (float)(transitionFactor * Timing.Step);
                Gain = Util.SmoothStep(Gain, targetGain, maxStep);
            }
            else
            {
                Gain = targetGain;
            }
        }

        private void UpdatePitch()
        {
            if (SoundInfo.IsChargeSound)
            {
                return;
            }

            if (ignorePitch || !config.PitchEnabled)
            {
                Pitch = 1;
                return;
            };

            // Flow/fire sounds are handled differently.
            if (AudioIsFlow || AudioIsFire)
            {
                UpdateFlowFirePitch();
                return;
            }

            float mult = 1;
            float currentPitch = startPitch;

            mult += SoundInfo.PitchMult - 1;

            // Voice.
            if (AudioIsVoice)
            {
                mult += MathHelper.Lerp(config.UnmuffledVoicePitchMultiplier, config.MuffledLoopingPitchMultiplier, MuffleStrength) - 1;
            }
            // Either a component or status effect sound.
            else if (Channel.Looping)
            {
                mult += MathHelper.Lerp(config.UnmuffledSoundPitchMultiplier, config.MuffledLoopingPitchMultiplier, MuffleStrength) - 1;

                if (Eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
                else if (Hydrophoned) mult += config.HydrophonePitchMultiplier - 1;

                if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
                if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1; 
            }
            // Single (non-looping sound).
            else
            {
                mult += MathHelper.Lerp(config.UnmuffledSoundPitchMultiplier, config.MuffledSoundPitchMultiplier, MuffleStrength) - 1;

                if (config.PitchWithDistance) // Additional pitching based on distance and muffle strength.
                {
                    float distanceRatio = Math.Clamp(1 - Distance / Channel.Far, 0, 1);
                    mult += MathHelper.Lerp(1, distanceRatio, MuffleStrength) - 1;
                }

                if (Eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
                else if (Hydrophoned) mult += config.HydrophonePitchMultiplier - 1;

                if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
                if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1;
            }

            float targetPitch = currentPitch * mult;

            float transitionFactor = config.PitchTransitionFactor;
            if (transitionFactor > 0 && IsInUpdateLoop && !isFirstIteration)
            {
                float maxStep = (float)(transitionFactor * Timing.Step);
                Pitch = Util.SmoothStep(Pitch, targetPitch, maxStep);
            }
            else
            {
                Pitch = targetPitch;
            }
        }

        private void UpdateFlowFireObstructions()
        {
            if (AudioIsFlow && !config.MuffleFlowSounds) return;
            else if (AudioIsFire && !config.MuffleFireSounds) return;

            if (Listener.IsSubmerged) { AddObstruction(Obstruction.WaterSurface, "Listener submerged"); }
            if (Listener.IsWearingDivingSuit) { AddObstruction(Obstruction.Suit, "Wearing suit"); }
            if (Listener.IsUsingHydrophones) { AddObstruction(Obstruction.WaterSurface, "Hydrophones"); }

            if (!config.MuffleFlowFireSoundsWithEstimatedPath) { return; }

            if (AudioIsFire)
            {
                float fireThreshold = 0;
                if (ShortName.Contains("firelarge.ogg")) { fireThreshold = SoundPlayer.FireSoundLargeLimit; }
                else if (ShortName.Contains("firemedium.ogg")) { fireThreshold = SoundPlayer.FireSoundMediumLimit; }

                bool pathToFire = false;
                foreach (Hull hull in Listener.ConnectedHulls)
                {
                    foreach (FireSource fs in hull.FireSources)
                    {
                        if (fs.Size.X > fireThreshold) { pathToFire = true; break; }
                    }
                }

                if (!pathToFire)
                {
                    AddObstruction(Obstruction.WallThick, "No path");
                }
                else if (Listener.IsEavesdropping)
                {
                    Eavesdropped = true;
                }
            }
            else if (AudioIsFlow)
            {
                HashSet<Hull> hullsWithFlow = Listener.HullsWithSmallFlow;
                if (ShortName.Contains("flowlarge.ogg")) { hullsWithFlow = Listener.HullsWithLargeFlow; }
                else if (ShortName.Contains("flowmedium.ogg")) { hullsWithFlow = Listener.HullsWithMediumFlow; }

                bool pathToLeaks = false;
                foreach (Hull hull in Listener.ConnectedHulls)
                {
                    if (hullsWithFlow.Contains(hull))
                    {
                        pathToLeaks = true;
                        break;
                    }
                }
                if (!pathToLeaks)
                {
                    AddObstruction(Obstruction.WallThick, "No path");
                }
                else if (Listener.IsEavesdropping)
                {
                    Eavesdropped = true;
                }
            }
        }

        private void UpdateFlowFireGain()
        {
            float mult = 1;
            mult += SoundInfo.GainMult - 1;
            mult += MathHelper.Lerp(config.UnmuffledLoopingVolumeMultiplier, config.MuffledLoopingVolumeMultiplier, MuffleStrength) - 1;

            // Fade transition between two hull soundscapes when eavesdropping.
            float eavesdropEfficiency = EavesdropManager.Efficiency;
            if (eavesdropEfficiency > 0)
            {
                if (!Eavesdropped) { mult *= Math.Clamp(1 - eavesdropEfficiency * (1 / config.EavesdroppingThreshold), 0.1f, 1); }
                else if (Eavesdropped) { mult *= Math.Clamp(eavesdropEfficiency * (1 / config.EavesdroppingThreshold) - 1, 0.1f, 1); }
            }

            float thisSidechainRelease = SoundInfo.SidechainRelease + config.SidechainReleaseMaster;
            float thisSidechainStartingStrength = SoundInfo.SidechainMult * config.SidechainIntensityMaster * (1 - (float)Math.Pow(MuffleStrength, config.SidechainMufflePower));
            float globalSidechainStartingStrength = Plugin.Sidechain.SidechainRawStartValue;
            // If a more powerful loud sound is playing than the current loud sound.
            if (IsLoud && Plugin.Sidechain.ActiveSoundGroup != SoundInfo.CustomSound && globalSidechainStartingStrength > thisSidechainStartingStrength)
            {
                float overpowerMult = Math.Clamp(1 - (globalSidechainStartingStrength - thisSidechainStartingStrength), 0f, 1f);
                mult *= MathHelper.Lerp(1, overpowerMult, Plugin.Sidechain.CompletionRatio);
            }

            if (IsLoud) Plugin.Sidechain.StartRelease(thisSidechainStartingStrength, thisSidechainRelease, SoundInfo.CustomSound);
            else if (!IsLoud) mult *= 1 - Plugin.Sidechain.SidechainMultiplier;

            // Master volume multiplier.
            if (AudioIsFlow) mult *= config.FlowSoundVolumeMultiplier;
            else if (AudioIsFire) mult *= config.FireSoundVolumeMultiplier;

            // Fade flow and fire sounds in. Attempts to prevent the channels spamming on and off.
            if (isFirstIteration && config.GainTransitionFactor > 0)
            {
                mult *= 0;
            }

            // Get current gain.
            float currentGain = 1;
            if (flowFireChannelIndex != -1)
            {
                if (AudioIsFlow) currentGain = Math.Max(SoundPlayer.flowVolumeRight[flowFireChannelIndex], SoundPlayer.flowVolumeLeft[flowFireChannelIndex]);
                else if (AudioIsFire) currentGain = Math.Max(SoundPlayer.fireVolumeRight[flowFireChannelIndex], SoundPlayer.fireVolumeLeft[flowFireChannelIndex]);
            }

            float targetGain = currentGain * mult;
            float transitionFactor = config.GainTransitionFactor;
            if (transitionFactor > 0 && !isFirstIteration && Plugin.Sidechain.SidechainMultiplier <= 0)
            {
                float maxStep = (float)(transitionFactor * Timing.Step);
                Gain = Util.SmoothStep(Gain, targetGain, maxStep);
            }
            else
            {
                Gain = targetGain;
            }
        }

        private void UpdateFlowFirePitch()
        {
            float mult = 1;
            if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
            if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1;
            if (Listener.IsUsingHydrophones) mult += config.HydrophonePitchMultiplier - 1;

            float targetPitch = 1 * mult;

            float transitionFactor = config.PitchTransitionFactor;
            if (transitionFactor > 0 && !isFirstIteration)
            {
                float maxStep = (float)(transitionFactor * Timing.Step);
                Pitch = Util.SmoothStep(Pitch, targetPitch, maxStep);
            }
            else
            {
                Pitch = targetPitch;
            }
        }

        private void AddObstruction(Obstruction obs, string debugContext)
        {
            obstructions.Add(obs);
            if (ConfigManager.LocalConfig.ShowChannelInfo) { DebugObstructions(debugContext); }
        }

        private void DebugObstructions(string reason)
        {
            DebugObstructionsList += $"          {DebugObstructionsCount + 1}. {obstructions[DebugObstructionsCount]} {obstructions[DebugObstructionsCount].GetStrength() * SoundInfo.MuffleInfluence * (config.DynamicFx ? config.DynamicMuffleStrengthMultiplier : 1)} ({reason})\n";
            DebugObstructionsCount++;
        }

        public static float CalculateLinearDistanceClampedMult(float distance, float near, float far, float rolloffFactor = 1)
        {
            float currentDistance = Math.Max(distance, near);
            currentDistance = Math.Min(currentDistance, far);
            float denominator = far - near;
            float gain;

            if (Math.Abs(denominator) < float.Epsilon) // Check if denominator is effectively zero (i.e., far == near)
            {
                gain = 1.0f;
            }
            else
            {
                gain = 1.0f - rolloffFactor * (currentDistance - near) / denominator;
            }

            return Math.Max(config.MinDistanceFalloffVolume, Math.Min(1.0f, gain));
        }
        
        private bool ShouldSkipMuffleUpdate()
        {
            bool shouldSkip = false;

            double currentTime = Timing.TotalTime;

            if (ItemComp != null && ItemComp.loopingSoundChannel == Channel)
            {
                muffleUpdateInterval = config.ComponentMuffleUpdateInterval;
                if (currentTime < ItemComp.lastMuffleCheckTime + muffleUpdateInterval)
                {
                    shouldSkip = true;
                }
                else
                {
                    ItemComp.lastMuffleCheckTime = (float)currentTime; // Why is this one a float? devs please fix.
                }
            }
            else if (StatusEffect != null)
            {
                muffleUpdateInterval = config.StatusEffectMuffleUpdateInterval;
                if (currentTime < StatusEffect.LastMuffleCheckTime + muffleUpdateInterval)
                {
                    shouldSkip = true;
                }
                else
                {
                    StatusEffect.LastMuffleCheckTime = currentTime;
                }
            }
            else if (AudioIsVoice)
            {
                muffleUpdateInterval = config.VoiceMuffleUpdateInterval;
                if (currentTime < lastMuffleCheckTime + muffleUpdateInterval)
                {
                    shouldSkip = true;
                }
                else
                {
                    lastMuffleCheckTime = currentTime;
                }
            }
            else if (!Channel.Looping && config.UpdateNonLoopingSounds)
            {
                muffleUpdateInterval = config.NonLoopingSoundMuffleUpdateInterval;
                if (currentTime < lastMuffleCheckTime + muffleUpdateInterval)
                {
                    shouldSkip = true;
                }
                else
                {
                    lastMuffleCheckTime = currentTime;
                }
            }

            return shouldSkip;
        }

        private static bool SoundPropagatesToHull(Hull targetHull, Vector2 soundWorldPos)
        {
            if (targetHull == null) { return false; }

            float soundRadius = config.SoundPropagationRange;

            Rectangle soundBounds = new Rectangle(
                (int)(soundWorldPos.X - soundRadius),
                (int)(soundWorldPos.Y - soundRadius),
                (int)(soundRadius * 2),
                (int)(soundRadius * 2)
            );

            // Check if the rectangles intersect
            return soundBounds.Intersects(targetHull.WorldRect);
        }

        // Gets the distance between two localised positions going through gaps. Returns MaxValue if no path or out of range.
        private static float GetApproximateDistance(Vector2 startPos, Vector2 endPos, Hull? startHull, Hull? endHull, float maxDistance = float.MaxValue, float distanceMultiplierPerClosedDoor = 0, Gap? ignoredGap = null)
        {
            if (startHull != endHull && (startHull == null || endHull == null)) { return float.MaxValue; }
            return GetApproximateHullDistance(startPos, endPos, startHull, endHull, new HashSet<Hull>(), 0.0f, maxDistance, distanceMultiplierPerClosedDoor, ignoredGap);
        }

        private static float GetApproximateHullDistance(Vector2 startPos, Vector2 endPos, Hull? startHull, Hull? endHull, HashSet<Hull> connectedHulls, float distance, float maxDistance, float distanceMultiplierFromDoors = 0, Gap? ignoredGap = null)
        {
            if (distance >= maxDistance) { return float.MaxValue; }
            if (startHull == endHull) { return distance + Vector2.Distance(startPos, endPos); }

            connectedHulls.Add(startHull);
            foreach (Gap g in startHull.ConnectedGaps)
            {
                float distanceMultiplier = 1;
                // For doors.
                if (ignoredGap != null && g == ignoredGap)
                {
                    // avoid other else if blocks and go straight to for loop.
                }
                else if (g.ConnectedDoor != null)
                {
                    if (Util.IsDoorClosed(g.ConnectedDoor))
                    {
                        if (distanceMultiplierFromDoors <= 0) { continue; }
                        distanceMultiplier *= distanceMultiplierFromDoors;
                    }
                }
                // For holes in hulls.
                else if (g.Open < config.OpenWallThreshold)
                {
                    continue;
                }

                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    if (g.linkedTo[i] is Hull newStartHull && !connectedHulls.Contains(newStartHull))
                    {
                        Vector2 optimalGapPos = GetGapIntersectionPos(startPos, endPos, g);
                        float dist = GetApproximateHullDistance(optimalGapPos, endPos, newStartHull, endHull, connectedHulls, distance + Vector2.Distance(startPos, optimalGapPos) * distanceMultiplier, maxDistance, distanceMultiplierFromDoors, ignoredGap);
                        if (dist < float.MaxValue)
                        {
                            return dist;
                        }
                    }
                }
            }

            return float.MaxValue;
        }

        private static Vector2 GetGapIntersectionPos(Vector2 startPos, Vector2 endPos, Gap gap)
        {
            if (gap == null) { return startPos; }

            Vector2 gapCenter = gap.Position;
            float epsilon = 0.0001f;

            if (gap.IsHorizontal)
            {
                float gapFixedY = gapCenter.Y;
                float gapMinX = gapCenter.X - gap.Rect.Width / 2f;
                float gapMaxX = gapCenter.X + gap.Rect.Width / 2f;
                float intersectX;

                if (Math.Abs(endPos.Y - startPos.Y) < epsilon)
                {
                    intersectX = endPos.X;
                }
                else if (Math.Abs(endPos.X - startPos.X) < epsilon)
                {
                    intersectX = endPos.X;
                }
                else
                {
                    float slopeX = (endPos.X - startPos.X) / (endPos.Y - startPos.Y);
                    intersectX = startPos.X + slopeX * (gapFixedY - startPos.Y);
                }

                return new Vector2(Math.Clamp(intersectX, gapMinX, gapMaxX), gapFixedY);
            }
            else
            {
                float gapFixedX = gapCenter.X;
                float gapMinY = gapCenter.Y - gap.Rect.Height / 2f;
                float gapMaxY = gapCenter.Y + gap.Rect.Height / 2f;
                float intersectY;

                if (Math.Abs(endPos.X - startPos.X) < epsilon)
                {
                    intersectY = endPos.Y;
                }
                else if (Math.Abs(endPos.Y - startPos.Y) < epsilon)
                {
                    intersectY = endPos.Y;
                }
                else
                {
                    float slopeY = (endPos.Y - startPos.Y) / (endPos.X - startPos.X);
                    intersectY = startPos.Y + slopeY * (gapFixedX - startPos.X);
                }

                return new Vector2(gapFixedX, Math.Clamp(intersectY, gapMinY, gapMaxY));
            }
        }

        private static bool IsContainedWithinContainer(Item? item)
        {
            while (item?.ParentInventory != null)
            {
                if (item.ParentInventory.Owner is Item parent && parent.HasTag("container"))
                {
                    return true;
                }
                item = item.ParentInventory.Owner as Item;
            }
            return false;
        }
    }
}
