using Barotrauma;
using System.Text.Json.Serialization;

namespace SoundproofWalls
{
    public class Config
    {

        [JsonIgnore]
        public KeyOrMouse EavesdroppingKeyOrMouse { get; set; } = new KeyOrMouse(MouseButton.SecondaryMouse);
        public KeyOrMouse ParseEavesdroppingBind()
        {
            if (Enum.IsDefined(typeof(Microsoft.Xna.Framework.Input.Keys), EavesdroppingBind))
            {
                return new KeyOrMouse((Microsoft.Xna.Framework.Input.Keys)Enum.Parse(typeof(Microsoft.Xna.Framework.Input.Keys), EavesdroppingBind));
            }
            else if (Enum.IsDefined(typeof(MouseButton), EavesdroppingBind))
            {
                return new KeyOrMouse((MouseButton)Enum.Parse(typeof(MouseButton), EavesdroppingBind));
            }
            return EavesdroppingKeyOrMouse;
        }

        public Config()
        {
            EavesdroppingKeyOrMouse = ParseEavesdroppingBind();
        }

        // General
        public bool Enabled { get; set; } = true;
        public bool SyncSettings { get; set; } = true;
        public bool TalkingRagdolls { get; set; } = true;
        public bool FocusTargetAudio { get; set; } = false;
        public double GeneralLowpassFrequency { get; set; } = 320f;
        public double DivingSuitLowpassFrequency { get; set; } = 1200;
        public double VoiceLowpassFrequency { get; set; } = 160f;
        public float SoundRangeMultiplier { get; set; } = 1.8f;
        public float VoiceRangeMultiplier { get; set; } = 0.80f;
        public float RadioRangeMultiplier { get; set; } = 0.75f;

        // Volume
        public float MuffledSoundVolumeMultiplier { get; set; } = 0.65f;
        public float MuffledVoiceVolumeMultiplier { get; set; } = 0.80f;
        public float MuffledComponentVolumeMultiplier { get; set; } = 0.75f;
        public float SubmergedVolumeMultiplier { get; set; } = 3f;
        public float UnmuffledComponentVolumeMultiplier { get; set; } = 1f;

        // Eavesdropping
        public string EavesdroppingBind { get; set; } = "SecondaryMouse";
        public float EavesdroppingSoundVolumeMultiplier { get; set; } = 0.75f;
        public float EavesdroppingVoiceVolumeMultiplier { get; set; } = 0.70f;
        public float EavesdroppingPitchMultiplier { get; set; } = 0.85f;
        public int EavesdroppingMaxDistance { get; set; } = 40; // distance in cm from door

        // Hydrophone monitoring
        public float HydrophoneSoundRange { get; set; } = 7500; // range in cm
        public float HydrophoneVolumeMultiplier { get; set; } = 1.1f;
        public float HydrophonePitchMultiplier { get; set; } = 0.75f;
        public bool HydrophoneLegacySwitch { get; set; } = false;
        public bool HydrophoneSwitchEnabled { get; set; } = true;

        // Ambience
        public float UnsubmergedWaterAmbienceVolumeMultiplier { get; set; } = 0.18f;
        public float SubmergedWaterAmbienceVolumeMultiplier { get; set; } = 1.1f;
        public float HydrophoneWaterAmbienceVolumeMultiplier { get; set; } = 2f;
        public float WaterAmbienceTransitionSpeedMultiplier { get; set; } = 3.5f;

        // Advanced settings
        public Dictionary<string, float> SoundVolumeMultipliers { get; set; } = new Dictionary<string, float>
        {
            { "explosion", 1.8f },
            { "weapon", 1.4f },
            { "footstep", 0.9f },
            { "metalimpact", 0.75f }
        };

        public HashSet<string> IgnoredSounds { get; set; } = new HashSet<string>
        {
            "/ambient/",
            "ambience",
            "dropitem",
            "pickitem",
        };

        public HashSet<string> PitchIgnoredSounds { get; set; } = new HashSet<string>
        {
            "deconstructor",
            "alarm",
            "sonar",
            "male",
            "female"
        };

        public HashSet<string> LowpassIgnoredSounds { get; set; } = new HashSet<string>
        {
        };

        public HashSet<string> ContainerIgnoredSounds { get; set; } = new HashSet<string>
        {
        };

        public HashSet<string> PathIgnoredSounds { get; set; } = new HashSet<string>
        {
        };

        // Ignore sounds in water without player - Underwater sounds that are NOT occluded when propagating from water to air.
        public HashSet<string> WaterIgnoredSounds { get; set; } = new HashSet<string>
        {
            "splash",
            "footstep",
            "door",
            "pump",
            "emp",
            "electricaldischarge",
            "sonardecoy",
            "alien"
        };

        // Ignore sounds in water with player - Underwater sounds that can be heard clearly when also underwater.
        public HashSet<string> SubmersionIgnoredSounds { get; set; } = new HashSet<string>
        {
            "/characters/",
            "divingsuit",
            "sonardecoy",
            "alienturret",
            "alien_artifactholderloop"
        };

        public HashSet<string> BubbleIgnoredNames { get; set; } = new HashSet<string>
        {
        };

        // Extra settings
        public bool MuffleSubmergedPlayer { get; set; } = true; // the equivalent of adding all sounds into SubmersionIgnoredSounds
        public bool MuffleSubmergedViewTarget { get; set; } = true; // ^
        public bool MuffleSubmergedSounds { get; set; } = true; // the equivalent of adding all sounds into WaterIgnoredSounds
        public bool MuffleFlowSounds { get; set; } = true;
        public bool MuffleFireSounds { get; set; } = true;
        public bool MuffleDivingSuits { get; set; } = true;
        public bool MuffleExoSuitSound { get; set; } = true; // Muffles the sound of the exo suit moving when wearing it.
        public bool EstimatePathToFakeSounds { get; set; } = false;
        public float MuffledSoundPitchMultiplier { get; set; } = 1f;
        public float DivingSuitPitchMultiplier { get; set; } = 1f;
        public float SubmergedPitchMultiplier { get; set; } = 0.9f;
        public float MuffledVoicePitchMultiplier { get; set; } = 1f;
        public float UnmuffledVoicePitchMultiplier { get; set; } = 1f;
        public float MuffledComponentPitchMultiplier { get; set; } = 1f;
        public float UnmuffledComponentPitchMultiplier { get; set; } = 1f;
        [JsonIgnore]
        public bool HideSettings { get; set; } = false;
    }
}
