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
        public bool MuffleDivingSuits { get; set; } = true;
        public bool TalkingRagdolls { get; set; } = true;
        public bool FocusTargetAudio { get; set; } = true;
        public double GeneralLowpassFrequency { get; set; } = 200f;
        public double DivingSuitLowpassFrequency { get; set; } = 1200; // Also used to muffle propagating sounds and path ignored sounds.
        public float SoundRangeMultiplier { get; set; } = 1.8f;

        // Voice
        public double VoiceLowpassFrequency { get; set; } = 150f;
        public double RadioBandpassFrequency { get; set; } = 1200;
        public float RadioBandpassQualityFactor { get; set; } = 2.5f;
        public float RadioDistortion { get; set; } = 0.0f;
        public float RadioStatic { get; set; } = 0.0f;
        public float RadioCompressionThreshold { get; set; } = 0.5f;
        public float RadioCompressionRatio { get; set; } = 4.0f;
        public float VoiceRangeMultiplier { get; set; } = 0.80f;
        public float RadioRangeMultiplier { get; set; } = 0.75f;

        // Volume
        public bool Sidechaining { get; set; } = true;
        public float SidechainIntensity { get; set; } = 1f;
        public float SidechainReleaseMultiplier { get; set; } = 1f;
        public float MuffledSoundVolumeMultiplier { get; set; } = 0.8f;
        public float MuffledVoiceVolumeMultiplier { get; set; } = 0.8f;
        public float MuffledComponentVolumeMultiplier { get; set; } = 0.75f;
        public float SubmergedVolumeMultiplier { get; set; } = 3f;
        public float UnmuffledComponentVolumeMultiplier { get; set; } = 1f;
        public float FlowSoundVolumeMultiplier { get; set; } = 0.9f;
        public float FireSoundVolumeMultiplier { get; set; } = 1f;

        // Eavesdropping
        public string EavesdroppingBind { get; set; } = "SecondaryMouse";
        public bool MuffleEavesdropping { get; set; } = true;
        public double EavesdroppingLowpassFrequency { get; set; } = 700;
        public float EavesdroppingSoundVolumeMultiplier { get; set; } = 1.1f;
        public float EavesdroppingVoiceVolumeMultiplier { get; set; } = 0.9f;
        public float EavesdroppingPitchMultiplier { get; set; } = 1f;
        public int EavesdroppingMaxDistance { get; set; } = 50; // Max distance in cm from gap.
        public float EavesdroppingThreshold { get; set; } = 0.3f; // How high EavesdroppingEfficiency needs to be before your listening hull swaps.
        public bool EavesdroppingFadeEnabled { get; set; } = true;

        // Hydrophone monitoring
        public float HydrophoneSoundRange { get; set; } = 7500; // In cm.
        public float HydrophoneVolumeMultiplier { get; set; } = 1.1f;
        public float HydrophonePitchMultiplier { get; set; } = 0.8f;
        public bool HydrophoneLegacySwitch { get; set; } = false;
        public bool HydrophoneSwitchEnabled { get; set; } = true;

        // Ambience
        public float UnsubmergedWaterAmbienceVolumeMultiplier { get; set; } = 0.2f;
        public float SubmergedWaterAmbienceVolumeMultiplier { get; set; } = 1.1f;
        public float HydrophoneWaterAmbienceVolumeMultiplier { get; set; } = 2f;
        public float WaterAmbienceTransitionSpeedMultiplier { get; set; } = 3.5f;

        // Advanced settings

        // A general rule is to keep the most vague names at the bottom.
        public HashSet<CustomSound> CustomSounds { get; set; } = new HashSet<CustomSound>(new ElementEqualityComparer())
        {
            new CustomSound("footstep", 0.9f),
            new CustomSound("metalimpact", 0.8f),
            new CustomSound("revolver", 2f, 1, 1.1f),
            new CustomSound("shotgunshot", 2.5f, 1, 1.5f),
            new CustomSound("rifleshot", 2.5f, 1, 1.3f, "harpooncoilrifleshot"),
            new CustomSound("shot", 2f, 1, 0.9f, "shotgunload", "tasershot", "riflegrenadeshot"),

            new CustomSound("coilgun", 2.3f, 1, 1.3f),
            new CustomSound("flakgun", 2.5f, 1, 1.5f),
            new CustomSound("railgun", 3f, 1, 1.9f, "railgunloop", "railgunstart", "railgunstop"),
            new CustomSound("lasergunshot", 2.8f, 1, 1.7f),
            new CustomSound("gravityshells_boom.ogg", 2.0f, 1, 3),

            new CustomSound("incendiumgrenade", 2f, 1, 3),
            new CustomSound("stungrenade", 3f, 1, 4),
            new CustomSound("explosion", 3f, 1, 5),
            new CustomSound("gravityshells", 1.5f, 0.6f, 1),
            new CustomSound("tinnitus", 1, 1, 8),
        };

        public HashSet<string> IgnoredSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/ui",
            "/ambient/",
            "ambience",
            "creak",
            "hull/hull",
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

        // Sounds that ignore muffling by default that should still be checked by MuffleInfo.
        public HashSet<string> LowpassForcedSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/water/flow",
            "barotrauma/content/sounds/fire"
        };

        public HashSet<string> ContainerIgnoredSounds { get; set; } = new HashSet<string>
        {
        };
        // Alarms/sirens should be able to be heard across the ship regardless of walls.
        public HashSet<string> PathIgnoredSounds { get; set; } = new HashSet<string>
        {
            "items/alarmdivingloop.ogg",
            "items/alarmbuzzerloop.ogg",
            "items/warningsiren.ogg"
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
        public HashSet<string> WallPropagatingSounds { get; set; } = new HashSet<string>
        {
            "damage/structure",
            "damage/creak",
            "damage/glass",
            "damage/damage_alienruins",
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
        public bool MuffleExoSuitSound { get; set; } = true; // Muffles the sound of the exo suit moving when wearing it.
        public bool EstimatePathToFakeSounds { get; set; } = false;
        public float SoundPropagationRange { get; set; } = 500; // Range in x/y direction that a sound in WallPropagatingSounds can search for a hull to propagate to.
        public float MuffledSoundPitchMultiplier { get; set; } = 1f; // Strength of the distance-based pitch effect on muffled non-looping sounds.
        public float DivingSuitPitchMultiplier { get; set; } = 1f;
        public float SubmergedPitchMultiplier { get; set; } = 1f;
        public float MuffledVoicePitchMultiplier { get; set; } = 1f;
        public float UnmuffledVoicePitchMultiplier { get; set; } = 1f;
        public float MuffledComponentPitchMultiplier { get; set; } = 1f;
        public float UnmuffledComponentPitchMultiplier { get; set; } = 1f;
        [JsonIgnore]
        public bool HideSettings { get; set; } = false;
    }
}
