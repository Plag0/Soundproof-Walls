﻿using Barotrauma;
using System.Text.Json.Serialization;

namespace SoundproofWalls
{
    public class Config
    {
        [JsonIgnore]
        public const uint EFFECT_PROCESSING_VANILLA = 0;
        [JsonIgnore]
        public const uint EFFECT_PROCESSING_STATIC = 1;
        [JsonIgnore]
        public const uint EFFECT_PROCESSING_DYNAMIC = 2;

        [JsonIgnore]
        public bool VanillaFx => EffectProcessingMode == EFFECT_PROCESSING_VANILLA;
        [JsonIgnore]
        public bool StaticFx => EffectProcessingMode == EFFECT_PROCESSING_STATIC;
        [JsonIgnore]
        public bool DynamicFx => EffectProcessingMode == EFFECT_PROCESSING_DYNAMIC;

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
        public uint EffectProcessingMode { get; set; } = 2;
        public bool SyncSettings { get; set; } = true;
        public bool TalkingRagdolls { get; set; } = true;
        public bool FocusTargetAudio { get; set; } = true;
        public double HeavyLowpassFrequency { get; set; } = 200; // Used for wall and water obstructions.
        public double MediumLowpassFrequency { get; set; } = 700; // Used for eavesdropping.
        public double LightLowpassFrequency { get; set; } = 1200; // Used for wearing suits, propagating sounds, and path ignored sounds.
        public float SoundRangeMultiplier { get; set; } = 2.0f;
        public float SoundPropagationRange { get; set; } = 500; // Area^2 that a sound in WallPropagatingSounds can search for a hull to propagate to.

        // Voice
        public double VoiceHeavyLowpassFrequency { get; set; } = 150f; // Used when a player's voice is muffled heavily. Otherwise, uses medium or light.
        public double RadioBandpassFrequency { get; set; } = 2010;
        public float RadioBandpassQualityFactor { get; set; } = 2.5f;
        public float RadioDistortion { get; set; } = 0.0f;
        public float RadioStatic { get; set; } = 0.0f;
        public float RadioCompressionThreshold { get; set; } = 0.2f;
        public float RadioCompressionRatio { get; set; } = 0.2f;
        public float VoiceRangeMultiplier { get; set; } = 0.80f;
        public float RadioRangeMultiplier { get; set; } = 0.75f;

        // Muffle
        public bool MuffleDivingSuits { get; set; } = true; // Requires StaticEffectProcessing to be on.
        public bool MuffleEavesdropping { get; set; } = true; // Requires StaticEffectProcessing to be on.
        public bool MuffleSubmergedPlayer { get; set; } = true; // the equivalent of adding all sounds into SubmersionIgnoredSounds
        public bool MuffleSubmergedViewTarget { get; set; } = true; // ^
        public bool MuffleSubmergedSounds { get; set; } = true; // the equivalent of adding all sounds into SurfaceIgnoredSounds
        public bool MuffleFlowSounds { get; set; } = true;
        public bool MuffleFireSounds { get; set; } = true;

        // Volume
        public bool Sidechaining { get; set; } = true;
        public bool SidechainingDucksMusic { get; set; } = false;
        public float SidechainIntensityMaster { get; set; } = 1f;
        public float SidechainReleaseMaster { get; set; } = 0;
        public float SidechainReleaseCurve { get; set; } = 0.4f;
        public float MuffledSoundVolumeMultiplier { get; set; } = 0.8f;
        public float MuffledVoiceVolumeMultiplier { get; set; } = 0.8f;
        public float MuffledComponentVolumeMultiplier { get; set; } = 0.75f;
        public float UnmuffledComponentVolumeMultiplier { get; set; } = 1f;
        public float SubmergedVolumeMultiplier { get; set; } = 2.5f;
        public float FlowSoundVolumeMultiplier { get; set; } = 0.9f;
        public float FireSoundVolumeMultiplier { get; set; } = 1f;

        // Eavesdropping
        public bool EavesdroppingEnabled { get; set; } = true;
        public bool EavesdroppingFadeEnabled { get; set; } = true;
        public string EavesdroppingBind { get; set; } = "SecondaryMouse";
        public float EavesdroppingSoundVolumeMultiplier { get; set; } = 1.1f;
        public float EavesdroppingVoiceVolumeMultiplier { get; set; } = 0.9f;
        public float EavesdroppingPitchMultiplier { get; set; } = 1f;
        public int EavesdroppingMaxDistance { get; set; } = 50; // Max distance in cm from gap.
        public float EavesdroppingFadeDuration { get; set; } = 1.75f;
        public float EavesdroppingThreshold { get; set; } = 0.3f; // How high EavesdroppingEfficiency needs to be before your listening hull swaps.

