using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Sounds;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

        static Random Random = new Random();

        public static void Update()
        {
            bool shouldFadeOut = Listener.EavesdroppedHull == null || !ConfigManager.Config.Enabled;

            UpdateEavesdroppingSounds();

            float duration = MathF.Max(ConfigManager.Config.EavesdroppingTransitionDuration * 60, 0.01f); // Instant transition if duration <= 0.01
            if (!ConfigManager.Config.EavesdroppingTransitionEnabled) { duration = 0.01f; }

            if (shouldFadeOut && EavesdroppingTextAlpha <= 0 && Efficiency <= 0) { return; }
            else if (shouldFadeOut)
            {
                float textChange = 255 / duration;
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha - textChange * 6, 0, 255);
                Efficiency = Math.Clamp(Efficiency - (1 / duration * 3), 0, 1); // More rapid fade out with *3
            }
            else if (!shouldFadeOut)
            {
                PlayEavesdroppingActivationSound();
                float textChange = 255 / duration;
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha + textChange * 6, 0, 255);
                Efficiency = Math.Clamp(Efficiency + 1 / duration, 0, 1);

                ModStateManager.State.TimeSpentEavesdropping += Timing.Step;
            }
        }

        public static void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            Character character = Character.Controlled;
            if (character == null || cam == null) { return; }

            // Eavesdropping vignette.
            int vignetteOpacity = (int)(EavesdroppingTextAlpha * ConfigManager.Config.EavesdroppingVignetteOpacityMultiplier);
            Vignette.Color = new Color(0, 0, 0, vignetteOpacity);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch);
            Vignette.Draw(spriteBatch); // Draw multiple times for extra darkness. Yeah this is pretty stupid.

            DrawEavesdroppingOverlay(spriteBatch, cam);

            // Eavesdropping text.
            Limb limb = Util.GetCharacterHead(character);
            Vector2 position = cam.WorldToScreen(limb.body.DrawPosition + new Vector2(0, 40));
            LocalizedString text = TextManager.Get("spw_listening");
            float size = 1.6f;
            Color color = new Color(224, 214, 164, (int)EavesdropManager.EavesdroppingTextAlpha);
            GUIFont font = GUIStyle.Font;
            font.DrawString(spriteBatch, text, position, color, 0, Vector2.Zero,
                cam.Zoom / size, 0, 0.001f, Alignment.Center);
        }

        private static void DrawEavesdroppingOverlay(SpriteBatch spriteBatch, Camera cam)
        {
            if (ConfigManager.Config.EavesdroppingSpriteOpacity <= 0 || Efficiency <= 0 || ChannelInfoManager.EavesdroppedChannels.Count == 0 || cam == null)
            {
                return;
            }

            spriteBatch.End();

            float effectState = (float)Timing.TotalTime;
            float pulse = 0.8f + MathF.Sin(effectState * 2.0f) * 0.2f; // Smooth pulse from 0.6 to 1.0
            float colorIntensityBase = 0.65f; //Multiplies the overlay color by this amount, the higher the value, the more bright/vibrant the color.
            float colorIntensityVariance = 0.05f; //The variance of the pulse effect affecting the color's brightness/vibrance 
            GameMain.LightManager.SolidColorEffect.Parameters["color"].SetValue(Color.DarkSeaGreen.ToVector4() * (colorIntensityBase + pulse * colorIntensityVariance) * EavesdropManager.Efficiency * ConfigManager.Config.EavesdroppingSpriteOpacity);
            GameMain.LightManager.SolidColorEffect.CurrentTechnique = GameMain.LightManager.SolidColorEffect.Techniques["SolidColorBlur"];
            GameMain.LightManager.SolidColorEffect.Parameters["blurDistance"].SetValue(0.005f + pulse * 0.0025f);
            GameMain.LightManager.SolidColorEffect.CurrentTechnique.Passes[0].Apply();

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: cam.Transform, effect: GameMain.LightManager.SolidColorEffect);

            Sprite glowSprite = GUIStyle.UIThermalGlow.Value.Sprite;

            foreach (ChannelInfo channelInfo in ChannelInfoManager.EavesdroppedChannels)
            {
                if (!channelInfo.Channel.IsPlaying) { continue; }

                float noise1;
                float noise2;
                Vector2 spriteScale;
                Vector2 drawPos;

                // Draw character body.
                Character? c = channelInfo.ChannelCharacter;
                if (c != null && !c.IsDead)
                {
                    foreach (Limb limb in channelInfo.ChannelCharacter!.AnimController.Limbs)
                    {
                        // Show legs for footsteps, and show upper body for voice.
                        if (limb.Mass < 0.5f || (channelInfo.SpeakingClient != null ? limb.IsLowerBody : !limb.IsLeg)) { continue; }
                        noise1 = PerlinNoise.GetPerlin((effectState + limb.Params.ID + c.ID) * 0.01f, (effectState + limb.Params.ID + c.ID) * 0.02f);
                        noise2 = PerlinNoise.GetPerlin((effectState + limb.Params.ID + c.ID) * 0.01f, (effectState + limb.Params.ID + c.ID) * 0.008f);
                        spriteScale = ConvertUnits.ToDisplayUnits(limb.body.GetSize()) / glowSprite.size * (noise1 * 0.5f + 2f);
                        drawPos = new Vector2(limb.body.DrawPosition.X + (noise1 - 0.5f) * 100, -limb.body.DrawPosition.Y + (noise2 - 0.5f) * 100);
                        glowSprite.Draw(spriteBatch, drawPos, 0.0f, scale: Math.Max(spriteScale.X, spriteScale.Y));
                    }
                }

                int maxSpriteRange = ConfigManager.Config.EavesdroppingSpriteMaxSize;
                float spriteSizeMult = ConfigManager.Config.EavesdroppingSpriteSizeMultiplier;
                // If we're seeing a non-eavesdropped sound (reveal all is enabled) make it smaller using its muffle strength. 
                if (!channelInfo.Eavesdropped) { spriteSizeMult *= 1 - channelInfo.MuffleStrength * 0.75f; }
                float soundLifeMult = 1;
                double? duration = channelInfo.Channel?.Sound?.DurationSeconds;
                int? sampleRate = channelInfo.Channel?.Sound?.SampleRate;
                if (duration != null && sampleRate != null && !channelInfo.Channel.Looping)
                {
                    soundLifeMult = 1 - Math.Clamp((float)channelInfo.PlaybackPosition / (float)(duration * sampleRate), 0f, 1f);
                    soundLifeMult = (float)Math.Pow(soundLifeMult, ConfigManager.Config.EavesdroppingSpriteFadeCurve);
                }

                // Create a glow radius around the sound source.
                uint sourceId = channelInfo.Channel.Sound.Owner.GetSourceFromIndex(channelInfo.Channel.Sound.SourcePoolIndex, channelInfo.Channel.ALSourceIndex);
                noise1 = PerlinNoise.GetPerlin((effectState + sourceId) * 0.01f, (effectState + sourceId) * 0.02f);
                noise2 = PerlinNoise.GetPerlin((effectState + sourceId) * 0.01f, (effectState + sourceId) * 0.008f);
                spriteScale = new Vector2(Math.Min(channelInfo.Channel.Far * spriteSizeMult, maxSpriteRange)) / glowSprite.size * (noise1 * 0.5f + 2f);
                spriteScale *= soundLifeMult;
                drawPos = channelInfo.WorldPos;
                Submarine? sub = channelInfo.ChannelHull?.Submarine;
                if (sub != null)
                {
                    drawPos = channelInfo.LocalPos + sub.WorldPosition - sub.HiddenSubPosition;
                }
                drawPos = new Vector2(drawPos.X, -drawPos.Y);
                glowSprite.Draw(spriteBatch, pos: drawPos, rotate: 0.0f, scale: Math.Max(spriteScale.X, spriteScale.Y), color: Color.White);
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
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
                int randomIndex = Random.Next(EavesdroppingActivationSounds.Count);
                Sound eavesdropSound = EavesdroppingActivationSounds[randomIndex];
                SoundChannel channel = eavesdropSound.Play(null, 1, muffle: false); //SoundPlayer.PlaySound(eavesdropSound, Character.Controlled.Position, volume: 10, ignoreMuffling: true);
                if (channel != null && channel.Sound != null)
                {
                    channel.Gain = 1.0f;
                    channel.FrequencyMultiplier = Random.Range(0.8f, 1.2f);
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
