using Barotrauma.Networking;
using Barotrauma;
using Microsoft.Xna.Framework;
using System.Reflection;
using System.Text.Json;
using System.Collections.Generic;

namespace SoundproofWalls
{
    public static class Menu
    {
        static Config defaultConfig = new Config();
        public static JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static GUIListBox? currentSettingsList = null;
        private static float lastScrollPosition = 0.0f;
        private static bool ShouldUpdateConfig = false;
        public static Config NewLocalConfig = ConfigManager.CloneConfig(ConfigManager.LocalConfig);
        public static GUIFrame? currentMenuFrame = null; // To hold the reference to the created menu
        public static GUIScrollBar? EffectsModeSlider = null;
        private static GUIButton? settingsButton = null; // To store the button added to the pause menu

        private static GUIFrame? currentPopupFrame = null;
        private static GUIListBox? currentPopupList = null;

        public static void ForceOpenMenu()
        {
            if (!GUI.PauseMenuOpen)
            {
                GUI.TogglePauseMenu();
            }

            SPW_TogglePauseMenu();

            if (GUI.PauseMenuOpen)
            {
                ShowSettingsFrame();
            }
        }

        public static void ForceOpenWelcomePopUp()
        {
            if (!GUI.PauseMenuOpen)
            {
                GUI.TogglePauseMenu();
            }

            if (GUI.PauseMenuOpen)
            {
                ShowPopUpFrame();
            }
        }

