using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;

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

        private float muffleStrength;
        private List<Obstruction> obstructions = new List<Obstruction>();
        private float worldDistance;
        private float? localDistance;
        private float distance => localDistance ?? worldDistance;
        private Vector2 worldPos;
        private Vector2 localPos;
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
        private bool audioIsVoice => type == AudioType.LocalVoice;
        private bool audioIsRadio => type == AudioType.RadioVoice;
        private bool audioIsFlow => type == AudioType.FlowSound;
        private bool audioIsFire => type == AudioType.FireSound;

        private int flowFireChannelIndex;

        private float gainMult;
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

        private bool ignorePath = false;
        private bool ignoreSurface = false;
        private bool ignoreSubmersion = false;
        private bool ignorePitch = false;
        private bool ignoreLowpass = false;
        private bool ignoreContainer = false;
        private bool ignoreAll = false;
        private bool propagateWalls = false;

        private bool reverbed = false;
        private bool eavesdropped = false;
        private bool hydrophoned = false;

        // For attenuating other sounds.
        private bool isLoud = false;
        private float sidechainMult = 0;
        private float release = 60;

        private Hull? soundHull = null;
        private StatusEffect? statusEffect = null;
        private ItemComponent? itemComp = null;
        private Item? item = null;
        private Client? speakingClient = null;
        private ChatMessageType? messageType = null;

        private static Config config { get { return ConfigManager.Config; } }

        public SoundInfo(SoundChannel channel, Hull? soundHull = null, StatusEffect? statusEffect = null, ItemComponent? itemComp = null, Client? speakingClient = null, ChatMessageType? messageType = null, bool dontMuffle = false, bool dontPitch = false)
        {
            if (speakingClient != null && messageType == null) { messageType = Util.GetMessageType(speakingClient); }
            else if (itemComp != null && item == null) { item = itemComp.Item; }
            else if (statusEffect != null && item == null) { item = statusEffect.soundEmitter as Item; }
            if (item != null && soundHull == null) { soundHull = item.CurrentHull; }

            this.channel = channel;
            this.soundHull = soundHull;
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
                ignorePath = ignoreLowpass || Util.StringHasKeyword(filename, config.WallIgnoredSounds);
                ignoreSurface = ignoreLowpass || ignorePath || !config.MuffleSubmergedSounds || Util.StringHasKeyword(filename, config.SurfaceIgnoredSounds);
                ignoreSubmersion = ignoreLowpass || Util.StringHasKeyword(filename, config.SubmersionIgnoredSounds, exclude: "Barotrauma/Content/Characters/Human/");
                ignoreContainer = ignoreLowpass || Util.StringHasKeyword(filename, config.ContainerIgnoredSounds);
                ignoreAll = ignoreLowpass && ignorePitch;
                propagateWalls = !ignoreAll && !ignoreLowpass && Util.StringHasKeyword(filename, config.PropagatingSounds);

                GetCustomSoundData(filename, out gainMult, out sidechainMult, out release);
                isLoud = sidechainMult > 0;

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
            if (speakingClient != null && messageType == null) { messageType = Util.GetMessageType(speakingClient); }
            else if (itemComp != null && item == null) { item = itemComp.Item; }
            else if (statusEffect != null && item == null) { item = statusEffect.soundEmitter as Item; }
            if (item != null && soundHull == null) { soundHull = item.CurrentHull; }

            this.soundHull = soundHull;
            this.itemComp = itemComp;
            this.statusEffect = statusEffect;
            this.speakingClient = speakingClient;
            this.messageType = messageType;

            if (ignoreAll)
            {
                worldDistance = Vector2.Distance(Listener.WorldPos, worldPos);
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
        }

        private void UpdateProperties()
        {
            Character listener = Character.Controlled;
            Character? speaker = speakingClient?.Character;
            Limb? speakerHead = speaker?.AnimController?.GetLimb(LimbType.Head);

            worldPos = speakerHead?.WorldPosition ?? Util.GetSoundChannelPos(channel);
            soundHull = soundHull ?? Hull.FindHull(worldPos, speaker?.CurrentHull ?? listener?.CurrentHull);
            localPos = Util.LocalizePosition(worldPos, soundHull);
            inWater = Util.SoundInWater(localPos, soundHull);
            inContainer = item != null && !ignoreContainer && IsContainedWithinContainer(item);
            localDistance = null;
            worldDistance = Vector2.Distance(Listener.WorldPos, worldPos); // euclidean distance

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
            muffleType = MuffleType.None;
            if (ignoreLowpass)
            {
                Muffled = false;
                return; 
            };

            UpdateObstructions();

            float strength = 0;
            foreach (Obstruction obstruction in obstructions)
            {
                strength += SoundInfoManager.ObstructionStrength[obstruction];
                if (strength >= 1) break;
            }
            muffleStrength = Math.Clamp(strength, 0, 1);

            if (config.DynamicFx && Plugin.EffectsManager != null)
            {
                Muffled = false;
                UpdateDynamicFx();
                return;
            }

            if (config.VanillaFx && muffleStrength >= 0.25f)
            {
                muffleType = MuffleType.Heavy;
                Muffled = true;
            }
            else if (config.StaticFx && muffleStrength >= 0.1f)
            {
                muffleType = MuffleType.Heavy;
                if (muffleStrength <= SoundInfoManager.ObstructionStrength[Obstruction.Suit])
                {
                    muffleType = MuffleType.Light;
                }
                else if (muffleStrength < SoundInfoManager.ObstructionStrength[Obstruction.DoorThick])
                {
                    muffleType = MuffleType.Medium;
                }
                Muffled = true;
            }
            else { Muffled = false; }
        }

        private void UpdateGain() //TODO (all types use same distance falloff as looping, unless that is done inside openAL alrady? in which case maybe just disable it and do my own...)
        {
            // Flow/fire sounds are handled differently.
            if (audioIsFlow || audioIsFire)
            {
                UpdateFlowFireGain();
                return;
            }

            float mult = 1;
            float currentGain = itemComp?.GetSoundVolume(itemComp.loopingSound) ?? channel.Gain;

            mult += gainMult - 1;

            // Radio can only be muffled when the sender is drowning.
            if (audioIsRadio)
            {
                mult += MathHelper.Lerp(1, config.MuffledVoiceVolumeMultiplier, muffleStrength) - 1;
            }
            else if (audioIsVoice)
            {
                mult += MathHelper.Lerp(1, config.MuffledVoiceVolumeMultiplier, muffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingVoiceVolumeMultiplier - 1;
                else if (hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }
            // Either a component or status effect sound.
            else if (channel.Looping)
            {
                mult += MathHelper.Lerp(config.UnmuffledLoopingVolumeMultiplier, config.MuffledLoopingVolumeMultiplier, muffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
                // Distance falloff. TODO 0.7f is fairly arbitrary and was chosen to avoid audio pops. Retest this.
                mult *= noPath ? 1f : 1 - MathUtils.InverseLerp(channel.Near, channel.Far, distance);
            }
            // Single (non-looping sound).
            else
            {
                mult += MathHelper.Lerp(1, config.MuffledSoundVolumeMultiplier, muffleStrength) - 1;
                if (bothInWater) mult += config.SubmergedVolumeMultiplier - 1;
                else if (eavesdropped) mult += config.EavesdroppingSoundVolumeMultiplier - 1;
                else if (hydrophoned) { mult += config.HydrophoneVolumeMultiplier - 1; mult *= HydrophoneManager.HydrophoneEfficiency; }
            }
            // Note that the following multipliers also apply to radio channels this way.
            // 1. Fade transition between two hull soundscapes when eavesdropping.
            float eavesdropEfficiency = EavesdropManager.Efficiency;
            if (config.EavesdroppingFadeEnabled && eavesdropEfficiency > 0)
            {
                if (!eavesdropped) { mult *= Math.Clamp(1 - eavesdropEfficiency * (1 / config.EavesdroppingThreshold), 0.1f, 1); }
                else if (eavesdropped) { mult *= Math.Clamp(eavesdropEfficiency * (1 / config.EavesdroppingThreshold) - 1, 0.1f, 1); }
            }
            // 2. The strength of the sidechaining decreases the more muffled the loud sound is.
            if (isLoud) Plugin.Sidechain.StartRelease(sidechainMult * (1 - muffleStrength), release * (1 - muffleStrength));
            else if (!isLoud) mult *= 1 - Plugin.Sidechain.SidechainMultiplier;
            
            Gain = currentGain * mult;
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
            bool single = false;

            // Voice.
            if (!audioIsSound)
            {
                mult = MathHelper.Lerp(config.UnmuffledVoicePitchMultiplier, config.MuffledLoopingPitchMultiplier, muffleStrength);
            }
            // Either a component or status effect sound.
            else if (channel.Looping)
            {
                mult = MathHelper.Lerp(config.UnmuffledSoundPitchMultiplier, config.MuffledLoopingPitchMultiplier, muffleStrength);

                if (eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
                else if (hydrophoned) mult += config.HydrophonePitchMultiplier - 1;

                if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
                if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1; 
            }
            // Single (non-looping sound).
            else
            {
                single = true;
                mult = MathHelper.Lerp(config.UnmuffledSoundPitchMultiplier, config.MuffledSoundPitchMultiplier, muffleStrength);

                if (config.PitchSoundsByDistance) // Additional pitching based on distance and muffle strength.
                {
                    float distanceRatio = Math.Clamp(1 - worldDistance / channel.Far, 0, 1);
                    mult += MathHelper.Lerp(1, distanceRatio, muffleStrength) - 1;
                }

                if (eavesdropped) mult += config.EavesdroppingPitchMultiplier - 1;
                else if (hydrophoned) mult += config.HydrophonePitchMultiplier - 1;

                if (Listener.IsSubmerged) mult += config.SubmergedPitchMultiplier - 1;
                if (Listener.IsWearingDivingSuit) mult += config.DivingSuitPitchMultiplier - 1;
            }

            //TODO can I make single sounds be processed multiple times?
            if (single) Pitch *= mult; // Maintain any pre-existing pitch adjustments to a non-looping sound.
            else Pitch = 1 * mult; // Sounds that are processed multiple times are multiplied by 1.
        }

        private void UpdateDynamicFx()
        {
            if (Plugin.EffectsManager == null) return;

            // TODO Calculate these values here.
            float gainHf = 1;
            bool isInListenerEnvironment = true;

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            Plugin.EffectsManager.UpdateSourceLowpass(sourceId, gainHf);
            Plugin.EffectsManager.RouteSourceToEnvironment(sourceId, isInListenerEnvironment);
        }

        //TODO HEAPS of issues in here...
        private void UpdateObstructions()
        {
            // Reset obstruction properties.
            eavesdropped = false;
            hydrophoned = false;
            obstructions.Clear();
            noPath = false;
            localDistance = null;

            // Flow/fire sounds are handled differently.
            if (audioIsFlow || audioIsFire)
            { 
                UpdateFlowFireObstructions(); 
                return; 
            }

            // Muffle radio comms underwater to make room for bubble sounds.
            if (audioIsRadio && speakingClient != null)
            {
                Character speaker = speakingClient.Character;
                bool wearingDivingGear = Util.IsCharacterWearingDivingGear(speaker);
                bool oxygenReqMet = wearingDivingGear && speaker.Oxygen < 11 || !wearingDivingGear && speaker.OxygenAvailable < 96;
                bool ignoreBubbles = Util.StringHasKeyword(speaker.Name, config.BubbleIgnoredNames) || this.ignoreSurface;
                if (oxygenReqMet && inWater && !ignoreBubbles)
                {
                    obstructions.Add(Obstruction.WaterBody);
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
                if (inWater && !this.ignoreSurface)
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
                if (soundHull == null)
                {
                    hydrophoned = true;
                    obstructions.Add(Obstruction.Suit);
                    return;
                }
                // Sound is in a station or other submarine.
                else if (soundHull != null && soundHull.Submarine != LightManager.ViewTarget?.Submarine)
                {
                    hydrophoned = true;
                    obstructions.Add(Obstruction.WallThin);
                    return;
                }
                // Sound is in own submarine.
                else
                {
                    obstructions.Add(Obstruction.WaterSurface);
                }
            }

            // If soundHull and focusedHull are not null (and therefore might be connected to each other),
            // use a more approximate distance because it considers pathing around corners.
            // If the sound can't find a path to the focused hull, localDistance will be equal to float.MaxValue.
            bool propagated = false;
            if (Listener.FocusedHull != null && Listener.FocusedHull != soundHull)
            {
                HashSet<Hull> listenerConnectedHulls = new HashSet<Hull>();
                // Run this even if SoundHull might be null for the sake of collecting listenerConnectedHulls for later.
                float distance = GetApproximateDistance(Listener.LocalPos, localPos, Listener.FocusedHull, soundHull, channel.Far, listenerConnectedHulls);

                if (distance >= float.MaxValue)
                {
                    noPath = true;
                }

                // Only switch to this distance if both the listenHull and soundHull are not null.
                if (soundHull != null)
                {
                    localDistance = distance;
                }

                // Allow certain sounds near a wall to be faintly heard on the opposite side.
                if (noPath && propagateWalls)
                {
                    foreach (Hull hull in listenerConnectedHulls)
                    {
                        if (ShouldSoundPropagateToHull(hull, worldPos))
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
                    localDistance = null;
                    // Add a slight muffle to these sounds.
                    obstructions.Add(Obstruction.Suit);
                }
                else
                {
                    obstructions.Add(Obstruction.WallThick);
                }
            }

            if (Listener.IsEavesdropping && !ignorePath && !noPath)
            {
                eavesdropped = true;
                obstructions.Add(Obstruction.DoorThin);
            }

            // For players without extended sounds, muffle the annoying vanilla exosuit sound for the wearer. TODO TEST THIS
            if (!config.StaticFx &&
                !config.MuffleDivingSuits &&
                Listener.IsWearingExoSuit &&
                worldDistance <= 1 && // Only muffle player's own exosuit.
                channel.Sound.Filename.EndsWith("WEAPONS_chargeUp.ogg")) // Exosuit sound file name.
            {
                obstructions.Add(Obstruction.WallThick);
            }

            bool earsInWater = Listener.IsSubmerged;
            bool ignoreSubmersion = this.ignoreSubmersion || (Listener.IsCharacter ? !config.MuffleSubmergedPlayer : !config.MuffleSubmergedViewTarget);
            bool ignoreSurface = this.ignoreSurface || propagateWalls;

            // Neither in water.
            if (!earsInWater && !inWater)
            {
                return;
            }

            if (channel.Sound.Filename.ToLower().Contains(""))
            {
                LuaCsLogger.Log($"sound: {Path.GetFileName(channel.Sound.Filename)} count: {obstructions.Count}");
            }

            // Both in water, but submersion is ignored.
            else if (earsInWater && inWater && ignoreSubmersion)
            {
                obstructions.Add(Obstruction.Suit);
            }
            // Both in water.
            else if (inWater && earsInWater)
            {
                bothInWater = true;
                obstructions.Add(Obstruction.WaterBody);
            }
            // Sound is under, ears are above, but water surface is ignored.
            else if (ignoreSurface && inWater && !earsInWater)
            {
                if (propagateWalls) // Add some muffle if the surface was only ignored because the sound propagates walls.
                {
                    obstructions.Add(Obstruction.WallThin);
                }
            }
            // Sound is above, ears are below, but water surface and submersion is ignored.
            else if (ignoreSurface && !inWater && earsInWater && ignoreSubmersion)
            {
                if (propagateWalls)
                {
                    obstructions.Add(Obstruction.WallThin);
                }
                else
                {
                    obstructions.Add(Obstruction.Suit);
                }
            }
            // Separated by the water surface.
            else
            {
                obstructions.Add(Obstruction.WaterSurface);
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
            mult += MathHelper.Lerp(config.UnmuffledLoopingVolumeMultiplier, config.MuffledLoopingVolumeMultiplier, muffleStrength) - 1;

            // Dip audio when eavesdropping.
            float eavesdropEfficiency = EavesdropManager.Efficiency;
            float eavesdropMinMult = 0.3f; // Arbitrary magic number.
            if (config.EavesdroppingFadeEnabled && eavesdropEfficiency > 0)
            {
                mult *= Math.Clamp(1 - eavesdropEfficiency, eavesdropMinMult, 1);
            }
            else if (!config.EavesdroppingFadeEnabled && eavesdropEfficiency > 0)
            {
                mult *= eavesdropMinMult;
            }

            // The strength of the sidechaining decreases the more muffled the loud sound is.
            if (isLoud) Plugin.Sidechain.StartRelease(sidechainMult * (1 - muffleStrength), release * (1 - muffleStrength));
            else if (!isLoud) mult *= 1 - Plugin.Sidechain.SidechainMultiplier;

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
            if (distance >= maxDistance || endHull == null) { return float.MaxValue; }
            if (startHull == endHull)
            {
                return distance + Vector2.Distance(startPos, endPos);
            }

            connectedHulls.Add(startHull);
            foreach (Gap g in startHull.ConnectedGaps)
            {
                float distanceMultiplier = 1;
                // For doors.
                if (g.ConnectedDoor != null && !g.ConnectedDoor.IsBroken)
                {
                    // Gap blocked if the door is closed or is curently closing and 90% closed.
                    if ((!g.ConnectedDoor.PredictedState.HasValue && g.ConnectedDoor.IsClosed || g.ConnectedDoor.PredictedState.HasValue && !g.ConnectedDoor.PredictedState.Value) && g.ConnectedDoor.OpenState < 0.1f)
                    {
                        if (distanceMultiplierFromDoors <= 0) { continue; }
                        distanceMultiplier *= distanceMultiplierFromDoors;
                    }
                }
                // For holes in hulls.
                else if (g.Open <= 0.33f)
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

        private static bool GetCustomSoundData(string inputString, out float gainMult, out float sidechainMult, out float release)
        {
            string s = inputString.ToLower();
            gainMult = 1; sidechainMult = 0; release = 60;

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
