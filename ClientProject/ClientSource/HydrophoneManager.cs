using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenAL;
using System.Data;

namespace SoundproofWalls
{
    public static class HydrophoneManager
    {
        private const int NumSectors = 12;
        private const float SectorAngle = 360.0f / NumSectors;
        private const string SectorSpriteAtlasPath = "Content/UI/HydrophoneSectorAtlas.png";

        public static readonly HydrophoneSector[] Sectors = new HydrophoneSector[NumSectors];
        public static readonly Dictionary<Sonar, HydrophoneSwitch> HydrophoneSwitches = new Dictionary<Sonar, HydrophoneSwitch>();
        private static readonly Dictionary<Size, List<Sound>> HydrophoneSounds = new Dictionary<Size, List<Sound>>()
        {
            { Size.Small, new List<Sound>() },
            { Size.Medium, new List<Sound>() },
            { Size.Large, new List<Sound>() },
        };

        private static readonly Dictionary<int, Sprite> DegreesToSectorSpritesMap = new Dictionary<int, Sprite>()
        {
            { 90, new Sprite(Path.Combine(Plugin.ModPath, SectorSpriteAtlasPath), sourceRectangle: new Rectangle(0, 0, 512, 512), origin: (0, 1)) },
            { 60, new Sprite(Path.Combine(Plugin.ModPath, SectorSpriteAtlasPath), sourceRectangle: new Rectangle(512, 0, 512, 512), origin: (0, 1)) },
            { 30, new Sprite(Path.Combine(Plugin.ModPath, SectorSpriteAtlasPath), sourceRectangle: new Rectangle(0, 512, 256, 512), origin: (0, 1)) },
            { 10, new Sprite(Path.Combine(Plugin.ModPath, SectorSpriteAtlasPath), sourceRectangle: new Rectangle(256, 512, 256, 512), origin: (0, 1)) },
            { 5, new Sprite(Path.Combine(Plugin.ModPath, SectorSpriteAtlasPath), sourceRectangle: new Rectangle(512, 512, 256, 512), origin: (0, 1)) },
            { 1, new Sprite(Path.Combine(Plugin.ModPath, SectorSpriteAtlasPath), sourceRectangle: new Rectangle(768, 512, 256, 512), origin: (0, 1)) }
        };

        private static Sound? AmbienceSound;
        private static SoundChannel? AmbienceChannel;

        private static Random Random = new Random();

        private static double lastHydrophonePlayTime = 0.0f;
        private static double hydrophoneUpdateInterval = 0.2f;
        private static float hydrophoneEfficiency = 1;
        public static float HydrophoneEfficiency { get { return hydrophoneEfficiency; } set { hydrophoneEfficiency = Math.Clamp(value, 0, 1); } }

        public class HydrophoneSector
        {
            public SoundChannel ActiveSoundChannel { get; set; }
            public Size Size { get; set; }
            public readonly List<Character> CharactersInSector = new List<Character>();

            public void ClearCharacters()
            {
                CharactersInSector.Clear();
            }
        }

        public enum Size
        {
            Small,
            Medium,
            Large,
        }

        public class HydrophoneSwitch
        {
            public static Color textDefaultColor = new Color(228, 217, 167);
            public static Color textDisabledColor = new Color(114, 108, 83);
            public static Color buttonDefaultColor = new Color(255, 255, 255);
            public static Color buttonDisabledColor = new Color(127, 127, 127);

            public bool State { get; set; }
            public GUIButton? Switch { get; set; }
            public GUITextBlock? TextBlock { get; set; }

            public HydrophoneSwitch(bool state, GUIButton? switchButton, GUITextBlock? textBlock)
            {
                State = state;
                Switch = switchButton;
                TextBlock = textBlock;
            }
        }

        public static void Setup()
        {
            for (int i = 0; i < NumSectors; i++)
            {
                Sectors[i] = new HydrophoneSector();
            }
            LoadSounds();
            SetupHydrophoneSwitches();
        }

        private static void LoadSounds()
        {
            try
            {
                string targetSoundPath = Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneAmbienceGreatSea];
                string currentBiomeId = GameMain.GameSession?.LevelData?.Biome.Identifier.ToString() ?? string.Empty;
                switch (currentBiomeId)
                {
                    case "coldcaverns":
                        targetSoundPath = Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneAmbienceColdCaverns];
                        break;

                    case "europanridge":
                        targetSoundPath = Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneAmbienceEuropanRidge];
                        break;

                    case "theaphoticplateau":
                        targetSoundPath = Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneAmbienceAphoticPlateau];
                        break;