        private static void ShowPopUpFrame()
        {
            GUIFrame parentElement = GUI.PauseMenu;
            if (parentElement == null) return;

            // If frame exists but is hidden, just show it
            if (currentPopupFrame != null && currentPopupFrame.Parent == parentElement)
            {
                currentPopupFrame.Visible = true;
                return;
            }

            // Create UI elements.
            currentPopupFrame = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.30f), parentElement.RectTransform, Anchor.Center));

            // Add text.
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), currentPopupFrame.RectTransform), TextManager.Get("spw_popuptitle").Value, textAlignment: Alignment.Center, font: GUIStyle.LargeFont, color: Color.White);
            GUITextBlock messageText = new GUITextBlock(new RectTransform(new Vector2(1f, 1f), currentPopupFrame.RectTransform), TextManager.Get("spw_popupmessage").Value, textAlignment: Alignment.Center, wrap: true);
            messageText.Padding = new Vector4(50, 50, 50, 50);
            messageText.SetTextPos();

            // Add close button.
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), currentPopupFrame.RectTransform, Anchor.BottomCenter), TextManager.Get("close").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                if (currentPopupFrame != null)
                {
                    currentPopupFrame.Visible = false;
                }
                return true;
            };

            currentPopupFrame.Visible = true; // Ensure it's visible
        }

        public static void SPW_TogglePauseMenu()
        {
            if (GUI.PauseMenuOpen)
            {
                GUIFrame pauseMenuFrame = GUI.PauseMenu;
                if (pauseMenuFrame == null) return;

                GUIComponent? pauseMenuList = null;
                var frameChildren = GetChildren(pauseMenuFrame);
                if (frameChildren.Count > 1)
                {
                    var secondChildChildren = GetChildren(frameChildren[1]);
                    if (secondChildChildren.Count > 0)
                    {
                        pauseMenuList = secondChildChildren[0];
                    }
                }

                if (pauseMenuList == null)
                {
                    LuaCsLogger.LogError("[SoundproofWalls] Failed to add settings button");
                    return;
                }


                // --- Check if button already exists ---
                bool buttonExists = false;
                if (settingsButton != null && settingsButton.Parent == pauseMenuList)
                {
                    buttonExists = true; // Our tracked button is still there
                }
                else
                {
                    // Fallback check if button reference was lost
                    foreach (var child in pauseMenuList.Children)
                    {
                        if (child is GUIButton btn && btn.Text == TextManager.Get("spw_settings").Value)
                        {
                            buttonExists = true;
                            settingsButton = btn; // Re-assign tracked button
                            // Ensure correct callback is assigned in case it was lost
                            settingsButton.OnClicked = (sender, args) => { ShowSettingsFrame(); return true; };
                            break;
                        }
                    }
                }

                // Don't add button if the option to hide it is enabled.
                if (ConfigManager.Config.HideSettings) { return; }

                // --- Add the button if it doesn't exist ---
                if (!buttonExists)
                {
                    string buttonText = TextManager.Get("spw_settings").Value; // Get the settings text for the button
                    settingsButton = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), pauseMenuList.RectTransform), buttonText, Alignment.Center, "GUIButtonSmall");
                    settingsButton.OnClicked = (sender, args) =>
                    {
                        ShowSettingsFrame(); // Call the function to show the actual settings panel
                        return true;
                    };
                }
            }
            else // Pause menu is CLOSING
            {
                // Store position before potentially hiding
                if (currentSettingsList != null && currentMenuFrame != null && currentMenuFrame.Visible) // Only save if it was visible
                {
                    lastScrollPosition = currentSettingsList.BarScroll;
                }

                // Hide the settings frame if it exists and is visible
                if (currentMenuFrame != null && currentMenuFrame.Visible)
                {
                    currentMenuFrame.Visible = false;
                }

                // Hide popup
                if (currentPopupFrame != null && currentPopupFrame.Visible)
                {
                    currentPopupFrame.Visible = false;
                }

                ApplyPendingSettings();
            }
        }

        private static void ShowSettingsFrame()
        {
            GUIFrame parentElement = GUI.PauseMenu; // Parent to pause menu
            if (parentElement == null) return; // Should not happen if button exists, but safety check

            // If frame exists but is hidden, just show it
            if (currentMenuFrame != null && currentMenuFrame.Parent == parentElement)
            {
                currentMenuFrame.Visible = true;
                return;
            }

            // --- Create the menu UI elements ---
            currentMenuFrame = new GUIFrame(new RectTransform(new Vector2(0.25f, 0.65f), parentElement.RectTransform, Anchor.Center));
            currentSettingsList = BasicList(currentMenuFrame);

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), currentMenuFrame.RectTransform), TextManager.Get("spw_settings").Value, textAlignment: Alignment.Center);
            CreateSettingsContentInternal(currentSettingsList);

            // Should remember scroll position.
            if (currentSettingsList != null && ConfigManager.Config.RememberScroll)
            {
                currentSettingsList.BarScroll = lastScrollPosition;
            }

            CloseButton(currentMenuFrame); // Creates the close button
            //RevertAllButton(currentMenuFrame); //TODO
            ResetAllButton(currentMenuFrame); // Creates the reset button

            currentMenuFrame.Visible = true; // Ensure it's visible
        }

        /// <summary>
        /// Applies the configuration changes stored in NewLocalConfig if ShouldUpdateConfig is true.
        /// </summary>
        private static void ApplyPendingSettings()
        {
            if (!ShouldUpdateConfig) { return; }

            ShouldUpdateConfig = false;
            Config oldConfig = ConfigManager.Config;

            ConfigManager.LocalConfig = ConfigManager.CloneConfig(NewLocalConfig);

            // Multiplayer config update
            if (GameMain.IsMultiplayer && (GameMain.Client.IsServerOwner || GameMain.Client.HasPermission(ClientPermissions.Ban)))
            {
                ConfigManager.UploadClientConfigToServer(manualUpdate: true);
            }

            // Singleplayer/nosync config update
            if (!GameMain.IsMultiplayer || ConfigManager.ServerConfig == null)
            {
                ConfigManager.UpdateConfig(NewLocalConfig, oldConfig);
            }
        }

        /// <summary>
        /// Populates the settings list box with controls. (Internal use)
        /// </summary>
        private static void CreateSettingsContentInternal(GUIListBox list)
        {
            Config config = NewLocalConfig;

            GUIScrollBar slider = null;
            GUITickBox tick;

            string default_preset = TextManager.Get("spw_default").Value;
            string vanilla_preset = TextManager.Get("spw_vanilla").Value;
            string slider_text = string.Empty;
            list.Enabled = false;

            if (ConfigManager.ServerConfig != null)
            {
                TextBlock(list, TextManager.Get("spw_syncsettingsalert").Value, y: 0.1f, color: Color.Orange);
            }

            TextBlock(list, TextManager.Get("spw_generalsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            tick = TickBox(list.Content, string.Empty, config.Enabled, state =>
            {
                config.Enabled = state;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
            });
            tick.Text = $"{TextManager.Get("spw_enabled").Value}{GetServerValueString(nameof(config.Enabled))}";
            tick.ToolTip = TextManager.Get("spw_enabledtooltip").Value;

            if (Client.ClientList.Count > 0 && GameMain.Client.SessionId == Client.ClientList[0].SessionId)
            {
                tick = TickBox(list.Content, string.Empty, config.SyncSettings, state =>
                {
                    config.SyncSettings = state;
                    ConfigManager.SaveConfig(config);
                    ShouldUpdateConfig = true;
                });
                tick.Text = $"{TextManager.Get("spw_syncsettings").Value}{GetServerValueString(nameof(config.SyncSettings))}";
                tick.ToolTip = TextManager.Get("spw_syncsettingstooltip").Value;
            }

            tick = TickBox(list.Content, string.Empty, config.TalkingRagdolls, state =>
            {
                config.TalkingRagdolls = state;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
            });
            tick.Text = $"{TextManager.Get("spw_talkingragdolls").Value}{GetServerValueString(nameof(config.TalkingRagdolls))}";
            tick.ToolTip = TextManager.Get("spw_talkingragdollstooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.FocusTargetAudio, state =>
            {
                config.FocusTargetAudio = state;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
            });
            tick.Text = $"{TextManager.Get("spw_focustargetaudio").Value}{GetServerValueString(nameof(config.FocusTargetAudio))}";
            tick.ToolTip = TextManager.Get("spw_focustargetaudiotooltip").Value;

            GUITextBlock textBlockEM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 2, (float)config.EffectProcessingMode, value =>
            {
                value = RoundToNearestMultiple(value, 1);
                config.EffectProcessingMode = (uint)value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;

                if (config.ClassicFx) { slider.ToolTip = TextManager.Get("spw_vanillafxtooltip"); }
                else if (config.StaticFx) { slider.ToolTip = TextManager.Get("spw_staticfxtooltip"); }
                else if (config.DynamicFx) { slider.ToolTip = TextManager.Get("spw_dynamicfxtooltip"); }

                slider_text = string.Empty;
                if (config.EffectProcessingMode == defaultConfig.EffectProcessingMode) { slider_text = default_preset; }

                textBlockEM.Text = $"{TextManager.Get("spw_effectprocessingmode").Value}: {GetEffectProcessingModeName((uint)value)} {slider_text}";
            }, 1);
            textBlockEM.Text = $"{TextManager.Get("spw_effectprocessingmode").Value}: {GetEffectProcessingModeName((uint)RoundToNearestMultiple(slider.BarScrollValue, 1))}{GetServerValueString(nameof(config.EffectProcessingMode))}";
            slider.ToolTip = TextManager.Get("spw_effectprocessingmodetooltip");
            EffectsModeSlider = slider;

            GUITextBlock textBlockHOF = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 10, 3200, (float)config.HeavyLowpassFrequency, value =>
            {
                value = RoundToNearestMultiple(value, 10);
                config.HeavyLowpassFrequency = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.HeavyLowpassFrequency == defaultConfig.HeavyLowpassFrequency) { slider_text = default_preset; }
                else if (config.HeavyLowpassFrequency == SoundPlayer.MuffleFilterFrequency) { slider_text = vanilla_preset; }
                textBlockHOF.Text = $"{TextManager.Get("spw_heavylowpassfrequency").Value}: {value}Hz {slider_text}";
            }, 10);
            textBlockHOF.Text = $"{TextManager.Get("spw_heavylowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 10)}Hz{GetServerValueString(nameof(config.HeavyLowpassFrequency), "Hz")}";
            slider.ToolTip = TextManager.Get("spw_heavylowpassfrequencytooltip");

            GUITextBlock textBlockMOF = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 10, 3200, (float)config.MediumLowpassFrequency, value =>
            {
                value = RoundToNearestMultiple(value, 10);
                config.MediumLowpassFrequency = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.MediumLowpassFrequency == defaultConfig.MediumLowpassFrequency) { slider_text = default_preset; }
                textBlockMOF.Text = $"{TextManager.Get("spw_mediumlowpassfrequency").Value}: {value}Hz {slider_text}";
            }, 10);
            textBlockMOF.Text = $"{TextManager.Get("spw_mediumlowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 10)}Hz{GetServerValueString(nameof(config.MediumLowpassFrequency), "Hz")}";
            slider.ToolTip = TextManager.Get("spw_mediumlowpassfrequencytooltip");

            GUITextBlock textBlockLOF = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 10, 3200, (float)config.LightLowpassFrequency, value =>
            {
                value = RoundToNearestMultiple(value, 10);
                config.LightLowpassFrequency = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.LightLowpassFrequency == defaultConfig.LightLowpassFrequency) { slider_text = default_preset; }
                textBlockLOF.Text = $"{TextManager.Get("spw_lightlowpassfrequency").Value}: {value}Hz {slider_text}";
            }, 10);
            textBlockLOF.Text = $"{TextManager.Get("spw_lightlowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 10)}Hz{GetServerValueString(nameof(config.LightLowpassFrequency), "Hz")}";
            slider.ToolTip = TextManager.Get("spw_lightlowpassfrequencytooltip");

            GUITextBlock textBlockSR = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.SoundRangeMultiplierMaster, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f);
                float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.SoundRangeMultiplierMaster = realvalue;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.SoundRangeMultiplierMaster == defaultConfig.SoundRangeMultiplierMaster) { slider_text = default_preset; }
                else if (config.SoundRangeMultiplierMaster == 1) { slider_text = vanilla_preset; }
                textBlockSR.Text = $"{TextManager.Get("spw_soundrange").Value}: {displayValue}% {slider_text}";
            });
            textBlockSR.Text = $"{TextManager.Get("spw_soundrange").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SoundRangeMultiplierMaster))}";
            slider.ToolTip = TextManager.Get("spw_soundrangetooltip");

            GUITextBlock textBlockSPR = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 2000, config.SoundPropagationRange, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 100);
                float displayValue = RoundToNearestMultiple(value / 100, 1);
                config.SoundPropagationRange = (int)realvalue;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.SoundPropagationRange == defaultConfig.SoundPropagationRange) { slider_text = default_preset; }
                textBlockSPR.Text = $"{TextManager.Get("spw_soundpropagationrange").Value}: +{displayValue}m {slider_text}";
            }, 1);
            textBlockSPR.Text = $"{TextManager.Get("spw_soundpropagationrange").Value}: +{RoundToNearestMultiple(slider.BarScrollValue / 100, 1)}m{GetServerValueString(nameof(config.SoundPropagationRange), "m", 100)}";
            slider.ToolTip = TextManager.Get("spw_soundpropagationrangetooltip");

            TextBlock(list, TextManager.Get("spw_voicesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            GUITextBlock textBlockVLF = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 10, ChannelInfoManager.VANILLA_VOIP_LOWPASS_FREQUENCY * 1.5f, (float)config.VoiceHeavyLowpassFrequency, value =>
            {
                value = RoundToNearestMultiple(value, 10);
                if (value == SoundPlayer.MuffleFilterFrequency) { value += 10; }
                config.VoiceHeavyLowpassFrequency = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.VoiceHeavyLowpassFrequency == defaultConfig.VoiceHeavyLowpassFrequency) { slider_text = default_preset; }
                else if (config.VoiceHeavyLowpassFrequency == ChannelInfoManager.VANILLA_VOIP_LOWPASS_FREQUENCY) { slider_text = vanilla_preset; }
                textBlockVLF.Text = $"{TextManager.Get("spw_voicelowpassfrequency").Value}: {value}Hz {slider_text}";
            }, 10);
            textBlockVLF.Text = $"{TextManager.Get("spw_voicelowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 10)}Hz{GetServerValueString(nameof(config.VoiceHeavyLowpassFrequency), "Hz")}";
            slider.ToolTip = TextManager.Get("spw_voicelowpassfrequencytooltip");

            GUITextBlock textBlockRCF = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 300, ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY * 1.5f, (float)config.RadioBandpassFrequency, value =>
            {
                value = (float)RoundToNearestMultiple(value, 10);
                if (value == ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY) { value += 10; }
                config.RadioBandpassFrequency = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.RadioBandpassFrequency == defaultConfig.RadioBandpassQualityFactor) { slider_text = default_preset; }
                else if (config.RadioBandpassFrequency == ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY) { slider_text = vanilla_preset; }
                textBlockRCF.Text = $"{TextManager.Get("spw_radiobandpassfrequency").Value}: {value}Hz {slider_text}";
            }, 0.01f);
            textBlockRCF.Text = $"{TextManager.Get("spw_radiobandpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 10)}Hz{GetServerValueString(nameof(config.RadioBandpassFrequency), "Hz")}";
            slider.ToolTip = TextManager.Get("spw_radiobandpassfrequencytooltip");

            GUITextBlock textBlockRBQ = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 0.5f, 10.0f, (float)config.RadioBandpassQualityFactor, value =>
            {
                value = RoundToNearestMultiple(value, 0.1f);
                config.RadioBandpassQualityFactor = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.RadioBandpassQualityFactor == defaultConfig.RadioBandpassQualityFactor) { slider_text = default_preset; }
                else if (config.RadioBandpassQualityFactor == 0.7f) { slider_text = vanilla_preset; }
                textBlockRBQ.Text = $"{TextManager.Get("spw_radiobandpassqualityfactor").Value}: {value} {slider_text}";
            }, 0.1f);
            textBlockRBQ.Text = $"{TextManager.Get("spw_radiobandpassqualityfactor").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 0.1f)}{GetServerValueString(nameof(config.RadioBandpassQualityFactor), "")}";
            slider.ToolTip = TextManager.Get("spw_radiobandpassqualityfactortooltip");

            GUITextBlock textBlockRD = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 10.0f, (float)config.RadioDistortion, value =>
            {
                value = RoundToNearestMultiple(value, 0.1f);
                config.RadioDistortion = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.RadioDistortion == defaultConfig.RadioDistortion) { slider_text = default_preset; }
                textBlockRD.Text = $"{TextManager.Get("spw_radiodistortion").Value}: {value} {slider_text}";
            }, 0.1f);
            textBlockRD.Text = $"{TextManager.Get("spw_radiodistortion").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 0.1f)}{GetServerValueString(nameof(config.RadioDistortion), "")}";
            slider.ToolTip = TextManager.Get("spw_radiodistortiontooltip");

            GUITextBlock textBlockRS = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 10.0f, (float)config.RadioStatic, value =>
            {
                value = RoundToNearestMultiple(value, 0.1f);
                config.RadioStatic = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.RadioStatic == defaultConfig.RadioStatic) { slider_text = default_preset; }
                textBlockRS.Text = $"{TextManager.Get("spw_radiostatic").Value}: {value} {slider_text}";
            }, 0.1f);
            textBlockRS.Text = $"{TextManager.Get("spw_radiostatic").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 0.1f)}{GetServerValueString(nameof(config.RadioStatic), "")}";
            slider.ToolTip = TextManager.Get("spw_radiostatictooltip");

            GUITextBlock textBlockRCT = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 0.1f, 10.0f, (float)config.RadioCompressionThreshold, value =>
            {
                value = RoundToNearestMultiple(value, 0.1f);
                config.RadioCompressionThreshold = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.RadioCompressionThreshold == defaultConfig.RadioCompressionThreshold) { slider_text = default_preset; }
                textBlockRCT.Text = $"{TextManager.Get("spw_radiocompressionthreshold").Value}: {value} {slider_text}";
            }, 0.1f);
            textBlockRCT.Text = $"{TextManager.Get("spw_radiocompressionthreshold").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 0.1f)}{GetServerValueString(nameof(config.RadioCompressionThreshold), "")}";
            slider.ToolTip = TextManager.Get("spw_radiocompressionthresholdtooltip");

            GUITextBlock textBlockRCR = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 0.1f, 36.0f, (float)config.RadioCompressionRatio, value =>
            {
                value = RoundToNearestMultiple(value, 0.1f);
                config.RadioCompressionRatio = value;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.RadioCompressionRatio == defaultConfig.RadioCompressionRatio) { slider_text = default_preset; }
                textBlockRCR.Text = $"{TextManager.Get("spw_radiocompressionratio").Value}: {value} {slider_text}";
            }, 0.1f);
            textBlockRCR.Text = $"{TextManager.Get("spw_radiocompressionratio").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 0.1f)}{GetServerValueString(nameof(config.RadioCompressionRatio), "")}";
            slider.ToolTip = TextManager.Get("spw_radiocompressionratiotooltip");

            GUITextBlock textBlockVR = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.VoiceRangeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f);
                float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.VoiceRangeMultiplier = realvalue;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.VoiceRangeMultiplier == defaultConfig.VoiceRangeMultiplier) { slider_text = default_preset; }
                else if (config.VoiceRangeMultiplier == 1) { slider_text = vanilla_preset; }
                textBlockVR.Text = $"{TextManager.Get("spw_voicerange").Value}: {displayValue}% {slider_text}";
            });
            textBlockVR.Text = $"{TextManager.Get("spw_voicerange").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.VoiceRangeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_voicerangetooltip");

            GUITextBlock textBlockRR = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.RadioRangeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f);
                float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.RadioRangeMultiplier = realvalue;
                ConfigManager.SaveConfig(config);
                ShouldUpdateConfig = true;
                slider_text = string.Empty;
                if (config.RadioRangeMultiplier == defaultConfig.RadioRangeMultiplier) { slider_text = default_preset; }
                else if (config.RadioRangeMultiplier == 1) { slider_text = vanilla_preset; }
                textBlockRR.Text = $"{TextManager.Get("spw_radiorange").Value}: {displayValue}% {slider_text}";
            });
            textBlockRR.Text = $"{TextManager.Get("spw_radiorange").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.RadioRangeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_radiorangetooltip");

            TextBlock(list, TextManager.Get("spw_muffledsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            tick = TickBox(list.Content, string.Empty, config.MuffleDivingSuits, state => { config.MuffleDivingSuits = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_muffledivingsuit").Value}{GetServerValueString(nameof(config.MuffleDivingSuits))}";
            tick.ToolTip = TextManager.Get("spw_muffledivingsuittooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.MuffleEavesdropping, state => { config.MuffleEavesdropping = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_muffleeavesdropping").Value}{GetServerValueString(nameof(config.MuffleEavesdropping))}";
            tick.ToolTip = TextManager.Get("spw_muffleeavesdroppingtooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.MuffleSubmergedPlayer, state => { config.MuffleSubmergedPlayer = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_mufflesubmergedplayer").Value}{GetServerValueString(nameof(config.MuffleSubmergedPlayer))}";
            tick.ToolTip = TextManager.Get("spw_mufflesubmergedplayertooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.MuffleSubmergedViewTarget, state => { config.MuffleSubmergedViewTarget = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_mufflesubmergedviewtarget").Value}{GetServerValueString(nameof(config.MuffleSubmergedViewTarget))}";
            tick.ToolTip = TextManager.Get("spw_mufflesubmergedviewtargettooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.MuffleWaterSurface, state => { config.MuffleWaterSurface = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_mufflesubmergedsounds").Value}{GetServerValueString(nameof(config.MuffleWaterSurface))}";
            tick.ToolTip = TextManager.Get("spw_mufflesubmergedsoundstooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.MuffleFlowSounds, state => { config.MuffleFlowSounds = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_muffleflowsounds").Value}{GetServerValueString(nameof(config.MuffleFlowSounds))}";
            tick.ToolTip = TextManager.Get("spw_muffleflowsoundstooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.MuffleFireSounds, state => { config.MuffleFireSounds = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_mufflefiresounds").Value}{GetServerValueString(nameof(config.MuffleFireSounds))}";
            tick.ToolTip = TextManager.Get("spw_mufflefiresoundstooltip").Value;

            TextBlock(list, TextManager.Get("spw_volumesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            tick = TickBox(list.Content, string.Empty, config.SidechainingEnabled, state => { config.SidechainingEnabled = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_sidechaining").Value}{GetServerValueString(nameof(config.SidechainingEnabled))}";
            tick.ToolTip = TextManager.Get("spw_sidechainingtooltip").Value;

            GUITextBlock textBlockSIM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.SidechainIntensityMaster, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.SidechainIntensityMaster = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.SidechainIntensityMaster == defaultConfig.SidechainIntensityMaster) { slider_text = default_preset; } else if (config.SidechainIntensityMaster == 1) { slider_text = vanilla_preset; }
                textBlockSIM.Text = $"{TextManager.Get("spw_sidechainintensitymaster").Value}: {displayValue}% {slider_text}";
            });
            textBlockSIM.Text = $"{TextManager.Get("spw_sidechainintensitymaster").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SidechainIntensityMaster))}";
            slider.ToolTip = TextManager.Get("spw_sidechainintensitymastertooltip");

            GUITextBlock textBlockSRM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, -10, 10, config.SidechainReleaseMaster, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.1f); config.SidechainReleaseMaster = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                string slider_operator = ""; slider_text = string.Empty; if (config.SidechainReleaseMaster == defaultConfig.SidechainReleaseMaster) { slider_text = default_preset; }
                if (realvalue > 0) { slider_operator = "+"; }
                textBlockSRM.Text = $"{TextManager.Get("spw_sidechainreleasemaster").Value}: {slider_operator}{realvalue}s {slider_text}";
            }, 0.1f);
            textBlockSRM.Text = $"{TextManager.Get("spw_sidechainreleasemaster").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 0.1f)}s{GetServerValueString(nameof(config.SidechainReleaseMaster), "s")}";
            slider.ToolTip = TextManager.Get("spw_sidechainreleasemastertooltip");

            GUITextBlock textBlockSRC = TextBlock(list, string.Empty);
            slider = LogSlider(list.Content, 0.1f, 5.0f, (float)config.SidechainReleaseCurve, value =>
            {
                value = RoundToNearestMultiple(value, 0.1f); config.SidechainReleaseCurve = value; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; slider_text = string.Empty;
                if (config.SidechainReleaseCurve == 1) { slider_text = TextManager.Get("spw_linear").Value; } else if (config.SidechainReleaseCurve < 1) { slider_text = TextManager.Get("spw_concave").Value; } else if (config.SidechainReleaseCurve > 1) { slider_text = TextManager.Get("spw_convex").Value; }
                if (config.SidechainReleaseCurve == defaultConfig.SidechainReleaseCurve) { slider_text += " " + default_preset; }
                textBlockSRC.Text = $"{TextManager.Get("spw_sidechainreleasecurve").Value}: {value} {slider_text}";
            }, 0.1f);
            textBlockSRC.Text = $"{TextManager.Get("spw_sidechainreleasecurve").Value}: {RoundToNearestMultiple(slider.GetConvertedLogValue(), 0.1f)}{GetServerValueString(nameof(config.SidechainReleaseCurve), "")}";
            slider.ToolTip = TextManager.Get("spw_sidechainreleasecurvetooltip");

            GUITextBlock textBlockMSV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.MuffledSoundVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.MuffledSoundVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.MuffledSoundVolumeMultiplier == defaultConfig.MuffledSoundVolumeMultiplier) { slider_text = default_preset; }
                textBlockMSV.Text = $"{TextManager.Get("spw_muffledsoundvolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockMSV.Text = $"{TextManager.Get("spw_muffledsoundvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledSoundVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_muffledsoundvolumetooltip");

            GUITextBlock textBlockMVV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.MuffledVoiceVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.MuffledVoiceVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.MuffledVoiceVolumeMultiplier == defaultConfig.MuffledVoiceVolumeMultiplier) { slider_text = default_preset; }
                textBlockMVV.Text = $"{TextManager.Get("spw_muffledvoicevolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockMVV.Text = $"{TextManager.Get("spw_muffledvoicevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledVoiceVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_muffledvoicevolumetooltip");

            GUITextBlock textBlockMCV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.MuffledLoopingVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.MuffledLoopingVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.MuffledLoopingVolumeMultiplier == defaultConfig.MuffledLoopingVolumeMultiplier) { slider_text = default_preset; }
                textBlockMCV.Text = $"{TextManager.Get("spw_muffledcomponentvolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockMCV.Text = $"{TextManager.Get("spw_muffledcomponentvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledLoopingVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_muffledcomponentvolumetooltip");

            GUITextBlock textBlockUCV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 1, config.UnmuffledLoopingVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.UnmuffledLoopingVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.UnmuffledLoopingVolumeMultiplier == defaultConfig.UnmuffledLoopingVolumeMultiplier) { slider_text = default_preset; }
                textBlockUCV.Text = $"{TextManager.Get("spw_unmuffledcomponentvolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockUCV.Text = $"{TextManager.Get("spw_unmuffledcomponentvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnmuffledLoopingVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_unmuffledcomponentvolumetooltip");

            GUITextBlock textBlockSV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.SubmergedVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.SubmergedVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.SubmergedVolumeMultiplier == defaultConfig.SubmergedVolumeMultiplier) { slider_text = default_preset; }
                textBlockSV.Text = $"{TextManager.Get("spw_submergedvolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockSV.Text = $"{TextManager.Get("spw_submergedvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SubmergedVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_submergedvolumetooltip");

            GUITextBlock textBlockFLSV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.FlowSoundVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.FlowSoundVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.FlowSoundVolumeMultiplier == defaultConfig.FlowSoundVolumeMultiplier) { slider_text = default_preset; } if (config.FlowSoundVolumeMultiplier == 1.0f) { slider_text = vanilla_preset; }
                textBlockFLSV.Text = $"{TextManager.Get("spw_flowsoundvolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockFLSV.Text = $"{TextManager.Get("spw_flowsoundvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.FlowSoundVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_flowsoundvolumetooltip");

            GUITextBlock textBlockFISV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.FireSoundVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.FireSoundVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.FireSoundVolumeMultiplier == defaultConfig.FireSoundVolumeMultiplier) { slider_text = default_preset; } if (config.FireSoundVolumeMultiplier == 1.0f) { slider_text = vanilla_preset; }
                textBlockFISV.Text = $"{TextManager.Get("spw_firesoundvolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockFISV.Text = $"{TextManager.Get("spw_firesoundvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.FireSoundVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_firesoundvolumetooltip");

            TextBlock(list, TextManager.Get("spw_eavesdroppingsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            tick = TickBox(list.Content, string.Empty, config.EavesdroppingEnabled, state => { config.EavesdroppingEnabled = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_eavesdroppingenabled").Value}{GetServerValueString(nameof(config.EavesdroppingEnabled))}";
            tick.ToolTip = TextManager.Get("spw_eavesdroppingenabledtooltip").Value;

            tick = TickBox(list.Content, string.Empty, config.EavesdroppingTransitionEnabled, state => { config.EavesdroppingTransitionEnabled = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_eavesdroppingfade").Value}{GetServerValueString(nameof(config.EavesdroppingTransitionEnabled))}";
            tick.ToolTip = TextManager.Get("spw_eavesdroppingfadetooltip").Value;

            GUITextBlock textBlockEFT = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.1f, 10, config.EavesdroppingTransitionDuration, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.1f); config.EavesdroppingTransitionDuration = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.EavesdroppingTransitionDuration == defaultConfig.EavesdroppingTransitionDuration) { slider_text = default_preset; }
                textBlockEFT.Text = $"{TextManager.Get("spw_eavesdroppingfadeduration").Value}: {realvalue}s {slider_text}";
            }, 0.1f);
            textBlockEFT.Text = $"{TextManager.Get("spw_eavesdroppingfadeduration").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 0.1f)}s{GetServerValueString(nameof(config.EavesdroppingTransitionDuration), "s")}";
            slider.ToolTip = TextManager.Get("spw_eavesdroppingfadedurationtooltip");

            GUITextBlock textBlockET = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 1, config.EavesdroppingThreshold, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.EavesdroppingThreshold = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.EavesdroppingThreshold == defaultConfig.EavesdroppingThreshold) { slider_text = default_preset; }
                textBlockET.Text = $"{TextManager.Get("spw_eavesdroppingthreshold").Value}: {displayValue}% {slider_text}";
            });
            textBlockET.Text = $"{TextManager.Get("spw_eavesdroppingthreshold").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.EavesdroppingThreshold))}";
            slider.ToolTip = TextManager.Get("spw_eavesdroppingthresholdtooltip");

            GUITextBlock textBlockEMD = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 100, config.EavesdroppingMaxDistance, value =>
            {
                value = RoundToNearestMultiple(value, 1); config.EavesdroppingMaxDistance = (int)value; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.EavesdroppingMaxDistance == defaultConfig.EavesdroppingMaxDistance) { slider_text = default_preset; }
                textBlockEMD.Text = $"{TextManager.Get("spw_eavesdroppingmaxdistance").Value}: {value}cm {slider_text}";
            }, 1);
            textBlockEMD.Text = $"{TextManager.Get("spw_eavesdroppingmaxdistance").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 1)}cm{GetServerValueString(nameof(config.EavesdroppingMaxDistance), "cm")}";
            slider.ToolTip = TextManager.Get("spw_eavesdroppingmaxdistancetooltip");

            GUITextBlock textBlockESV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.EavesdroppingSoundVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.EavesdroppingSoundVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.EavesdroppingSoundVolumeMultiplier == defaultConfig.EavesdroppingSoundVolumeMultiplier) { slider_text = default_preset; }
                textBlockESV.Text = $"{TextManager.Get("spw_eavesdroppingsoundvolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockESV.Text = $"{TextManager.Get("spw_eavesdroppingsoundvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.EavesdroppingSoundVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_eavesdroppingsoundvolumetooltip");

            GUITextBlock textBlockEVV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 3, config.EavesdroppingVoiceVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.EavesdroppingVoiceVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.EavesdroppingVoiceVolumeMultiplier == defaultConfig.EavesdroppingVoiceVolumeMultiplier) { slider_text = default_preset; }
                textBlockEVV.Text = $"{TextManager.Get("spw_eavesdroppingvoicevolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockEVV.Text = $"{TextManager.Get("spw_eavesdroppingvoicevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.EavesdroppingVoiceVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_eavesdroppingvoicevolumetooltip");

            GUITextBlock textBlockESP = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4f, config.EavesdroppingPitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.EavesdroppingPitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.EavesdroppingPitchMultiplier == defaultConfig.EavesdroppingPitchMultiplier) { slider_text = default_preset; }
                textBlockESP.Text = $"{TextManager.Get("spw_eavesdroppingpitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockESP.Text = $"{TextManager.Get("spw_eavesdroppingpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.EavesdroppingPitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_eavesdroppingpitchtooltip");

            TextBlock(list, TextManager.Get("spw_hydrophonesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            tick = TickBox(list.Content, string.Empty, config.HydrophoneSwitchEnabled, state => { config.HydrophoneSwitchEnabled = state; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; });
            tick.Text = $"{TextManager.Get("spw_hydrophoneswitchenabled").Value}{GetServerValueString(nameof(config.HydrophoneSwitchEnabled))}";
            tick.ToolTip = TextManager.Get("spw_hydrophoneswitchenabledtooltip").Value;

            GUITextBlock textBlockHSR = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 20000, config.HydrophoneSoundRange, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 100); float displayValue = RoundToNearestMultiple(value / 100, 1);
                config.HydrophoneSoundRange = (int)realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.HydrophoneSoundRange == defaultConfig.HydrophoneSoundRange) { slider_text = default_preset; }
                textBlockHSR.Text = $"{TextManager.Get("spw_hydrophonerange").Value}: +{displayValue}m {slider_text}";
            }, 1);
            textBlockHSR.Text = $"{TextManager.Get("spw_hydrophonerange").Value}: +{RoundToNearestMultiple(slider.BarScrollValue / 100, 1)}m{GetServerValueString(nameof(config.HydrophoneSoundRange), "m", 100)}";
            slider.ToolTip = TextManager.Get("spw_hydrophonerangetooltip");

            GUITextBlock textBlockHSV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 2, config.HydrophoneVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.HydrophoneVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.HydrophoneVolumeMultiplier == defaultConfig.HydrophoneVolumeMultiplier) { slider_text = default_preset; }
                textBlockHSV.Text = $"{TextManager.Get("spw_hydrophonevolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockHSV.Text = $"{TextManager.Get("spw_hydrophonevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.HydrophoneVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_hydrophonevolumetooltip");

            GUITextBlock textBlockHSP = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4f, config.HydrophonePitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.HydrophonePitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.HydrophonePitchMultiplier == defaultConfig.HydrophonePitchMultiplier) { slider_text = default_preset; }
                textBlockHSP.Text = $"{TextManager.Get("spw_hydrophonepitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockHSP.Text = $"{TextManager.Get("spw_hydrophonepitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.HydrophonePitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_hydrophonepitchtooltip");

            TextBlock(list, TextManager.Get("spw_ambiencesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            GUITextBlock textBlockUWAV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 2, config.UnsubmergedWaterAmbienceVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.UnsubmergedWaterAmbienceVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.UnsubmergedWaterAmbienceVolumeMultiplier == defaultConfig.UnsubmergedWaterAmbienceVolumeMultiplier) { slider_text = default_preset; }
                textBlockUWAV.Text = $"{TextManager.Get("spw_unsubmergedwaterambiencevolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockUWAV.Text = $"{TextManager.Get("spw_unsubmergedwaterambiencevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnsubmergedWaterAmbienceVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_unsubmergedwaterambiencevolumetooltip");

            GUITextBlock textBlockSWAV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 2, config.SubmergedWaterAmbienceVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.SubmergedWaterAmbienceVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.SubmergedWaterAmbienceVolumeMultiplier == defaultConfig.SubmergedWaterAmbienceVolumeMultiplier) { slider_text = default_preset; }
                textBlockSWAV.Text = $"{TextManager.Get("spw_submergedwaterambiencevolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockSWAV.Text = $"{TextManager.Get("spw_submergedwaterambiencevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SubmergedWaterAmbienceVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_submergedwaterambiencevolumetooltip");

            GUITextBlock textBlockWAV = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 2, config.HydrophoneWaterAmbienceVolumeMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.HydrophoneWaterAmbienceVolumeMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.HydrophoneWaterAmbienceVolumeMultiplier == defaultConfig.HydrophoneWaterAmbienceVolumeMultiplier) { slider_text = default_preset; }
                textBlockWAV.Text = $"{TextManager.Get("spw_hydrophonewaterambiencevolume").Value}: {displayValue}% {slider_text}";
            });
            textBlockWAV.Text = $"{TextManager.Get("spw_hydrophonewaterambiencevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.HydrophoneWaterAmbienceVolumeMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_hydrophonewaterambiencevolumetooltip");

            GUITextBlock textBlockWATS = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.5f, 5, config.WaterAmbienceTransitionSpeedMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.WaterAmbienceTransitionSpeedMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.WaterAmbienceTransitionSpeedMultiplier == defaultConfig.WaterAmbienceTransitionSpeedMultiplier) { slider_text = default_preset; } else if (config.WaterAmbienceTransitionSpeedMultiplier == 1) { slider_text = vanilla_preset; }
                textBlockWATS.Text = $"{TextManager.Get("spw_waterambiencetransitionspeed").Value}: {displayValue}% {slider_text}";
            });
            textBlockWATS.Text = $"{TextManager.Get("spw_waterambiencetransitionspeed").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.WaterAmbienceTransitionSpeedMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_waterambiencetransitionspeedtooltip");

            TextBlock(list, TextManager.Get("spw_pitchsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            GUITextBlock textBlockDPM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4, config.DivingSuitPitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.DivingSuitPitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.DivingSuitPitchMultiplier == defaultConfig.DivingSuitPitchMultiplier) { slider_text = default_preset; }
                textBlockDPM.Text = $"{TextManager.Get("spw_divingsuitpitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockDPM.Text = $"{TextManager.Get("spw_divingsuitpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.DivingSuitPitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_divingsuitpitchtooltip");

            GUITextBlock textBlockSPM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4, config.SubmergedPitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.SubmergedPitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.SubmergedPitchMultiplier == defaultConfig.SubmergedPitchMultiplier) { slider_text = default_preset; }
                textBlockSPM.Text = $"{TextManager.Get("spw_submergedpitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockSPM.Text = $"{TextManager.Get("spw_submergedpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SubmergedPitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_submergedpitchtooltip");

            GUITextBlock textBlockMSPM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0, 1, config.MuffledSoundPitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.MuffledSoundPitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.MuffledSoundPitchMultiplier == defaultConfig.MuffledSoundPitchMultiplier) { slider_text = default_preset; }
                textBlockMSPM.Text = $"{TextManager.Get("spw_muffledsoundpitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockMSPM.Text = $"{TextManager.Get("spw_muffledsoundpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledSoundPitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_muffledsoundpitchtooltip");

            GUITextBlock textBlockMCPM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4, config.MuffledLoopingPitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.MuffledLoopingPitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.MuffledLoopingPitchMultiplier == defaultConfig.MuffledLoopingPitchMultiplier) { slider_text = default_preset; }
                textBlockMCPM.Text = $"{TextManager.Get("spw_muffledcomponentpitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockMCPM.Text = $"{TextManager.Get("spw_muffledcomponentpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledLoopingPitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_muffledcomponentpitchtooltip");

            GUITextBlock textBlockUCPM = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4, config.UnmuffledSoundPitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.UnmuffledSoundPitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.UnmuffledSoundPitchMultiplier == defaultConfig.UnmuffledSoundPitchMultiplier) { slider_text = default_preset; }
                textBlockUCPM.Text = $"{TextManager.Get("spw_unmuffledcomponentpitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockUCPM.Text = $"{TextManager.Get("spw_unmuffledcomponentpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnmuffledSoundPitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_unmuffledcomponentpitchtooltip");

            GUITextBlock textBlockMVP = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4, config.MuffledVoicePitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.MuffledVoicePitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.MuffledVoicePitchMultiplier == defaultConfig.MuffledVoicePitchMultiplier) { slider_text = default_preset; }
                textBlockMVP.Text = $"{TextManager.Get("spw_muffledvoicepitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockMVP.Text = $"{TextManager.Get("spw_muffledvoicepitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledVoicePitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_muffledvoicepitchtooltip");

            GUITextBlock textBlockUVP = TextBlock(list, string.Empty);
            slider = Slider(list.Content, 0.25f, 4, config.UnmuffledVoicePitchMultiplier, value =>
            {
                float realvalue = RoundToNearestMultiple(value, 0.01f); float displayValue = RoundToNearestMultiple(value * 100, 1);
                config.UnmuffledVoicePitchMultiplier = realvalue; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true;
                slider_text = string.Empty; if (config.UnmuffledVoicePitchMultiplier == defaultConfig.UnmuffledVoicePitchMultiplier) { slider_text = default_preset; }
                textBlockUVP.Text = $"{TextManager.Get("spw_unmuffledvoicepitch").Value}: {displayValue}% {slider_text}";
            });
            textBlockUVP.Text = $"{TextManager.Get("spw_unmuffledvoicepitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnmuffledVoicePitchMultiplier))}";
            slider.ToolTip = TextManager.Get("spw_unmuffledvoicepitchtooltip");

            TextBlock(list, TextManager.Get("spw_advancedsoundsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

            GUITextBlock textBlockCS = TextBlock(list, $"{TextManager.Get("spw_customsounds").Value}{GetServerDictString(nameof(config.CustomSounds))}");
            GUITextBox soundListCS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.CustomSounds, jsonOptions));
            soundListCS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.CustomSounds = JsonSerializer.Deserialize<HashSet<CustomSound>>(textBox.Text) ?? new HashSet<CustomSound>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockCS.Text = TextManager.Get("spw_customsounds").Value; }
                catch (JsonException) { textBlockCS.Text = $"{TextManager.Get("spw_customsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListCS.ToolTip = TextManager.Get("spw_customsoundstooltip");
            GUIButton buttonCS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonCS.OnClicked = (sender, args) => { config.CustomSounds = defaultConfig.CustomSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListCS.Text = JsonSerializer.Serialize(config.CustomSounds, jsonOptions); return true; };

            GUITextBlock textBlockIS = TextBlock(list, $"{TextManager.Get("spw_ignoredsounds").Value}{GetServerHashSetString(nameof(config.IgnoredSounds))}");
            GUITextBox soundListIS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.IgnoredSounds, jsonOptions), 0.15f);
            soundListIS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.IgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockIS.Text = TextManager.Get("spw_ignoredsounds").Value; }
                catch (JsonException) { textBlockIS.Text = $"{TextManager.Get("spw_ignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListIS.ToolTip = TextManager.Get("spw_ignoredsoundstooltip");
            GUIButton buttonIS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonIS.OnClicked = (sender, args) => { config.IgnoredSounds = defaultConfig.IgnoredSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListIS.Text = JsonSerializer.Serialize(config.IgnoredSounds, jsonOptions); return true; };

            GUITextBlock textBlockWIS = TextBlock(list, $"{TextManager.Get("spw_waterignoredsounds").Value}{GetServerHashSetString(nameof(config.SurfaceIgnoredSounds))}");
            GUITextBox soundListWIS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.SurfaceIgnoredSounds, jsonOptions), 0.15f);
            soundListWIS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.SurfaceIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockWIS.Text = TextManager.Get("spw_waterignoredsounds").Value; }
                catch (JsonException) { textBlockWIS.Text = $"{TextManager.Get("spw_waterignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListWIS.ToolTip = TextManager.Get("spw_waterignoredsoundstooltip");
            GUIButton buttonWIS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonWIS.OnClicked = (sender, args) => { config.SurfaceIgnoredSounds = defaultConfig.SurfaceIgnoredSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListWIS.Text = JsonSerializer.Serialize(config.SurfaceIgnoredSounds, jsonOptions); return true; };

            GUITextBlock textBlockSIS = TextBlock(list, $"{TextManager.Get("spw_submersionignoredsounds").Value}{GetServerHashSetString(nameof(config.SubmersionIgnoredSounds))}");
            GUITextBox soundListSIS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.SubmersionIgnoredSounds, jsonOptions), 0.15f);
            soundListSIS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.SubmersionIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockSIS.Text = TextManager.Get("spw_submersionignoredsounds").Value; }
                catch (JsonException) { textBlockSIS.Text = $"{TextManager.Get("spw_submersionignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListSIS.ToolTip = TextManager.Get("spw_submersionignoredsoundstooltip");
            GUIButton buttonSIS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonSIS.OnClicked = (sender, args) => { config.SubmersionIgnoredSounds = defaultConfig.SubmersionIgnoredSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListSIS.Text = JsonSerializer.Serialize(config.SubmersionIgnoredSounds, jsonOptions); return true; };

            GUITextBlock textBlockWPS = TextBlock(list, $"{TextManager.Get("spw_wallpropagatingsounds").Value}{GetServerHashSetString(nameof(config.PropagatingSounds))}");
            GUITextBox soundListWPS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.PropagatingSounds, jsonOptions), 0.15f);
            soundListWPS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.PropagatingSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockWPS.Text = TextManager.Get("spw_wallpropagatingsounds").Value; }
                catch (JsonException) { textBlockWPS.Text = $"{TextManager.Get("spw_wallpropagatingsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListWPS.ToolTip = TextManager.Get("spw_wallpropagatingsoundstooltip");
            GUIButton buttonWPS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonWPS.OnClicked = (sender, args) => { config.PropagatingSounds = defaultConfig.PropagatingSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListWPS.Text = JsonSerializer.Serialize(config.PropagatingSounds, jsonOptions); return true; };

            GUITextBlock textBlockPathIS = TextBlock(list, $"{TextManager.Get("spw_pathignoredsounds").Value}{GetServerHashSetString(nameof(config.PathIgnoredSounds))}");
            GUITextBox soundListPathIS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.PathIgnoredSounds, jsonOptions), 0.15f);
            soundListPathIS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.PathIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockPathIS.Text = TextManager.Get("spw_pathignoredsounds").Value; }
                catch (JsonException) { textBlockPathIS.Text = $"{TextManager.Get("spw_pathignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListPathIS.ToolTip = TextManager.Get("spw_pathignoredsoundstooltip");
            GUIButton buttonPathIS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonPathIS.OnClicked = (sender, args) => { config.PathIgnoredSounds = defaultConfig.PathIgnoredSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListPathIS.Text = JsonSerializer.Serialize(config.PathIgnoredSounds, jsonOptions); return true; };

            GUITextBlock textBlockPIS = TextBlock(list, $"{TextManager.Get("spw_pitchignoredsounds").Value}{GetServerHashSetString(nameof(config.PitchIgnoredSounds))}");
            GUITextBox soundListPIS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.PitchIgnoredSounds, jsonOptions), 0.15f);
            soundListPIS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.PitchIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockPIS.Text = TextManager.Get("spw_pitchignoredsounds").Value; }
                catch (JsonException) { textBlockPIS.Text = $"{TextManager.Get("spw_pitchignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListPIS.ToolTip = TextManager.Get("spw_pitchignoredsoundstooltip");
            GUIButton buttonPIS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonPIS.OnClicked = (sender, args) => { config.PitchIgnoredSounds = defaultConfig.PitchIgnoredSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListPIS.Text = JsonSerializer.Serialize(config.PitchIgnoredSounds, jsonOptions); return true; };

            GUITextBlock textBlockLFS = TextBlock(list, $"{TextManager.Get("spw_lowpassforcedsounds").Value}{GetServerHashSetString(nameof(config.LowpassForcedSounds))}");
            GUITextBox soundListLFS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.LowpassForcedSounds, jsonOptions), 0.15f);
            soundListLFS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.LowpassForcedSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockLFS.Text = TextManager.Get("spw_lowpassforcedsounds").Value; }
                catch (JsonException) { textBlockLFS.Text = $"{TextManager.Get("spw_lowpassforcedsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListLFS.ToolTip = TextManager.Get("spw_lowpassforcedsoundstooltip");
            GUIButton buttonLFS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonLFS.OnClicked = (sender, args) => { config.LowpassForcedSounds = defaultConfig.LowpassForcedSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListLFS.Text = JsonSerializer.Serialize(config.LowpassForcedSounds, jsonOptions); return true; };

            GUITextBlock textBlockLIS = TextBlock(list, $"{TextManager.Get("spw_lowpassignoredsounds").Value}{GetServerHashSetString(nameof(config.LowpassIgnoredSounds))}");
            GUITextBox soundListLIS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.LowpassIgnoredSounds, jsonOptions), 0.15f);
            soundListLIS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.LowpassIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockLIS.Text = TextManager.Get("spw_lowpassignoredsounds").Value; }
                catch (JsonException) { textBlockLIS.Text = $"{TextManager.Get("spw_lowpassignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListLIS.ToolTip = TextManager.Get("spw_lowpassignoredsoundstooltip");
            GUIButton buttonLIS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonLIS.OnClicked = (sender, args) => { config.LowpassIgnoredSounds = defaultConfig.LowpassIgnoredSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListLIS.Text = JsonSerializer.Serialize(config.LowpassIgnoredSounds, jsonOptions); return true; };

            GUITextBlock textBlockCIS = TextBlock(list, $"{TextManager.Get("spw_containerignoredsounds").Value}{GetServerHashSetString(nameof(config.ContainerIgnoredSounds))}");
            GUITextBox soundListCIS = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.ContainerIgnoredSounds, jsonOptions), 0.15f);
            soundListCIS.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.ContainerIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockCIS.Text = TextManager.Get("spw_containerignoredsounds").Value; }
                catch (JsonException) { textBlockCIS.Text = $"{TextManager.Get("spw_containerignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListCIS.ToolTip = TextManager.Get("spw_containerignoredsoundstooltip");
            GUIButton buttonCIS = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonCIS.OnClicked = (sender, args) => { config.ContainerIgnoredSounds = defaultConfig.ContainerIgnoredSounds; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListCIS.Text = JsonSerializer.Serialize(config.ContainerIgnoredSounds, jsonOptions); return true; };

            GUITextBlock textBlockBIP = TextBlock(list, $"{TextManager.Get("spw_bubbleignorednames").Value}{GetServerHashSetString(nameof(config.BubbleIgnoredNames))}");
            textBlockBIP.ToolTip = "hello hello";
            GUITextBox soundListBIP = MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.BubbleIgnoredNames, jsonOptions), 0.15f);
            soundListBIP.OnTextChangedDelegate = (textBox, text) =>
            {
                try { config.BubbleIgnoredNames = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>(); ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; textBlockBIP.Text = TextManager.Get("spw_bubbleignorednames").Value; }
                catch (JsonException) { textBlockBIP.Text = $"{TextManager.Get("spw_bubbleignorednames").Value} ({TextManager.Get("spw_invalidinput").Value})"; }
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText); GUIListBox? parentListBox = textBox.Parent?.Parent as GUIListBox; if (parentListBox != null) { parentListBox.RectTransform.NonScaledSize = new Point(parentListBox.RectTransform.NonScaledSize.X, (int)parentListBox.Font.MeasureString(textBox.WrappedText).Y + 30); textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(parentListBox.Content.Rect.Height, (int)textSize.Y + 15)); }
                textBox.SetText(textBox.Text, store: false); return true;
            };
            soundListBIP.ToolTip = TextManager.Get("spw_bubbleignorednamestooltip");
            GUIButton buttonBIP = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
            buttonBIP.OnClicked = (sender, args) => { config.BubbleIgnoredNames = defaultConfig.BubbleIgnoredNames; ConfigManager.SaveConfig(config); ShouldUpdateConfig = true; soundListBIP.Text = JsonSerializer.Serialize(config.BubbleIgnoredNames, jsonOptions); return true; };
        }


        // --- Helper Methods ---

        public static GUIListBox BasicList(GUIFrame parent, Vector2? size = null)
        {
            GUIListBox menuList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.91f), parent.RectTransform, Anchor.Center));
            return menuList;
        }

        public static GUITickBox TickBox(GUIFrame parent, string text, bool? state, Action<bool> onSelected)
        {
            GUITickBox tickBox = new GUITickBox(new RectTransform(new Vector2(1f, 0.2f), parent.RectTransform), text);
            tickBox.Selected = state ?? true;
            tickBox.OnSelected = (sender) => { onSelected(tickBox.State == GUIComponent.ComponentState.Selected); return true; };
            tickBox.RectTransform.RelativeOffset = new Vector2(0.05f, 0);
            return tickBox;
        }

        public static GUIScrollBar Slider(GUIFrame parent, float min, float max, float? value, Action<float> onSelected, float multiple = 0.01f)
        {
            GUIScrollBar scrollBar = new GUIScrollBar(new RectTransform(new Vector2(1f, 0.1f), parent.RectTransform), 0.1f, style: "GUISlider");
            scrollBar.Range = new Vector2(min, max);
            scrollBar.BarScrollValue = value ?? max / 2;
            float startValue = RoundToNearestMultiple(scrollBar.BarScrollValue, multiple);
            scrollBar.OnMoved = (sender, args) => { onSelected(scrollBar.BarScrollValue); return true; };
            scrollBar.RectTransform.RelativeOffset = new Vector2(0.01f, 0);
            return scrollBar;
        }

        public static GUIScrollBar LogSlider(GUIFrame parent, float logMin, float logMax, float? value, Action<float> onSelected, float multiple = 0.01f)
        {
            float logMinValue = (float)Math.Log10(logMin);
            float logMaxValue = (float)Math.Log10(logMax);
            GUIScrollBar scrollBar = new GUIScrollBar(new RectTransform(new Vector2(1f, 0.1f), parent.RectTransform), 0.1f, style: "GUISlider");
            scrollBar.Range = new Vector2(logMinValue, logMaxValue);
            float initialValue = value ?? (float)Math.Pow(10, (logMinValue + logMaxValue) / 2);
            float initialLogValue = (float)Math.Log10(initialValue);
            scrollBar.BarScrollValue = initialLogValue;
            float startValue = initialValue;
            scrollBar.OnMoved = (sender, args) => { float actualValue = (float)Math.Pow(10, scrollBar.BarScrollValue); onSelected(actualValue); return true; };
            scrollBar.RectTransform.RelativeOffset = new Vector2(0.01f, 0);
            return scrollBar;
        }

        // Uses log scaling.
        public static float GetConvertedLogValue(this GUIScrollBar scrollBar)
        {
            if (scrollBar.Range.X != 0) { try { return (float)Math.Pow(10, scrollBar.BarScrollValue); } catch (OverflowException) { return float.MaxValue; } }
            else { return scrollBar.BarScrollValue; }
        }

        public static GUITextBlock TextBlock(GUIListBox list, string text, float x = 1f, float y = 0.05f, float size = 1, Color? color = null)
        {
            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(x, y), list.Content.RectTransform), text, textAlignment: Alignment.Center, wrap: true);
            textBlock.Enabled = false;
            textBlock.OverrideTextColor(textBlock.TextColor);
            textBlock.TextScale = size;
            if (color.HasValue) { textBlock.OverrideTextColor((Color)color); }
            return textBlock;
        }

        public static GUITextBox MultiLineTextBox(RectTransform rectTransform, string text, float? height = null)
        {
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(1, height ?? 1), rectTransform));
            GUITextBox textBox = new GUITextBox(new RectTransform(new Vector2(1, 1), listBox.Content.RectTransform), text, wrap: true, style: "GUITextBoxNoBorder");
            listBox.RectTransform.NonScaledSize = new Point(listBox.RectTransform.NonScaledSize.X, (int)listBox.Font.MeasureString(textBox.WrappedText).Y + 30);
            listBox.ScrollBarEnabled = false;
            string startValue = text;

            Action updateSizes = () => {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                listBox.RectTransform.NonScaledSize = new Point(listBox.RectTransform.NonScaledSize.X, (int)listBox.Font.MeasureString(textBox.WrappedText).Y + 30);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(listBox.Content.Rect.Height, (int)textSize.Y + 15));
                listBox.Content.RectTransform.NonScaledSize = textBox.RectTransform.NonScaledSize;
            };

            textBox.OnTextChangedDelegate = (sender, e) => { textBox.SetText(textBox.Text, store: false); updateSizes(); return true; };
            textBox.OnEnterPressed = (sender, e) => { string str = textBox.Text; int caretIndex = textBox.CaretIndex; textBox.Text = str.Substring(0, caretIndex) + "\n" + str.Substring(caretIndex); textBox.CaretIndex = caretIndex + 1; updateSizes(); return true; };
            updateSizes();
            return textBox;
        }

        public static void UpdateMessageScrollFromCaret(GUITextBox textBox, GUIListBox listBox)
        {
            if (listBox.ScrollBar == null || textBox.Rect.Height <= listBox.Rect.Height) return;
            float caretY = textBox.CaretScreenPos.Y;
            float bottomCaretExtent = textBox.Font.LineHeight * 1.5f;
            float topCaretExtent = -textBox.Font.LineHeight * 0.5f;
            if (caretY + bottomCaretExtent > listBox.Rect.Bottom) { listBox.ScrollBar.BarScroll = Math.Clamp((caretY - textBox.Rect.Top - listBox.Rect.Height + bottomCaretExtent) / (textBox.Rect.Height - listBox.Rect.Height), 0f, 1f); }
            else if (caretY + topCaretExtent < listBox.Rect.Top) { listBox.ScrollBar.BarScroll = Math.Clamp((caretY - textBox.Rect.Top + topCaretExtent) / (textBox.Rect.Height - listBox.Rect.Height), 0f, 1f); }
        }

        public static GUIButton CloseButton(GUIFrame parent)
        {
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), parent.RectTransform, Anchor.BottomRight), TextManager.Get("close").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                if (currentMenuFrame != null)
                {
                    currentMenuFrame.Visible = false;
                }
                ApplyPendingSettings();
                return true;
            };
            return button;
        }

        public static GUIButton RevertAllButton(GUIFrame parent)
        {
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), parent.RectTransform, Anchor.BottomCenter), TextManager.Get("spw_revertall").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                NewLocalConfig = new Config();
                ConfigManager.SaveConfig(NewLocalConfig);
                ShouldUpdateConfig = true; // Flag that changes need applying *when the pause menu closes*

                if (currentMenuFrame != null)
                {
                    GUIListBox? listBox = null;
                    foreach (var child in currentMenuFrame.Children) { if (child is GUIListBox lb) { listBox = lb; break; } }
                    if (listBox != null) { listBox.Content.ClearChildren(); CreateSettingsContentInternal(listBox); }
                    else { LuaCsLogger.LogError("Could not find GUIListBox child in currentMenuFrame during ResetAll."); }
                }
                return true;
            };
            return button;
        }

        public static GUIButton ResetAllButton(GUIFrame parent)
        {
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), parent.RectTransform, Anchor.BottomLeft), TextManager.Get("spw_resetall").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                NewLocalConfig = new Config();
                ConfigManager.SaveConfig(NewLocalConfig);
                ShouldUpdateConfig = true; // Flag that changes need applying *when the pause menu closes*

                if (currentMenuFrame != null)
                {
                    GUIListBox? listBox = null;
                    foreach (var child in currentMenuFrame.Children) { if (child is GUIListBox lb) { listBox = lb; break; } }
                    if (listBox != null) { listBox.Content.ClearChildren(); CreateSettingsContentInternal(listBox); }
                    else { LuaCsLogger.LogError("Could not find GUIListBox child in currentMenuFrame during ResetAll."); }
                }
                return true;
            };
            return button;
        }

        public static List<GUIComponent> GetChildren(GUIComponent comp)
        {
            List<GUIComponent> children = new List<GUIComponent>();
            foreach (var child in comp.GetAllChildren()) { children.Add(child); }
            return children;
        }

        public static string GetEffectProcessingModeName(uint modeId)
        {
            if (modeId == Config.EFFECT_PROCESSING_CLASSIC) return TextManager.Get("spw_vanillafx").Value;
            else if (modeId == Config.EFFECT_PROCESSING_STATIC) return TextManager.Get("spw_staticfx").Value;
            else if (modeId == Config.EFFECT_PROCESSING_DYNAMIC) return TextManager.Get("spw_dynamicfx").Value;
            return "";
        }

        public static string GetServerValueString(string propertyName, string suffix = "", float divideBy = 1)
        {
            Config? config = ConfigManager.ServerConfig; if (config == null) { return string.Empty; }
            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName); if (propertyInfo == null) { return string.Empty; }

            if (propertyName == nameof(Config.EffectProcessingMode))
            {
                string modeName = GetEffectProcessingModeName(config.EffectProcessingMode);
                return $" ({modeName})";
            }

            object? value = propertyInfo.GetValue(config); if (value == null) { return string.Empty; }

            if (value is bool boolValue) { return $" ({TextManager.Get(boolValue ? "spw_enabled" : "spw_disabled").Value})"; }
            else if (value is float floatValue) { float displayValue = floatValue / divideBy; return $" ({displayValue}{suffix})"; }
            else if (value is int intValue && divideBy != 1) { float displayValue = (float)intValue / divideBy; return $" ({displayValue}{suffix})"; }
            else { return $" ({value}{suffix})"; }
        }

        public static string GetServerPercentString(string propertyName)
        {
            Config? config = ConfigManager.ServerConfig; if (config == null) { return string.Empty; }
            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName); if (propertyInfo == null) { return string.Empty; }
            object? value = propertyInfo.GetValue(config); if (value is float floatValue) { return $" ({RoundToNearestMultiple(floatValue * 100, 1)}%)"; }
            return string.Empty;
        }

        public static string GetServerDictString(string propertyName)
        {
            Config? config = ConfigManager.ServerConfig; if (config == null) { return string.Empty; }
            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName); if (propertyInfo == null) { return string.Empty; }
            object? value = propertyInfo.GetValue(config); if (value == null) { return string.Empty; }
            if (value is System.Collections.IDictionary dict)
            {
                var defaultDictValue = defaultConfig.GetType().GetProperty(propertyName)?.GetValue(defaultConfig) as System.Collections.IDictionary;
                if (defaultDictValue != null && AreDictionariesEqual(dict, defaultDictValue)) { return $" ({TextManager.Get("spw_default").Value})"; }
                else { return $" ({TextManager.Get("spw_custom").Value})"; }
            }
            return string.Empty;
        }

        public static bool AreDictionariesEqual(System.Collections.IDictionary dict1, System.Collections.IDictionary dict2)
        {
            if (dict1.Count != dict2.Count) return false;
            foreach (System.Collections.DictionaryEntry kvp in dict1) { if (!dict2.Contains(kvp.Key)) return false; if (!object.Equals(dict2[kvp.Key], kvp.Value)) return false; }
            return true;
        }

        public static string GetServerHashSetString(string propertyName)
        {
            Config? config = ConfigManager.ServerConfig; if (config == null) { return string.Empty; }
            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName); if (propertyInfo == null) { return string.Empty; }
            object? value = propertyInfo.GetValue(config); if (value == null) { return string.Empty; }
            if (value is HashSet<string> hashSet)
            {
                var defaultHashSetValue = defaultConfig.GetType().GetProperty(propertyName)?.GetValue(defaultConfig) as HashSet<string>;
                if (defaultHashSetValue != null && hashSet.SetEquals(defaultHashSetValue)) { return $" {TextManager.Get("spw_default").Value}"; }
                else { return $" {TextManager.Get("spw_custom").Value}"; }
            }
            return string.Empty;
        }

        public static float RoundToNearestMultiple(float num, float input)
        {
            if (input == 0) return num;
            float rounded = MathF.Round(num / input) * input;
            return (float)Math.Round(rounded, 2);
        }
    }
}
