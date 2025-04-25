using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using Barotrauma;
using System.Collections.Concurrent;

namespace SoundproofWalls
{
    public static class SoundInfoManager
    {
        private static ConcurrentDictionary<uint, SoundInfo> soundInfoMap = new ConcurrentDictionary<uint, SoundInfo>();
        private static ConcurrentDictionary<SoundChannel, bool> pitchedChannels = new ConcurrentDictionary<SoundChannel, bool>();

        // Expensive or unnecessary sounds that are unlikely to be muffled and so are ignored when reloading sounds as a loading time optimisation.
        public static readonly HashSet<string> IgnoredPrefabs = new HashSet<string>
        {
            "Barotrauma/Content/Sounds/Music/",
            "Barotrauma/Content/Sounds/UI/",
            "Barotrauma/Content/Sounds/Ambient/",
            "Barotrauma/Content/Sounds/Hull/",
            "Barotrauma/Content/Sounds/Water/WaterAmbience",
            "Barotrauma/Content/Sounds/RadioStatic",
            "Barotrauma/Content/Sounds/MONSTER_farLayer.ogg",
            "Barotrauma/Content/Sounds/Tinnitus",
            "Barotrauma/Content/Sounds/Heartbeat",
        };

        public static readonly Dictionary<Obstruction, float> ObstructionStrength = new Dictionary<Obstruction, float>
        {
            { Obstruction.WaterSurface, 0.6f },
            { Obstruction.WaterBody, 0.25f },
            { Obstruction.WallThick, 0.4f },
            { Obstruction.WallThin, 0.2f },
            { Obstruction.DoorThick, 0.3f },
            { Obstruction.DoorThin, 0.15f },
            { Obstruction.Suit, 0.1f },
        };

        /// <summary>
        /// Applies updated information to the SoundChannel directly.
        /// </summary>
        public static SoundInfo UpdateSoundInfo(SoundChannel channel, Hull? soundHull = null, ItemComponent? itemComp = null, StatusEffect? statusEffect = null, Client? speakingClient = null, ChatMessageType? messageType = null, bool dontMuffle = false, bool dontPitch = false)
        {
            if (channel == null) { return null; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            if (!soundInfoMap.TryGetValue(sourceId, out SoundInfo? info))
            {
                info = new SoundInfo(channel, soundHull, statusEffect, itemComp, speakingClient, messageType, dontMuffle, dontPitch);
                soundInfoMap[sourceId] = info;
            }
            else
            {
                info.Update(soundHull, statusEffect, itemComp, speakingClient, messageType);
            }

            return info;
        }

        public static bool TryGetSoundInfo(SoundChannel channel, out SoundInfo? soundInfo)
        {
            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            bool success = soundInfoMap.TryGetValue(sourceId, out SoundInfo? info);
            soundInfo = info;
            return success;
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
        public static bool RemoveSoundInfo(SoundChannel channel)
        {
            if (channel == null) { return false; }

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            bool success = soundInfoMap.TryRemove(sourceId, out _);

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

        public static void ClearSoundInfo()
        {
            soundInfoMap.Clear();
        }
    }
}