                    case "thegreatsea":
                        targetSoundPath = Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneAmbienceGreatSea];
                        break;

                    case "hydrothermalwastes":
                        targetSoundPath = Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneAmbienceHydrothermalWastes];
                        break;
                }
                AmbienceSound = GameMain.SoundManager.LoadSound(targetSoundPath);
                HydrophoneSounds[Size.Small].Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneMovementSmall1]));
                HydrophoneSounds[Size.Small].Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneMovementSmall2]));
                // Add more here.
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Failed to load hydrophone sounds: {ex.Message}");
            }
        }

        public static void Update()
        {
            UpdateHydrophoneSwitches();

            // If hydrophones are off, stop all sounds and do nothing.
            if (!ConfigManager.Config.Enabled || !Listener.IsUsingHydrophones)
            {
                StopAllSectorSounds();
                StopAmbienceSound();
                return;
            }

            // Only update sectors every interval.
            if (ConfigManager.Config.HydrophoneMovementSounds && 
                Timing.TotalTime > lastHydrophonePlayTime + hydrophoneUpdateInterval)
            {
                lastHydrophonePlayTime = (float)Timing.TotalTime;
                ClearAllSectors();
                AssignCharactersToSectors();
            }

            UpdateAmbience();
            UpdateAllSectorSounds(); // Update sounds every tick.

            ModStateManager.State.TimeSpentHydrophones += Timing.Step;
        }

        public static void Dispose()
        {
            StopAllSectorSounds();

            foreach (List<Sound> soundList in HydrophoneSounds.Values)
            {
                for (int i = 0; i < soundList.Count; i++)
                {
                    soundList[i]?.Dispose();
                    soundList[i] = null;
                }
                soundList.Clear();
            }

            DisposeAllHydrophoneSwitches();
        }

        private static void ClearAllSectors()
        {
            foreach (var sector in Sectors)
            {
                sector.ClearCharacters();
            }
        }

        public static void DrawHydrophoneSprites(Sonar __instance, SpriteBatch spriteBatch, Rectangle rect)
        {
            Submarine sub = Listener.CurrentHull?.Submarine;

            if (sub == null || 
                !ConfigManager.Config.Enabled || 
                !ConfigManager.Config.HydrophoneVisualFeedbackEnabled || 
                !HydrophoneSwitches.TryGetValue(__instance, out var hydrophoneSwitch) || 
                !hydrophoneSwitch.State)
            {
                return;
            }

            Vector2 submarineWorldPos = sub.WorldPosition;
            Vector2 displayCenter = rect.Center.ToVector2();
            float submarineRadius = sub.Borders.Height / 2;

            spriteBatch.End();
            //GameMain.LightManager.SolidColorEffect.Parameters["color"].SetValue(Color.DarkRed.ToVector4() * HydrophoneEfficiency);
            //GameMain.LightManager.SolidColorEffect.CurrentTechnique = GameMain.LightManager.SolidColorEffect.Techniques["SolidColorBlur"];
            //GameMain.LightManager.SolidColorEffect.Parameters["blurDistance"].SetValue(0.005f);
            //GameMain.LightManager.SolidColorEffect.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

            foreach (ChannelInfo info in ChannelInfoManager.HydrophonedChannels)
            {
                Vector2 soundWorldPos = info.WorldPos;

                // The angle to the sound source from the submarine in radians.
                float angleToCenter = -MathUtils.VectorToAngle(soundWorldPos - submarineWorldPos) + MathHelper.ToRadians(90.0f);

                // The radius of the sound source. Subtract HydrophoneSoundRange because it's added to all sounds played while using hydrophones.
                float soundRange = info.Channel.Far - ConfigManager.Config.HydrophoneSoundRange;
                if (soundRange <= 0) { soundRange = 400; } // Arbitrary base range value if the sound is missing a range.
                float soundRadius = (soundRange / 2.0f) * ConfigManager.Config.HydrophoneVisualFeedbackSizeMultiplier;
                float soundDistance = Vector2.Distance(soundWorldPos, submarineWorldPos);

                // Get color and opacity based on distance and amplitude // 1 - soundDistance / soundRange
                int opacity = (int)(MathHelper.Lerp(0, 255, info.Channel.CurrentAmplitude) * ConfigManager.Config.HydrophoneVisualFeedbackOpacityMultiplier);
                Color color = new Color(Color.Lerp(Color.Yellow, Color.Red, info.Channel.Gain), opacity);

                int sectorWidth = 0;
                // Case 1: Distant Sound (Circles are NOT overlapping).
                // Calculates the tangents from the sub's center to the sound's circle.
                if (soundDistance >= soundRadius + submarineRadius)
                {
                    // Ensure we don't get a math error if the sound is right on the edge
                    if (soundDistance > soundRadius)
                    {
                        double sectorWidthDegrees = 2 * Math.Asin(soundRadius / soundDistance) * (180.0 / Math.PI);
                        sectorWidth = (int)Math.Round(sectorWidthDegrees);
                    }
                    else
                    {
                        // If soundRadius is somehow larger, it would be an intersection, handled below.
                        // This just prevents a crash.
                        sectorWidth = 0;
                    }
                    //LuaCsLogger.Log($"{info.ShortName} Case 1 - soundDistance {soundDistance} soundRadius {soundRadius} submarineRadius {submarineRadius} sectorWidth {sectorWidth}");
                }
                // Case 2: Engulfed Sound (submarine is completely inside the sound).
                else if (soundDistance <= soundRadius - submarineRadius)
                {
                    sectorWidth = 360;
                    //LuaCsLogger.Log($"{info.ShortName} Case 2 - soundDistance {soundDistance} soundRadius {soundRadius} submarineRadius {submarineRadius} sectorWidth {sectorWidth}");
                }
                // Case 3: Contained Sound (sound is completely inside the submarine's radius).
                // Prevents the invalid Law of Cosines calculation.
                else if (soundDistance + soundRadius < submarineRadius)
                {
                    sectorWidth = 0;
                    //LuaCsLogger.Log($"{info.ShortName} Case 3 - soundDistance {soundDistance} soundRadius {soundRadius} submarineRadius {submarineRadius} sectorWidth {sectorWidth}");
                }
                // Case 4: Intersecting Sound.
                // Sounds can "wrap" around the submarine.
                else
                {
                    double cosAngle = (submarineRadius * submarineRadius + soundDistance * soundDistance - soundRadius * soundRadius) / (2 * submarineRadius * soundDistance);
                    double halfAngleRadians = Math.Acos(MathHelper.Clamp((float)cosAngle, -1.0f, 1.0f));
                    double sectorWidthDegrees = 2 * halfAngleRadians * (180.0 / Math.PI);
                    sectorWidth = (int)Math.Round(sectorWidthDegrees);
                    //LuaCsLogger.Log($"{info.ShortName} Case 4 - cosAngle {cosAngle} halfAngleRadians {halfAngleRadians} sectorWidthDegrees {sectorWidthDegrees} soundDistance {soundDistance} soundRadius {soundRadius} submarineRadius {submarineRadius} sectorWidth {sectorWidth}");
                }

                if (sectorWidth <= 0) continue;

                // Calculate the starting angle to center the sector
                float sectorWidthRadians = MathHelper.ToRadians(sectorWidth);
                float currentAngle = angleToCenter - (sectorWidthRadians / 2.0f);

                // Greedily select sprites
                int remainingWidth = sectorWidth;
                foreach (var kvp in DegreesToSectorSpritesMap)
                {
                    int denom = kvp.Key;
                    Sprite sprite = kvp.Value;
                    while (remainingWidth >= denom)
                    {
                        sprite.Draw(
                            spriteBatch,
                            pos: displayCenter,
                            color: color,
                            origin: sprite.Origin,
                            rotate: currentAngle,
                            scale: 1,
                            spriteEffect: SpriteEffects.None
                        );

                        // Advance the angle for the next sprite
                        currentAngle += MathHelper.ToRadians(denom);
                        remainingWidth -= denom;
                    }
                }
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
        }

        public static bool DrawSonarBlips()
        {
            return !ConfigManager.Config.HydrophoneUsageDisablesSonarBlips || !ConfigManager.Config.HydrophoneVisualFeedbackEnabled || !Listener.IsUsingHydrophones;
        }

        public static bool DrawDockingPorts()
        {
            return !ConfigManager.Config.HydrophoneUsageDisablesSubOutline || !ConfigManager.Config.HydrophoneVisualFeedbackEnabled || !Listener.IsUsingHydrophones;
        }

        public static bool DrawSubBorders()
        {
            return !ConfigManager.Config.HydrophoneUsageDisablesSubOutline || !ConfigManager.Config.HydrophoneVisualFeedbackEnabled || !Listener.IsUsingHydrophones;
        }

        private static void AssignCharactersToSectors()
        {
            float range = ConfigManager.Config.HydrophoneSoundRange;
            float rangeSq = range * range;

            Vector2 listenerPos = Listener.CurrentHull?.Submarine?.WorldPosition ?? new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);

            for (int i = 0; i < Character.CharacterList.Count; i++)
            {
                Character character = Character.CharacterList[i];
                if (character == null || !character.InWater || character.isDead)
                {
                    continue;
                }

                if (Vector2.DistanceSquared(listenerPos, character.WorldPosition) > rangeSq)
                {
                    continue;
                }

                // Calculate angle from listener to character
                Vector2 delta = character.WorldPosition - listenerPos;
                // Atan2 returns radians, convert to degrees and normalize to 0-360 range
                float angle = MathHelper.ToDegrees((float)Math.Atan2(delta.Y, delta.X));
                if (angle < 0) { angle += 360.0f; }

                // Determine the sector index
                int sectorIndex = (int)(angle / SectorAngle);
                sectorIndex = Math.Clamp(sectorIndex, 0, NumSectors - 1); // Clamp for safety

                Sectors[sectorIndex].CharactersInSector.Add(character);
            }
        }

        private static void UpdateAmbience()
        {
            if (AmbienceSound == null) { return; }

            float minAmbiencePitch = 0.25f; float maxAmbiencePitch = 1.0f;

            if (AmbienceChannel == null || !AmbienceChannel.IsPlaying)
            {
                AmbienceChannel = SoundPlayer.PlaySound(AmbienceSound, new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), volume: 0.01f, freqMult: MathHelper.Lerp(minAmbiencePitch, maxAmbiencePitch, HydrophoneEfficiency), ignoreMuffling: true);
                if (AmbienceChannel != null && AmbienceChannel.Sound != null && AmbienceChannel.Sound.DurationSeconds != null) 
                { 
                    AmbienceChannel.Looping = true;

                    // Start playing from random playback position.
                    int sampleRate = 44100;
                    uint sourceId = AmbienceChannel.Sound.Owner.GetSourceFromIndex(AmbienceChannel.Sound.SourcePoolIndex, AmbienceChannel.ALSourceIndex);
                    int totalSamples = (int)(AmbienceChannel.Sound.DurationSeconds * sampleRate) - sampleRate; // Subtract 1 second for safety
                    int randomOffset = Random.Next(0, Math.Max(totalSamples, 0));
                    
                    int alError;
                    Al.GetError();
                    Al.Sourcei(sourceId, Al.SampleOffset, randomOffset);
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to start hydrophone ambience ID {sourceId} at offset {randomOffset}, {Al.GetErrorString(alError)}");
                }
            }
            else
            {
                float freqMult = 1;
                freqMult += MathHelper.Lerp(minAmbiencePitch, maxAmbiencePitch, HydrophoneEfficiency) - 1;
                Submarine? sub = Character.Controlled?.Submarine;
                if (sub != null) 
                {
                    float movementFactor = (sub.Velocity == Vector2.Zero) ? 0.0f : sub.Velocity.Length() / 10.0f;
                    movementFactor = MathHelper.Clamp(movementFactor, 0.0f, 1.0f);
                    float minVelocityPitch = 1.0f; float maxVelocityPitch = 2.0f;
                    freqMult += MathHelper.Lerp(minVelocityPitch, maxVelocityPitch, movementFactor) - 1;
                }
                AmbienceChannel.Gain = 1 * HydrophoneEfficiency * ConfigManager.Config.HydrophoneAmbienceVolumeMultiplier;
                AmbienceChannel.FrequencyMultiplier = 1 * freqMult;
                AmbienceChannel.Gain = MathHelper.Lerp(0.1f, 1, Math.Clamp(HydrophoneEfficiency * 3, 0, 1));
            }
        }

        private static void UpdateAllSectorSounds()
        {
            foreach (var sector in Sectors)
            {
                if (sector.CharactersInSector.Count > 0 && ConfigManager.Config.HydrophoneMovementSounds)
                {
                    // This sector has creatures, so we need to play or update its sound.
                    UpdateActiveSector(sector);
                }
                else
                {
                    // This sector is empty, so stop any sound it might be playing.
                    StopSectorSound(sector);
                }
            }
        }

        private static void UpdateActiveSector(HydrophoneSector sector)
        {
            // Position.
            var xPositions = sector.CharactersInSector.Select(c => c.WorldPosition.X).ToList();
            var yPositions = sector.CharactersInSector.Select(c => c.WorldPosition.Y).ToList();
            Vector2 medianPosition = new Vector2(GetMedian(xPositions), GetMedian(yPositions));

            // Gain. Base range + hydrophone range.
            float range = ConfigManager.Config.HydrophoneSoundRange;
            float gain = ChannelInfo.CalculateLinearDistanceClampedMult(
                distance: Vector2.Distance(Listener.WorldPos, medianPosition), 
                near: range * ConfigManager.Config.LoopingComponentSoundNearMultiplier, 
                far: range) * 
                HydrophoneEfficiency * 
                ConfigManager.Config.HydrophoneMovementVolumeMultiplier;

            // Pitch. // TODO make pitch based on speed, mass, and charactersInSector.
            var speeds = sector.CharactersInSector.Select(c => c.CurrentSpeed).ToList();
            float medianSpeed = GetMedian(speeds);
            float minSpeed = 0f;
            float maxSpeed = 12f;
            float clampedSpeed = Math.Clamp(medianSpeed, minSpeed, maxSpeed);
            float targetPitch = MathHelper.Lerp(0.25f, 4f, clampedSpeed / maxSpeed);

            // Movement type.
            Size currentSize = sector.Size;
            Size targetSize = Size.Large;
            if (sector.CharactersInSector.Count < 2) { targetSize = Size.Small; }
            else if (sector.CharactersInSector.Count < 3) { targetSize = Size.Medium; }

            if (sector.ActiveSoundChannel != null && (!sector.ActiveSoundChannel.IsPlaying || currentSize != targetSize))
            {
                // If channel exists but stopped for some reason, null it out so we can create a new one.
                StopSectorSound(sector);
            }
            else if (sector.ActiveSoundChannel == null)
            {
                if (HydrophoneSounds.TryGetValue(targetSize, out var soundList) && soundList.Count > 0)
                {
                    // No active sound, so let's start one.
                    int randomIndex = Random.Next(soundList.Count);
                    Sound soundToPlay = soundList[randomIndex];

                    sector.ActiveSoundChannel = soundToPlay?.Play(gain, range, targetPitch, medianPosition, muffle: true); // Muffle must be true so the sound gets a Hydrophoned flag
                    if (sector.ActiveSoundChannel != null)
                    {
                        sector.ActiveSoundChannel.Looping = true;
                        sector.Size = targetSize;
                    }
                }
            }
            else
            {
                // Sound is already playing, just update its properties.
                sector.ActiveSoundChannel.Gain = gain;
                sector.ActiveSoundChannel.Position = new Vector3(medianPosition, 0f);

                // Smooth pitch changes
                float transitionFactor = 1;
                float currentPitch = sector.ActiveSoundChannel.FrequencyMultiplier;
                float maxStep = (float)(transitionFactor * Timing.Step);
                sector.ActiveSoundChannel.FrequencyMultiplier = Util.SmoothStep(currentPitch, targetPitch, maxStep);
            }
        }

        private static void StopAmbienceSound()
        {
            if (AmbienceChannel != null)
            {
                ChannelInfoManager.RemovePitchedChannel(AmbienceChannel);

                AmbienceChannel.Looping = false;
                AmbienceChannel.Gain = 0f;
                AmbienceChannel.Dispose();
                AmbienceChannel = null;
            }
        }

        private static void StopSectorSound(HydrophoneSector sector)
        {
            if (sector.ActiveSoundChannel != null)
            {
                ChannelInfoManager.RemovePitchedChannel(sector.ActiveSoundChannel); // TODO do I need this?

                sector.ActiveSoundChannel.Looping = false;
                sector.ActiveSoundChannel.Gain = 0;
                sector.ActiveSoundChannel.Dispose();
                sector.ActiveSoundChannel = null;
            }
        }

        private static void StopAllSectorSounds()
        {
            foreach (var sector in Sectors)
            {
                StopSectorSound(sector);
            }
        }

        private static float GetMedian(List<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            values.Sort();

            int midIndex = values.Count / 2;

            if (values.Count % 2 == 0)
            {
                // Even number of elements, return the average of the two middle ones
                return (values[midIndex - 1] + values[midIndex]) / 2.0f;
            }
            else
            {
                // Odd number of elements, return the middle one
                return values[midIndex];
            }
        }

        public static void DisposeAllHydrophoneSwitches()
        {
            foreach (var kvp in HydrophoneSwitches)
            {
                HydrophoneSwitch hydrophoneSwitch = kvp.Value;

                if (hydrophoneSwitch == null || hydrophoneSwitch.Switch == null || hydrophoneSwitch.TextBlock == null) continue;

                hydrophoneSwitch.Switch.RemoveFromGUIUpdateList();
                hydrophoneSwitch.Switch.Visible = false;
                hydrophoneSwitch.TextBlock.RemoveFromGUIUpdateList();
                hydrophoneSwitch.TextBlock.Visible = false;

            }
            HydrophoneSwitches.Clear();
        }

        public static void SetupHydrophoneSwitches(bool firstStartup = false)
        {
            DisposeAllHydrophoneSwitches();

            if (!ConfigManager.Config.Enabled || !ConfigManager.Config.HydrophoneSwitchEnabled || !Util.RoundStarted) { return; }

            foreach (Item item in Item.RepairableItems)
            {
                if (item.Tags.Contains("command") && !item.Tags.Contains("spw_ignorehydrophones"))
                {
                    Sonar? sonar = item.GetComponent<Sonar>();
                    if (sonar == null) { continue; }

                    if (firstStartup)
                    {
                        if (sonar.HasMineralScanner)
                        {
                            MakeRoomForHydrophoneSwitchMineralScanner(sonar);
                        }
                        else
                        {
                            MakeRoomForHydrophoneSwitchDefault(sonar);
                        }
                    }

                    AddHydrophoneSwitchToGUI(sonar);
                }
            }
        }

        public static void UpdateHydrophoneSwitches()
        {
            // Don't update if spectating.
            if (Character.Controlled == null || LightManager.ViewTarget == null) return;

            if (ConfigManager.Config.HydrophoneLegacySwitch)
            {
                UpdateHydrophoneSwitchesLegacy();
            }
            else
            {
                UpdateHydrophoneSwitchesNew();
            }
        }

        // Deprecated. No one should use this.
        private static void UpdateHydrophoneSwitchesLegacy()
        {
            foreach (var kvp in HydrophoneSwitches)
            {
                if (kvp.Value.Switch == null || kvp.Value.TextBlock == null) { return; }
                Sonar instance = kvp.Key;
                GUIButton button = kvp.Value.Switch;
                GUITextBlock textBlock = kvp.Value.TextBlock;
                bool updated = false;
                if (button.Enabled && instance.CurrentMode == Sonar.Mode.Active)
                {
                    button.Enabled = false;
                    button.Selected = false;
                    textBlock.Enabled = false;
                    kvp.Value.State = false;
                    updated = true;

                }
                else if (!button.Enabled && instance.CurrentMode != Sonar.Mode.Active)
                {
                    button.Enabled = true;
                    textBlock.Enabled = true;
                    updated = true;
                }

                if (updated && GameMain.Client != null)
                {
                    instance.unsentChanges = true;
                    instance.correctionTimer = Sonar.CorrectionDelay;
                }
            }
        }

        public static void UpdateHydrophoneSwitchesNew()
        {
            Sonar? instance = null;
            GUIButton? button = null;
            GUITextBlock? textBlock = null;

            foreach (var kvp in HydrophoneSwitches)
            {
                if (Character.Controlled?.SelectedItem?.GetComponent<Sonar>() is Sonar sonar && sonar == kvp.Key)
                {
                    instance = sonar;
                    button = kvp.Value.Switch;
                    textBlock = kvp.Value.TextBlock;
                    break;
                }
            }

            if (instance == null || button == null || textBlock == null)
            {
                // If not using any terminals start increasing hydrophone efficiency.
                if (HydrophoneEfficiency < 1)
                {
                    HydrophoneEfficiency += 0.005f * (HydrophoneEfficiency + 1);
                }
                return;
            }


            if (instance.CurrentMode == Sonar.Mode.Active)
            {
                HydrophoneEfficiency = 0;
                if (button.Selected && button.Color != HydrophoneSwitch.buttonDisabledColor)
                {
                    textBlock.TextColor = InterpolateColor(HydrophoneSwitch.textDisabledColor, HydrophoneSwitch.textDefaultColor, HydrophoneEfficiency);
                    button.SelectedColor = InterpolateColor(HydrophoneSwitch.buttonDisabledColor, HydrophoneSwitch.buttonDefaultColor, HydrophoneEfficiency);
                    button.Color = InterpolateColor(HydrophoneSwitch.buttonDisabledColor, HydrophoneSwitch.buttonDefaultColor, HydrophoneEfficiency);
                    button.HoverColor = InterpolateColor(HydrophoneSwitch.buttonDisabledColor, HydrophoneSwitch.buttonDefaultColor, HydrophoneEfficiency);
                }
                else if (!button.Selected && button.Color == HydrophoneSwitch.buttonDisabledColor)
                {
                    textBlock.TextColor = HydrophoneSwitch.textDefaultColor;
                    button.SelectedColor = HydrophoneSwitch.buttonDefaultColor;
                    button.Color = HydrophoneSwitch.buttonDefaultColor;
                    button.HoverColor = HydrophoneSwitch.buttonDefaultColor;
                }
            }

            else if (instance.CurrentMode != Sonar.Mode.Active && HydrophoneEfficiency < 1)
            {
                HydrophoneEfficiency += 0.005f * (HydrophoneEfficiency + 1);

                if (button.Selected)
                {
                    textBlock.TextColor = InterpolateColor(HydrophoneSwitch.textDisabledColor, HydrophoneSwitch.textDefaultColor, HydrophoneEfficiency);
                    button.SelectedColor = InterpolateColor(HydrophoneSwitch.buttonDisabledColor, HydrophoneSwitch.buttonDefaultColor, HydrophoneEfficiency);
                    button.Color = InterpolateColor(HydrophoneSwitch.buttonDisabledColor, HydrophoneSwitch.buttonDefaultColor, HydrophoneEfficiency);
                    button.HoverColor = InterpolateColor(HydrophoneSwitch.buttonDisabledColor, HydrophoneSwitch.buttonDefaultColor, HydrophoneEfficiency);

                }
                else if (!button.Selected && button.Color != HydrophoneSwitch.buttonDefaultColor)
                {
                    textBlock.TextColor = HydrophoneSwitch.textDefaultColor;
                    button.SelectedColor = HydrophoneSwitch.buttonDefaultColor;
                    button.Color = HydrophoneSwitch.buttonDefaultColor;
                    button.HoverColor = HydrophoneSwitch.buttonDefaultColor;
                }
            }

            // If hydrophone efficiency increases back to 1 while not looking at a terminal.
            else if (button.Color != HydrophoneSwitch.buttonDefaultColor && instance.CurrentMode != Sonar.Mode.Active && HydrophoneEfficiency >= 1)
            {
                textBlock.TextColor = HydrophoneSwitch.textDefaultColor;
                button.SelectedColor = HydrophoneSwitch.buttonDefaultColor;
                button.Color = HydrophoneSwitch.buttonDefaultColor;
                button.HoverColor = HydrophoneSwitch.buttonDefaultColor;
            }
        }

        public static Color InterpolateColor(Color startColor, Color endColor, float mult)
        {
            int r = (int)(startColor.R + (endColor.R - startColor.R) * mult);
            int g = (int)(startColor.G + (endColor.G - startColor.G) * mult);
            int b = (int)(startColor.B + (endColor.B - startColor.B) * mult);
            int a = (int)(startColor.A + (endColor.A - startColor.A) * mult);
            return new Color(r, g, b, a);
        }

        public static void MakeRoomForHydrophoneSwitchDefault(Sonar instance)
        {
            instance.controlContainer.RectTransform.RelativeSize = new Vector2(
                instance.controlContainer.RectTransform.RelativeSize.X,
                instance.controlContainer.RectTransform.RelativeSize.Y * 1.25f);
            instance.SonarModeSwitch.Parent.RectTransform.RelativeSize = new Vector2(
                instance.SonarModeSwitch.Parent.RectTransform.RelativeSize.X,
                instance.SonarModeSwitch.Parent.RectTransform.RelativeSize.Y * 0.8f);
            instance.lowerAreaFrame.Parent.GetChildByUserData("horizontalline").RectTransform.RelativeOffset =
                new Vector2(0.0f, -0.1f);
            instance.lowerAreaFrame.RectTransform.RelativeSize = new Vector2(
                instance.lowerAreaFrame.RectTransform.RelativeSize.X,
                instance.lowerAreaFrame.RectTransform.RelativeSize.Y * 1.2f);
            instance.zoomSlider.Parent.RectTransform.RelativeSize = new Vector2(
                instance.zoomSlider.Parent.RectTransform.RelativeSize.X,
                instance.zoomSlider.Parent.RectTransform.RelativeSize.Y * (2.0f / 3.0f));
            instance.directionalModeSwitch.Parent.RectTransform.RelativeSize = new Vector2(
                instance.directionalModeSwitch.Parent.RectTransform.RelativeSize.X,
                instance.zoomSlider.Parent.RectTransform.RelativeSize.Y);
            instance.directionalModeSwitch.Parent.RectTransform.SetPosition(Anchor.Center);

            instance.PreventMineralScannerOverlap();
        }

        public static void MakeRoomForHydrophoneSwitchMineralScanner(Sonar instance)
        {
            instance.controlContainer.RectTransform.RelativeSize = new Vector2(
                instance.controlContainer.RectTransform.RelativeSize.X,
                instance.controlContainer.RectTransform.RelativeSize.Y * 1.2f);
            instance.SonarModeSwitch.Parent.RectTransform.RelativeOffset = new Vector2(0.0f, -0.025f);
            instance.SonarModeSwitch.Parent.RectTransform.RelativeSize = new Vector2(
                instance.SonarModeSwitch.Parent.RectTransform.RelativeSize.X,
                instance.SonarModeSwitch.Parent.RectTransform.RelativeSize.Y * 0.83f);
            instance.lowerAreaFrame.Parent.GetChildByUserData("horizontalline").RectTransform.RelativeOffset = new Vector2(0.0f, -0.19f);
            instance.lowerAreaFrame.RectTransform.RelativeSize = new Vector2(
                instance.lowerAreaFrame.RectTransform.RelativeSize.X,
                instance.lowerAreaFrame.RectTransform.RelativeSize.Y * 1.25f);
            instance.zoomSlider.Parent.RectTransform.RelativeOffset = new Vector2(0.0f, 0.065f);
            instance.zoomSlider.Parent.RectTransform.RelativeSize = new Vector2(
                instance.zoomSlider.Parent.RectTransform.RelativeSize.X,
                instance.zoomSlider.Parent.RectTransform.RelativeSize.Y * (2.0f / 3.0f));
            instance.directionalModeSwitch.Parent.RectTransform.RelativeOffset = new Vector2(0.0f, -0.108f);
            instance.directionalModeSwitch.Parent.RectTransform.RelativeSize = new Vector2(
                instance.directionalModeSwitch.Parent.RectTransform.RelativeSize.X,
                instance.zoomSlider.Parent.RectTransform.RelativeSize.Y);
            instance.mineralScannerSwitch.Parent.RectTransform.RelativeOffset = new Vector2(0.0f, 0.255f);
            instance.mineralScannerSwitch.Parent.RectTransform.RelativeSize = new Vector2(
                instance.mineralScannerSwitch.Parent.RectTransform.RelativeSize.X,
                instance.zoomSlider.Parent.RectTransform.RelativeSize.Y);

            PreventMineralScannerOverlapCustom(instance.item, instance);
        }

        public static void PreventMineralScannerOverlapCustom(Item item, Sonar sonar)
        {
            if (item.GetComponent<Steering>() is { } steering && sonar.controlContainer is { } container)
            {
                int containerBottom = container.Rect.Y + container.Rect.Height,
                    steeringTop = steering.ControlContainer.Rect.Top;

                int amountRaised = 0;

                while (GetContainerBottom() > steeringTop) { amountRaised++; }

                container.RectTransform.AbsoluteOffset = new Point(0, -amountRaised * 2);

                int GetContainerBottom() => containerBottom - amountRaised;

                sonar.CanBeSelected = true;
            }
        }

        public static void AddHydrophoneSwitchToGUI(Sonar instance)
        {
            HydrophoneSwitches[instance] = new HydrophoneSwitch(false, null, null);

            // Then add the scanner switch
            var hydrophoneSwitchFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, instance.zoomSlider.Parent.RectTransform.RelativeSize.Y), instance.lowerAreaFrame.RectTransform, Anchor.BottomCenter), style: null);
            GUIButton hydrophoneSwitch = new GUIButton(new RectTransform(new Vector2(0.3f, 0.8f), hydrophoneSwitchFrame.RectTransform, Anchor.CenterLeft), string.Empty, style: "SwitchHorizontal")
            {
                Selected = false,
                OnClicked = (button, data) =>
                {
                    HydrophoneSwitches[instance].State = !HydrophoneSwitches[instance].State;
                    button.Selected = HydrophoneSwitches[instance].State;
                    return true;
                }
            };

            if (instance.CurrentMode == Sonar.Mode.Active)
            {
                hydrophoneSwitch.Enabled = false;
            }

            GUITextBlock hydrophoneSwitchText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1), hydrophoneSwitchFrame.RectTransform, Anchor.CenterRight),
                TextManager.Get("spw_hydrophonemonitoring"), GUIStyle.TextColorNormal, GUIStyle.SubHeadingFont, Alignment.CenterLeft);
            instance.textBlocksToScaleAndNormalize.Add(hydrophoneSwitchText);

            HydrophoneSwitches[instance].Switch = hydrophoneSwitch;
            HydrophoneSwitches[instance].TextBlock = hydrophoneSwitchText;
        }
    }
}
