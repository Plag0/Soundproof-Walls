using Barotrauma;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;

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

        public bool IgnoreBarriers = false;
        public bool IgnoreSurface = false;
        public bool IgnoreSubmersion = false;
        public bool IgnorePitch = false;
        public bool IgnoreLowpass = false;
        public bool IgnoreXML = false;
        public bool IgnoreAirReverb = false;
        public bool IgnoreWaterReverb = false;
        public bool ForceReverb = false;
        public bool IgnoreContainer = false;
        public bool IgnoreAll = false;
        public bool PropagateWalls = false;
        public bool IgnoreHydrophoneVisuals = false;
        public bool IgnoreHydrophoneMuffle = false;

        public bool IsChargeSound = false;

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
            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.SoundInfoCtor);

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
                IgnoreXML = Util.StringHasKeyword(filename, config.XMLIgnoredSounds);
                IgnorePitch = Util.StringHasKeyword(filename, config.PitchIgnoredSounds);
                IgnoreAirReverb = Util.StringHasKeyword(filename, config.AirReverbIgnoredSounds);
                IgnoreWaterReverb = Util.StringHasKeyword(filename, config.WaterReverbIgnoredSounds);
                ForceReverb = Util.StringHasKeyword(filename, config.ReverbForcedSounds);
                IgnoreBarriers = IgnoreLowpass || Util.StringHasKeyword(filename, config.BarrierIgnoredSounds);
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
            else if (lower.Contains("content/sounds/water/flow"))
            {
                StaticType = AudioType.FlowSound;
            }
            else if (lower.Contains("content/sounds/fire"))
            {
                StaticType = AudioType.FireSound;
            }
            else if (Sound is VoipSound)
            {
                StaticType = AudioType.LocalVoice;
            }
            else if (lower.Contains("barotrauma/content/sounds/ambient") || 
                     lower.Contains("barotrauma/content/sounds/damage/creak") || 
                     lower.Contains("barotrauma/content/sounds/hull"))
            {
                StaticType = AudioType.AmbientSound;
            }
            else if (lower.Contains("chargeup"))
            {
                IsChargeSound = true;
            }

            PerformanceProfiler.Instance.StopTimingEvent();
        }

        private static CustomSound? GetCustomSound(string filename)
        {
            filename = filename.ToLower();
            CustomSound? customSound = null;
            ContentPackage? overridingMod = null;

            foreach (var sound in ConfigManager.Config.CustomSounds)
            {
                if (sound.Keyword == null)
                {
                    LuaCsLogger.LogError("[SoundproofWalls] Warning: local config file contains null keyword for custom sound. Please remove this keyword or reset your config.");
                    continue;
                }

                string keyword = sound.Keyword.ToLower();

                if (filename.Contains(keyword))
                {
                    bool excluded = false;
                    foreach (string exclusion in sound.KeywordExclusions)
                    {
                        if (filename.Contains(exclusion.ToLower())) { excluded = true; break; }
                    }

                    if (excluded) { continue; }
                    
                    if (customSound == null) { customSound = sound; }
                    //else { LuaCsLogger.LogError($"[SoundproofWalls] Warning: sound with filename \"{filename}\" matched with multiple CustomSound keywords (\"{customSound.Keyword.ToLower()}\" & \"{keyword}\"). The CustomSound highest on the list has been selected (\"{customSound.Keyword.ToLower()}\")."); }
                }
            }

            foreach (var kvp in ConfigManager.ModdedCustomSounds)
            {
                ContentPackage mod = kvp.Key;
                HashSet<CustomSound> moddedCustomSounds = kvp.Value;

                foreach (var sound in moddedCustomSounds)
                {
                    if (sound.Keyword == null)
                    {
                        LuaCsLogger.LogError($"[SoundproofWalls] Warning: the mod \"{mod.Name}\" has an spw_overrides.json file that contains a null keyword. Please remove this keyword or delete the file.");
                        continue;
                    }

                    string keyword = sound.Keyword.ToLower();

                    if (filename.Contains(keyword))
                    {
                        bool excluded = false;
                        foreach (string exclusion in sound.KeywordExclusions)
                        {
                            if (filename.Contains(exclusion.ToLower())) { excluded = true; break; }
                        }

                        if (excluded) { continue; }

                        // Putting this warning here for other modders.
                        if (overridingMod == null) 
                        { 
                            if (customSound != null) { LuaCsLogger.Log($"[SoundproofWalls] Potential conflict warning: sound with filename \"{filename}\" was already assigned CustomSound data using the keyword \"{customSound.Keyword.ToLower()}\". The mod \"{mod.Name}\" is overriding this assignment with their own CustomSound using the keyword \"{keyword}\". If this is intentional you can ignore this message.", color: Color.Yellow); }
                            customSound = sound; overridingMod = mod;
                        }
                        else { LuaCsLogger.LogError($"[SoundproofWalls] Mod conflict warning: sound with filename \"{filename}\" was already assigned CustomSound data by the mod \"{overridingMod.Name}\" using the keyword \"{customSound.Keyword.ToLower()}\". The mod \"{mod.Name}\" is overriding this assignment with their own CustomSound using the keyword \"{keyword}\". The mod with the highest load order has been selected (\"{overridingMod.Name}\")."); }
                    }
                }
            }

            return customSound;
        }
    }
}
