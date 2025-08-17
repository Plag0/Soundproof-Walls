using Barotrauma;
using Barotrauma.Sounds;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SoundproofWalls
{
    public static class SoundInfoManager
    {
        private static ConcurrentDictionary<string, SoundInfo> soundInfoMap = new ConcurrentDictionary<string, SoundInfo>();

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

        public static void UpdateSoundInfoMap()
        {
            ChannelInfoManager.ClearChannelInfo();
            ClearSoundInfo();

            List<Sound> loadedSounds;
            lock (GameMain.SoundManager.loadedSounds)
            {
                loadedSounds = GameMain.SoundManager.loadedSounds.ToList();
            }

            for (int i = 0; i < loadedSounds.Count; i++)
            {
                CreateSoundInfo(loadedSounds[i]);
            }
        }

        public static bool ShouldUpdateSoundInfo(Config newConfig, Config oldConfig = null)
        {
            return !oldConfig.CustomSounds.SetEquals(newConfig.CustomSounds) ||
                    !oldConfig.IgnoredSounds.SetEquals(newConfig.IgnoredSounds) ||
                    !oldConfig.SurfaceIgnoredSounds.SetEquals(newConfig.SurfaceIgnoredSounds) ||
                    !oldConfig.SubmersionIgnoredSounds.SetEquals(newConfig.SubmersionIgnoredSounds) ||
                    !oldConfig.PropagatingSounds.SetEquals(newConfig.PropagatingSounds) ||
                    !oldConfig.BarrierIgnoredSounds.SetEquals(newConfig.BarrierIgnoredSounds) ||
                    !oldConfig.PitchIgnoredSounds.SetEquals(newConfig.PitchIgnoredSounds) ||
                    !oldConfig.XMLIgnoredSounds.SetEquals(newConfig.XMLIgnoredSounds) ||
                    !oldConfig.LowpassIgnoredSounds.SetEquals(newConfig.LowpassIgnoredSounds) ||
                    !oldConfig.ReverbForcedSounds.SetEquals(newConfig.ReverbForcedSounds) ||
                    !oldConfig.WaterReverbIgnoredSounds.SetEquals(newConfig.WaterReverbIgnoredSounds) ||
                    !oldConfig.AirReverbIgnoredSounds.SetEquals(newConfig.AirReverbIgnoredSounds) ||
                    !oldConfig.ContainerIgnoredSounds.SetEquals(newConfig.ContainerIgnoredSounds) ||
                    !oldConfig.HydrophoneMuffleIgnoredSounds.SetEquals(newConfig.HydrophoneMuffleIgnoredSounds) ||
                    !oldConfig.HydrophoneVisualIgnoredSounds.SetEquals(newConfig.HydrophoneVisualIgnoredSounds);
        }

        public static bool ShouldUpdateSoundInfo(Dictionary<ContentPackage, HashSet<CustomSound>> dict1, Dictionary<ContentPackage, HashSet<CustomSound>> dict2)
        {
            if (dict1 == dict2) { return false; }
            if (dict1 == null || dict2 == null) { return true; }
            if (dict1.Count != dict2.Count) { return true; }

            // Check each key and its corresponding hash set.
            foreach (var kvp in dict1)
            {
                ContentPackage key = kvp.Key;
                HashSet<CustomSound> set1 = kvp.Value;

                // Check if the second dictionary has the same key.
                // If not, or if the hash sets don't match, they're not equal.
                if (!dict2.TryGetValue(key, out HashSet<CustomSound> set2) ||
                    JsonSerializer.Serialize(set1) != JsonSerializer.Serialize(set2))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CreateSoundInfo(Sound sound, bool onlyUnique = true)
        {
            string filename = sound.Filename;
            if (onlyUnique && soundInfoMap.TryGetValue(filename, out SoundInfo? soundInfo) && soundInfo != null) { return; }

            soundInfoMap[filename] = new SoundInfo(sound);
        }

        public static SoundInfo EnsureGetSoundInfo(Sound sound)
        {
            string filename = sound.Filename;
            if (!soundInfoMap.TryGetValue(filename, out SoundInfo? soundInfo) || soundInfo == null)
            {
                soundInfo = new SoundInfo(sound);
                soundInfoMap[filename] = soundInfo;
            }

            return soundInfo;
        }

        public static void ClearSoundInfo()
        {
            soundInfoMap.Clear();
        }
    }
}
