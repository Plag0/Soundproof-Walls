using Barotrauma;
using System.Text.Json.Serialization;

namespace SoundproofWalls
{
    public class Config
    {
        [JsonIgnore]
        public const uint EFFECT_PROCESSING_CLASSIC = 0;
        [JsonIgnore]
        public const uint EFFECT_PROCESSING_STATIC = 1;
        [JsonIgnore]
        public const uint EFFECT_PROCESSING_DYNAMIC = 2;

        [JsonIgnore]
        public bool ClassicFx => EffectProcessingMode == EFFECT_PROCESSING_CLASSIC;
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
        public bool DrowningBubblesEnabled { get; set; } = true;
        public bool FocusTargetAudio { get; set; } = true;
        public bool AttenuateWithApproximateDistance = true;
        public bool WhisperMode { get; set; } = true;
        public double HeavyLowpassFrequency { get; set; } = 200; // Used for wall and water obstructions.
        public float SoundRangeMultiplierMaster { get; set; } = 1.6f;
        public float LoopingSoundRangeMultiplierMaster { get; set; } = 0.9f;

        // DynamicFx
        public bool DynamicReverbEnabled { get; set; } = true;
        public bool DynamicReverbRadio { get; set; } = false;
        public float DynamicReverbAreaSizeMultiplier { get; set; } = 1.0f;
        public float DynamicReverbAirTargetGain { get; set; } = 0.40f;
        public float DynamicReverbWaterTargetGain { get; set; } = 0.6f;
        public float DyanmicReverbWaterAmplitudeThreshold { get; set; } = 0.75f; // The necessary amplitude * gain needed for a non "loud" source to have reverb applied in water.
        public bool LoudSoundDistortionEnabled { get; set; } = true; // Warning: CAN make loud sounds extremely loud.
        public float LoudSoundDistortionTargetGain { get; set; } = 0.2f;
        public float LoudSoundDistortionTargetEdge { get; set; } = 0.34f;
        public int LoudSoundDistortionTargetFrequency { get; set; } = 200; // The frequencies targeted by the distortion
        public int LoudSoundDistortionLowpassFrequency { get; set; } = 24000; // The frequencies allowed through post-distortion
        public bool HydrophoneDistortionEnabled { get; set; } = true;
        public float HydrophoneDistortionTargetGain { get; set; } = 0.24f;
        public float HydrophoneDistortionTargetEdge { get; set; } = 0.22f;
        public bool HydrophoneBandpassFilterEnabled { get; set; } = true;
        public float HydrophoneBandpassFilterHfGain { get; set; } = 0.2f;
        public float HydrophoneBandpassFilterLfGain { get; set; } = 0.65f;
        public float DynamicMuffleTransitionFactor { get; set; } = 1.2f; // The max change of high frequency gain over the span of a second. Transitions the muffle effect on and off.
        public float DynamicMuffleStrengthMultiplier { get; set; } = 1.0f;
        public int MaxSimulatedSoundDirections { get; set; } = 0; // How many additional versions of the same sound can be playing simultaneously from different directions.
        public bool OccludeSounds { get; set; } = true; // Enable muffle strength from wall occlusion. Is still disabled for classicFx
        public bool AutoAttenuateMuffledSounds { get; set; } = true; // Should the volume of the lower frequencies (not just the high freqs) be attenuated with muffle strength.
        public bool HighFidelityMuffling { get; set; } = false; // Creates a new effect slot with an EQ for each pool of uniquely muffled channels. Higher performance cost over basic lowpass filters.
        public bool RemoveUnusedBuffers { get; set; } = false; // If enabled, sounds are loaded without the vanilla muffle buffer which saves roughly 200MB of memory. Downside is 1-2 seconds of extra loading times.

