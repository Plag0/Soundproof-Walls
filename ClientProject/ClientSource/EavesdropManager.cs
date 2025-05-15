using Barotrauma.Extensions;
using Barotrauma;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    public static class EavesdropManager
    {
        static float eavesdroppingEfficiency = 0;
        public static float Efficiency { get { return eavesdroppingEfficiency; } set { eavesdroppingEfficiency = Math.Clamp(value, 0, 1); } }
        public static float EavesdroppingTextAlpha = 0;

        public static GUIButton VignetteHolder = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
        public static GUIFrame Vignette = new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, VignetteHolder.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

        static Sound? EavesdroppingAmbienceDryRoomSound;
        static Sound? EavesdroppingAmbienceWetRoomSound;
        static SoundChannel? EavesdroppingAmbienceDryRoomChannel;
        static SoundChannel? EavesdroppingAmbienceWetRoomChannel;
        static List<Sound> EavesdroppingActivationSounds = new List<Sound>();

        public static void Update()
        {
            bool shouldFadeOut = Listener.EavesdroppedHull == null || !ConfigManager.Config.Enabled;

            UpdateEavesdroppingSounds();

            if (shouldFadeOut && EavesdroppingTextAlpha <= 0 && Efficiency <= 0) { return; }

            else if (shouldFadeOut)
            {
                float textChange = 255 / (ConfigManager.Config.EavesdroppingTransitionDuration * 60);
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha - textChange * 6, 0, 255);
                Efficiency = Math.Clamp(Efficiency - (1 / (ConfigManager.Config.EavesdroppingTransitionDuration * 60) * 3), 0, 1);
            }
            else if (!shouldFadeOut)
            {
                PlayEavesdroppingActivationSound();
                float textChange = 255 / (ConfigManager.Config.EavesdroppingTransitionDuration * 60);
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha + textChange * 6, 0, 255);
                Efficiency = Math.Clamp(Efficiency + 1 / (ConfigManager.Config.EavesdroppingTransitionDuration * 60), 0, 1);
            }
        }

        public static void Dispose()
        {
            // Disposing the sounds also kills the channels.
            EavesdroppingAmbienceDryRoomSound?.Dispose();
            EavesdroppingAmbienceWetRoomSound?.Dispose();
            EavesdroppingAmbienceDryRoomSound = null;
            EavesdroppingAmbienceWetRoomSound = null;

            for (int i = 0; i < EavesdroppingActivationSounds.Count(); i++)
            {
                EavesdroppingActivationSounds[i]?.Dispose();
                EavesdroppingActivationSounds[i] = null;
            }
            EavesdroppingActivationSounds.Clear();
        }

        public static void Setup()
        {
            try
            {
                EavesdroppingAmbienceDryRoomSound = GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.EavesdroppingAmbienceDry]);
                EavesdroppingAmbienceWetRoomSound = GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.EavesdroppingAmbienceWet]);
                EavesdroppingActivationSounds.Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.EavesdroppingActivation1]));
                EavesdroppingActivationSounds.Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.EavesdroppingActivation2]));
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Failed to load eavesdropping sounds: {ex.Message}");
            }
        }

        private static void PlayEavesdroppingActivationSound()
        {
            if (Efficiency < 0.01 && EavesdroppingActivationSounds.Count > 0 && EavesdroppingActivationSounds.All(sound => sound != null && !sound.IsPlaying()))
            {
                Random random = new Random();
                int randomIndex = random.Next(EavesdroppingActivationSounds.Count);
                Sound eavesdropSound = EavesdroppingActivationSounds[randomIndex];
                SoundChannel channel = eavesdropSound.Play(null, 1, muffle: false); //SoundPlayer.PlaySound(eavesdropSound, Character.Controlled.Position, volume: 10, ignoreMuffling: true);
                if (channel != null && channel.Sound != null)
                {
                    channel.Gain = 1.0f;
                    channel.FrequencyMultiplier = random.Range(0.8f, 1.2f);
                }
            }
        }

        private static void UpdateEavesdroppingSounds()
        {
            Sound? drySound = EavesdroppingAmbienceDryRoomSound;
            Sound? wetSound = EavesdroppingAmbienceWetRoomSound;
            SoundChannel? dryChannel = EavesdroppingAmbienceDryRoomChannel;
            SoundChannel? wetChannel = EavesdroppingAmbienceWetRoomChannel;

            float maxPitch = 0.75f;

            Hull? eavesdroppedHull = Listener.EavesdroppedHull;
            float waterRatio = eavesdroppedHull?.WaterPercentage / 100 ?? 1;

            bool isPlayingDry = dryChannel != null && dryChannel.Sound != null && dryChannel.IsPlaying;
            bool isPlayingWet = wetChannel != null && wetChannel.Sound != null && wetChannel.IsPlaying;

            bool shouldPlayDry = drySound != null && Efficiency > 0 && waterRatio < 1;
            bool shouldPlayWet = wetSound != null && Efficiency > 0 && waterRatio > 0;

            // Do nothing.
            if (!shouldPlayDry && !isPlayingDry && !shouldPlayWet && !isPlayingWet) { return; }

            // Start playing.
            if (shouldPlayDry && !isPlayingDry)
            {
                dryChannel = SoundPlayer.PlaySound(drySound, new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), volume: 0.01f, freqMult: MathHelper.Lerp(0.25f, maxPitch, Efficiency), ignoreMuffling: true);
                if (dryChannel != null && dryChannel.Sound != null) { dryChannel.Looping = true; }
            }
            if (shouldPlayWet && !isPlayingWet)
            {
                wetChannel = SoundPlayer.PlaySound(wetSound, new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), volume: 0.01f, freqMult: MathHelper.Lerp(0.25f, maxPitch, Efficiency), ignoreMuffling: true);
                if (wetChannel != null && wetChannel.Sound != null) { wetChannel.Looping = true; }
            }

            // Update playing.
            if (shouldPlayDry && isPlayingDry)
            {
                dryChannel!.FrequencyMultiplier = MathHelper.Lerp(0.25f, maxPitch, Efficiency);
                dryChannel.Gain = MathHelper.Lerp(0.1f, 1 - waterRatio, Math.Clamp(Efficiency * 3, 0, 1));
            }
            if (shouldPlayWet && isPlayingWet)
            {
                wetChannel!.FrequencyMultiplier = MathHelper.Lerp(0.25f, maxPitch, Efficiency);
                wetChannel.Gain = MathHelper.Lerp(0.1f, waterRatio, Math.Clamp(Efficiency * 3, 0, 1));
            }

            // Stop playing.
            if (!shouldPlayDry && isPlayingDry)
            {
                dryChannel!.Looping = false;
                dryChannel.FrequencyMultiplier = 1;
                dryChannel.Gain = 0;
                dryChannel.Dispose();
                dryChannel = null;
            }
            if (!shouldPlayWet && isPlayingWet)
            {
                wetChannel!.Looping = false;
                wetChannel.FrequencyMultiplier = 1;
                wetChannel.Gain = 0;
                wetChannel.Dispose();
                wetChannel = null;
            }

            EavesdroppingAmbienceDryRoomChannel = dryChannel;
            EavesdroppingAmbienceWetRoomChannel = wetChannel;
        }
    }
}
