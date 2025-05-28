using Barotrauma;
using Barotrauma.Sounds;
using System.Collections.Concurrent;

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
