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

        public static int SourceCount = 128;

        private static ConcurrentDictionary<uint, ChannelInfo> channelInfoMap = new ConcurrentDictionary<uint, ChannelInfo>();
        private static ConcurrentDictionary<SoundChannel, bool> pitchedChannels = new ConcurrentDictionary<SoundChannel, bool>();
        public static List<ChannelInfo> EavesdroppedChannels = new List<ChannelInfo>();
        public static List<ChannelInfo> HydrophonedChannels = new List<ChannelInfo>();

        public static IEnumerable<ChannelInfo> ActiveChannelInfos => channelInfoMap.Values.Where(info => info.Channel != null && info.Channel.IsPlaying);

        public static float ScreamModeTrailingRangeFar = ConfigManager.Config.ScreamModeMinRange;

        public static void Update()
        {
            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.ChannelInfoManagerUpdate);

            if (ConfigManager.Config.ScreamMode)
            {
                float changePerSecond = ConfigManager.Config.ScreamModeReleaseRate;
                ScreamModeTrailingRangeFar = Util.SmoothStep(ScreamModeTrailingRangeFar, ConfigManager.Config.ScreamModeMinRange, (float)(changePerSecond * Timing.Step));
            }

            List<ChannelInfo> eavesdroppedChannels = new List<ChannelInfo>();
            List<ChannelInfo> hydrophonedChannels = new List<ChannelInfo>();

            foreach (ChannelInfo info in channelInfoMap.Values)
            {
                if (ConfigManager.Config.EavesdroppingVisualFeedbackEnabled &&
                    (info.Eavesdropped || ConfigManager.Config.EavesdroppingRevealsAll && Listener.IsEavesdropping) && 
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

                // Update non-looping sounds.
                if (ConfigManager.Config.UpdateNonLoopingSounds && !info.Channel.Looping && !info.AudioIsVoice && info.Channel.IsPlaying)
                {
                    info.Update();
                }
            }

            EavesdroppedChannels = eavesdroppedChannels;
            HydrophonedChannels = hydrophonedChannels;

            PerformanceProfiler.Instance.StopTimingEvent();
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
                Obstruction.Drowning => config.ObstructionDrowning,
                _ => 0f
            };
        }

        public static ChannelInfo EnsureUpdateVoiceInfo(SoundChannel channel, Client speakingClient, ChatMessageType messageType, Hull? soundHull = null)
        {
            if (channel == null || 
                channel.Sound == null || 
                speakingClient == null) 
            { 
                return null; 
            }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            if (!channelInfoMap.TryGetValue(sourceId, out ChannelInfo? info))
            {
                info = new ChannelInfo(channel, soundHull, null, null, speakingClient, messageType, false);
                channelInfoMap[sourceId] = info;
            }
            else
            {
                info.Update(soundHull, null, null, speakingClient, messageType);
            }

            return info;
        }

        // Updates or creates a ChannelInfo object.
        public static ChannelInfo EnsureUpdateChannelInfo(SoundChannel channel, Hull? soundHull = null, ItemComponent? itemComp = null, StatusEffect? statusEffect = null, Client? speakingClient = null, ChatMessageType? messageType = null, bool dontMuffle = false)
        {
            if (channel == null) { return null; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            if (!channelInfoMap.TryGetValue(sourceId, out ChannelInfo? info))
            {
                info = new ChannelInfo(channel, soundHull, statusEffect, itemComp, speakingClient, messageType, dontMuffle);
                channelInfoMap[sourceId] = info;
            }
            else
            {
                info.Update(soundHull, statusEffect, itemComp, speakingClient, messageType);
            }

            return info;
        }

        public static bool TryGetChannelInfo(SoundChannel channel, out ChannelInfo? channelInfo)
        {
            channelInfo = null;
            if (channel == null) { return false; }
            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            return channelInfoMap.TryGetValue(sourceId, out channelInfo);
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
            if (channel == null || channel.Sound == null) { return false; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
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
