using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using System.Collections.Concurrent;

namespace SoundproofWalls
{
    public static class ChannelInfoManager
    {
        // The origin of these magic numbers can be found in the vanilla VoipSound class under the initialization of the muffleFilters and radioFilters arrays.
        public const short VANILLA_VOIP_LOWPASS_FREQUENCY = 800;
        public const short VANILLA_VOIP_BANDPASS_FREQUENCY = 2000;

        public const float CLONE_FREQ_MULT_CODE = 1.006921f;

        public static int SourceCount = 128;

        private static ConcurrentDictionary<uint, ChannelInfo> channelInfoMap = new ConcurrentDictionary<uint, ChannelInfo>();
        private static ConcurrentDictionary<SoundChannel, bool> pitchedChannels = new ConcurrentDictionary<SoundChannel, bool>();
        public static List<ChannelInfo> EavesdroppedChannels = new List<ChannelInfo>();
        public static List<ChannelInfo> HydrophonedChannels = new List<ChannelInfo>();

        public static void Update()
        {
            List<ChannelInfo> eavesdroppedChannels = new List<ChannelInfo>();
            List<ChannelInfo> hydrophonedChannels = new List<ChannelInfo>();

            foreach (ChannelInfo info in channelInfoMap.Values)
            {
                if ((info.Eavesdropped || ConfigManager.Config.EavesdroppingRevealsAll && Listener.IsEavesdropping) && 
                    !info.AudioIsFlow && !info.AudioIsFire && !info.AudioIsAmbience && 
                    info.Channel.IsPlaying && info.ChannelHull != Listener.CurrentHull)
                {
                    eavesdroppedChannels.Add(info);
                }
                // Don't display flow/fire sounds or engine sounds.
                else if (info.Hydrophoned && !info.AudioIsFlow && !info.AudioIsFire && !info.AudioIsAmbience 
                    && info.Channel.IsPlaying && info.ItemComp as Engine == null && !info.SoundInfo.IgnoreHydrophoneVisuals)
                {
                    hydrophonedChannels.Add(info);
                }

                // Update obstructions for voice on the main thread.
                double currentTime = Timing.TotalTime;
                if (ConfigManager.Config.DynamicFx && info.AudioIsVoice &&
                    currentTime > info.LastVoiceMainThreadUpdateTime + info.VoiceMainThreadUpdateInterval)
                {
                    info.LastVoiceMainThreadUpdateTime = currentTime;

                    // Get occluding bodies.
                    IEnumerable<FarseerPhysics.Dynamics.Body> bodies = Submarine.PickBodies(Listener.SimPos, info.SimPos, collisionCategory: Physics.CollisionWall);
                    lock (info.VoiceOcclusionsLock) { info.VoiceOcclusions = bodies.ToList(); }

                    // Get best path.
                    Hull? listenerHull = Listener.FocusedHull;
                    List<SoundPathfinder.PathfindingResult> topResults = SoundPathfinder.FindShortestPaths(
                        info.WorldPos, info.ChannelHull,
                        Listener.WorldPos, listenerHull,
                        listenerHull?.Submarine, 1);
                    lock (info.VoicePathResultsLock) { info.VoicePathResults = topResults.ToList(); }
                }

                // Update non-looping sounds.
                else if (ConfigManager.Config.UpdateNonLoopingSounds && !info.Channel.Looping && !info.AudioIsVoice && info.Channel.IsPlaying)
                {
                    info.Update();  
                }
            }

            EavesdroppedChannels = eavesdroppedChannels;
            HydrophonedChannels = hydrophonedChannels;

            if (ConfigManager.LocalConfig.DebugPlayingSounds)
            {
                int i = 1;
                string newLines = "\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n";
                LuaCsLogger.Log($"{newLines}[Soundproof Walls] DebugPlayingSounds enabled.\nCurrently playing channels:");
                foreach (ChannelInfo info in channelInfoMap.Values)
                {
                    if (info.Channel.IsPlaying)
                    { 
                        // TODO could add more info like soundInfo exclusions and muffle level etc
                        LuaCsLogger.Log($"          {i}. {info.LongName}");
                        i++;
                    }
                }
            }
        }

