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
        public const uint CUSTOM_RADIO_BROKEN = 0;
        [JsonIgnore]
        public const uint CUSTOM_RADIO_DIRTY = 1;
        [JsonIgnore]
        public const uint CUSTOM_RADIO_NORMAL = 2;
        [JsonIgnore]
        public const uint CUSTOM_RADIO_CLEAN = 3;

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
        public bool SyncSettings { get; set; } = true;
        public uint EffectProcessingMode { get; set; } = 2;
        public bool FocusTargetAudio { get; set; } = true;
        public bool AttenuateWithApproximateDistance { get; set; } = true;
        public float SoundRangeMultiplierMaster { get; set; } = 1.6f;
        public float LoopingSoundRangeMultiplierMaster { get; set; } = 0.9f;

        // DynamicFx
        public bool OccludeSounds { get; set; } = true; // Enable muffle strength from wall occlusion. Is still disabled for classicFx
        public int MaxOcclusions { get; set; } = 0; // The maximum walls registered by occlusion. 0 is infinite
        public bool OverMuffle { get; set; } = true; // Should the volume of the lower frequencies (not just the high freqs) be attenuated with muffle strength.
        public float DynamicMuffleStrengthMultiplier { get; set; } = 1.0f;
        public float OverMuffleStrengthMultiplier { get; set; } = 0.33f;
        public bool DynamicReverbEnabled { get; set; } = true;
        public bool DynamicReverbBloom { get; set; } = true;
        public bool DynamicReverbWaterSubtractsArea { get; set; } = true;
        public int DynamicReverbMinArea { get; set; } = 0;
        public float DynamicReverbAreaSizeMultiplier { get; set; } = 1.0f;
        public float DynamicReverbWetRoomAreaSizeMultiplier { get; set; } = 1.5f;
        public float DynamicReverbAirTargetGain { get; set; } = 0.30f;
        public float DynamicReverbAirDurationMultiplier { get; set; } = 1.0f;
        public float DynamicReverbAirGainHf { get; set; } = 0.3f;
        public float DynamicReverbWaterTargetGain { get; set; } = 0.55f;
        public float DyanmicReverbAirAmplitudeThreshold { get; set; } = 0.0f; // Zero to avoid audio pops
        public float DyanmicReverbWaterAmplitudeThreshold { get; set; } = 0.75f; // The necessary amplitude * gain needed for a non "loud" source to have reverb applied in water.
        public bool LoudSoundDistortionAirEnabled { get; set; } = true;
        public float LoudSoundDistortionAirMaxMuffleThreshold { get; set; } = 0.5f;
        public float LoudSoundDistortionAirTargetGain { get; set; } = 0.22f;
        public float LoudSoundDistortionAirTargetEdge { get; set; } = 0.34f;
        public int LoudSoundDistortionAirTargetFrequency { get; set; } = 250; // The frequencies targeted by the distortion
        public int LoudSoundDistortionAirLowpassFrequency { get; set; } = 24000; // The frequencies allowed through post-distortion
        public bool LoudSoundDistortionWaterEnabled { get; set; } = true;
        public float LoudSoundDistortionWaterMaxMuffleThreshold { get; set; } = 0.9f;
        public float LoudSoundDistortionWaterTargetGain { get; set; } = 0.85f;
        public float LoudSoundDistortionWaterTargetEdge { get; set; } = 0.15f;
        public int LoudSoundDistortionWaterTargetFrequency { get; set; } = 200; // The frequencies targeted by the distortion
        public int LoudSoundDistortionWaterLowpassFrequency { get; set; } = 180; // The frequencies allowed through post-distortion
        public bool RemoveUnusedBuffers { get; set; } = false; // If enabled, sounds are loaded without the vanilla muffle buffer which saves roughly 200MB of memory. Downside is 1-2 seconds of extra loading times.

        // StaticFx
        public int HeavyLowpassFrequency { get; set; } = 200;
        public int MediumLowpassFrequency { get; set; } = 700;
        public int LightLowpassFrequency { get; set; } = 1920;

        public bool StaticReverbEnabled { get; set; } = true;
        public bool StaticReverbAlwaysOnLoudSounds { get; set; } = true;
        public float StaticReverbDuration { get; set; } = 3.2f;
        public float StaticReverbWetDryMix { get; set; } = 0.20f;
        public float StaticReverbDamping { get; set; } = 0.45f;
        public int StaticReverbMinArea { get; set; } = 375_000; // The minimum area the listener has to be in for non-looping non-muffled sounds to use reverb buffers.

        // Voice
            // General
        public bool TalkingRagdolls { get; set; } = true;
        public int VoiceHeavyLowpassFrequency { get; set; } = 150;
        public int VoiceMediumLowpassFrequency { get; set; } = 600;
        public int VoiceLightLowpassFrequency { get; set; } = 1600;

        public bool VoiceLocalReverb { get; set; } = true;
        public bool VoiceRadioReverb { get; set; } = false;
        public bool DrowningRadioDistortion { get; set; } = true;
        public int VoiceMinLowpassFrequency { get; set; } = 90;
        public bool ScreamMode { get; set; } = false;
        public int ScreamModeMaxRange { get; set; } = 1450; // cm
        public int ScreamModeMinRange { get; set; } = 500; // cm
        public int ScreamModeReleaseRate { get; set; } = 350; // cm
        public float VoiceDynamicMuffleMultiplier { get; set; } = 1.0f;
        public float VoiceLocalRangeMultiplier { get; set; } = 1.1f;
        public float VoiceRadioRangeMultiplier { get; set; } = 1.0f;
        public float VoiceLocalVolumeMultiplier { get; set; } = 1.0f;
        public float VoiceRadioVolumeMultiplier { get; set; } = 1.0f;
            // Bubbles
        public bool DrowningBubblesEnabled { get; set; } = true;
        public float DrowningBubblesLocalRangeMultiplier { get; set; } = 1.2f; // in cm
        public float DrowningBubblesLocalVolumeMultiplier { get; set; } = 1.0f;
        public float DrowningBubblesRadioVolumeMultiplier { get; set; } = 0.80f;
            // Custom Filter
        public bool RadioCustomFilterEnabled { get; set; } = true;
        public uint RadioCustomPreset { get; set; } = 1;
        public int RadioBandpassFrequency { get; set; } = 2250;
        public float RadioBandpassQualityFactor { get; set; } = 7f;
        public float RadioDistortionDrive { get; set; } = 8.0f;
        public float RadioDistortionThreshold { get; set; } = 0.55f;
        public float RadioStatic { get; set; } = 0.05f;
        public float RadioCompressionThreshold { get; set; } = 0.25f;
        public float RadioCompressionRatio { get; set; } = 0.8f;
        public float RadioPostFilterBoost { get; set; } = 0.9f;

        // Muffle
        public bool MuffleDivingSuits { get; set; } = true; // Not available for Classic mode.
        public bool MuffleSubmergedPlayer { get; set; } = true; // the equivalent of adding all sounds into SubmersionIgnoredSounds
        public bool MuffleSubmergedViewTarget { get; set; } = true; // ^
        public bool MuffleWaterSurface { get; set; } = true; // the equivalent of adding all sounds into SurfaceIgnoredSounds
        public bool MuffleFlowFireSoundsWithEstimatedPath { get; set; } = true;
        public bool MuffleFlowSounds { get; set; } = true;
        public bool MuffleFireSounds { get; set; } = true;

        public int ClassicMuffleFrequency { get; set; } = 200;

        // How muffled each type of obstruction is. Lowpass doesn't increase on values > 1.0, but gain continues to be reduced if Over-muffle is enabled.
        public float ObstructionWaterSurface { get; set; } = 1.0f;
        public float ObstructionWaterBody { get; set; } = 0.75f;
        public float ObstructionWallThick { get; set; } = 0.998f;
        public float ObstructionWallThin { get; set; } = 0.70f;
        public float ObstructionDoorThick { get; set; } = 1.0f;
        public float ObstructionDoorThin { get; set; } = 0.80f;
        public float ObstructionSuit { get; set; } = 0.50f;
        public float ObstructionDrowning { get; set; } = 0.98f;
        // Amount of combined obstruction strength needed to achieve different muffle levels
        public float ClassicMinMuffleThreshold { get; set; } = 0.85f;
        public float StaticMinLightMuffleThreshold { get; set; } = 0.01f;
        public float StaticMinMediumMuffleThreshold { get; set; } = 0.80f;
        public float StaticMinHeavyMuffleThreshold { get; set; } = 0.95f;

        // Volume
        public bool SidechainingEnabled { get; set; } = true;
        public bool SidechainMusic { get; set; } = true;
        public bool SidechainRadio { get; set; } = true;
        public float SidechainIntensityMaster { get; set; } = 1f;
        public float SidechainReleaseMaster { get; set; } = 0;
        public float SidechainReleaseCurve { get; set; } = 0.4f;
        public float SidechainMusicMultiplier { get; set; } = 0.05f;
        public float MuffledSoundVolumeMultiplier { get; set; } = 0.8f;
        public float UnmuffledSoundVolumeMultiplier { get; set; } = 1.0f;
        public float MuffledVoiceVolumeMultiplier { get; set; } = 0.8f;
        public float UnmuffledVoiceVolumeMultiplier { get; set; } = 1.0f;
        public float MuffledLoopingVolumeMultiplier { get; set; } = 0.75f;
        public float UnmuffledLoopingVolumeMultiplier { get; set; } = 1f;
        public float SubmergedVolumeMultiplier { get; set; } = 3.0f;
        public float FlowSoundVolumeMultiplier { get; set; } = 0.85f;
        public float FireSoundVolumeMultiplier { get; set; } = 1.1f;

        // Advanced
        public float VanillaExosuitVolumeMultiplier { get; set; } = 0.0f; // Needs a unique setting instead of being added to CustomSounds because the audio file is shared. Doesn't apply to modded exosuit audio.
        public float LoopingComponentSoundNearMultiplier { get; set; } = 0.0f; // near = far * thisMult  |  "near" is the max range before volume falloff starts.
        public float MinDistanceFalloffVolume { get; set; } = 0.0f; // The minimum gain a sound being attenuated by approx dist can reach.
        public float SidechainMufflePower { get; set; } = 6f;

        // Eavesdropping
        public bool EavesdroppingEnabled { get; set; } = true;
        public bool EavesdroppingMuffle { get; set; } = true; // Not available for Classic mode.
        public bool EavesdroppingTransitionEnabled { get; set; } = true;
        public bool EavesdroppingDucksRadio { get; set; } = true;
        public bool EavesdroppingVisualFeedbackEnabled { get; set; } = true;
        public bool EavesdroppingRevealsCharacterOutline { get; set; } = true;
        public bool EavesdroppingRevealsAll { get; set; } = false;
        public int EavesdroppingSpriteMaxSize { get; set; } = 400;
        public float EavesdroppingSpriteSizeMultiplier { get; set; } = 0.2f;
        public float EavesdroppingSpriteOpacity { get; set; } = 0.3f;
        public float EavesdroppingSpriteFadeCurve { get; set; } = 0.45f;
        public string EavesdroppingBind { get; set; } = "SecondaryMouse";
        public float EavesdroppingSoundVolumeMultiplier { get; set; } = 2.5f;
        public float EavesdroppingVoiceVolumeMultiplier { get; set; } = 1.5f;
        public float EavesdroppingPitchMultiplier { get; set; } = 1f;
        public int EavesdroppingMaxDistance { get; set; } = 50; // Max distance in cm from gap.
        public float EavesdroppingTransitionDuration { get; set; } = 1.6f;
        public float EavesdroppingThreshold { get; set; } = 0.28f; // How high Efficiency needs to be before your listening hull swaps.
        public float EavesdroppingZoomMultiplier { get; set; } = 1.15f;
        public float EavesdroppingVignetteOpacityMultiplier { get; set; } = 0.55f;

        // Hydrophone monitoring
        public bool HydrophoneSwitchEnabled { get; set; } = true;
        public bool HydrophoneMovementSounds { get; set; } = true;
        public bool HydrophoneHearEngine { get; set; } = true;
        public bool HydrophoneHearIntoStructures { get; set; } = true;
        public bool HydrophoneMuffleOwnSub { get; set; } = true;
        public bool HydrophoneMuffleAmbience { get; set; } = true;
        public bool HydrophoneLegacySwitch { get; set; } = false;
        public int HydrophoneSoundRange { get; set; } = 9_500; // In cm. Making this greater than 10k is problematic because you can hear if any creatures are in range of your sonar before pinging.
        public float HydrophoneVolumeMultiplier { get; set; } = 1.0f;
        public float HydrophonePitchMultiplier { get; set; } = 0.75f;
        public float HydrophoneAmbienceVolumeMultiplier { get; set; } = 0.85f;
        public float HydrophoneMovementVolumeMultiplier { get; set; } = 0.75f;
            // Visuals
        public bool HydrophoneVisualFeedbackEnabled { get; set; } = true;
        public bool HydrophoneUsageDisablesSonarBlips { get; set; } = true;
        public bool HydrophoneUsageDisablesSubOutline { get; set; } = false;
        public float HydrophoneVisualFeedbackSizeMultiplier { get; set; } = 0.85f;
        public float HydrophoneVisualFeedbackOpacityMultiplier { get; set; } = 1.0f;
            // DynamicFx
        public bool HydrophoneReverbEnabled { get; set; } = true;
        public float HydrophoneReverbTargetGain { get; set; } = 0.35f;
        public bool HydrophoneDistortionEnabled { get; set; } = true;
        public float HydrophoneDistortionTargetGain { get; set; } = 0.10f; // Note that a higher gain reduces the dry signal, so directionality is then harder to determine
        public float HydrophoneDistortionTargetEdge { get; set; } = 0.17f;
        public bool HydrophoneBandpassFilterEnabled { get; set; } = false;
        public float HydrophoneBandpassFilterHfGain { get; set; } = 0.2f;
        public float HydrophoneBandpassFilterLfGain { get; set; } = 0.65f;
            // Advanced

        public HashSet<string> HydrophoneMuffleIgnoredSounds { get; set; } = new HashSet<string> // Sounds that are always muffled.
        {
            "sonar", // Don't muffle sonar pings when toggling it while eavesdropping
        };
        public HashSet<string> HydrophoneVisualIgnoredSounds { get; set; } = new HashSet<string> // Sounds that are always reverbed.
        {
        };

        // Ambience
        public bool DisableWhiteNoise { get; set; } = true;

        // Pertains to specific channels in all environments.
        public float WaterAmbienceInVolumeMultiplier { get; set; } = 1.0f; 
        public float WaterAmbienceOutVolumeMultiplier { get; set; } = 0.25f;
        public float WaterAmbienceMovingVolumeMultiplier { get; set; } = 1.5f;

        // Pertains to all channels in specific environments.
        public float UnsubmergedNoSuitWaterAmbienceVolumeMultiplier { get; set; } = 0.14f;
        public float UnsubmergedSuitWaterAmbienceVolumeMultiplier { get; set; } = 0.05f;
        public float SubmergedNoSuitWaterAmbienceVolumeMultiplier { get; set; } = 0.75f;
        public float SubmergedSuitWaterAmbienceVolumeMultiplier { get; set; } = 0.25f;
        public float WaterAmbienceTransitionSpeedMultiplier { get; set; } = 3.5f;

        // Pitch settings
        public bool PitchEnabled { get; set; } = true; // Global pitch toggle. Affects Custom Sound pitches too.
        public bool PitchStatusEffectSounds { get; set; } = false;
        public bool PitchWithDistance { get; set; } = true;
        public float DivingSuitPitchMultiplier { get; set; } = 1f;
        public float SubmergedPitchMultiplier { get; set; } = 0.70f;
        public float MuffledSoundPitchMultiplier { get; set; } = 1f; // Strength of the distance-based pitch effect on muffled non-looping sounds.
        public float UnmuffledSoundPitchMultiplier { get; set; } = 1f;
        public float MuffledLoopingPitchMultiplier { get; set; } = 0.98f;
        public float UnmuffledLoopingPitchMultiplier { get; set; } = 1.0f;
        public float MuffledVoicePitchMultiplier { get; set; } = 1f;
        public float UnmuffledVoicePitchMultiplier { get; set; } = 1f;

        // Advanced settings
        public bool ShowPerformance { get; set; } = false;
        public bool ShowPlayingSounds { get; set; } = false; // See all playing sounds and their filenames.
        public bool ShowChannelInfo { get; set; } = false; // See what is obstructing all audio with console output.
        public bool HideSettingsButton { get; set; } = false;
        public bool RememberMenuTabAndScroll { get; set; } = true;
        public int MaxSourceCount { get; set; } = 128; // How many sounds can be playing at once. Vanilla is 32 (cite SoundManager.cs)
        public int MaxSimultaneousInstances { get; set; } = 64; // How many instances of the same sound clip can be playing at the same time. Vanilla is 5 (cite Sound.cs)

            // Update Intervals
        public bool UpdateNonLoopingSounds { get; set; } = true; // Updates the gain and pitch of non looping "single-shot" sounds every tick. Muffle is updated every NonLoopingSoundMuffleUpdateInterval.
        public float VoiceMuffleUpdateInterval { get; set; } = 0.01f;
        public float NonLoopingSoundMuffleUpdateInterval { get; set; } = 0.01f; // Only applied if UpdateNonLoopingSounds is enabled.
        public float OpenALEffectsUpdateInterval { get; set; } = 0.01f; // Recommend lowering if having trouble with reverb amplitude gates
        public float ComponentMuffleUpdateInterval { get; set; } = 0.01f;
        public float StatusEffectMuffleUpdateInterval { get; set; } = 0.01f;

            // Transitions
        public bool DisableVanillaFadeOutAndDispose { get; set; } = false; // Disables the vanilla "FadeOutAndDispose" function that has the potential to cause issues with permanently looping sounds.
        public float GainTransitionFactor { get; set; } = 2.5f;
        public float PitchTransitionFactor { get; set; } = 0f;
        public float AirReverbGainTransitionFactor { get; set; } = 0.6f;
        public float HydrophoneReverbGainTransitionFactor { get; set; } = 0.5f;
        public float DynamicMuffleTransitionFactor { get; set; } = 0f; // The max change of high frequency gain over the span of a second. Transitions the muffle effect on and off.
        public float DynamicMuffleFlowFireTransitionFactor { get; set; } = 3.5f;

            // Sound Pathfinding
        public bool TraverseWaterDucts { get; set; } = false; // Should the search algorithm pass through water ducts?
        public bool FlowSoundsTraverseWaterDucts { get; set; } = true;
        public float OpenDoorThreshold { get; set; } = 0.1f; // How open a door/hatch/duct must be for sound to pass through unobstructed.
        public float OpenWallThreshold { get; set; } = 0.35f; // How open a gap in a wall must be for sound to pass through unobstructed.
        public int SoundPropagationRange { get; set; } = 500; // Distance that a sound in WallPropagatingSounds can search for a hull to propagate to.

            // Character AI
        public float AITargetSoundRangeMultiplierMaster { get; set; } = 1.0f;
        public float AITargetSightRangeMultiplierMaster { get; set; } = 1.0f;

        // Sound Rules
        // A general rule is to keep the most vague names at the bottom so when searching for a match the more specific ones can be found first.
        public HashSet<CustomSound> CustomSounds { get; set; } = new HashSet<CustomSound>()
        {
        new CustomSound("footstep",
            gainMultiplier: 0.75f,
            rangeMultiplier: 0.8f,
            muffleInfluence: 1.1f),
        new CustomSound("divingsuitloop",
            gainMultiplier: 0.75f,
            rangeMultiplier: 0.9f,
            muffleInfluence: 1.2f),
        new CustomSound("door",
            gainMultiplier: 0.9f,
            rangeMultiplier: 1.3f,
            exclusions: ["doorbreak"]),
        new CustomSound("metalimpact",
            gainMultiplier: 0.8f,
            rangeMultiplier: 1.2f),


        new CustomSound("revolver",
            gainMultiplier: 2.0f,
            rangeMultiplier: 1.2f,
            sidechainMultiplier: 1.1f,
            release: 1.1f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.95f),
        new CustomSound("shotgun",
            gainMultiplier: 2.5f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 1.4f,
            release: 1.5f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.94f,
            exclusions: ["shotgunload"]),
        new CustomSound("assault",
            gainMultiplier: 2.2f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 1.3f,
            release: 1.2f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.94f),
        new CustomSound("hmgshot",
            gainMultiplier: 2.2f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 1.3f,
            release: 1.2f,
            distortion: true,
            pitchMultiplier: 0.95f,
            muffleInfluence: 0.94f),
        new CustomSound("rifleshot",
            gainMultiplier: 2.5f,
            rangeMultiplier: 1.2f,
            sidechainMultiplier: 1.2f,
            release: 1.3f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.95f,
            exclusions: ["harpooncoilrifleshot"]),
        new CustomSound("handcannon",
            gainMultiplier: 2.5f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 1.4f,
            release: 1.8f,
            distortion: true,
            pitchMultiplier: 1.05f,
            muffleInfluence: 0.95f),
        new CustomSound("rapidfissileaccelerator",
            gainMultiplier: 2.5f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 1.5f,
            release: 1.7f,
            distortion: true,
            pitchMultiplier: 0.90f,
            muffleInfluence: 0.95f,
            exclusions: ["startloop"]),
        new CustomSound("alienturret",
            gainMultiplier: 1.9f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 1.1f,
            release: 1.1f,
            distortion: true,
            pitchMultiplier: 2.5f,
            muffleInfluence: 0.95f),
        new CustomSound("alienweapon",
            gainMultiplier: 1.3f,
            rangeMultiplier: 1.2f,
            sidechainMultiplier: 0.8f,
            release: 0.8f,
            distortion: true,
            pitchMultiplier: 2.2f,
            muffleInfluence: 0.98f),
        new CustomSound("electricaldischarge",
            gainMultiplier: 1.2f,
            rangeMultiplier: 0.9f,
            sidechainMultiplier: 1.0f,
            release: 2.1f,
            distortion: true,
            pitchMultiplier: 0.9f,
            muffleInfluence: 1.0f),
        new CustomSound("scrapcannonshot",
            gainMultiplier: 1f,
            rangeMultiplier: 1.2f,
            sidechainMultiplier: 1f,
            release: 1.1f,
            distortion: true,
            pitchMultiplier: 2.45f,
            muffleInfluence: 1.0f),

        new CustomSound("chaingunshot",
            gainMultiplier: 2.1f,
            rangeMultiplier: 1.35f,
            sidechainMultiplier: 1.1f,
            release: 1.3f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.85f),
        new CustomSound("coilgun",
            gainMultiplier: 2.3f,
            rangeMultiplier: 1.35f,
            sidechainMultiplier: 1.2f,
            release: 1.5f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.85f),
        new CustomSound("flakgun",
            gainMultiplier: 2.5f,
            rangeMultiplier: 1.4f,
            sidechainMultiplier: 1.3f,
            release: 1.7f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.85f),
        new CustomSound("railgun",
            gainMultiplier: 3.0f,
            rangeMultiplier: 1.5f,
            sidechainMultiplier: 1.5f,
            release: 3.2f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.85f,
            exclusions: ["railgunstart", "railgunloop", "railgunstop"]),
        new CustomSound("lasergunshot",
            gainMultiplier: 2.8f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 1.4f,
            release: 3.2f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.85f),

        new CustomSound("gravityshells_boom",
            gainMultiplier: 2.0f,
            rangeMultiplier: 1.5f,
            sidechainMultiplier: 2.0f,
            release: 4.0f,
            distortion: true),
        new CustomSound("sonardecoy.ogg",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.1f,
            sidechainMultiplier: 0.6f,
            release: 1.5f,
            distortion: false),
        new CustomSound("fraggrenade",
            gainMultiplier: 2.0f,
            rangeMultiplier: 1.3f,
            sidechainMultiplier: 2.0f,
            release: 5f,
            distortion: true),
        new CustomSound("incendiumgrenade",
            gainMultiplier: 2.0f,
            rangeMultiplier: 1.2f,
            sidechainMultiplier: 1.4f,
            release: 4.0f,
            distortion: true),
        new CustomSound("stungrenade",
            gainMultiplier: 3.0f,
            rangeMultiplier: 1.2f,
            sidechainMultiplier: 3.0f,
            release: 11.0f,
            distortion: true),
        new CustomSound("explosionlarge", // Reactor explosion.
            gainMultiplier: 3.0f,
            rangeMultiplier: 1.8f,
            sidechainMultiplier: 10.0f,
            release: 10.0f,
            distortion: true,
            pitchMultiplier: 0.77f,
            muffleInfluence: 0.55f),
        new CustomSound("explosion",
            gainMultiplier: 2.0f,
            rangeMultiplier: 1.5f,
            sidechainMultiplier: 3.0f,
            release: 6.0f,
            distortion: true,
            pitchMultiplier: 0.92f,
            muffleInfluence: 0.95f),
        new CustomSound("gravityshells",
            gainMultiplier: 1.5f,
            rangeMultiplier: 1.4f,
            sidechainMultiplier: 0.7f,
            release: 1.5f,
            distortion: true),

        new CustomSound("shot", // Placed last like an else block for sounds in with "shot" in them.
            gainMultiplier: 2.0f,
            rangeMultiplier: 1.15f,
            sidechainMultiplier: 1.0f,
            release: 0.9f,
            distortion: true,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.95f,
            exclusions: ["shotgunload", "tasershot", "riflegrenadeshot"]),


        new CustomSound("firelarge.ogg",
            gainMultiplier: 1.4f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 0.6f,
            release: 1.0f,
            distortion: false),
        

        new CustomSound("damage/structure",
            gainMultiplier: 2.0f,
            rangeMultiplier: 1.5f,
            sidechainMultiplier: 0.5f,
            release: 2.0f,
            distortion: false,
            pitchMultiplier: 0.9f,
            muffleInfluence: 0.55f),
        new CustomSound("doorbreak",
            gainMultiplier: 1.5f,
            rangeMultiplier: 1.2f,
            sidechainMultiplier: 0.4f,
            release: 1.7f,
            distortion: false,
            pitchMultiplier: 0.96f,
            muffleInfluence: 0.98f),
        new CustomSound("items/alarmdivingloop.ogg",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.1f,
            sidechainMultiplier: 0.3f,
            release: 1.0f,
            distortion: false,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.98f),
        new CustomSound("items/alarmbuzzerloop.ogg",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.8f,
            sidechainMultiplier: 0.3f,
            release: 1.0f,
            distortion: false,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.98f),
        new CustomSound("items/warningsiren.ogg",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.8f,
            sidechainMultiplier: 0.3f,
            release: 1.0f,
            distortion: false,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.98f),


        // Pitch adjustments. The ambience sounds really cool stretched out over a low pitch
        new CustomSound("barotrauma/content/sounds/ambient",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 0.0f,
            release: 0.0f,
            distortion: false,
            pitchMultiplier: 0.5f),
        new CustomSound("barotrauma/content/sounds/damage/creak",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 0.0f,
            release: 0.0f,
            distortion: false,
            pitchMultiplier: 0.5f),
        new CustomSound("barotrauma/content/sounds/hull",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 0.0f,
            release: 0.0f,
            distortion: false,
            pitchMultiplier: 0.4f),

        // Soundproof Walls sounds.
        new CustomSound("sounds/spw_",
            gainMultiplier: 1.0f,
            muffleInfluence: 0.0f,
            exclusions: ["bubblesloopmono"]),

        // Real Sonar sounds.
        new CustomSound("2936760984/sounds/sonar",
            gainMultiplier: 1.0f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 3.0f,
            release: 5.0f,
            distortion: false,
            pitchMultiplier: 1.0f,
            muffleInfluence: 0.0f,
            exclusions: ["air", "sonarpoweron", "sonarambience", "sonardistortion"]),
        new CustomSound("2936760984/sounds/cortizide",
            gainMultiplier: 0.6f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 1.0f,
            release: 15.0f,
            distortion: false),
        new CustomSound("2936760984/sounds/tinnitus",
            gainMultiplier: 0.6f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 0.55f,
            release: 3.0f,
            distortion: false),
        
        // Vanilla/general tinnitus sounds - non looping.
        // Place real sonar specific tinnitus before vanilla so it's discovered first.
        new CustomSound("tinnitus",
            gainMultiplier: 0.7f,
            rangeMultiplier: 1.0f,
            sidechainMultiplier: 0.8f,
            release: 10.0f,
            distortion: false),
        };

        // Sounds in this list are ignored by all muffling/pitching/other processing except for gain (which is confusing I know).
        public HashSet<string> IgnoredSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/ui",
            "barotrauma/content/sounds/dropitem",
            "barotrauma/content/sounds/pickitem",
            "barotrauma/content/sounds/water/waterambience"
        };

        // Sounds that don't treat the surface of the water like another wall and can pass in and out freely.
        public HashSet<string> SurfaceIgnoredSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/water/splash",
            "barotrauma/content/items/pump",
            "explosionlarge", // so underwater reactor explosion can be heard by spectators
            "footstep", // Prevents footsteps from being muffled when standing in an inch of water.
            "spw_bubblesloopmono.ogg"
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
            "explosion",
            "sonar",
            "spw_bubblesloopmono.ogg"
        };

        // Propagating sounds pierce through one layer of walls/water.
        public HashSet<string> PropagatingSounds { get; set; } = new HashSet<string>
        {
            "damage/structure",
            "damage/glass",
            "damage/damage_alienruins",
            "doorbreak",
            "electricaldischarge",
        };

        // These sounds pass through walls, doors, and water surfaces unobstructed and ignore occlusion.
        public HashSet<string> BarrierIgnoredSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/ambient",
            "barotrauma/content/sounds/damage/creak",
            "barotrauma/content/sounds/hull",
        };

        public HashSet<string> PitchIgnoredSounds { get; set; } = new HashSet<string>
        {
            "items/alarmdivingloop.ogg",
            "items/alarmbuzzerloop.ogg",
            "items/warningsiren.ogg",
            "items/warningbeep",
            "items/fabricators",
            "items/button",
            "items/door",
            "sounds/spw_", // Don't allow pitching of SPW sounds to take place outside their managing classes.
            "divingsuitloop",
            "divingsuitoxygenleakloop",
            "railgunloop",
            "railgunstart",
            "railgunstop",
            "tinnitus",
            "repairloop",
            "music",
            "sonar",
            "male",
            "female"
        };

        // Sounds that are labelled internally with the dontmuffle tag that should still be processed for muffling regardless.
        public HashSet<string> XMLIgnoredSounds { get; set; } = new HashSet<string>
        {
            "barotrauma/content/sounds/water/flow",
            "barotrauma/content/sounds/fire",
            "barotrauma/content/characters",
            "monster_farlayer",
            "sonardecoy"
        };

        public HashSet<string> LowpassIgnoredSounds { get; set; } = new HashSet<string>
        {
            "tinnitus",
            "sonarambience" // Real Sonar entry.
        };

        public HashSet<string> ReverbForcedSounds { get; set; } = new HashSet<string> // Sounds that are always reverbed.
        {
            "explosion",
        };

        public HashSet<string> AirReverbIgnoredSounds { get; set; } = new HashSet<string> // Sounds that are not reverbed by static or dynamic processing modes.
        {
            "content/items/medical/item_cigarette"
        };

        public HashSet<string> WaterReverbIgnoredSounds { get; set; } = new HashSet<string> // Sounds that are not reverbed by static or dynamic processing modes.
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
    }
}
