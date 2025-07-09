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
        private enum AudioType
        {
            BaseSound,
            DoorSound,
            FlowSound,
            FireSound,
            LocalVoice,
            RadioVoice
        }

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

        public float MuffleStrength;
        private float startGain;
        private float startPitch;
        private float gainMultHF;
        private float trailingGainMultHF;
        private float gainMultLF;
        private float trailingGainMultLF;
        private bool isFirstIteration;
        private List<Obstruction> obstructions = new List<Obstruction>();
        private List<ChannelInfo> clones = new List<ChannelInfo>();

        // Temporary solution to the multi-thread problem of voice and sounds. Get this information on the main thread.
        public IEnumerable<Body> VoiceOcclusions = new List<Body>();
        public readonly object VoiceOcclusionsLock = new object();
        public List<SoundPathfinder.PathfindingResult> VoicePathResults = new List<SoundPathfinder.PathfindingResult>();
        public readonly object VoicePathResultsLock = new object();

        private float euclideanDistance;
        private float? approximateDistance;
        public Vector2 WorldPos;
        private Vector2 localPos;
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

        private AudioType type;
        private bool audioIsFromDoor => type == AudioType.DoorSound;
        public bool audioIsVoice => type == AudioType.LocalVoice;
        public bool audioIsRadio => type == AudioType.RadioVoice;
        private bool audioIsFlow => type == AudioType.FlowSound;
        private bool audioIsFire => type == AudioType.FireSound;

        private bool dontMuffle = false;
        private bool dontPitch = false;
        private bool dontReverb = false;
        private bool ignoreLowpass => SoundInfo.IgnoreLowpass || (dontMuffle && !SoundInfo.ForceLowpass);
        private bool ignorePitch => SoundInfo.IgnorePitch || dontPitch;
        public bool IgnoreReverb => SoundInfo.IgnoreReverb || (dontReverb && !SoundInfo.ForceReverb) || Listener.IsSpectating;
        public bool Ignored => SoundInfo.IgnoreAll || (ignoreLowpass && ignorePitch);
        public bool IsLoud => SoundInfo.IsLoud;
        public bool IsInUpdateLoop => Channel.Looping || audioIsVoice || audioIsRadio || config.UpdateNonLoopingSounds;

        private int flowFireChannelIndex;

        private bool rolloffFactorModified = false;
        private float rolloffFactor
        {
            get
            {
                float rolloff = 1;
                uint sourceId = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
                int alError;
                Al.GetError();
                Al.GetSourcef(sourceId, Al.RolloffFactor, out rolloff);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to get rolloff factor for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                return rolloff;
            }
            set
            {
                uint sourceId = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
                int alError;
                Al.GetError();
                Al.Sourcef(sourceId, Al.RolloffFactor, value);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to set rolloff factor of {value} for source ID: {sourceId}, {Al.GetErrorString(alError)}");
            }
        }
        public float Gain
        {
            get { return Channel.gain; }
            set
            {
                float currentGain = Channel.gain;
                float targetGain = value;

                if (targetGain != currentGain)
                {
                    Channel.Gain = value;
                }
            }
        }
        public float Pitch
        {
            get { return Channel.frequencyMultiplier; }
            set
            {
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
        }
        public bool Muffled
        {
            get { return Channel.muffled; }
            set
            {
                bool currentMuffle = Muffled;
                bool targetMuffle = value && !ignoreLowpass;

                if (targetMuffle != currentMuffle || muffleType != lastMuffleType || (config.StaticFx && StaticIsUsingReverbBuffer != StaticShouldUseReverbBuffer))
                {
                    Channel.Muffled = targetMuffle;
                    lastMuffleType = muffleType;
                }
            }
        }

        public float Distance
        {
            get { return approximateDistance ?? euclideanDistance; }
        }

        public bool StaticShouldUseReverbBuffer = false; // For extended sounds only.
        public bool StaticIsUsingReverbBuffer = false; // For extended sounds only.

        private bool sidechainTriggered = false;
        private bool propagated = false;
        private bool eavesdropped = false;
        public bool Hydrophoned = false;

        private double muffleUpdateInterval = Timing.Step;
        private double lastMuffleCheckTime = 0;

        public Hull? ChannelHull = null;
        public SoundInfo SoundInfo;
        private StatusEffect? statusEffect = null;
        private ItemComponent? itemComp = null;
        private Item? item = null;
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
                throw new Exception("Failed to create clone SoundChannel");
            }

            isFirstIteration = original.isFirstIteration;
            startGain = original.startGain;
            startPitch = original.startPitch;

            obstructions = new List<Obstruction>(original.obstructions);
            Muffled = false;

            gainMultLF = original.gainMultLF;
            gainMultHF = original.gainMultHF;

            WorldPos = original.WorldPos;
            localPos = original.localPos;
            SimPos = original.SimPos;
            inWater = original.inWater;
            bothInWater = original.bothInWater;
            inContainer = original.inContainer;
            noPath = original.noPath;
            type = original.type;

            euclideanDistance = original.euclideanDistance;
            approximateDistance = original.approximateDistance;
            eavesdropped = original.eavesdropped;
            Hydrophoned = original.Hydrophoned;

            ChannelHull = original.ChannelHull;
            statusEffect = original.statusEffect;
            itemComp = original.itemComp;
            item = original.item;
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
            else if (itemComp != null && item == null) { item = itemComp.Item; }
            else if (statusEffect != null && item == null) { item = statusEffect.soundEmitter as Item; }
            if (item != null && channelHull == null) { channelHull = item.CurrentHull; }

            this.dontMuffle = dontMuffle;
            this.dontPitch = dontPitch;

            Channel = channel;
            LongName = channel.Sound.Filename.ToLower();
            ShortName = Path.GetFileName(LongName);
            startGain = channel.Gain;
            startPitch = channel.FrequencyMultiplier;
            isFirstIteration = true;
            this.ChannelHull = channelHull;
            this.itemComp = itemComp;
            this.statusEffect = statusEffect;
            this.speakingClient = speakingClient;
            this.messageType = messageType;

            SoundInfo = SoundInfoManager.EnsureGetSoundInfo(channel.Sound);

            lastMuffleType = MuffleType.None;

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
            else if (itemComp != null && item == null) { item = itemComp.Item; }
            else if (statusEffect != null && item == null) { item = statusEffect.soundEmitter as Item; }
            if (item != null && soundHull == null) { soundHull = item.CurrentHull; }

            ChannelHull = soundHull ?? ChannelHull;
            this.itemComp = itemComp;
            this.statusEffect = statusEffect;
            this.speakingClient = speakingClient;
            this.messageType = messageType;

            if (Ignored)
            {
                euclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos);
                Muffled = false;
                Pitch = 1;
                UpdateGain();
                return;
            }

            bool skipMuffleUpdate = false;
            double currentTime = Timing.TotalTime;

            if (itemComp != null && itemComp.loopingSoundChannel == Channel)
            {
                muffleUpdateInterval = config.ComponentMuffleUpdateInterval;
                if (currentTime < itemComp.lastMuffleCheckTime + muffleUpdateInterval)
                {
                    skipMuffleUpdate = true;
                }
                else
                {
                    itemComp.lastMuffleCheckTime = (float)currentTime; // Why is this one a float? devs please fix.
                }
            }
            else if (statusEffect != null)
            {
                muffleUpdateInterval = config.StatusEffectMuffleUpdateInterval;
                if (currentTime < StatusEffect.LastMuffleCheckTime + muffleUpdateInterval)
                {
                    skipMuffleUpdate = true;
                }
                else
                {
                    StatusEffect.LastMuffleCheckTime = currentTime;
                }
            }
            else if (!Channel.Looping && config.UpdateNonLoopingSounds)
            {
                muffleUpdateInterval = config.NonLoopingSoundMuffleUpdateInterval;
                if (currentTime < lastMuffleCheckTime + muffleUpdateInterval)
                {
                    skipMuffleUpdate = true;
                }
                else
                {
                    lastMuffleCheckTime = currentTime;
                }
            }

            UpdateProperties();

            if (!skipMuffleUpdate || isFirstIteration)
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
            Character listener = Character.Controlled;
            Character? speaker = speakingClient?.Character;
            Limb? speakerHead = speaker?.AnimController?.GetLimb(LimbType.Head);

            WorldPos = speakerHead?.WorldPosition ?? Util.GetSoundChannelWorldPos(Channel);
            ChannelHull = ChannelHull ?? Hull.FindHull(WorldPos, speaker?.CurrentHull ?? listener?.CurrentHull);
            localPos = Util.LocalizePosition(WorldPos, ChannelHull);
            SimPos = localPos / 100;
            inWater = Util.SoundInWater(localPos, ChannelHull);
            inContainer = item != null && !SoundInfo.IgnoreContainer && IsContainedWithinContainer(item);
            euclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos); // euclidean distance

            // Determine the audio type.
            // Note: For now, this is done every update because many SoundInfos are made before necessary info about their type is known.
            //       This can change if SoundInfos are no longer made within the SoundChannel constructor.
            type = AudioType.BaseSound;
            if (speakingClient != null && messageType != null)
            {
                type = (messageType == ChatMessageType.Radio) ? AudioType.RadioVoice : AudioType.LocalVoice;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.flowSoundChannels, Channel)) != -1)
            {
                type = AudioType.FlowSound;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.fireSoundChannels, Channel)) != -1)
            { 
                type = AudioType.FireSound;
            }
            else if (ShortName.Contains("door") && !ShortName.Contains("doorbreak") || ShortName.Contains("duct") && !ShortName.Contains("ductbreak"))
            {
                type = AudioType.DoorSound;
            }
        }

        private void UpdateMuffle()
        {
            bool shouldMuffle = false;
            bool shouldUseReverbBuffer = false; // Extended sounds only.
            MuffleType type = MuffleType.None;

            if (ignoreLowpass) { Muffled = false; return; };

            if (!isClone)
            { 
                UpdateObstructions();

                if (ConfigManager.LocalConfig.DebugObstructions)
                {
                    string firstIterText = isFirstIteration ? " (NEW)" : "";
                    LuaCsLogger.Log($"Obstructions for \"{ShortName}\"{firstIterText}:", color: isFirstIteration ? Color.LightSeaGreen : Color.LightSkyBlue);
                    for (int i = 0; i < obstructions.Count; i++)
                    {
                        LuaCsLogger.Log($"          {i + 1}. {obstructions[i]} {obstructions[i].GetStrength()}");
                    }
                    if (obstructions.Count <= 0) { LuaCsLogger.Log($"          None"); }
                }
            }

            // Calculate muffle strength based on obstructions.
            float gainMultHf = 1;
            float total = 0;
            float mult = config.DynamicFx ? config.DynamicMuffleStrengthMultiplier : 1;
            for (int i = 0; i < obstructions.Count; i++)
            {
                float strength = obstructions[i].GetStrength();
                total += strength * mult;
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
            else if ((config.StaticFx || audioIsVoice || audioIsRadio) && MuffleStrength >= config.StaticMinLightMuffleThreshold)
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
            if (config.StaticFx && config.StaticReverbEnabled && !shouldMuffle && !Channel.Looping &&
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

            float transitionFactor = config.DynamicMuffleTransitionFactor;
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
            eavesdropped = false;
            Hydrophoned = false;
            propagated = false;
            obstructions.Clear();

            noPath = false;
            approximateDistance = null;

            // Flow/fire sounds are handled differently.
            if (audioIsFlow || audioIsFire)
            {
                UpdateFlowFireObstructions();
                return;
            }

            // Muffle radio comms underwater to make room for bubble sounds.
            if (audioIsRadio && speakingClient != null)
            {
                if (BubbleManager.ShouldPlayBubbles(speakingClient.Character))
                {
                    obstructions.Add(Obstruction.WaterSurface);
                }

                // Return because radio comms aren't muffled under any other circumstances.
                dontReverb = !config.DynamicReverbRadio;
                return;
            }

            // Mildly muffle sounds in containers. Don't return yet because the container could be submerged with the sound inside.
            if (inContainer)
            {
                obstructions.Add(Obstruction.DoorThick);
            }

            // Early return for spectators.
            if (Listener.IsSpectating)
            {
                if (inWater && !SoundInfo.IgnoreSurface)
                {
                    obstructions.Add(Obstruction.WaterSurface);
                }
                // Return because nothing else can muffle sounds for spectators.
                return;
            }

            if (Listener.IsWearingDivingSuit && config.MuffleDivingSuits)
            {
                obstructions.Add(Obstruction.Suit);
            }

            // Hydrophone check. Muffle sounds inside your own sub while still hearing sounds in other subs/structures.
            if (Listener.IsUsingHydrophonesNow)
            {
                // Sound can be heard clearly outside in the water.
                if (ChannelHull == null || config.HydrophoneHearEngine && itemComp as Engine != null)
                {
                    Hydrophoned = true;
                    obstructions.Add(Obstruction.Suit);
                    return; // No path obstructions
                }
                // Sound is in a station or other submarine.
                else if (config.HydrophoneHearIntoStructures && ChannelHull != null && ChannelHull.Submarine != LightManager.ViewTarget?.Submarine)
                {
                    Hydrophoned = true;
                    obstructions.Add(Obstruction.WallThin);
                    return; // No path obstructions
                }
                // Sound is in own submarine.
                else if (config.HydrophoneMuffleOwnSub)
                {
                    obstructions.Add(Obstruction.WaterSurface);
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
                if (earsInWater) { obstructions.Add(Obstruction.WaterBody); }
                if (soundInWater != earsInWater) { obstructions.Add(Obstruction.WaterSurface); }
                return;
            }

            // From here it is assumed there is a path to the sound,
            // usually meaning the listener and the water share the same hull.

            // Ears in water.
            if (earsInWater)
            {
                if (!ignoreSubmersion) { obstructions.Add(Obstruction.WaterBody); }
                // Sound in same body of water, return because no need to apply water surface.
                if (soundInWater) { bothInWater = true; return; }
            }

            // Separated by water surface.
            if (soundInWater != earsInWater)
            {
                Obstruction obstruction = Obstruction.WaterSurface;
                if (SoundInfo.PropagateWalls && !propagated) { obstruction = Obstruction.WallThin; }
                if (!ignoreSurface) { obstructions.Add(obstruction); }
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
                if (audioIsVoice || audioIsRadio) { lock (VoiceOcclusionsLock) { bodies = VoiceOcclusions; } }
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
                        inWater = Util.SoundInWater(localPos, ChannelHull); // Update for water obstruction check later.
                        break;
                    }
                }
            }

            // 3. Get path obstructions.
            bool simulateSoundDirection = config.RealSoundDirectionsEnabled && config.RealSoundDirectionsMax > 0 && !audioIsRadio && !audioIsVoice;
            int targetNumResults = simulateSoundDirection ? config.RealSoundDirectionsMax : 1; // Always need to get at least one result, even if sound directions are disabled
            targetNumResults = Math.Max(targetNumResults, 3); // Cap the maximum clones to 3 for performance and practicality.
            List<SoundPathfinder.PathfindingResult> topResults;
            if (audioIsVoice || audioIsRadio) { lock (VoicePathResultsLock) { topResults = VoicePathResults; } }
            else { topResults = SoundPathfinder.FindShortestPaths(WorldPos, soundHull, Listener.WorldPos, listenerHull, listenerHull?.Submarine, targetNumResults, maxRawDistance: Channel.Far, isDoorSound: audioIsFromDoor); }
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
                obstructions.Add(Obstruction.Suit); // Add a slight muffle to these sounds.
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
                eavesdropped = true;
                if (config.EavesdroppingMuffle) { obstructions.Add(Obstruction.DoorThin); }
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
                if (propagated) { numWallObstructions--; obstructions.Add(Obstruction.WallThin); } // Replacing thick wall with thin.

                for (int i = 0; i < numWallObstructions; i++)
                {
                    if (noPath || Channel?.Far < approximateDistance || !noDirectionalSounds) { obstructions.Add(Obstruction.WallThick); }
                    else { obstructions.Add(Obstruction.WallThin); } // Wall occlusion isn't as strong if there's a path to the listener.
                }
            }
            else
            {
                if (propagated) { numDoorObstructions--; obstructions.Add(Obstruction.DoorThin); }
                for (int i = 0; i < numDoorObstructions; i++) { obstructions.Add(Obstruction.DoorThick); }
                for (int j = 0; j < numWaterSurfaceObstructions; j++) { obstructions.Add(Obstruction.WaterSurface); }
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

                clone.obstructions = new List<Obstruction>(clonedObstructions);
                for (int j = 0; j < topResults[i].ClosedDoorCount; j++) { clone.obstructions.Add(Obstruction.DoorThick); }
                for (int j = 0; j < topResults[i].WaterSurfaceCrossings; j++) { clone.obstructions.Add(Obstruction.WaterSurface); }
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

            if (!audioIsFromDoor) // Check if there's a path to the sound.
            {
                noPath = listenerHull != soundHull && (listenerHull == null || soundHull == null || !listenerConnectedHulls.Contains(soundHull!));
            }
            else // Exception for door opening/closing sounds - must check hulls while ignorning the doorway gap.
            {
                float distance = GetApproximateDistance(Listener.LocalPos, localPos, listenerHull, soundHull, ignoredGap: Util.GetDoorSoundGap(audioIsFromDoor, ChannelHull, localPos));
                noPath = distance >= float.MaxValue;
                if (!noPath)
                {
                    approximateDistance = distance;
                }
            }

            if (!noPath && approximateDistance == null) // There is a valid path - get the approx distance if not already (door sounds get earlier).
            {
                approximateDistance = GetApproximateDistance(Listener.LocalPos, localPos, listenerHull, soundHull, ignoredGap: Util.GetDoorSoundGap(audioIsFromDoor, ChannelHull, localPos));
            }

            bool exceedsRange = Distance >= Channel.Far; // Refers to the range being greater than the approx dist NOT the euclidean distance because then the sound wouldn't be playing.

            // Allow certain sounds near a wall to be faintly heard on the opposite side.
            // Sounds outside the sub can propagate in, but sounds sounds inside cannot propagate out.
            if (SoundInfo.IgnorePath)
            {
                approximateDistance = null; // Null this so the Distance property defaults to euclidean dist.
                obstructions.Add(Obstruction.Suit); // Add a slight muffle to these sounds.
            }
            else if (noPath || exceedsRange) // One extra feature the simple obstruction function has is allowing sounds to propagate when out of range.
            {
                if (SoundInfo.PropagateWalls)
                {
                    foreach (Hull hull in Listener.ConnectedHulls)
                    {
                        if (SoundPropagatesToHull(hull, WorldPos))
                        {
                            obstructions.Add(Obstruction.WallThin);
                            ChannelHull = hull;
                            inWater = Util.SoundInWater(localPos, ChannelHull); // Update for water obstruction check later.
                            propagated = true;
                            break;
                        }
                    }
                }

                if (!propagated)
                {
                    obstructions.Add(Obstruction.WallThick);
                }
            }

            // Assign 'eavesdropped' variable and add obstruction.
            // Make sure to use the 'ChannelHull' variable and not 'soundHull' as 'ChannelHull' may have been updated via propagation
            if (Listener.IsEavesdropping &&
                ChannelHull != null &&
                ChannelHull != Listener.CurrentHull &&
                listenerConnectedHulls.Contains(ChannelHull))
            {
                eavesdropped = true;
                if (config.EavesdroppingMuffle) { obstructions.Add(Obstruction.DoorThin); }
            }

            UpdateWaterObstructions();
        }

        private void UpdateGain()
        {
            // Flow/fire sounds are handled differently.
            if (audioIsFlow || audioIsFire)
            {
                UpdateFlowFireGain();
                return;
            }

            float mult = 1;
            float currentGain = itemComp?.GetSoundVolume(itemComp.loopingSound) ?? startGain;

            mult += SoundInfo.GainMult - 1;

            // Radio can only be muffled when the sender is drowning.
            if (audioIsRadio)
            {
                mult += MathHelper.Lerp(config.UnmuffledVoiceVolumeMultiplier, config.MuffledVoiceVolumeMultiplier, MuffleStrength) - 1;
            }
            else if (audioIsVoice)
            {
                mult += MathHelper.Lerp(config.UnmuffledVoiceVolumeMultiplier, config.MuffledVoiceVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingVoiceVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }
            // Either a component or status effect sound.
            else if (Channel.Looping)
            {
                mult += MathHelper.Lerp(config.UnmuffledLoopingVolumeMultiplier, config.MuffledLoopingVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }

                // Tweak for vanilla exosuit volume.
                if (item != null && item.HasTag(new Identifier("deepdivinglarge")) && LongName.EndsWith("WEAPONS_chargeUp.ogg")) { mult *= config.VanillaExosuitVolumeMultiplier; }
            }
            // Single (non-looping sound).
            else
            {
                mult += MathHelper.Lerp(config.UnmuffledSoundVolumeMultiplier, config.MuffledSoundVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }

            // Note that the following multipliers may also apply to radio channels.

            // Fade transition between two hull soundscapes when eavesdropping.
            float eavesdropEfficiency = EavesdropManager.Efficiency;
            if (eavesdropEfficiency > 0 && (!audioIsRadio || config.EavesdroppingDucksRadio))
            {
                if (!eavesdropped) { mult *= Math.Clamp(1 - eavesdropEfficiency * (1 / config.EavesdroppingThreshold), 0.1f, 1); }
                else if (eavesdropped) { mult *= Math.Clamp(eavesdropEfficiency * (1 / config.EavesdroppingThreshold) - 1, 0.1f, 1); }
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
            if (IsLoud && (Channel.Looping || audioIsVoice || audioIsRadio || !Channel.Looping && !sidechainTriggered))
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
            if ((config.AttenuateWithApproximateDistance || isClone) && approximateDistance != null && MathF.Abs(Distance - euclideanDistance) > 100)
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

                float distance = newModel == DistanceModel.Euclidean ? euclideanDistance : Distance;
                float distMult = CalculateLinearDistanceClampedMult(distance, Channel.Near, Channel.Far);
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
            if (ignorePitch)
            {
                Pitch = 1;
                return;
            };

            // Flow/fire sounds are handled differently.
            if (audioIsFlow || audioIsFire)
            {
                UpdateFlowFirePitch();
                return;
            }

            float mult = 1;
            float currentPitch = startPitch;

            mult += SoundInfo.PitchMult - 1;

            // Voice.
            if (audioIsVoice || audioIsRadio)
            {
                mult += MathHelper.Lerp(config.UnmuffledVoicePitchMultiplier, config.MuffledLoopingPitchMultiplier, MuffleStrength) - 1;
            }
            // Either a component or status effect sound.
            else if (Channel.Looping)
            {
                mult += MathHelper.Lerp(config.UnmuffledSoundPitchMultiplier, config.MuffledLoopingPitchMultiplier, MuffleStrength) - 1;

                if (eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
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

                if (eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
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
            if (audioIsFlow && !config.MuffleFlowSounds) return;
            else if (audioIsFire && !config.MuffleFireSounds) return;

            if (Listener.IsSubmerged) { obstructions.Add(Obstruction.WaterSurface); }
            if (Listener.IsWearingDivingSuit) { obstructions.Add(Obstruction.Suit); }
            if (Listener.IsUsingHydrophones) { obstructions.Add(Obstruction.WaterSurface); }

            if (!config.MuffleFlowFireSoundsWithEstimatedPath) { return; }

            if (audioIsFire)
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
                    obstructions.Add(Obstruction.WallThick);
                }
                else if (Listener.IsEavesdropping)
                {
                    eavesdropped = true;
                }
            }
            else if (audioIsFlow)
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
                    obstructions.Add(Obstruction.WallThick);
                }
                else if (Listener.IsEavesdropping)
                {
                    eavesdropped = true;
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
                if (!eavesdropped) { mult *= Math.Clamp(1 - eavesdropEfficiency * (1 / config.EavesdroppingThreshold), 0.1f, 1); }
                else if (eavesdropped) { mult *= Math.Clamp(eavesdropEfficiency * (1 / config.EavesdroppingThreshold) - 1, 0.1f, 1); }
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
            if (audioIsFlow) mult *= config.FlowSoundVolumeMultiplier;
            else if (audioIsFire) mult *= config.FireSoundVolumeMultiplier;

            // Get current gain.
            float currentGain = 1;
            if (audioIsFlow) currentGain = Math.Max(SoundPlayer.flowVolumeRight[flowFireChannelIndex], SoundPlayer.flowVolumeLeft[flowFireChannelIndex]);
            else if (audioIsFire) currentGain = Math.Max(SoundPlayer.fireVolumeRight[flowFireChannelIndex], SoundPlayer.fireVolumeLeft[flowFireChannelIndex]);

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

        private static bool SoundPropagatesToListener(Vector2 soundWorldPos)
        {
            HashSet<Hull> connectedHulls = Listener.ConnectedHulls;
            foreach (Hull hull in connectedHulls)
            {
                if (SoundPropagatesToHull(hull, soundWorldPos))
                {
                    return true;
                }
            }
            return false;
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
