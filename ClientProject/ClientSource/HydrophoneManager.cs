using Barotrauma.Items.Components;
using Barotrauma.Sounds;
using Barotrauma;
using Microsoft.Xna.Framework;
using Barotrauma.Lights;

namespace SoundproofWalls
{
    public static class HydrophoneManager
    {
        private static Dictionary<SoundChannel, Character> hydrophoneSoundChannels = new Dictionary<SoundChannel, Character>();
        public static Dictionary<Sonar, HydrophoneSwitch> HydrophoneSwitches = new Dictionary<Sonar, HydrophoneSwitch>();

        static List<Sound> HydrophoneMovementSounds = new List<Sound>();
        static double LastHydrophonePlayTime = 0.1f;
        static float hydrophoneEfficiency = 1;
        public static float HydrophoneEfficiency { get { return hydrophoneEfficiency; } set { hydrophoneEfficiency = Math.Clamp(value, 0, 1); } }

        public static void Setup()
        {
            LoadSounds();
            SetupHydrophoneSwitches();
        }

        private static void LoadSounds()
        {
            try
            {
                HydrophoneMovementSounds.Add(GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.HydrophoneMovement1]));
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

            if (Timing.TotalTime > LastHydrophonePlayTime + 0.1)
            {
                PlayHydrophoneSounds();
                LastHydrophonePlayTime = (float)Timing.TotalTime;
            }

            UpdateHydrophoneSounds();
        }

        public static void Dispose()
        {
            for (int i = 0; i < HydrophoneMovementSounds.Count(); i++)
            {
                HydrophoneMovementSounds[i]?.Dispose();
                HydrophoneMovementSounds[i] = null;
            }
            HydrophoneMovementSounds.Clear();

            DisposeAllHydrophoneSwitches();
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

        public static void StopAllHydrophoneChannels()
        {
            foreach (var kvp in hydrophoneSoundChannels)
            {
                kvp.Key.FadeOutAndDispose();
            }
            hydrophoneSoundChannels.Clear();
        }

        private static void StopHydrophoneChannel(SoundChannel channel)
        {
            if (channel != null)
            {
                ChannelInfoManager.RemovePitchedChannel(channel);
                channel.Looping = false;
                channel.Gain = 0;
                channel.Dispose();
                RemoveHydrophoneChannel(channel);
            }
        }

        public static bool RemoveHydrophoneChannel(SoundChannel channel)
        {
            return hydrophoneSoundChannels.Remove(channel);
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

        public static void PlayHydrophoneSounds()
        {
            if (!ConfigManager.Config.Enabled || !Listener.IsUsingHydrophones) { return; }

            float range = ConfigManager.Config.HydrophoneSoundRange;

            foreach (Character character in Character.CharacterList)
            {
                if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), character.WorldPosition) > range * range ||
                    hydrophoneSoundChannels.Any(kvp => kvp.Value == character) ||
                    character.CurrentHull != null ||
                    character.CurrentSpeed < 0.05 ||
                    character.isDead)
                {
                    continue;
                }

                float startingGain = 1 * MathUtils.InverseLerp(0f, 2f, character.CurrentSpeed) * HydrophoneEfficiency;
                float speed = Math.Clamp(character.CurrentSpeed, 0f, 10f);
                float freqMult = MathHelper.Lerp(0.25f, 4f, speed / 10f);

                Random random = new Random();
                int randomIndex = random.Next(HydrophoneMovementSounds.Count);
                Sound hydrophoneMovementSound = HydrophoneMovementSounds[randomIndex];

                SoundChannel? channel = hydrophoneMovementSound?.Play(startingGain, range, freqMult, character.WorldPosition, false);

                if (channel != null)
                {
                    channel.Looping = true;
                    hydrophoneSoundChannels[channel] = character;
                }
            }
        }

        public static void UpdateHydrophoneSounds()
        {
            if (!ConfigManager.Config.Enabled || !Listener.IsUsingHydrophones)
            {
                StopAllHydrophoneChannels();
                return;
            }

            foreach (var kvp in hydrophoneSoundChannels)
            {
                Character character = kvp.Value;
                SoundChannel channel = kvp.Key;
                uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);

                if (channel == null || character == null || Character.Controlled == null) { continue; }

                float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, Character.Controlled.WorldPosition);
                channel.Gain = 1 * MathUtils.InverseLerp(0f, 2f, character.CurrentSpeed) * HydrophoneEfficiency;

                if (distanceSquared > channel.far * channel.far || channel.Gain < 0.001f || character.CurrentHull != null || character.isDead)
                {
                    ChannelInfoManager.RemoveChannelInfo(channel);
                    StopHydrophoneChannel(channel);
                }
                else
                {
                    float minSpeed = 0f;
                    float maxSpeed = 12f;
                    float speed = Math.Clamp(character.CurrentSpeed, minSpeed, maxSpeed);
                    channel.FrequencyMultiplier = MathHelper.Lerp(0.25f, 4f, speed / maxSpeed);
                    channel.Position = new Vector3(character.WorldPosition, 0f);
                }
            }
        }
    }
}
