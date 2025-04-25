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

        static Sound? EavesdroppingAmbienceDryRoomSound;
        static Sound? EavesdroppingAmbienceWetRoomSound;
        static SoundChannel? EavesdroppingAmbienceSoundChannel;
        static List<Sound> EavesdroppingActivationSounds = new List<Sound>();

        public static void Update()
        {
            bool shouldFadeOut = Listener.EavesdroppedHull == null || !ConfigManager.Config.Enabled;

            UpdateEavesdroppingSounds();

            if (shouldFadeOut && EavesdroppingTextAlpha <= 0 && Efficiency <= 0) { return; }

            else if (shouldFadeOut)
            {
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha - 15, 0, 255);
                Efficiency = Math.Clamp(Efficiency - 0.04f, 0, 1);
            }
            else if (!shouldFadeOut)
            {
                PlayEavesdroppingActivationSound();
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha + 15, 0, 255);
                Efficiency = Math.Clamp(Efficiency + 1 / (ConfigManager.Config.EavesdroppingFadeDuration * 60), 0, 1);
            }
        }

        public static void Dispose()
        {
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
            string? modPath = Util.GetModDirectory();

            try
            {
                EavesdroppingAmbienceDryRoomSound = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingAmbienceDryRoom.ogg"));
                EavesdroppingAmbienceWetRoomSound = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingAmbienceWetRoom.ogg"));
                EavesdroppingActivationSounds.Add(GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingActivation1.ogg")));
                EavesdroppingActivationSounds.Add(GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingActivation2.ogg")));
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Failed to load eavesdropping sounds: {ex.Message}");
            }
        }

        private static void PlayEavesdroppingActivationSound()
        {
            if (Efficiency < 0.01 && EavesdroppingActivationSounds.All(sound => sound != null && !sound.IsPlaying()))
            {
                Random random = new Random();
                int randomIndex = random.Next(EavesdroppingActivationSounds.Count);
                Sound eavesdropSound = EavesdroppingActivationSounds[randomIndex];
                SoundChannel channel = eavesdropSound.Play(null, 1, muffle: false); //SoundPlayer.PlaySound(eavesdropSound, Character.Controlled.Position, volume: 10, ignoreMuffling: true);
                if (channel != null)
                {
                    channel.Gain = 1;
                    channel.FrequencyMultiplier = random.Range(0.8f, 1.2f);
                }
            }
        }

        private static void UpdateEavesdroppingSounds()
        {
            Sound? drySound = EavesdroppingAmbienceDryRoomSound;
            Sound? wetSound = EavesdroppingAmbienceWetRoomSound;
            SoundChannel? channel = EavesdroppingAmbienceSoundChannel;

            Hull? eavesdroppedHull = Listener.EavesdroppedHull;
            bool isPlaying = channel != null && channel.IsPlaying;
            bool isDry = eavesdroppedHull?.WaterPercentage < 50;
            Sound? correctSound = isDry ? drySound : wetSound;
            bool matchesEnvironment = isPlaying ? channel!.Sound == correctSound : true;

            bool shouldPlay = matchesEnvironment && Efficiency > 0;

            if (correctSound == null || !shouldPlay && !isPlaying)
            {
                return;
            }
            if (shouldPlay && !isPlaying)
            {
                channel = SoundPlayer.PlaySound(correctSound, new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), volume: 0.1f, freqMult: MathHelper.Lerp(0.25f, 0.75f, Efficiency), ignoreMuffling: true);
                if (channel != null && channel.Sound != null) { channel.Looping = true; }
            }
            else if (shouldPlay && isPlaying)
            {
                channel.FrequencyMultiplier = MathHelper.Lerp(0.25f, 0.75f, Math.Clamp(Efficiency * 1, 0, 1));
                channel.Gain = MathHelper.Lerp(0.1f, 1, Math.Clamp(Efficiency * 3, 0, 1));
            }
            else if (!shouldPlay && isPlaying)
            {
                channel.Looping = false;
                channel.FrequencyMultiplier = 1;
                channel.Gain = 0;
                channel.Dispose();
                channel = null;
            }

            EavesdroppingAmbienceSoundChannel = channel;
        }
    }
}
