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
    public class SoundInfo
    {
        private enum AudioType
        {
            Sound,
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

        // The sound being played.
        private SoundChannel channel;

        public float MuffleStrength;
        public bool Ignored { get { return ignoreAll; } }
        private float gainMultHF;
        private float trailingGainMultHF;
        private float gainMultLF;
        private float trailingGainMultLF;
        private List<Obstruction> obstructions = new List<Obstruction>();
        private List<SoundInfo> clones = new List<SoundInfo>();

        // Temporary solution to the multi-thread problem of voice and sounds. Get this information on the main thread.
        public IEnumerable<FarseerPhysics.Dynamics.Body> VoiceOcclusions = new List<FarseerPhysics.Dynamics.Body>();
        public readonly object VoiceOcclusionsLock = new object();
        public List<SoundPathfinder.PathfindingResult> VoicePathResults = new List<SoundPathfinder.PathfindingResult>();
        public readonly object VoicePathResultsLock = new object();

        private float euclideanDistance;
        private float? approximateDistance;
        private float distance => approximateDistance ?? euclideanDistance;
        public Vector2 WorldPos;
        private Vector2 localPos;
        public Vector2 SimPos;
        private bool isClone;
        private bool inWater;
        private bool bothInWater;
        private bool inContainer;
        private bool noPath;

        private MuffleType muffleType;
        private MuffleType lastMuffleType;
        public bool NoMuffle => muffleType != MuffleType.None;
        public bool LightMuffle => muffleType == MuffleType.Light;
        public bool MediumMuffle => muffleType == MuffleType.Medium;
        public bool HeavyMuffle => muffleType == MuffleType.Heavy;

        private AudioType type;
        private bool audioIsSound => type == AudioType.Sound;
        public bool audioIsVoice => type == AudioType.LocalVoice;
        public bool audioIsRadio => type == AudioType.RadioVoice;
        private bool audioIsFlow => type == AudioType.FlowSound;
        private bool audioIsFire => type == AudioType.FireSound;

        private int flowFireChannelIndex;

        private float gainMult;

        public float RolloffFactor
        {
            get
            {
                float rolloff = 1;
                uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
                int alError;
                Al.GetError();
                Al.GetSourcef(sourceId, Al.RolloffFactor, out rolloff);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to get rolloff factor for source ID: {sourceId}, {Al.GetErrorString(alError)}");
                return rolloff;
            }
            set
            {
                uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
                int alError;
                Al.GetError();
                Al.Sourcef(sourceId, Al.RolloffFactor, value);
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to set rolloff factor of {value} for source ID: {sourceId}, {Al.GetErrorString(alError)}");
            }
        }
        public float Gain
        {
            get { return channel.gain; }
            set
            {
                float currentGain = channel.gain;
                float targetGain = value;

                if (targetGain != currentGain)
                {
                    channel.Gain = value;
                }
            }
        }
        public float Pitch
        {
            get { return channel.frequencyMultiplier; }
            set
            {
                float currentFrequency = Pitch;
                float targetFrequency = Math.Clamp(value, 0.25f, 4f);

                if (targetFrequency != currentFrequency)
                {
                    channel.FrequencyMultiplier = targetFrequency;

                    if (targetFrequency == 1)
                    {
                        SoundInfoManager.RemovePitchedChannel(channel);
                    }
                    else
                    {
                        SoundInfoManager.AddPitchedChannel(channel);
                    }
                }
            }
        }
        public bool Muffled
        {
            get { return channel.muffled; }
            set
            {
                bool currentMuffle = Muffled;
                bool targetMuffle = value && !ignoreLowpass;

                if (targetMuffle != currentMuffle || muffleType != lastMuffleType)
                {
                    channel.Muffled = targetMuffle;
                    lastMuffleType = muffleType;
                }
            }
        }

        public bool StaticShouldUseReverbBuffer = false; // For extended sounds only.
        public bool StaticIsUsingReverbBuffer = false; // For extended sounds only.

        private bool ignorePath = false;
        private bool ignoreSurface = false;
        private bool ignoreSubmersion = false;
        private bool ignorePitch = false;
        private bool ignoreLowpass = false;
        private bool ignoreReverb = false;
        private bool ignoreContainer = false;
        private bool ignoreAll = false;
        private bool propagateWalls = false;

        private bool propagated = false;
        private bool eavesdropped = false;
        public bool Hydrophoned = false;

        // For attenuating other sounds.
        public bool IsLoud = false;
        private float sidechainMult = 0;
        private float release = 60;

        public Hull? SoundHull = null;
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
        public SoundInfo(SoundInfo original)
        {
            isClone = true;
            channel = original.channel;
            obstructions = new List<Obstruction>(original.obstructions);
            Muffled = false;

            gainMult = original.gainMult;
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

            ignorePath = original.ignorePath;
            ignoreSurface = original.ignoreSurface;
            ignoreSubmersion = original.ignoreSubmersion;
            ignorePitch = original.ignorePitch;
            ignoreLowpass = original.ignoreLowpass;
            ignoreContainer = original.ignoreContainer;
            ignoreAll = original.ignoreAll;
            propagateWalls = original.propagateWalls;

            ignoreReverb = original.ignoreReverb;
            eavesdropped = original.eavesdropped;
            Hydrophoned = original.Hydrophoned;

            IsLoud = original.IsLoud;
            sidechainMult = original.sidechainMult;
            release = original.release;

            SoundHull = original.SoundHull;
            statusEffect = original.statusEffect;
            itemComp = original.itemComp;
            item = original.item;
            speakingClient = original.speakingClient;
            messageType = original.messageType;
    }
        public SoundInfo CreateIndependentCopy()
        {
            return new SoundInfo(this);
        }

        public SoundInfo(SoundChannel channel, Hull? soundHull = null, StatusEffect? statusEffect = null, ItemComponent? itemComp = null, Client? speakingClient = null, ChatMessageType? messageType = null, bool dontMuffle = false, bool dontPitch = false)
        {
            if (speakingClient != null && messageType == null) { messageType = Util.GetMessageType(speakingClient); }
            else if (itemComp != null && item == null) { item = itemComp.Item; }
            else if (statusEffect != null && item == null) { item = statusEffect.soundEmitter as Item; }
            if (item != null && soundHull == null) { soundHull = item.CurrentHull; }

            this.channel = channel;
            this.SoundHull = soundHull;
            this.itemComp = itemComp;
            this.statusEffect = statusEffect;
            this.speakingClient = speakingClient;
            this.messageType = messageType;

            lastMuffleType = MuffleType.None;

            string filename = this.channel.Sound.Filename;
            if (Util.StringHasKeyword(filename, Util.Config.IgnoredSounds))
            {
                ignoreAll = true;
            }
            else
            {
                ignoreLowpass = (dontMuffle && !Util.StringHasKeyword(filename, config.LowpassForcedSounds)) || Util.StringHasKeyword(filename, config.LowpassIgnoredSounds);
                ignorePitch = dontPitch || Util.StringHasKeyword(filename, config.PitchIgnoredSounds);
                ignoreReverb = Util.StringHasKeyword(filename, config.ReverbIgnoredSounds);
                ignorePath = ignoreLowpass || Util.StringHasKeyword(filename, config.PathIgnoredSounds);
                ignoreSurface = ignoreLowpass || !config.MuffleWaterSurface || Util.StringHasKeyword(filename, config.SurfaceIgnoredSounds);
                ignoreSubmersion = ignoreLowpass || Util.StringHasKeyword(filename, config.SubmersionIgnoredSounds, exclude: "Barotrauma/Content/Characters/Human/");
                ignoreContainer = ignoreLowpass || Util.StringHasKeyword(filename, config.ContainerIgnoredSounds);
                ignoreAll = ignoreLowpass && ignorePitch;
                propagateWalls = !ignoreAll && !ignoreLowpass && Util.StringHasKeyword(filename, config.PropagatingSounds);

                GetCustomSoundData(filename, out gainMult, out float rolloffFactor, out sidechainMult, out release);
                RolloffFactor = rolloffFactor;
                IsLoud = sidechainMult > 0;

                if (config.DynamicFx && Plugin.EffectsManager != null)
                {
                    uint sourceId = this.channel.Sound.Owner.GetSourceFromIndex(this.channel.Sound.SourcePoolIndex, this.channel.ALSourceIndex);
                    Plugin.EffectsManager.RegisterSource(sourceId);
                }
            }

            Update(soundHull, statusEffect, itemComp, speakingClient, messageType);
        }

        // TODO This method takes arguments because sometimes the constructor is called without any of this info available (created from soundchannel ctor with no contex, should I even do this or allow the different types to make themselves later? pretty sure it caused popping previously.)
        public void Update(Hull? soundHull = null, StatusEffect? statusEffect = null, ItemComponent? itemComp = null, Client? speakingClient = null, ChatMessageType? messageType = null)
        {
            if (channel == null) { return; }

            if (!isClone)
            {
                foreach(SoundInfo clone in clones) { clone.Update(); }
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

            this.SoundHull = soundHull;
            this.itemComp = itemComp;
            this.statusEffect = statusEffect;
            this.speakingClient = speakingClient;
            this.messageType = messageType;

            if (ignoreAll)
            {
                euclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos);
                Muffled = false;
                Pitch = 1;
                return;
            }

            bool skipMuffleUpdate = false;
            float currentTime = (float)Timing.TotalTime;

            if (itemComp != null && itemComp.loopingSoundChannel == channel)
            {
                if (currentTime < itemComp.lastMuffleCheckTime + config.ComponentMuffleUpdateInterval)
                {
                    skipMuffleUpdate = true;
                }
                else
                {
                    itemComp.lastMuffleCheckTime = currentTime;
                }
            }
            else if (statusEffect != null)
            {
                if (currentTime < StatusEffect.LastMuffleCheckTime + config.StatusEffectMuffleUpdateInterval)
                {
                    skipMuffleUpdate = true;
                }
                else
                {
                    StatusEffect.LastMuffleCheckTime = currentTime;
                }
            }

            if (!skipMuffleUpdate)
            {
                UpdateProperties();
                UpdateMuffle();
            }

            UpdateGain();
            UpdatePitch();
            UpdateVoice();
        }

        private void UpdateVoice()
        {
            voiceUseMuffleFilter = Muffled; ;
            voiceUseRadioFilter = messageType == ChatMessageType.Radio && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters;
        }

        private void UpdateProperties()
        {
            Character listener = Character.Controlled;
            Character? speaker = speakingClient?.Character;
            Limb? speakerHead = speaker?.AnimController?.GetLimb(LimbType.Head);

            WorldPos = speakerHead?.WorldPosition ?? Util.GetSoundChannelWorldPos(channel);
            SoundHull = SoundHull ?? Hull.FindHull(WorldPos, speaker?.CurrentHull ?? listener?.CurrentHull);
            localPos = Util.LocalizePosition(WorldPos, SoundHull);
            SimPos = localPos / 100;
            inWater = Util.SoundInWater(localPos, SoundHull);
            inContainer = item != null && !ignoreContainer && IsContainedWithinContainer(item);
            euclideanDistance = Vector2.Distance(Listener.WorldPos, WorldPos); // euclidean distance

            // Determine the audio type.
            // Note: For now, this is done every update because many SoundInfos are made before necessary info about their type is known.
            //       This can change if SoundInfos are no longer made within the SoundChannel constructor.
            type = AudioType.Sound;
            if (speakingClient != null && messageType != null)
            {
                type = (messageType == ChatMessageType.Radio) ? AudioType.RadioVoice : AudioType.LocalVoice;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.flowSoundChannels, channel)) != -1)
            {
                type = AudioType.FlowSound;
            }
            else if ((flowFireChannelIndex = Array.IndexOf(SoundPlayer.fireSoundChannels, channel)) != -1)
            { 
                type = AudioType.FireSound;
            }
        }

        private void UpdateMuffle()
        {
            bool shouldMuffle = false;
            bool shouldUseReverbBuffer = false; // Extended sounds only.
            MuffleType type = MuffleType.None;

            if (ignoreLowpass) { Muffled = false; return; };

            if (!isClone) { UpdateObstructions(); }

            float gainMultHf = 1;
            float total = 0;
            float mult = config.DynamicFx ? config.DynamicMuffleStrengthMultiplier : 1;
            for (int i = 0; i < obstructions.Count; i++)
            {
                float strength = obstructions[i].GetStrength();
                total += strength * mult;
                gainMultHf *= 1 - strength; // Exponential muffling
                if (gainMultHf <= 0.01) { break; } // No point continuing.
            }
            MuffleStrength = 1 - gainMultHf;
            float gainMultLf = 1 - total / 3; // Arbitrary value. When total reaches 3 even low frequencies gain is 0.
            if (!config.AutoAttenuateMuffledSounds) { gainMultLf = 1; }
            gainMultHF = Math.Clamp(gainMultHf, 0, 1);
            gainMultLF = Math.Clamp(gainMultLf, 0, 1);

            if (config.DynamicFx)
            {
                UpdateDynamicFx();
            }
            else if (config.ClassicFx && MuffleStrength >= config.ClassicMinMuffleThreshold)
            {
                type = MuffleType.Heavy;
                shouldMuffle = true;
            }
            else if (config.StaticFx && MuffleStrength >= config.StaticMinLightMuffleThreshold)
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

            // Enable reverb buffers for extended sounds. Is not relevant to the DynamicFx reverb.
            if (config.StaticFx && config.StaticReverbEnabled && !shouldMuffle && !channel.Looping && (Listener.ConnectedArea >= config.StaticReverbMinArea || IsLoud && config.StaticReverbAlwaysOnLoudSounds))
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

            if (channel.Looping)
            {
                float rate = 1.5f * config.StatusEffectMuffleUpdateInterval;
                float gainDiff = gainMultHF - trailingGainMultHF;
                trailingGainMultHF += Math.Abs(gainDiff) < rate ? gainDiff : Math.Sign(gainDiff) * rate;
                gainDiff = gainMultLF - trailingGainMultLF;
                trailingGainMultLF += Math.Abs(gainDiff) < rate ? gainDiff : Math.Sign(gainDiff) * rate;
            }
            else
            {
                trailingGainMultHF = gainMultHF;
                trailingGainMultLF = gainMultLF;
            }
            bool useIndoorEnvironment = SoundHull != null;
            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            Plugin.EffectsManager.UpdateSource(sourceId, useIndoorEnvironment, ignoreReverb || Listener.IsSpectating, Hydrophoned, MuffleStrength, trailingGainMultHF, trailingGainMultLF);
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
                ignoreReverb = !config.DynamicReverbRadio;
                Character speaker = speakingClient.Character;
                bool wearingDivingGear = Util.IsCharacterWearingDivingGear(speaker);
                bool oxygenReqMet = wearingDivingGear && speaker.Oxygen < 11 || !wearingDivingGear && speaker.OxygenAvailable < 96;
                bool ignoreBubbles = Util.StringHasKeyword(speaker.Name, config.BubbleIgnoredNames) || ignoreSurface;
                if (oxygenReqMet && inWater && !ignoreBubbles)
                {
                    obstructions.Add(Obstruction.WaterSurface);
                }
                // Return because radio comms aren't muffled under any other circumstances.
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
                if (inWater && !ignoreSurface)
                {
                    obstructions.Add(Obstruction.WaterSurface);
                }
                // Return because nothing else can muffle sounds for spectators.
                return;
            }

            if (Listener.IsWearingDivingSuit)
            {
                obstructions.Add(Obstruction.Suit);
            }

            // Hydrophone check. Muffle sounds inside your own sub while still hearing sounds in other subs/structures.
            if (Listener.IsUsingHydrophonesNow)
            {
                // Sound can be heard clearly outside in the water.
                if (SoundHull == null)
                {
                    Hydrophoned = true;
                    obstructions.Add(Obstruction.Suit);
                    return;
                }
                // Sound is in a station or other submarine.
                else if (SoundHull != null && SoundHull.Submarine != LightManager.ViewTarget?.Submarine)
                {
                    Hydrophoned = true;
                    obstructions.Add(Obstruction.WallThin);
                    return;
                }
                // Sound is in own submarine.
                else
                {
                    obstructions.Add(Obstruction.WaterSurface);
                }
            }

            if (config.DynamicFx) { UpdatePathObstructionsAdvanced(); }
            else { UpdatePathObstructionsSimple(); }
        }

        private void UpdateWaterObstructions()
        {
            bool earsInWater = Listener.IsSubmerged;
            bool soundInWater = inWater;
            bool ignoreSubmersion = this.ignoreSubmersion || (Listener.IsCharacter ? !config.MuffleSubmergedPlayer : !config.MuffleSubmergedViewTarget);
            bool ignoreSurface = this.ignoreSurface;

            // Neither in water.
            if (!earsInWater && !soundInWater) { return; }
            else if (noPath) // The listener and sound are in separate rooms or one of them is outside the sub.
            {                // In this case, it feels more natural for ignoreSubmersion and ignoreSurface to be ignored.
                if (earsInWater) { obstructions.Add(Obstruction.WaterBody); }
                // Even if the listener's ears are in water, apply a water surface because the sound is in a different room.
                if (soundInWater) { obstructions.Add(Obstruction.WaterSurface); }
                return;
            }

            // From here it is assumed there is a path to the sound,
            // usually meaning the listener and the water share the same hull.

            // Ears in water.
            if (earsInWater)
            {
                obstructions.Add(ignoreSubmersion ? Obstruction.Suit : Obstruction.WaterBody);
                // Sound in same body of water, return because no need to apply water surface.
                if (soundInWater) { bothInWater = true; return; }
            }

            // Separated by water surface.
            if (soundInWater != earsInWater)
            {
                Obstruction obstruction = Obstruction.WaterSurface;
                if (propagateWalls && !propagated) { obstruction = Obstruction.WallThin; }
                if (ignoreSurface)                 { obstruction = Obstruction.Suit; }
                obstructions.Add(obstruction);
            }
        }

        public void DisposeClones()
        {
            foreach (SoundInfo clone in clones)
            {
                clone.channel?.Dispose();
            }
            clones.Clear();
        }

        private void UpdatePathObstructionsAdvanced()
        {
            Hull? listenerHull = Listener.FocusedHull;
            HashSet<Hull> listenerConnectedHulls = Listener.ConnectedHulls;
            
            // Eavesdropped obstruction.
            if (Listener.IsEavesdropping && SoundHull != null && listenerConnectedHulls.Contains(SoundHull))
            {
                eavesdropped = true;
                obstructions.Add(Obstruction.DoorThin);
            }

            noPath = listenerHull != SoundHull && (listenerHull == null || SoundHull == null) || listenerHull != null && !listenerConnectedHulls.Contains(SoundHull);

            // Ray check how many walls are between the sound and the listener using a direct line.
            // Voice ray casts are performed on the main thread and sent to VoiceOcclusions.
            IEnumerable<Body> bodies;
            if (audioIsVoice || audioIsRadio) { lock (VoiceOcclusionsLock) { bodies = VoiceOcclusions; } }
            else { bodies = Submarine.PickBodies(Listener.SimPos, SimPos, collisionCategory: Physics.CollisionWall); }

            // Only count collisions for unique wall orientations.
            int numWallsInRay = 0;
            List<float> yWalls = new List<float>();
            List<float> xWalls = new List<float>();
            float epsilon = 5.0f;
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
                        numWallsInRay++;
                    }
                }
            }

            // Allow certain sounds near a wall to propagate to the opposite side.
            if (noPath && propagateWalls)
            {
                foreach (Hull hull in listenerConnectedHulls)
                {
                    if (ShouldSoundPropagateToHull(hull, WorldPos))
                    {
                        obstructions.Add(Obstruction.WallThin);
                        SoundHull = hull;
                        propagated = true;
                        numWallsInRay--; // Replacing thick wall with thin.
                        break;
                    }
                }
            }

            UpdateWaterObstructions();

            if (!noPath && numWallsInRay <= 0 && SoundHull == listenerHull)
            {
                DisposeClones();
                return;
            }

            // Saves the current obstructions to be applied to any clones.
            List<Obstruction> clonedObstructions = new List<Obstruction>(obstructions);

            if (config.OccludeSounds)
            {
                for (int i = 0; i < numWallsInRay; i++)
                {
                    if (noPath) { obstructions.Add(Obstruction.WallThick); }
                    else { obstructions.Add(Obstruction.WallThin); } // Wall occlusion isn't as strong if there's a path to the listener.
                }
            }

            bool simulateSoundDirection = config.MaxSimulatedSoundDirections > 0 && !audioIsRadio && !audioIsVoice;
            int targetNumResults = simulateSoundDirection ? config.MaxSimulatedSoundDirections : 1;
            
            List<SoundPathfinder.PathfindingResult> topResults;
            if (audioIsVoice || audioIsRadio) { lock (VoicePathResultsLock) { topResults = VoicePathResults; } }
            else { topResults = SoundPathfinder.FindShortestPaths(WorldPos, SoundHull, Listener.WorldPos, listenerHull, listenerHull?.Submarine, targetNumResults); }

            // Failed to find any paths.
            if (topResults.Count <= 0) { DisposeClones(); return; }

            SoundPathfinder.PathfindingResult bestResult = topResults[0];

            for (int i = 0; i < bestResult.ClosedDoorCount; i++) { obstructions.Add(Obstruction.DoorThick); }
            for (int j = 0; j < bestResult.WaterSurfaceCrossings; j++) { obstructions.Add(Obstruction.WaterSurface); }

            // For multiple sounds playing at different positions to simulate the sounds pathing.
            if (simulateSoundDirection)
            {
                approximateDistance = null; // The "parent" sound does not have an approximate distance.
                for (int i = 0; i < topResults.Count; i++)
                {
                    Vector2 intersectionPos = topResults[i].LastIntersectionPos;
                    float distance = topResults[i].RawDistance;
                    float originalRange = channel.Far;
                    float newRange = originalRange;
                    float distanceToListener = Vector2.Distance(intersectionPos, Listener.WorldPos);
                    if (distance > 0) { newRange = (distanceToListener * originalRange) / distance; }

                    SoundInfo clone;
                    if (clones.Count > i) 
                    {
                        clone = clones[i];
                        if (clone.channel == null || !clone.channel.IsPlaying) 
                        {
                            clone.channel = SoundPlayer.PlaySound(channel.Sound, intersectionPos, range: originalRange);
                            if (clone.channel != null)
                            {
                                clone.channel.Looping = channel.Looping;
                                uint sourceId = clone.channel.Sound.Owner.GetSourceFromIndex(clone.channel.Sound.SourcePoolIndex, clone.channel.ALSourceIndex);
                                Plugin.EffectsManager?.RegisterSource(sourceId);
                            }
                        }
                    }
                    else 
                    {
                        clone = CreateIndependentCopy();
                        // Play the duplicate sound at the intersection with a range that attenuates the sound based on how far the original would have to travel.
                        clone.channel = SoundPlayer.PlaySound(channel.Sound, intersectionPos, range: originalRange);
                        if (clone.channel != null)
                        {
                            clone.channel.Looping = channel.Looping;
                            uint sourceId = clone.channel.Sound.Owner.GetSourceFromIndex(clone.channel.Sound.SourcePoolIndex, clone.channel.ALSourceIndex);
                            Plugin.EffectsManager?.RegisterSource(sourceId);
                        }
                        if (clone != null)
                        {
                            clones.Add(clone);
                        }
                    }
                    
                    if (clone == null || clone.channel == null) { continue; }

                    //LuaCsLogger.Log($"og sound pos{channel.Position} gain:{channel.Gain}\nclone channel pos: {clone.channel.Position} gain: {clone.channel.Gain} obstruction count: {clone.obstructions.Count}\nListener pos {Listener.LocalPos} character controlled: {Character.Controlled.Position}");

                    // Update the clone's properties.
                    clone.obstructions = new List<Obstruction>(clonedObstructions);
                    for (int j = 0; j < topResults[i].ClosedDoorCount; j++) { clone.obstructions.Add(Obstruction.DoorThick); }
                    for (int j = 0; j < topResults[i].WaterSurfaceCrossings; j++) { clone.obstructions.Add(Obstruction.WaterSurface); }
                    clone.approximateDistance = distance;
                    clone.channel.Position = new Vector3(topResults[i].LastIntersectionPos, 0.0f);
                }
            }
        }

        private void UpdatePathObstructionsSimple()
        {

            // Path and realDistance check
            Hull? listenerHull = Listener.FocusedHull;
            if (listenerHull != null && listenerHull != SoundHull)
            {
                HashSet<Hull> listenerConnectedHulls = new HashSet<Hull>();

                // Run this even if SoundHull might be null for the sake of collecting listenerConnectedHulls for later.
                float distance = GetApproximateDistance(Listener.LocalPos, localPos, listenerHull, SoundHull, channel.Far, listenerConnectedHulls);

                if (distance >= float.MaxValue)
                {
                    noPath = true;
                }

                // Only switch to this distance if both the listenHull and soundHull are not null.
                if (SoundHull != null)
                {
                    approximateDistance = distance;
                }

                // Allow certain sounds near a wall to be faintly heard on the opposite side.
                if (noPath && propagateWalls)
                {
                    foreach (Hull hull in listenerConnectedHulls)
                    {
                        if (channel.Sound.Filename.ToLower().Contains("coil"))
                        {
                            LuaCsLogger.Log($"hull: {hull} shouldprop: {ShouldSoundPropagateToHull(hull, WorldPos)} worldPos: {WorldPos}");
                        }
                        if (ShouldSoundPropagateToHull(hull, WorldPos))
                        {
                            LuaCsLogger.Log($"{Path.GetFileName(channel.Sound.Filename)} SOUND HAS PROPAGATED TO {hull}");
                            propagated = true;
                            break;
                        }
                    }
                    if (propagated)
                    {
                        obstructions.Add(Obstruction.WallThin);
                    }
                }
            }

            // Sound could not find an open path to the player.
            if (noPath && !propagated)
            {
                if (ignorePath)
                {
                    // Switch back to euclidean distance if the sound ignores paths.
                    approximateDistance = null;
                    // Add a slight muffle to these sounds.
                    obstructions.Add(Obstruction.Suit);
                }
                else
                {
                    obstructions.Add(Obstruction.WallThick);
                }
            }

            if (Listener.IsEavesdropping && !noPath)
            {
                eavesdropped = true;
                obstructions.Add(Obstruction.DoorThin);
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
            float currentGain = itemComp?.GetSoundVolume(itemComp.loopingSound) ?? channel.Gain;
            bool looping = true;

            mult += gainMult - 1;

            // Radio can only be muffled when the sender is drowning.
            if (audioIsRadio)
            {
                mult += MathHelper.Lerp(1, config.MuffledVoiceVolumeMultiplier, MuffleStrength) - 1;
            }
            else if (audioIsVoice)
            {
                mult += MathHelper.Lerp(1, config.MuffledVoiceVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingVoiceVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }
            // Either a component or status effect sound.
            else if (channel.Looping)
            {
                mult += MathHelper.Lerp(config.UnmuffledLoopingVolumeMultiplier, config.MuffledLoopingVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }

                if (item != null && item.HasTag(new Identifier("deepdivinglarge")) && channel.Sound.Filename.EndsWith("WEAPONS_chargeUp.ogg")) { mult *= config.VanillaExosuitVolumeMultiplier; }
            }
            // Single (non-looping sound).
            else
            {
                looping = false;
                mult += MathHelper.Lerp(1, config.MuffledSoundVolumeMultiplier, MuffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (Hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }
            // Note that the following multipliers also apply to radio channels this way.
            // 1. Fade transition between two hull soundscapes when eavesdropping.
            float eavesdropEfficiency = EavesdropManager.Efficiency;
            if (config.EavesdroppingTransitionEnabled && eavesdropEfficiency > 0 && (!audioIsRadio || config.EavesdroppingDucksRadio))
            {
                if (!eavesdropped) { mult *= Math.Clamp(1 - eavesdropEfficiency * (1 / config.EavesdroppingThreshold), 0.1f, 1); }
                else if (eavesdropped) { mult *= Math.Clamp(eavesdropEfficiency * (1 / config.EavesdroppingThreshold) - 1, 0.1f, 1); }
            }
            // 2. The strength of the sidechaining decreases the more muffled the loud sound is.
            if (IsLoud) Plugin.Sidechain.StartRelease(sidechainMult * (1 - MuffleStrength), release * (1 - MuffleStrength));
            else if (!IsLoud) mult *= 1 - Plugin.Sidechain.SidechainMultiplier;

            float targetGain = currentGain * mult;

            if (looping && Plugin.Sidechain.SidechainMultiplier <= 0)
            {
                float gainDiff = targetGain - Gain;
                Gain += Math.Abs(gainDiff) < 0.1f ? gainDiff : Math.Sign(gainDiff) * 0.1f;
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
            bool looping = true;

            // Voice.
            if (!audioIsSound)
            {
                mult = MathHelper.Lerp(config.UnmuffledVoicePitchMultiplier, config.MuffledLoopingPitchMultiplier, MuffleStrength);
            }
            // Either a component or status effect sound.
            else if (channel.Looping)
            {
                mult = MathHelper.Lerp(config.UnmuffledSoundPitchMultiplier, config.MuffledLoopingPitchMultiplier, MuffleStrength);

                if (eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
                else if (Hydrophoned) mult += config.HydrophonePitchMultiplier - 1;

                if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
                if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1; 
            }
            // Single (non-looping sound).
            else
            {
                looping = false;
                mult = MathHelper.Lerp(config.UnmuffledSoundPitchMultiplier, config.MuffledSoundPitchMultiplier, MuffleStrength);

                if (config.PitchSoundsByDistance) // Additional pitching based on distance and muffle strength.
                {
                    float distanceRatio = Math.Clamp(1 - euclideanDistance / channel.Far, 0, 1);
                    mult += MathHelper.Lerp(1, distanceRatio, MuffleStrength) - 1;
                }

                if (eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
                else if (Hydrophoned) mult += config.HydrophonePitchMultiplier - 1;

                if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
                if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1;
            }

            float targetPitch = !looping ? Pitch * mult : 1 * mult;

            if (looping)
            {
                float pitchDiff = targetPitch - Pitch;
                Pitch += Math.Abs(pitchDiff) < 0.1f ? pitchDiff : Math.Sign(pitchDiff) * 0.1f;
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
        }

        private void UpdateFlowFireGain()
        {
            float mult = 1;
            mult += gainMult - 1;
            mult += MathHelper.Lerp(config.UnmuffledLoopingVolumeMultiplier, config.MuffledLoopingVolumeMultiplier, MuffleStrength) - 1;

            // Dip audio when eavesdropping.
            float eavesdropEfficiency = EavesdropManager.Efficiency;
            float eavesdropMinMult = 0.05f; // Arbitrary magic number.
            if (config.EavesdroppingTransitionEnabled && eavesdropEfficiency > 0)
            {
                mult *= Math.Clamp(1 - eavesdropEfficiency, eavesdropMinMult, 1);
            }
            else if (!config.EavesdroppingTransitionEnabled && eavesdropEfficiency > 0)
            {
                mult *= eavesdropMinMult;
            }

            // The strength of the sidechaining decreases the more muffled the loud sound is.
            if (IsLoud) Plugin.Sidechain.StartRelease(sidechainMult * (1 - MuffleStrength), release * (1 - MuffleStrength));
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

            private static bool ShouldSoundPropagateToHull(Hull targetHull, Vector2 soundWorldPos)
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
        private static float GetApproximateDistance(Vector2 startPos, Vector2 endPos, Hull startHull, Hull? endHull, float maxDistance, HashSet<Hull> connectedHulls, float distanceMultiplierPerClosedDoor = 0)
        {
            return GetApproximateHullDistance(startPos, endPos, startHull, endHull, connectedHulls, 0.0f, maxDistance, distanceMultiplierPerClosedDoor);
        }

        private static float GetApproximateHullDistance(Vector2 startPos, Vector2 endPos, Hull startHull, Hull? endHull, HashSet<Hull> connectedHulls, float distance, float maxDistance, float distanceMultiplierFromDoors = 0)
        {
            if (distance >= maxDistance) { return float.MaxValue; }
            if (startHull == endHull)
            {
                return distance + Vector2.Distance(startPos, endPos);
            }

            connectedHulls.Add(startHull);
            foreach (Gap g in startHull.ConnectedGaps)
            {
                float distanceMultiplier = 1;
                // For doors.
                if (g.ConnectedDoor != null)
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
                        float dist = GetApproximateHullDistance(optimalGapPos, endPos, newStartHull, endHull, connectedHulls, distance + Vector2.Distance(startPos, optimalGapPos) * distanceMultiplier, maxDistance);
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
            Vector2 gapPos = gap.Position;

            if (!gap.IsHorizontal)
            {
                float gapSize = gap.Rect.Width;
                float slope = (endPos.Y - startPos.Y) / (endPos.X - startPos.X);
                float intersectX = (gapPos.Y - startPos.Y + slope * startPos.X) / slope;

                // Clamp the x-coordinate to within the gap's width
                intersectX = Math.Clamp(intersectX, gapPos.X - gapSize / 2, gapPos.X + gapSize / 2);

                return new Vector2(intersectX, gapPos.Y);
            }
            else
            {
                float gapSize = gap.Rect.Height;
                float slope = (endPos.X - startPos.X) / (endPos.Y - startPos.Y);
                float intersectY = (gapPos.X - startPos.X + slope * startPos.Y) / slope;

                // Clamp the y-coordinate to within the gap's height
                intersectY = Math.Clamp(intersectY, gapPos.Y - gapSize / 2, gapPos.Y + gapSize / 2);

                return new Vector2(gapPos.X, intersectY);
            }
        }

        private static bool GetCustomSoundData(string inputString, out float gainMult, out float rolloffFactor, out float sidechainMult, out float release)
        {
            string s = inputString.ToLower();
            gainMult = 1; rolloffFactor = 1; sidechainMult = 0; release = 60;

            foreach (var sound in config.CustomSounds)
            {
                if (s.Contains(sound.Name.ToLower()))
                {
                    bool excluded = false;
                    foreach (string exclusion in sound.Exclusions)
                    {
                        if (s.Contains(exclusion.ToLower())) { excluded = true; }
                    }
                    if (excluded) { continue; }

                    gainMult = sound.GainMultiplier;
                    rolloffFactor = sound.RolloffFactor;
                    sidechainMult = sound.SidechainMultiplier * config.SidechainIntensityMaster;
                    release = sound.Release + config.SidechainReleaseMaster;
                    return true;
                }
            }
            return false;
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