        // StaticFx
        public bool StaticReverbEnabled { get; set; } = true;
        public bool StaticReverbAlwaysOnLoudSounds { get; set; } = true;
        public float StaticReverbDuration { get; set; } = 2.2f;
        public float StaticReverbWetDryMix { get; set; } = 0.30f;
        public int StaticReverbMinArea { get; set; } = 375_000; // The minimum area the listener has to be in for non-looping non-muffled sounds to use reverb buffers.
        public double MediumLowpassFrequency { get; set; } = 700; // Used for eavesdropping.
        public double LightLowpassFrequency { get; set; } = 1200; // Used for wearing suits, propagating sounds, and path ignored sounds.
        
        // Voice
        public bool RadioCustomFilterEnabled { get; set; } = true;
        public double VoiceHeavyLowpassFrequency { get; set; } = 150f; // Used when a player's voice is muffled heavily. Otherwise, uses medium or light.
        public double RadioBandpassFrequency { get; set; } = 2010;
        public float RadioBandpassQualityFactor { get; set; } = 2.5f;
        public float RadioDistortion { get; set; } = 0.0f;
        public float RadioStatic { get; set; } = 0.0f;
        public float RadioCompressionThreshold { get; set; } = 0.2f;
        public float RadioCompressionRatio { get; set; } = 0.2f;
        public float VoiceRangeMultiplier { get; set; } = 0.90f;
        public float RadioRangeMultiplier { get; set; } = 0.75f;

        // Muffle
        public bool MuffleDivingSuits { get; set; } = true; // Not available for Classic mode.
        public bool MuffleEavesdropping { get; set; } = true; // Not available for Classic mode.
        public bool MuffleSubmergedPlayer { get; set; } = true; // the equivalent of adding all sounds into SubmersionIgnoredSounds
        public bool MuffleSubmergedViewTarget { get; set; } = true; // ^
        public bool MuffleWaterSurface { get; set; } = true; // the equivalent of adding all sounds into SurfaceIgnoredSounds
        public bool MuffleFlowSounds { get; set; } = true;
        public bool MuffleFireSounds { get; set; } = true;

        // Volume
        public bool SidechainingEnabled { get; set; } = true;
        public bool SidechainingDucksMusic { get; set; } = true;
        public float SidechainMusicDuckMultiplier { get; set; } = 0.05f;
        public float SidechainIntensityMaster { get; set; } = 1f;
        public float SidechainReleaseMaster { get; set; } = 0;
        public float SidechainReleaseCurve { get; set; } = 0.4f;
        public float MuffledSoundVolumeMultiplier { get; set; } = 0.8f;
        public float MuffledVoiceVolumeMultiplier { get; set; } = 0.8f;
        public float MuffledLoopingVolumeMultiplier { get; set; } = 0.75f;
        public float UnmuffledLoopingVolumeMultiplier { get; set; } = 1f;
        public float SubmergedVolumeMultiplier { get; set; } = 3.0f;
        public float FlowSoundVolumeMultiplier { get; set; } = 0.85f;
        public float FireSoundVolumeMultiplier { get; set; } = 1.1f;
        public float VanillaExosuitVolumeMultiplier { get; set; } = 0.0f; // Needs a unique setting instead of being added to CustomSounds because the audio file is shared. Doesn't apply to modded exosuit audio.

        // Eavesdropping
        public bool EavesdroppingEnabled { get; set; } = true;
        public bool EavesdroppingTransitionEnabled { get; set; } = true;
        public bool EavesdroppingDucksRadio { get; set; } = true;
        public string EavesdroppingBind { get; set; } = "SecondaryMouse";
        public float EavesdroppingSoundVolumeMultiplier { get; set; } = 2.5f;
        public float EavesdroppingVoiceVolumeMultiplier { get; set; } = 1.5f;
        public float EavesdroppingPitchMultiplier { get; set; } = 1f;
        public int EavesdroppingMaxDistance { get; set; } = 50; // Max distance in cm from gap.
        public float EavesdroppingTransitionDuration { get; set; } = 1.6f;
        public float EavesdroppingZoomMultiplier { get; set; } = 1.15f;
        public float EavesdroppingVignetteOpacityMultiplier { get; set; } = 1.0f;
        public float EavesdroppingThreshold { get; set; } = 0.28f; // How high Efficiency needs to be before your listening hull swaps.