        // Hydrophone monitoring
        public bool HydrophoneSwitchEnabled { get; set; } = true;
        public bool HydrophoneLegacySwitch { get; set; } = false;
        public float HydrophoneSoundRange { get; set; } = 7500; // In cm.
        public float HydrophoneVolumeMultiplier { get; set; } = 1.5f;
        public float HydrophonePitchMultiplier { get; set; } = 0.8f;

        // Ambience
        public float UnsubmergedWaterAmbienceVolumeMultiplier { get; set; } = 0.2f;
        public float SubmergedWaterAmbienceVolumeMultiplier { get; set; } = 0.9f;
        public float HydrophoneWaterAmbienceVolumeMultiplier { get; set; } = 2.0f;
        public float WaterAmbienceTransitionSpeedMultiplier { get; set; } = 3.5f;

        // Pitch settings
        public float DivingSuitPitchMultiplier { get; set; } = 1f;
        public float SubmergedPitchMultiplier { get; set; } = 1f;
        public float MuffledSoundPitchMultiplier { get; set; } = 1f; // Strength of the distance-based pitch effect on muffled non-looping sounds.
        public float MuffledComponentPitchMultiplier { get; set; } = 1f;
        public float UnmuffledComponentPitchMultiplier { get; set; } = 1f;
        public float MuffledVoicePitchMultiplier { get; set; } = 1f;
        public float UnmuffledVoicePitchMultiplier { get; set; } = 1f;

        // Advanced settings

        // A general rule is to keep the most vague names at the bottom so when searching for a match the more specific ones can be found first.
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
            "barotrauma/content/sounds/ambient",
            "barotrauma/content/sounds/music",
            "barotrauma/content/sounds/damage/creak",
            "barotrauma/content/sounds/hull",
            "barotrauma/content/sounds/dropitem",
            "barotrauma/content/sounds/pickitem",
            "tinnitus",
            "sonarambience" // Real Sonar entry.
        };

        // Sounds that don't treat the surface of the water like another wall and can pass in and out freely.
        public HashSet<string> SurfaceIgnoredSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/water/splash",
            "barotrauma/content/items/pump",
            "footstep", // Prevents footsteps from being muffled when standing in an inch of water.
            "door", // These entries are more ambiguous to allow for mods that may replace rhese sounds.
        };

        // Submerged sounds that can freely travel through their body of water without it being treated like a wall.
        // Note: this is only relevant if the listener's ears are also submerged.
        //       It's not necessary for a sound to ignore submersion to exit through the ignored surface.
        //       This setting is basically, "can the listener's ears hear this sound clearly when they are submerged with it or is it muffled?"
        public HashSet<string> SubmersionIgnoredSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/characters", // Enemy creatures.
            "barotrauma/content/items/diving",
            "barotrauma/content/items/alien",
            "sonar"
        };

        public HashSet<string> WallPropagatingSounds { get; set; } = new HashSet<string>
        {
            "damage/structure",
            "damage/creak",
            "damage/glass",
            "damage/damage_alienruins",
            "doorbreak",
            "electricaldischarge",
            "sonarping",
            "explosion",

            // Allow turrets to be heard from the hull directly below/above them.
            "railgun", // Includes the turret moving sounds.
            "chaingunshot",
            "flakgun",
            "coilgun",
            "lasergunshot",
        };

        // Alarms/sirens should be able to be heard across the ship regardless of walls.
        public HashSet<string> PathIgnoredSounds { get; set; } = new HashSet<string>
        {
            "items/alarmdivingloop.ogg",
            "items/alarmbuzzerloop.ogg",
            "items/warningsiren.ogg"
        };

        public HashSet<string> PitchIgnoredSounds { get; set; } = new HashSet<string>
        {
            "items/alarmdivingloop.ogg",
            "items/alarmbuzzerloop.ogg",
            "items/warningsiren.ogg",
            "items/fabricators",
            "door",
            "sonar",
            "male",
            "female"
        };

        // Sounds that ignore muffling by default that should still be checked by MuffleInfo.
        public HashSet<string> LowpassForcedSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/water/flow",
            "barotrauma/content/sounds/fire",
            "spineling", // It seems that spineling attacks ignore muffling.
            "sonardecoy"
        };

        public HashSet<string> LowpassIgnoredSounds { get; set; } = new HashSet<string>
        {
        };

        public HashSet<string> ContainerIgnoredSounds { get; set; } = new HashSet<string>
        {
        };

        public HashSet<string> BubbleIgnoredNames { get; set; } = new HashSet<string>
        {
        };

        [JsonIgnore]
        public bool HideSettings { get; set; } = false;
    }
}
