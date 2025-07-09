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
        public float SidechainRelease = 1;
        public float PitchMult = 1;
        public bool IsLoud = false;

        public bool IgnorePath = false;
        public bool IgnoreSurface = false;
        public bool IgnoreSubmersion = false;
        public bool IgnorePitch = false;
        public bool IgnoreLowpass = false;
        public bool ForceLowpass = false;
        public bool IgnoreReverb = false;
        public bool ForceDistortion = false;
        public bool IgnoreDistortion = false;
        public bool ForceReverb = false;
        public bool IgnoreContainer = false;
        public bool IgnoreAll = false;
        public bool PropagateWalls = false;

        public SoundInfo(Sound sound)
        {
            this.Sound = sound;

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
                IgnoreReverb = Util.StringHasKeyword(filename, config.ReverbIgnoredSounds);
                ForceReverb = !IgnoreReverb && Util.StringHasKeyword(filename, config.ReverbForcedSounds);
                IgnoreDistortion = Util.StringHasKeyword(filename, config.DistortionIgnoredSounds);
                ForceDistortion = !IgnoreDistortion && Util.StringHasKeyword(filename, config.DistortionForcedSounds);
                IgnorePath = IgnoreLowpass || Util.StringHasKeyword(filename, config.PathIgnoredSounds);
                IgnoreSurface = IgnoreLowpass || !config.MuffleWaterSurface || Util.StringHasKeyword(filename, config.SurfaceIgnoredSounds);
                IgnoreSubmersion = IgnoreLowpass || Util.StringHasKeyword(filename, config.SubmersionIgnoredSounds, exclude: "Barotrauma/Content/Characters/Human/");
                IgnoreContainer = IgnoreLowpass || Util.StringHasKeyword(filename, config.ContainerIgnoredSounds);
                IgnoreAll = IgnoreLowpass && IgnorePitch;
                PropagateWalls = !IgnoreAll && !IgnoreLowpass && Util.StringHasKeyword(filename, config.PropagatingSounds);

                Sound.MaxSimultaneousInstances = config.MaxSimultaneousInstances;
            }

            CustomSound = GetCustomSound(filename);
            if (CustomSound != null)
            {
                GainMult = CustomSound.GainMult;
                RangeMult = CustomSound.RangeMult;
                SidechainMult = CustomSound.SidechainMult;
                SidechainRelease = CustomSound.SidechainRelease;
                PitchMult = CustomSound.PitchMult;
                IsLoud = SidechainMult > 0;
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