        // Hydrophone monitoring
        public bool HydrophoneSwitchEnabled { get; set; } = true;
        public bool HydrophoneLegacySwitch { get; set; } = false;
        public float HydrophoneSoundRange { get; set; } = 7500; // In cm.
        public float HydrophoneVolumeMultiplier { get; set; } = 1.5f;
        public float HydrophonePitchMultiplier { get; set; } = 0.75f;

        // Ambience
        public bool DisableWhiteNoise { get; set; } = true;
        public float UnsubmergedWaterAmbienceVolumeMultiplier { get; set; } = 0.15f;
        public float SubmergedWaterAmbienceVolumeMultiplier { get; set; } = 0.6f;
        public float HydrophoneWaterAmbienceVolumeMultiplier { get; set; } = 2.0f;
        public float WaterAmbienceTransitionSpeedMultiplier { get; set; } = 3.5f;

        // Pitch settings
        public bool PitchSoundsByDistance { get; set; } = true;
        public float DivingSuitPitchMultiplier { get; set; } = 1f;
        public float SubmergedPitchMultiplier { get; set; } = 0.65f;
        public float MuffledSoundPitchMultiplier { get; set; } = 1f; // Strength of the distance-based pitch effect on muffled non-looping sounds.
        public float MuffledLoopingPitchMultiplier { get; set; } = 0.98f;
        public float UnmuffledSoundPitchMultiplier { get; set; } = 1f;
        public float MuffledVoicePitchMultiplier { get; set; } = 1f;
        public float UnmuffledVoicePitchMultiplier { get; set; } = 1f;

        // Advanced settings
        public bool UpdateNonLoopingSounds { get; set; } = true; // Updates the gain and pitch of non looping "single-shot" sounds every tick. Muffle is updated every NonLoopingSoundMuffleUpdateInterval.
        public double NonLoopingSoundMuffleUpdateInterval { get; set; } = 0.2f; // Only applied if UpdateNonLoopingSounds is enabled.
        public double ComponentMuffleUpdateInterval { get; set; } = 0.2f;
        public double StatusEffectMuffleUpdateInterval { get; set; } = 0.2f;
        public float PitchTransitionFactor { get; set; } = 1.1f;
        public float GainTransitionFactor { get; set; } = 1.1f;

        public int MaxSimultaneousInstances = 8; // How many instances of the same sound clip can be playing at the same time. Vanilla is 5 (cite Sound.cs)
        public float LoopingComponentSoundNearMultiplier { get; set; } = 0.3f; // near = far * thisMult - "near" is the max range before volume falloff starts.
        public float SoundPropagationRange { get; set; } = 500; // Area^2 that a sound in WallPropagatingSounds can search for a hull to propagate to.
        public bool TraverseWaterDucts { get; set; } = false; // Should the search algorithm pass through water ducts?
        public float OpenDoorThreshold { get; set; } = 0.1f; // How open a door/hatch/duct must be for sound to pass through unobstructed.
        public float OpenWallThreshold { get; set; } = 0.33f; // How open a gap in a wall must be for sound to pass through unobstructed.

        // How muffled each type of obstruction is. Lowpass doesn't increase on values > 1.0, but gain continues to be reduced.
        public float ObstructionWaterSurface { get; set; } = 1.0f;
        public float ObstructionWaterBody { get; set; } = 0.55f;
        public float ObstructionWallThick { get; set; } = 0.90f;
        public float ObstructionWallThin { get; set; } = 0.70f;
        public float ObstructionDoorThick { get; set; } = 0.80f;
        public float ObstructionDoorThin { get; set; } = 0.65f;
        public float ObstructionSuit { get; set; } = 0.50f;

        // Amount of combined obstruction strength needed to achieve different muffle levels
        public float ClassicMinMuffleThreshold { get; set; } = 0.75f;
        public float StaticMinLightMuffleThreshold { get; set; } = 0.01f;
        public float StaticMinMediumMuffleThreshold { get; set; } = 0.65f;
        public float StaticMinHeavyMuffleThreshold { get; set; } = 0.80f;

