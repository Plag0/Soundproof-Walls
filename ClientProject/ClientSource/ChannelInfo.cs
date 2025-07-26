using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
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
    }

    // This class is responsible for calculating if a SoundChannel should be muffled and why.
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

        private int debugObstructionsCount = 0;
        private string debugObstructionsList = "";

        public float MuffleStrength;
        private float startGain;
        private float startPitch;
        private float gainMultHF;
        private float trailingGainMultHF;
        private float gainMultLF;
        private float trailingGainMultLF;
        private bool isFirstIteration;
        private Gap? ignoredGap = null;
        private List<Obstruction> obstructions = new List<Obstruction>();
        private List<ChannelInfo> clones = new List<ChannelInfo>();

        // Temporary? solution to the multi-thread problem of voice and sounds. Get this information on the main thread.
        public IEnumerable<Body> VoiceOcclusions = new List<Body>();
        public readonly object VoiceOcclusionsLock = new object();
        public List<SoundPathfinder.PathfindingResult> VoicePathResults = new List<SoundPathfinder.PathfindingResult>();
        public readonly object VoicePathResultsLock = new object();
        public double LastVoiceMainThreadUpdateTime = 0; // Separate from lastMuffleCheckTime.
        public double VoiceMainThreadUpdateInterval = config.VoiceMuffleUpdateInterval;

        public float EuclideanDistance;
        private float? approximateDistance;
        public Vector2 WorldPos;
        public Vector2 LocalPos;
        public Vector2 SimPos;
        private bool isClone;
        private bool inWater;
        private bool bothInWater;
        private bool inContainer;
        private bool noPath; // Failed to find a path from the sound to the listener irrespective of distance.

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

        private bool dontMuffle = false;
        private bool dontPitch = false;
        private bool dontReverb = false;
        private bool ignoreLowpass => SoundInfo.IgnoreLowpass || (dontMuffle && !SoundInfo.ForceLowpass);
        private bool ignorePitch => SoundInfo.IgnorePitch || dontPitch;
        public bool IgnoreWaterReverb => ((SoundInfo.IgnoreWaterReverb || dontReverb) && !SoundInfo.ForceReverb) || Listener.IsSpectating;
        public bool IgnoreAirReverb => ((SoundInfo.IgnoreAirReverb || dontReverb) && !SoundInfo.ForceReverb) || Listener.IsSpectating;
        public bool Ignored => SoundInfo.IgnoreAll || (ignoreLowpass && ignorePitch);
        public bool IsLoud => SoundInfo.IsLoud;
        public bool IsInUpdateLoop => Channel.Looping || AudioIsVoice || config.UpdateNonLoopingSounds;

        private int flowFireChannelIndex;

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

        public float Distance
        {
            get { return approximateDistance ?? EuclideanDistance; }
        }

        public bool StaticShouldUseReverbBuffer = false; // For extended sounds only.
        public bool StaticIsUsingReverbBuffer = false; // For extended sounds only.

        private bool sidechainTriggered = false;
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
        private Client? speakingClient = null;
        private ChatMessageType? messageType = null;

        private bool voiceUseMuffleFilter 
        { 
            set 
            { 
                if (speakingClient != null && speakingClient.VoipSound != null) 
                { 
                    speakingClient.VoipSound.UseMuffleFilter = value; 
                } 
            } 
        }

        private bool voiceUseRadioFilter
        {
            set
            {
                if (speakingClient != null && speakingClient.VoipSound != null)
                {
                    speakingClient.VoipSound.UseRadioFilter = value;
                }
            }
        }

        private static Config config { get { return ConfigManager.Config; } }

        // Copy constructor for directional sounds (disaster of a feature)
        public ChannelInfo(ChannelInfo original, Vector3 clonePosition)
        {
            isClone = true;
            SoundInfo = original.SoundInfo;
            ShortName = original.ShortName;
            LongName = original.LongName;
            
            Channel = new SoundChannel(SoundInfo.Sound, 1, clonePosition, ChannelInfoManager.CLONE_FREQ_MULT_CODE, original.Channel.Near, original.Channel.Far, SoundManager.SoundCategoryDefault);

            if (Channel != null)
            {
                Channel.Looping = original.Channel.Looping;
                uint sid = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
                Plugin.EffectsManager?.RegisterSource(sid);
            }
            else
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Failed to create clone SoundChannel for {original.ShortName} at {clonePosition}");
                return;
            }

            isFirstIteration = original.isFirstIteration;
            startGain = original.startGain;
            startPitch = original.startPitch;

            obstructions = new List<Obstruction>(original.obstructions);
            Muffled = false;

            gainMultLF = original.gainMultLF;
            gainMultHF = original.gainMultHF;

            WorldPos = original.WorldPos;
            LocalPos = original.LocalPos;
            SimPos = original.SimPos;
            inWater = original.inWater;
            bothInWater = original.bothInWater;
            inContainer = original.inContainer;
            noPath = original.noPath;

            EuclideanDistance = original.EuclideanDistance;
            approximateDistance = original.approximateDistance;
            Eavesdropped = original.Eavesdropped;
            Hydrophoned = original.Hydrophoned;

            ChannelHull = original.ChannelHull;
            StatusEffect = original.StatusEffect;
            ItemComp = original.ItemComp;
            Item = original.Item;
            speakingClient = original.speakingClient;
            messageType = original.messageType;
    }
        public ChannelInfo CreateClone(Vector3 pos)
        {
            return new ChannelInfo(this, pos);
        }

        public ChannelInfo(SoundChannel channel, Hull? channelHull = null, StatusEffect? statusEffect = null, ItemComponent? itemComp = null, Client? speakingClient = null, ChatMessageType? messageType = null, bool dontMuffle = false, bool dontPitch = false)
        {
            if (speakingClient != null && messageType == null) { messageType = Util.GetMessageType(speakingClient); }
            else if (itemComp != null && Item == null) { Item = itemComp.Item; }
            else if (statusEffect != null && Item == null) { Item = statusEffect.soundEmitter as Item; }
            if (Item != null && channelHull == null) { channelHull = Item.CurrentHull; }

            this.dontMuffle = dontMuffle;
            this.dontPitch = dontPitch;

            Channel = channel;
            LongName = channel.Sound.Filename.ToLower();
            ShortName = Path.GetFileName(LongName);
            startGain = channel.Gain;
            startPitch = channel.FrequencyMultiplier;
            isFirstIteration = true;
            this.ChannelHull = channelHull;
            this.ItemComp = itemComp;
            this.StatusEffect = statusEffect;
            this.speakingClient = speakingClient;
            this.messageType = messageType;

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
        }

        // TODO This method takes arguments because sometimes the constructor is called without any of this info available (created from soundchannel ctor with no contex, should I even do this or allow the different types to make themselves later? pretty sure it caused popping previously.)
        public void Update(Hull? soundHull = null, StatusEffect? statusEffect = null, ItemComponent? itemComp = null, Client? speakingClient = null, ChatMessageType? messageType = null)
        {
            if (Channel == null) { return; }

            if (!isClone)
            {
                foreach(ChannelInfo clone in clones) { clone.Update(); }
            }
            else if (isClone)
            {
                UpdateMuffle();
                UpdateGain();
                UpdatePitch();
                return;
            }

            if (speakingClient != null && messageType == null) { messageType = Util.GetMessageType(speakingClient); }
            else if (itemComp != null && Item == null) { Item = itemComp.Item; }
            else if (statusEffect != null && Item == null) { Item = statusEffect.soundEmitter as Item; }
            if (Item != null && soundHull == null) { soundHull = Item.CurrentHull; }

            ChannelHull = soundHull ?? ChannelHull;
            this.ItemComp = itemComp;
            this.StatusEffect = statusEffect;
            this.speakingClient = speakingClient;
            this.messageType = messageType;

            if (Ignored)
            {
                EuclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos);
                Muffled = false;
                Pitch = 1;
                UpdateGain();
                UpdateVoice();
                return;
            }

            UpdateProperties();

            if (!ShouldSkipMuffleUpdate() || isFirstIteration)
            {
                UpdateMuffle();
            }

            UpdateGain();
            UpdatePitch();
            UpdateVoice();
        }

        private void UpdateVoice()
        {
            voiceUseMuffleFilter = Muffled;
            voiceUseRadioFilter = messageType == ChatMessageType.Radio && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters;
        }

        private void UpdateProperties()
        {
            // Set type here using data inaccessible to the Sound object.
            // Currently this is important to do every update because some of this information might not be present on the first update
            // Potentially I could do something like: if (Type == Unknown && !IsFirstIteration)
            DynamicType = SoundInfo.StaticType;
            if (speakingClient != null && messageType != null)
            {
                DynamicType = (messageType == ChatMessageType.Radio) ? SoundInfo.AudioType.RadioVoice : SoundInfo.AudioType.LocalVoice;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.flowSoundChannels, Channel)) >= 0)
            {
                DynamicType = SoundInfo.AudioType.FlowSound;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.fireSoundChannels, Channel)) >= 0)
            {
                DynamicType = SoundInfo.AudioType.FireSound;
            }

            if (AudioIsFromDoor && ignoredGap == null)
            {
                ignoredGap = Util.GetDoorSoundGap(ChannelHull, LocalPos);
            }

            Character? speakerCharacter = null;
            Limb? speakerMouth = null;
            if (AudioIsVoice)
            {
                speakerCharacter = speakingClient?.Character;
                speakerMouth = speakerCharacter?.AnimController?.GetLimb(LimbType.Head) ?? speakerCharacter?.AnimController?.GetLimb(LimbType.Torso);
                bool speakerIsAlive = speakerCharacter != null && !speakerCharacter.IsDead;
                if (!speakerIsAlive) { inWater = false; return; } // No point updating anything for dead speakers. Note: can only hear a dead speaker if the listener is dead (spectating) too.
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

            inWater = Util.SoundInWater(LocalPos, ChannelHull);
            inContainer = Item != null && !SoundInfo.IgnoreContainer && IsContainedWithinContainer(Item);
            EuclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos); // euclidean distance
        }

        private void UpdateMuffle()
        {
            bool shouldMuffle = false;
            bool shouldUseReverbBuffer = false; // Extended sounds only.
            MuffleType type = MuffleType.None;

            bool ambienceWhileUsingHydrophones = AudioIsAmbience && Listener.IsUsingHydrophones;
            if (ignoreLowpass && !ambienceWhileUsingHydrophones) // Always muffle ambience while using hydrophones.
            { 
                Muffled = false; MuffleStrength = 0; gainMultHF = 0; gainMultLF = 0;
                return; 
            };

            if (!isClone)
            {
                // Start debug log.
                debugObstructionsList = "";
                debugObstructionsCount = 0;
                if (ConfigManager.LocalConfig.DebugObstructions)
                {
                    string firstIterText = isFirstIteration ? " (NEW)" : "";
                    LuaCsLogger.Log($"Obstructions for \"{ShortName}\"{firstIterText}:", color: isFirstIteration ? Color.LightSeaGreen : Color.LightSkyBlue);
                }

                UpdateObstructions();

                // Finish debug log.
                if (ConfigManager.LocalConfig.DebugObstructions)
                {
                    if (debugObstructionsCount <= 0 || debugObstructionsList.IsNullOrEmpty()) { debugObstructionsList += "          None\n"; }
                    LuaCsLogger.Log(debugObstructionsList);
                }
            }

            // Calculate muffle strength based on obstructions.
            float gainMultHf = 1;
            float total = 0;
            float mult = config.DynamicFx ? config.DynamicMuffleStrengthMultiplier : 1;
            mult *= SoundInfo.MuffleInfluence;
            for (int i = 0; i < obstructions.Count; i++)
            {
                float strength = obstructions[i].GetStrength() * mult;
                total += strength;
                gainMultHf *= 1 - strength; // Exponential muffling
            }
            MuffleStrength = Math.Clamp(1 - gainMultHf, 0, 1);
            float gainMultLf = 1 - total / 4; // Arbitrary value. When total reaches 4 even low frequencies gain is 0.
            if (!config.AutoAttenuateMuffledSounds) { gainMultLf = 1; }
            gainMultHF = Math.Clamp(gainMultHf, 0, 1);
            gainMultLF = Math.Clamp(gainMultLf, 0.1f, 1);

            if (config.DynamicFx)
            {
                UpdateDynamicFx();
            }
            else if ((config.StaticFx || AudioIsVoice) && MuffleStrength >= config.StaticMinLightMuffleThreshold)
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

            // Enable reverb buffers for extended sounds. Is not relevant to the DynamicFx reverb.
            if (config.StaticFx && config.StaticReverbEnabled && !shouldMuffle && !Channel.Looping && !IgnoreAirReverb &&
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
            if (Plugin.EffectsManager == null) return;

            float transitionFactor = AudioIsFlow || AudioIsFire ? config.DynamicMuffleFlowFireTransitionFactor : config.DynamicMuffleTransitionFactor;
            if (IsInUpdateLoop && !isFirstIteration && transitionFactor > 0)
            {
                float maxStep = (float)(transitionFactor * muffleUpdateInterval);
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
            obstructions.Clear();

            noPath = false;
            approximateDistance = null;

            // Flow/fire sounds are handled differently.
            if (AudioIsFlow || AudioIsFire)
            {
                UpdateFlowFireObstructions();
                return;
            }

            // Muffle radio comms underwater to make room for bubble sounds.
            if (AudioIsRadioVoice && speakingClient != null)
            {
                if (BubbleManager.ShouldPlayBubbles(speakingClient.Character))
                {
                    AddObstruction(Obstruction.WaterSurface, "Drowning");
                }

                // Return because radio comms aren't muffled under any other circumstances.
                dontReverb = !config.DynamicReverbRadio;
                return;
            }
            else if (AudioIsLocalVoice && speakingClient != null)
            {
                dontReverb = !config.DynamicReverbLocal;
            }

            // Mildly muffle sounds in containers. Don't return yet because the container could be submerged with the sound inside.
            if (inContainer)
            {
                AddObstruction(Obstruction.DoorThick, "In Container");
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
            if (soundInWater != earsInWater)
            {
                Obstruction obstruction = Obstruction.WaterSurface;
                if (SoundInfo.PropagateWalls && !propagated) { obstruction = Obstruction.WallThin; }
                if (!ignoreSurface) { AddObstruction(obstruction, "With path - air-water barrier"); }
            }
        }

        public void DisposeClones()
        {
            foreach (ChannelInfo clone in clones)
            {
                clone.Channel?.Dispose();
            }
            clones.Clear();
        }

        private void UpdatePathObstructionsAdvanced()
        {
            if (Channel == null) { return; } // Just in case.

            Hull? soundHull = ChannelHull;
            Hull? listenerHull = Listener.FocusedHull;
            HashSet<Hull> listenerConnectedHulls = Listener.ConnectedHulls;

            noPath = listenerHull != soundHull && (listenerHull == null || soundHull == null || !listenerConnectedHulls.Contains(soundHull!));

            int numWallObstructions = 0;
            int numDoorObstructions = 0;
            int numWaterSurfaceObstructions = 0;

            // 1. Get occlusion obstructions. Ray cast to check how many walls are between the sound and the listener.
            if (config.OccludeSounds)
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
            }

            // 2. Propagate to the correct hull, updating wall obstructions. Propagation allows certain sounds near a wall to propagate to the opposite side.
            if (!SoundInfo.IgnorePath && SoundInfo.PropagateWalls && noPath)
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
            bool simulateSoundDirection = config.RealSoundDirectionsEnabled && config.RealSoundDirectionsMax > 0 && !AudioIsVoice;
            int targetNumResults = simulateSoundDirection ? config.RealSoundDirectionsMax : 1; // Always need to get at least one result, even if sound directions are disabled
            targetNumResults = Math.Max(targetNumResults, 3); // Cap the maximum clones to 3 for performance and practicality.
            List<SoundPathfinder.PathfindingResult> topResults;
            if (AudioIsVoice) { lock (VoicePathResultsLock) { topResults = VoicePathResults; } }
            else { topResults = SoundPathfinder.FindShortestPaths(WorldPos, soundHull, Listener.WorldPos, listenerHull, listenerHull?.Submarine, targetNumResults, maxRawDistance: Channel.Far * 2, ignoredGap); }
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
            if (SoundInfo.IgnorePath)
            {
                approximateDistance = null;
                AddObstruction(Obstruction.Suit, "Ignored path"); // Add a slight muffle to these sounds.
                numWallObstructions = 0;
                numDoorObstructions = 0;
                // Probably shouldn't reset water surfaces.
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

            // 7. Add obstructions. TODO this works but is dreadful.
            bool noDirectionalSounds = !simulateSoundDirection || Channel == null || !Channel.IsPlaying || Channel.Sound == null || (!noPath && numWallObstructions <= 0);

            float totalDoorPathResistance = 0;
            float totalWallPathResistance = 0;
            for (int i = 0; i < numWallObstructions; i++) { totalWallPathResistance += Obstruction.WallThick.GetStrength(); }
            for (int i = 0; i < obstructions.Count; i++) { if (obstructions[i] == Obstruction.WaterSurface) { totalWallPathResistance += Obstruction.WaterSurface.GetStrength(); } }
            for (int i = 0; i < numDoorObstructions; i++) { totalDoorPathResistance += Obstruction.DoorThick.GetStrength(); }
            for (int i = 0; i < numWaterSurfaceObstructions; i++) { totalDoorPathResistance += Obstruction.WaterSurface.GetStrength(); }

            if (numWallObstructions <= 0) { totalWallPathResistance = float.MaxValue; }
            if (numDoorObstructions <= 0) { totalDoorPathResistance = float.MaxValue; }

            bool pathLeastResistanceIsWalls = totalWallPathResistance < totalDoorPathResistance || !noDirectionalSounds;

            if (pathLeastResistanceIsWalls)
            {
                if (propagated) { numWallObstructions--; AddObstruction(Obstruction.WallThin, "Propagated"); } // Replacing thick wall with thin.

                for (int i = 0; i < numWallObstructions; i++)
                {
                    if (noPath || Channel?.Far < approximateDistance || topResults.Count <= 0 || !noDirectionalSounds) { AddObstruction(Obstruction.WallThick, "Occlusion - no path"); }
                    else { AddObstruction(Obstruction.WallThin, "Occlusion - with path"); } // Wall occlusion isn't as strong if there's a path to the listener.
                }
            }
            else
            {
                if (propagated) { numDoorObstructions--; AddObstruction(Obstruction.DoorThin, "Propagated"); }
                for (int i = 0; i < numDoorObstructions; i++) { AddObstruction(Obstruction.DoorThick, "Passing through door"); }
                for (int j = 0; j < numWaterSurfaceObstructions; j++) { AddObstruction(Obstruction.WaterSurface, "Passing through air-water barrier"); }
            }

            // 8. Add/remove/update clones. For multiple sounds playing at different positions to simulate the sounds pathing.

                // Dispose all clones if there's a path to the sound with no occlusion.
            if (noDirectionalSounds)
            {
                DisposeClones();
                return;
            }

            approximateDistance = null; // The parent sound should always default to euclidean dist.

            // Prune clone count to the perfect amount.
            while (clones.Count > targetNumResults)
            {
                clones[0].Channel?.Dispose();
                clones.Remove(clones[0]);
            }

            for (int i = 0; i < topResults.Count; i++)
            {
                Vector2 direction = Util.GetRelativeDirection(Listener.WorldPos, topResults[i].LastIntersectionPos);
                Vector3 clonePos = new Vector3(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y, 0.0f) + new Vector3(direction, 0.0f);
                float cloneDist = topResults[i].RawDistance;
                ChannelInfo clone;

                // Too few clones. Create additional.
                if (clones.Count < targetNumResults)
                {
                    clone = CreateClone(clonePos);
                    if (clone != null)
                    {
                        clones.Add(clone);
                    }
                }

                clone = clones[i];
                
                if (clone == null || clone.Channel == null || !clone.Channel.IsPlaying) { continue; }

                //int alError;
                //Al.GetError();

                //uint sourceId = clone.Channel.Sound.Owner.GetSourceFromIndex(clone.Channel.Sound.SourcePoolIndex, clone.Channel.ALSourceIndex);

                // Attach clone position to listener
                //Al.Sourcei(sourceId, Al.SourceRelative, Al.True);
                //if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.NewMessage($"[SoundproofWalls] Failed to make clone source relative, {Al.GetErrorString(alError)}"); return; }

                // Set clone direction
                //Al.Source3f(sourceId, Al.Position, direction.X, direction.Y, 0.0f);
                //if ((alError = Al.GetError()) != Al.NoError) { DebugConsole.NewMessage($"[SoundproofWalls] Failed to set clone direction, {Al.GetErrorString(alError)}"); return; }

                clone.debugObstructionsCount = 0;
                clone.debugObstructionsList = ""; // We don't even do clone debugging but I'm leaving this here in case we ever do

                clone.obstructions = new List<Obstruction>(clonedObstructions);
                for (int j = 0; j < topResults[i].ClosedDoorCount; j++) { clone.AddObstruction(Obstruction.DoorThick, "Clone - passing through door"); }
                for (int j = 0; j < topResults[i].WaterSurfaceCrossings; j++) { clone.AddObstruction(Obstruction.WaterSurface, "Clone - passing through air-water barrier"); }
                clone.approximateDistance = cloneDist;
                clone.Channel.Position = clonePos;

                //LuaCsLogger.Log($"CLONE {i} FOR {ShortName}: Parent Position: {Channel.Position}\nClone Pos: {clone.Channel.Position}\nCloneGain: {clone.Channel.Gain}\nClone Pitch {clone.Channel.FrequencyMultiplier}\nObstructions: {clone.obstructions.Count}\nListener pos {Listener.WorldPos}");
            }

        }

        private void UpdatePathObstructionsSimple()
        {
            Hull? soundHull = ChannelHull;
            Hull? listenerHull = Listener.FocusedHull;
            HashSet<Hull> listenerConnectedHulls = Listener.ConnectedHulls;

            if (!AudioIsFromDoor) // Check if there's a path to the sound.
            {
                noPath = listenerHull != soundHull && (listenerHull == null || soundHull == null || !listenerConnectedHulls.Contains(soundHull!));
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

            if (!noPath && approximateDistance == null) // There is a valid path - get the approx distance if not already (door sounds get earlier).
            {
                approximateDistance = GetApproximateDistance(Listener.LocalPos, LocalPos, listenerHull, soundHull, ignoredGap: ignoredGap);
            }

            bool exceedsRange = Distance >= Channel.Far; // Refers to the range being greater than the approx dist NOT the euclidean distance because then the sound wouldn't be playing.

            // Allow certain sounds near a wall to be faintly heard on the opposite side.
            // Sounds outside the sub can propagate in, but sounds sounds inside cannot propagate out.
            if (SoundInfo.IgnorePath)
            {
                approximateDistance = null; // Null this so the Distance property defaults to euclidean dist.
                AddObstruction(Obstruction.Suit, "Ignored path"); // Add a slight muffle to these sounds.
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
                if (Item != null && Item.HasTag(new Identifier("deepdivinglarge")) && LongName.EndsWith("WEAPONS_chargeUp.ogg")) { mult *= config.VanillaExosuitVolumeMultiplier; }
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

            float mEfficiency = config.SidechainMuffleInfluence;
            float thisSidechainRelease = SoundInfo.SidechainRelease * (1 - MuffleStrength * mEfficiency) + config.SidechainReleaseMaster;
            float thisSidechainStartingStrength = SoundInfo.SidechainMult * (1 - MuffleStrength * mEfficiency) * config.SidechainIntensityMaster;
            float globalSidechainStartingStrength = Plugin.Sidechain.SidechainRawStartValue;
            // If a more powerful loud sound is playing than the current loud sound.
            if (IsLoud && Plugin.Sidechain.ActiveSoundGroup != SoundInfo.CustomSound && globalSidechainStartingStrength > thisSidechainStartingStrength)
            {
                float overpowerMult = Math.Clamp(1 - (globalSidechainStartingStrength - thisSidechainStartingStrength), 0f, 1f);
                mult *= MathHelper.Lerp(1, overpowerMult, Plugin.Sidechain.CompletionRatio);
            }

            // Start sidechain release for loud sounds.
            if (IsLoud && (Channel.Looping || AudioIsVoice || !Channel.Looping && !sidechainTriggered))
            {
                // The strength of the sidechaining slightly decreases the more muffled the loud sound is.
                Plugin.Sidechain.StartRelease(thisSidechainStartingStrength, thisSidechainRelease, SoundInfo.CustomSound);
                sidechainTriggered = true; // Only trigger sidechaining once for non-looping sounds in case UpdateNonLoopingSounds is enabled.
            }
            else if (!IsLoud) { mult *= 1 - Plugin.Sidechain.SidechainMultiplier; }

            // Replace OpenAL's distance attenuation using the approximate distance gathered from traversing gaps.
            // Only do this if there's a noticable difference between the euclideanDistance that OpenAL uses and the approximate distance,
            // because crossing the max distance threshold rapidly is noticably less smooth when applied this way, despite using the same formula.
            bool ignoreGainFade = false;
            if ((config.AttenuateWithApproximateDistance || isClone) && approximateDistance != null && MathF.Abs(Distance - EuclideanDistance) > 100)
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
                float distMult = CalculateLinearDistanceClampedMult(distance, Channel.Near, Channel.Far);

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

            float mEfficiency = config.SidechainMuffleInfluence;
            float thisSidechainRelease = SoundInfo.SidechainRelease * (1 - MuffleStrength * mEfficiency) + config.SidechainReleaseMaster;
            float thisSidechainStartingStrength = SoundInfo.SidechainMult * (1 - MuffleStrength * mEfficiency) * config.SidechainIntensityMaster;
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

            // Get current gain.
            float currentGain = 1;
            if (AudioIsFlow) currentGain = Math.Max(SoundPlayer.flowVolumeRight[flowFireChannelIndex], SoundPlayer.flowVolumeLeft[flowFireChannelIndex]);
            else if (AudioIsFire) currentGain = Math.Max(SoundPlayer.fireVolumeRight[flowFireChannelIndex], SoundPlayer.fireVolumeLeft[flowFireChannelIndex]);

            Gain = currentGain * mult;
        }

        private void UpdateFlowFirePitch()
        {
            float mult = 1;
            if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
            if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1;
            if (Listener.IsUsingHydrophones) mult += config.HydrophonePitchMultiplier - 1;
            Pitch = 1 * mult;
        }

        private void AddObstruction(Obstruction obs, string debugContext)
        {
            obstructions.Add(obs);
            if (config.DebugObstructions) { DebugObstructions(debugContext); }
        }

        private void DebugObstructions(string reason)
        {
            debugObstructionsList += $"          {debugObstructionsCount + 1}. {obstructions[debugObstructionsCount]} {obstructions[debugObstructionsCount].GetStrength() * SoundInfo.MuffleInfluence * (config.DynamicFx ? config.DynamicMuffleStrengthMultiplier : 1)} ({reason})\n";
            debugObstructionsCount++;
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