        // "Thin" versions are used when the Thick version is penetrated or eavesdropped.
        public static float GetStrength(this Obstruction obstruction)
        {
            var config = ConfigManager.Config;
            return obstruction switch
            {
                Obstruction.WaterSurface => config.ObstructionWaterSurface,
                Obstruction.WaterBody => config.ObstructionWaterBody,
                Obstruction.WallThick => config.ObstructionWallThick,
                Obstruction.WallThin => config.ObstructionWallThin,
                Obstruction.DoorThick => config.ObstructionDoorThick,
                Obstruction.DoorThin => config.ObstructionDoorThin,
                Obstruction.Suit => config.ObstructionSuit,
                _ => 0f
            };
        }

        public static void EnsureUpdateVoiceInfo(SoundChannel channel, Hull? soundHull = null, Client? speakingClient = null, ChatMessageType? messageType = null)
        {
            if (channel == null) { return; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            if (!channelInfoMap.TryGetValue(sourceId, out ChannelInfo? info))
            {
                info = new ChannelInfo(channel, soundHull, null, null, speakingClient, messageType, false, false);
                channelInfoMap[sourceId] = info;
            }
            else
            {
                info.Update(soundHull, null, null, speakingClient, messageType);
            }
        }

        // Updates an existing ChannelInfo object.
        public static bool UpdateChannelInfo(SoundChannel channel)
        {
            if (channel == null) { return false; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            if (channelInfoMap.TryGetValue(sourceId, out ChannelInfo? info))
            {
                info.Update();
                return true;
            }

            return false;
        }

        // Updates or creates a ChannelInfo object.
        public static ChannelInfo EnsureUpdateChannelInfo(SoundChannel channel, Hull? soundHull = null, ItemComponent? itemComp = null, StatusEffect? statusEffect = null, Client? speakingClient = null, ChatMessageType? messageType = null, bool dontMuffle = false, bool dontPitch = false)
        {
            if (channel == null) { return null; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            if (!channelInfoMap.TryGetValue(sourceId, out ChannelInfo? info))
            {
                info = new ChannelInfo(channel, soundHull, statusEffect, itemComp, speakingClient, messageType, dontMuffle, dontPitch);
                channelInfoMap[sourceId] = info;
            }
            else
            {
                info.Update(soundHull, statusEffect, itemComp, speakingClient, messageType);
            }

            return info;
        }

        public static bool TryGetChannelInfo(SoundChannel channel, out ChannelInfo? soundInfo)
        {
            soundInfo = null;
            if (channel == null) { return false; }
            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            return channelInfoMap.TryGetValue(sourceId, out soundInfo);
        }

        public static void ResetAllPitchedChannels()
        {
            foreach (var kvp in pitchedChannels)
            {
                SoundChannel channel = kvp.Key;
                RemovePitchedChannel(channel);
            }
            pitchedChannels.Clear();
        }
        public static bool RemoveChannelInfo(SoundChannel channel)
        {
            if (channel == null) { return false; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            channelInfoMap.TryGetValue(sourceId, out ChannelInfo? info);
            if (info != null) { info.DisposeClones(); }
            bool success = channelInfoMap.TryRemove(sourceId, out _);

            return success;
        }

        public static bool AddPitchedChannel(SoundChannel channel)
        {
            if (channel == null || channel.FrequencyMultiplier == 1) { return false; }

            pitchedChannels[channel] = true;

            return true;
        }

        public static bool RemovePitchedChannel(SoundChannel channel)
        {
            if (channel == null) { return false; }

            if (channel.FrequencyMultiplier != 1)
            {
                channel.FrequencyMultiplier = 1;
            }
            return pitchedChannels.TryRemove(channel, out _);
        }

        public static void ClearChannelInfo()
        {
            channelInfoMap.Clear();
        }
    }
}