        // A general rule is to keep the most vague names at the bottom so when searching for a match the more specific ones can be found first.
        public HashSet<CustomSound> CustomSounds { get; set; } = new HashSet<CustomSound>(new ElementEqualityComparer())
        {
            new CustomSound("footstep", 0.75f),
            new CustomSound("door", 1.0f, 2.0f),
            new CustomSound("metalimpact", 0.8f, 1.2f),
            new CustomSound("revolver", 2.0f, 1.2f, 1, 1.1f),
            new CustomSound("shotgunshot", 2.5f, 1.3f, 1, 1.5f),
            new CustomSound("rifleshot", 2.5f, 1.3f, 1, 1.3f, "harpooncoilrifleshot"),
            new CustomSound("shot", 2f, 1.15f, 1, 0.9f, "shotgunload", "tasershot", "riflegrenadeshot"),

            new CustomSound("coilgun", 2.3f, 1.35f, 1, 1.3f),
            new CustomSound("flakgun", 2.5f, 1.4f, 1, 1.5f),
            new CustomSound("railgun", 3f, 1.5f, 1, 1.9f, "railgunloop", "railgunstart", "railgunstop"),
            new CustomSound("lasergunshot", 2.8f, 1.3f, 1, 1.7f),
            new CustomSound("gravityshells_boom.ogg", 2.0f, 1.5f, 1, 3),

            new CustomSound("sonardecoy.ogg", 1, 1.1f, 0.6f, 1.5f),

            new CustomSound("incendiumgrenade", 2, 1.2f, 1, 3),
            new CustomSound("stungrenade", 3, 1.2f, 1, 4),
            new CustomSound("explosion", 3, 1.6f, 1, 5),
            new CustomSound("gravityshells", 1.5f, 1.4f, 1, 1.5f),
            new CustomSound("tinnitus", 1, 1, 1, 8),
        };

        // Sounds in this list are ignored by all muffling/pitching/other processing except for gain.
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
            "barotrauma/content/items/warningbeep",
            "barotrauma/content/items/alien",
            "sonar"
        };

        // Propagating sounds pierce through one layer of walls/water.
        public HashSet<string> PropagatingSounds { get; set; } = new HashSet<string>
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
            "railgun1",
            "railgun2",
            "railgun3",
            "chaingunshot",
            "flakgun",
            "coilgun",
            "lasergunshot",
        };

        // Alarms/sirens should be able to be heard across the ship regardless of walls (still affected by water).
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
            "items/warningbeep",
            "items/fabricators",
            "divingsuitloop",
            "divingsuitoxygenleakloop",
            "door",
            "sonar",
            "male",
            "female"
        };

        // Sounds that ignore muffling by default that should still be checked by SoundInfo.
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

        public HashSet<string> ReverbForcedSounds { get; set; } = new HashSet<string> // Sounds that are always reverbed.
        {
            "explosion",
        };

        public HashSet<string> ReverbIgnoredSounds { get; set; } = new HashSet<string> // Sounds that are not reverbed by static or dynamic processing modes.
        {
            "items/alarmdivingloop.ogg",
            "divingsuitloop", // Remove this line for some cool new ambience sounds :)
            "divingsuitoxygenleakloop",
            "scooterloop",

        };

        public HashSet<string> ContainerIgnoredSounds { get; set; } = new HashSet<string>
        {
        };

        public HashSet<string> BubbleIgnoredNames { get; set; } = new HashSet<string>
        {
        };

        [JsonIgnore]
        public bool HideSettings { get; set; } = false;
        [JsonIgnore]
        public bool RememberScroll { get; set; } = true;
        [JsonIgnore]
        public bool DebugObstructions { get; set; } = false; // See what is obstructing all audio with console output.
        [JsonIgnore]
        public bool DebugPlayingSounds { get; set; } = false; // See all playing sounds and their filenames.
    }
}
