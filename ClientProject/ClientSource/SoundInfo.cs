using Barotrauma.Sounds;

namespace SoundproofWalls
{
    public class SoundInfo
    {
        public Sound Sound;
        public CustomSound? CustomSound;

        public float GainMult = 1;
        public float RangeMult = 1;
        public float SidechainMult = 0;
        public float SidechainRelease = 0;
        public bool Distortion = false;
        public float PitchMult = 1;
        public float MuffleInfluence = 1;
        public bool IsLoud = false;

        public bool IgnorePath = false;
        public bool IgnoreSurface = false;
        public bool IgnoreSubmersion = false;
        public bool IgnorePitch = false;
        public bool IgnoreLowpass = false;
        public bool ForceLowpass = false;
        public bool IgnoreAirReverb = false;
        public bool IgnoreWaterReverb = false;
        public bool ForceReverb = false;
        public bool IgnoreContainer = false;
        public bool IgnoreAll = false;
        public bool PropagateWalls = false;
        public bool IgnoreHydrophoneVisuals = false;
        public bool IgnoreHydrophoneMuffle = false;

        public enum AudioType
        {
            BaseSound,
            DoorSound,
            FlowSound,
            FireSound,
            AmbientSound,
            LocalVoice,
            RadioVoice
        }

        public AudioType StaticType;

        public SoundInfo(Sound sound)
        {
            Sound = sound;

            Config config = ConfigManager.Config;

            string filename = sound.Filename;
            if (Util.StringHasKeyword(filename, config.IgnoredSounds))
            {
                IgnoreAll = true;
            }
            else
            {
                IgnoreLowpass = Util.StringHasKeyword(filename, config.LowpassIgnoredSounds);
                ForceLowpass = Util.StringHasKeyword(filename, config.LowpassForcedSounds);
                IgnorePitch = Util.StringHasKeyword(filename, config.PitchIgnoredSounds);
                IgnoreAirReverb = Util.StringHasKeyword(filename, config.AirReverbIgnoredSounds);
                IgnoreWaterReverb = Util.StringHasKeyword(filename, config.WaterReverbIgnoredSounds);
                ForceReverb = Util.StringHasKeyword(filename, config.ReverbForcedSounds);
                IgnorePath = IgnoreLowpass || Util.StringHasKeyword(filename, config.PathIgnoredSounds);
                IgnoreSurface = IgnoreLowpass || !config.MuffleWaterSurface || Util.StringHasKeyword(filename, config.SurfaceIgnoredSounds);
                IgnoreSubmersion = IgnoreLowpass || Util.StringHasKeyword(filename, config.SubmersionIgnoredSounds, exclude: "Barotrauma/Content/Characters/Human/");
                IgnoreContainer = IgnoreLowpass || Util.StringHasKeyword(filename, config.ContainerIgnoredSounds);
                IgnoreAll = IgnoreLowpass && IgnorePitch;
                PropagateWalls = !IgnoreAll && !IgnoreLowpass && Util.StringHasKeyword(filename, config.PropagatingSounds);
                IgnoreHydrophoneMuffle = Util.StringHasKeyword(filename, config.HydrophoneMuffleIgnoredSounds);
                IgnoreHydrophoneVisuals = Util.StringHasKeyword(filename, config.HydrophoneVisualIgnoredSounds);

                Sound.MaxSimultaneousInstances = config.MaxSimultaneousInstances;
            }

            CustomSound = GetCustomSound(filename);
            if (CustomSound != null)
            {
                GainMult = CustomSound.GainMult;
                RangeMult = CustomSound.RangeMult;
                SidechainMult = CustomSound.SidechainMult;
                SidechainRelease = CustomSound.SidechainRelease;
                Distortion = CustomSound.Distortion;
                PitchMult = CustomSound.PitchMult;
                MuffleInfluence = CustomSound.MuffleInfluence;
                IsLoud = SidechainMult > 0;
            }

            // We can only determine types based on their filename here. More advanced types are resolved in ChannelInfo.
            StaticType = AudioType.BaseSound;
            string lower = filename.ToLower();
            if (lower.Contains("door") && !lower.Contains("doorbreak") || lower.Contains("duct") && !lower.Contains("ductbreak"))
            {
                StaticType = AudioType.DoorSound;
            }
            else if (lower.Contains("barotrauma/content/sounds/ambient") || 
                     lower.Contains("barotrauma/content/sounds/damage/creak") || 
                     lower.Contains("barotrauma/content/sounds/hull"))
            {
                StaticType = AudioType.AmbientSound;
            }
        }

        private static CustomSound? GetCustomSound(string soundName)
        {
            string s = soundName.ToLower();

            foreach (var sound in ConfigManager.Config.CustomSounds)
            {
                if (s.Contains(sound.Keyword.ToLower()))
                {
                    bool excluded = false;
                    foreach (string exclusion in sound.KeywordExclusions)
                    {
                        if (s.Contains(exclusion.ToLower())) { excluded = true; }
                    }
                    if (!excluded) { return sound; }
                }
            }
            return null;
        }
    }
}
