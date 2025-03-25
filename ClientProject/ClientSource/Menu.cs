using Barotrauma.Networking;
using Barotrauma;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    public static class Menu
    {
        static Config defaultConfig = new Config();
        public static JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        public static void LoadMenu()
        {
            EasySettings.AddMenu(TextManager.Get("spw_settings").Value, parent =>
            {
                Config config = EasySettings.NewLocalConfig;

                GUIListBox list = EasySettings.BasicList(parent);
                GUIScrollBar slider;
                GUITickBox tick;

                string default_preset = TextManager.Get("spw_default").Value;
                string vanilla_preset = TextManager.Get("spw_vanilla").Value;
                string slider_text = string.Empty;
                list.Enabled = false; // Disables the hand mouse cursor with no drawbacks (I think)

                if (SoundproofWalls.ServerConfig != null)
                {
                    GUITextBlock alertText = EasySettings.TextBlock(list, TextManager.Get("spw_syncsettingsalert").Value, y: 0.1f, color: Color.Orange);
                }

                // General Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_generalsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Enabled:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.Enabled, state =>
                {
                    config.Enabled = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_enabled").Value}{Menu.GetServerValueString(nameof(config.Enabled))}";
                tick.ToolTip = TextManager.Get("spw_enabledtooltip").Value;

                // Sync Settings (Visible to host only):
                if (Client.ClientList.Count > 0 && GameMain.Client.SessionId == Client.ClientList[0].SessionId)
                {
                    tick = EasySettings.TickBox(list.Content, string.Empty, config.SyncSettings, state =>
                    {
                        config.SyncSettings = state;
                        ConfigManager.SaveConfig(config);
                    });
                    tick.Text = $"{TextManager.Get("spw_syncsettings").Value}{Menu.GetServerValueString(nameof(config.SyncSettings))}";
                    tick.ToolTip = TextManager.Get("spw_syncsettingstooltip").Value;
                }

                // Enable Extended Sounds:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.ExtendedSoundsEnabled, state =>
                {
                    config.ExtendedSoundsEnabled = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_extendedsoundsenabled").Value}{Menu.GetServerValueString(nameof(config.ExtendedSoundsEnabled))}";
                tick.ToolTip = TextManager.Get("spw_extendedsoundsenabledtooltip").Value;


                // Talk While Ragdoll:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.TalkingRagdolls, state =>
                {
                    config.TalkingRagdolls = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_talkingragdolls").Value}{Menu.GetServerValueString(nameof(config.TalkingRagdolls))}";
                tick.ToolTip = TextManager.Get("spw_talkingragdollstooltip").Value;

                // Focus Target Audio:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.FocusTargetAudio, state =>
                {
                    config.FocusTargetAudio = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_focustargetaudio").Value}{Menu.GetServerValueString(nameof(config.FocusTargetAudio))}";
                tick.ToolTip = TextManager.Get("spw_focustargetaudiotooltip").Value;

                // Heavy Obstruction Lowpass Freq:
                // The previous vanilla muffle frequency was 1600. Now it's 600 and can be accessed via SoundPlayer.MuffleFilterFrequency.
                GUITextBlock textBlockHOF = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.LogSlider(list.Content, 10, 1600, (float)config.HeavyLowpassFrequency, value =>
                {
                    value = RoundToNearestMultiple(value, 10);
                    config.HeavyLowpassFrequency = value;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.HeavyLowpassFrequency == defaultConfig.HeavyLowpassFrequency)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.HeavyLowpassFrequency == SoundPlayer.MuffleFilterFrequency)
                    {
                        slider_text = vanilla_preset;
                    }
                    textBlockHOF.Text = $"{TextManager.Get("spw_heavylowpassfrequency").Value}: {value}Hz {slider_text}";
                }, 10);
                textBlockHOF.Text = $"{TextManager.Get("spw_heavylowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedValue(), 10)}Hz{GetServerValueString(nameof(config.HeavyLowpassFrequency), "Hz")}";
                slider.ToolTip = TextManager.Get("spw_heavylowpassfrequencytooltip");

                // Medium Obstruction Lowpass Freq:
                GUITextBlock textBlockMOF = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.LogSlider(list.Content, 10, 3200, (float)config.MediumLowpassFrequency, value =>
                {
                    value = RoundToNearestMultiple(value, 10);
                    config.MediumLowpassFrequency = value;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.MediumLowpassFrequency == defaultConfig.MediumLowpassFrequency)
                    {
                        slider_text = default_preset;
                    }

                    textBlockMOF.Text = $"{TextManager.Get("spw_mediumlowpassfrequency").Value}: {value}Hz {slider_text}";
                }, 10);
                textBlockMOF.Text = $"{TextManager.Get("spw_mediumlowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedValue(), 10)}Hz{GetServerValueString(nameof(config.MediumLowpassFrequency), "Hz")}";
                slider.ToolTip = TextManager.Get("spw_mediumlowpassfrequencytooltip");

                // Light Obstruction Lowpass Freq:
                GUITextBlock textBlockLOF = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.LogSlider(list.Content, 10, 3200, (float)config.LightLowpassFrequency, value =>
                {
                    value = RoundToNearestMultiple(value, 10);
                    config.LightLowpassFrequency = value;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.LightLowpassFrequency == defaultConfig.LightLowpassFrequency)
                    {
                        slider_text = default_preset;
                    }

                    textBlockLOF.Text = $"{TextManager.Get("spw_lightlowpassfrequency").Value}: {value}Hz {slider_text}";
                }, 10);
                textBlockLOF.Text = $"{TextManager.Get("spw_lightlowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedValue(), 10)}Hz{GetServerValueString(nameof(config.LightLowpassFrequency), "Hz")}";
                slider.ToolTip = TextManager.Get("spw_lightlowpassfrequencytooltip");

                // Sound Range:
                GUITextBlock textBlockSR = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 3, config.SoundRangeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.SoundRangeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.SoundRangeMultiplier == defaultConfig.SoundRangeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.SoundRangeMultiplier == 1)
                    {
                        slider_text = vanilla_preset;
                    }
                    textBlockSR.Text = $"{TextManager.Get("spw_soundrange").Value}: {displayValue}% {slider_text}";
                });
                textBlockSR.Text = $"{TextManager.Get("spw_soundrange").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SoundRangeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_soundrangetooltip");

                // Voice Range:
                GUITextBlock textBlockVR = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 3, config.VoiceRangeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.VoiceRangeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.VoiceRangeMultiplier == defaultConfig.VoiceRangeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.VoiceRangeMultiplier == 1)
                    {
                        slider_text = vanilla_preset;
                    }
                    textBlockVR.Text = $"{TextManager.Get("spw_voicerange").Value}: {displayValue}% {slider_text}";
                });
                textBlockVR.Text = $"{TextManager.Get("spw_voicerange").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.VoiceRangeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_voicerangetooltip");

                // Radio Range:
                GUITextBlock textBlockRR = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 3, config.RadioRangeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.RadioRangeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.RadioRangeMultiplier == defaultConfig.RadioRangeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.RadioRangeMultiplier == 1)
                    {
                        slider_text = vanilla_preset;
                    }
                    textBlockRR.Text = $"{TextManager.Get("spw_radiorange").Value}: {displayValue}% {slider_text}";
                });
                textBlockRR.Text = $"{TextManager.Get("spw_radiorange").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.RadioRangeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_radiorangetooltip");

                // Muffled Pitch Strength Multiplier
                GUITextBlock textBlockMSPM = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 1, config.MuffledSoundPitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.MuffledSoundPitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.MuffledSoundPitchMultiplier == defaultConfig.MuffledSoundPitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMSPM.Text = $"{TextManager.Get("spw_muffledsoundpitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockMSPM.Text = $"{TextManager.Get("spw_muffledsoundpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledSoundPitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledsoundpitchtooltip");


                // Muffled Settings:

                // Muffle Diving Suits:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.MuffleDivingSuits, state =>
                {
                    config.MuffleDivingSuits = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_muffledivingsuit").Value}{Menu.GetServerValueString(nameof(config.MuffleDivingSuits))}";
                tick.ToolTip = TextManager.Get("spw_muffledivingsuittooltip").Value;


                // Voice Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_voicesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Voice Lowpass Freq:
                GUITextBlock textBlockVLF = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.LogSlider(list.Content, 10, SoundproofWalls.VANILLA_VOIP_LOWPASS_FREQUENCY * 1.5f, (float)config.VoiceHeavyLowpassFrequency, value =>
                {
                    value = RoundToNearestMultiple(value, 10);

                    // Prevents our BiQuad ctor from recieving a confusing frequency number used by the vanilla game.
                    if (value == SoundPlayer.MuffleFilterFrequency) { value += 10; }

                    config.VoiceHeavyLowpassFrequency = value;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.VoiceHeavyLowpassFrequency == defaultConfig.VoiceHeavyLowpassFrequency)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.VoiceHeavyLowpassFrequency == SoundproofWalls.VANILLA_VOIP_LOWPASS_FREQUENCY)
                    {
                        slider_text = vanilla_preset;
                    }
                    textBlockVLF.Text = $"{TextManager.Get("spw_voicelowpassfrequency").Value}: {value}Hz {slider_text}";
                }, 10);
                textBlockVLF.Text = $"{TextManager.Get("spw_voicelowpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedValue(), 10)}Hz{GetServerValueString(nameof(config.VoiceHeavyLowpassFrequency), "Hz")}";
                slider.ToolTip = TextManager.Get("spw_voicelowpassfrequencytooltip");

                // Radio Bandpass Quality Factor:
                GUITextBlock textBlockRBQ = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.LogSlider(list.Content, 0.5f, 10.0f, (float)config.RadioBandpassQualityFactor, value =>
                {
                    value = RoundToNearestMultiple(value, 0.1f);
                    LuaCsLogger.Log($"{value}");
                    config.RadioBandpassQualityFactor = value;
                    LuaCsLogger.Log($"{config.RadioBandpassQualityFactor}");
                    ConfigManager.SaveConfig(config);
                    slider_text = string.Empty;

                    if (config.RadioBandpassQualityFactor == defaultConfig.RadioBandpassQualityFactor) { slider_text = default_preset; }
                    else if (config.RadioBandpassQualityFactor == 0.7f) { slider_text = vanilla_preset; }

                    textBlockRBQ.Text = $"{TextManager.Get("spw_radiobandpassqualityfactor").Value}: {value} {slider_text}";
                }, 0.1f);
                textBlockRBQ.Text = $"{TextManager.Get("spw_radiobandpassqualityfactor").Value}: {RoundToNearestMultiple(slider.GetConvertedValue(), 0.1f)}{GetServerValueString(nameof(config.RadioBandpassQualityFactor), "")}";
                slider.ToolTip = TextManager.Get("spw_radiobandpassqualityfactortooltip");

                // Radio Bandpass Center Frequency:
                GUITextBlock textBlockRCF = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.LogSlider(list.Content, 300, SoundproofWalls.VANILLA_VOIP_BANDPASS_FREQUENCY * 1.5f, (float)config.RadioBandpassFrequency, value =>
                {
                    value = (float)RoundToNearestMultiple(value, 10);

                    // Prevents our BiQuad ctor from recieving a confusing frequency number used by the vanilla game.
                    if (value == SoundproofWalls.VANILLA_VOIP_BANDPASS_FREQUENCY) { value += 10; }

                    config.RadioBandpassFrequency = value;
                    ConfigManager.SaveConfig(config);
                    slider_text = string.Empty;

                    if (config.RadioBandpassFrequency == defaultConfig.RadioBandpassQualityFactor) { slider_text = default_preset; }
                    else if (config.RadioBandpassFrequency == SoundproofWalls.VANILLA_VOIP_BANDPASS_FREQUENCY) { slider_text = vanilla_preset; }

                    textBlockRCF.Text = $"{TextManager.Get("spw_radiobandpassfrequency").Value}: {value}Hz {slider_text}";
                }, 0.01f);
                textBlockRCF.Text = $"{TextManager.Get("spw_radiobandpassfrequency").Value}: {RoundToNearestMultiple(slider.GetConvertedValue(), 10)}Hz{GetServerValueString(nameof(config.RadioBandpassFrequency), "")}";
                slider.ToolTip = TextManager.Get("spw_radiobandpassfrequencytooltip");


                // Volume Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_volumesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Muffled Voice Volume:
                GUITextBlock textBlockMVV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 3, config.MuffledVoiceVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.MuffledVoiceVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.MuffledVoiceVolumeMultiplier == defaultConfig.MuffledVoiceVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMVV.Text = $"{TextManager.Get("spw_muffledvoicevolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockMVV.Text = $"{TextManager.Get("spw_muffledvoicevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledVoiceVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledvoicevolumetooltip");

                // Muffled Sound Volume:
                GUITextBlock textBlockMSV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 3, config.MuffledSoundVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.MuffledSoundVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.MuffledSoundVolumeMultiplier == defaultConfig.MuffledSoundVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMSV.Text = $"{TextManager.Get("spw_muffledsoundvolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockMSV.Text = $"{TextManager.Get("spw_muffledsoundvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledSoundVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledsoundvolumetooltip");

                // Muffled Component Volume:
                GUITextBlock textBlockMCV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 3, config.MuffledComponentVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.MuffledComponentVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.MuffledComponentVolumeMultiplier == defaultConfig.MuffledComponentVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMCV.Text = $"{TextManager.Get("spw_muffledcomponentvolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockMCV.Text = $"{TextManager.Get("spw_muffledcomponentvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledComponentVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledcomponentvolumetooltip");

                // Unmuffled Component Volume
                GUITextBlock textBlockUCV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 1, config.UnmuffledComponentVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.UnmuffledComponentVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.UnmuffledComponentVolumeMultiplier == defaultConfig.UnmuffledComponentVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockUCV.Text = $"{TextManager.Get("spw_unmuffledcomponentvolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockUCV.Text = $"{TextManager.Get("spw_unmuffledcomponentvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnmuffledComponentVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_unmuffledcomponentvolumetooltip");

                // Submerged Volume Multiplier:
                GUITextBlock textBlockSV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 3, config.SubmergedVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.SubmergedVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.SubmergedVolumeMultiplier == defaultConfig.SubmergedVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockSV.Text = $"{TextManager.Get("spw_submergedvolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockSV.Text = $"{TextManager.Get("spw_submergedvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SubmergedVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_submergedvolumetooltip");


                // Eavesdropping Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_eavesdroppingsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Eavesdropping Sound Volume:
                GUITextBlock textBlockESV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.EavesdroppingSoundVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.EavesdroppingSoundVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.EavesdroppingSoundVolumeMultiplier == defaultConfig.EavesdroppingSoundVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockESV.Text = $"{TextManager.Get("spw_eavesdroppingsoundvolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockESV.Text = $"{TextManager.Get("spw_eavesdroppingsoundvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.EavesdroppingSoundVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_eavesdroppingsoundvolumetooltip");

                // Eavesdropping Voice Volume:
                GUITextBlock textBlockEVV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.EavesdroppingVoiceVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.EavesdroppingVoiceVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.EavesdroppingVoiceVolumeMultiplier == defaultConfig.EavesdroppingVoiceVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockEVV.Text = $"{TextManager.Get("spw_eavesdroppingvoicevolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockEVV.Text = $"{TextManager.Get("spw_eavesdroppingvoicevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.EavesdroppingVoiceVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_eavesdroppingvoicevolumetooltip");

                // Eavesdropping Sound Pitch:
                GUITextBlock textBlockESP = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4f, config.EavesdroppingPitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.EavesdroppingPitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.EavesdroppingPitchMultiplier == defaultConfig.EavesdroppingPitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockESP.Text = $"{TextManager.Get("spw_eavesdroppingpitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockESP.Text = $"{TextManager.Get("spw_eavesdroppingpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.EavesdroppingPitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_eavesdroppingpitchtooltip");

                // Eavesdropping Max Distance:
                GUITextBlock textBlockEMD = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 100, config.EavesdroppingMaxDistance, value =>
                {
                    value = RoundToNearestMultiple(value, 1);
                    config.EavesdroppingMaxDistance = (int)value;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.EavesdroppingMaxDistance == defaultConfig.EavesdroppingMaxDistance)
                    {
                        slider_text = default_preset;
                    }
                    textBlockEMD.Text = $"{TextManager.Get("spw_eavesdroppingmaxdistance").Value}: {value}cm {slider_text}";
                }, 1);
                textBlockEMD.Text = $"{TextManager.Get("spw_eavesdroppingmaxdistance").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 1)}cm{GetServerValueString(nameof(config.EavesdroppingMaxDistance), "cm")}";
                slider.ToolTip = TextManager.Get("spw_eavesdroppingmaxdistancetooltip");


                // Hydrophone Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_hydrophonesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Hydrophone Switch Enabled:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.HydrophoneSwitchEnabled, state =>
                {
                    config.HydrophoneSwitchEnabled = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_hydrophoneswitchenabled").Value}{Menu.GetServerValueString(nameof(config.HydrophoneSwitchEnabled))}";
                tick.ToolTip = TextManager.Get("spw_hydrophoneswitchenabledtooltip").Value;

                // Hydrophone Extra Sound Range:
                GUITextBlock textBlockHSR = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 20000, config.HydrophoneSoundRange, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 100);
                    float displayValue = RoundToNearestMultiple(value / 100, 1);
                    config.HydrophoneSoundRange = (int)realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.HydrophoneSoundRange == defaultConfig.HydrophoneSoundRange)
                    {
                        slider_text = default_preset;
                    }
                    textBlockHSR.Text = $"{TextManager.Get("spw_hydrophonerange").Value}: +{displayValue}m {slider_text}";
                }, 1);
                textBlockHSR.Text = $"{TextManager.Get("spw_hydrophonerange").Value}: +{RoundToNearestMultiple(slider.BarScrollValue / 100, 1)}m{GetServerValueString(nameof(config.HydrophoneSoundRange), "m", 100)}";
                slider.ToolTip = TextManager.Get("spw_hydrophonerangetooltip");

                // Hydrophone Sound Volume:
                GUITextBlock textBlockHSV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.HydrophoneVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.HydrophoneVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.HydrophoneVolumeMultiplier == defaultConfig.HydrophoneVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockHSV.Text = $"{TextManager.Get("spw_hydrophonevolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockHSV.Text = $"{TextManager.Get("spw_hydrophonevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.HydrophoneVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_hydrophonevolumetooltip");

                // Hydrophone Sound Pitch:
                GUITextBlock textBlockHSP = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4f, config.HydrophonePitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.HydrophonePitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.HydrophonePitchMultiplier == defaultConfig.HydrophonePitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockHSP.Text = $"{TextManager.Get("spw_hydrophonepitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockHSP.Text = $"{TextManager.Get("spw_hydrophonepitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.HydrophonePitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_hydrophonepitchtooltip");


                // Ambience Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_ambiencesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Unsubmerged Water Ambience Volume:
                GUITextBlock textBlockUWAV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.UnsubmergedWaterAmbienceVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.UnsubmergedWaterAmbienceVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.UnsubmergedWaterAmbienceVolumeMultiplier == defaultConfig.UnsubmergedWaterAmbienceVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockUWAV.Text = $"{TextManager.Get("spw_unsubmergedwaterambiencevolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockUWAV.Text = $"{TextManager.Get("spw_unsubmergedwaterambiencevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnsubmergedWaterAmbienceVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_unsubmergedwaterambiencevolumetooltip");

                // Submerged Water Ambience Volume:
                GUITextBlock textBlockSWAV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.SubmergedWaterAmbienceVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.SubmergedWaterAmbienceVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.SubmergedWaterAmbienceVolumeMultiplier == defaultConfig.SubmergedWaterAmbienceVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockSWAV.Text = $"{TextManager.Get("spw_submergedwaterambiencevolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockSWAV.Text = $"{TextManager.Get("spw_submergedwaterambiencevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SubmergedWaterAmbienceVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_submergedwaterambiencevolumetooltip");

                // Hydrophone Water Ambience Volume:
                GUITextBlock textBlockWAV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.HydrophoneWaterAmbienceVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.HydrophoneWaterAmbienceVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.HydrophoneWaterAmbienceVolumeMultiplier == defaultConfig.HydrophoneWaterAmbienceVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockWAV.Text = $"{TextManager.Get("spw_hydrophonewaterambiencevolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockWAV.Text = $"{TextManager.Get("spw_hydrophonewaterambiencevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.HydrophoneWaterAmbienceVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_hydrophonewaterambiencevolumetooltip");

                // Water Ambience Transition Speed Multiplier:
                GUITextBlock textBlockWATS = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.5f, 5, config.WaterAmbienceTransitionSpeedMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.WaterAmbienceTransitionSpeedMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.WaterAmbienceTransitionSpeedMultiplier == defaultConfig.WaterAmbienceTransitionSpeedMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.WaterAmbienceTransitionSpeedMultiplier == 1)
                    {
                        slider_text = vanilla_preset;
                    }

                    textBlockWATS.Text = $"{TextManager.Get("spw_waterambiencetransitionspeed").Value}: {displayValue}% {slider_text}";
                });
                textBlockWATS.Text = $"{TextManager.Get("spw_waterambiencetransitionspeed").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.WaterAmbienceTransitionSpeedMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_waterambiencetransitionspeedtooltip");


                // Niche Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_nichesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Muffle Submerged Player:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.MuffleSubmergedPlayer, state =>
                {
                    config.MuffleSubmergedPlayer = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_mufflesubmergedplayer").Value}{Menu.GetServerValueString(nameof(config.MuffleSubmergedPlayer))}";
                tick.ToolTip = TextManager.Get("spw_mufflesubmergedplayertooltip").Value;

                // Muffle Submerged View Target:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.MuffleSubmergedViewTarget, state =>
                {
                    config.MuffleSubmergedViewTarget = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_mufflesubmergedviewtarget").Value}{Menu.GetServerValueString(nameof(config.MuffleSubmergedViewTarget))}";
                tick.ToolTip = TextManager.Get("spw_mufflesubmergedviewtargettooltip").Value;

                // Muffle Submerged Sounds:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.MuffleSubmergedSounds, state =>
                {
                    config.MuffleSubmergedSounds = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_mufflesubmergedsounds").Value}{Menu.GetServerValueString(nameof(config.MuffleSubmergedSounds))}";
                tick.ToolTip = TextManager.Get("spw_mufflesubmergedsoundstooltip").Value;

                // Muffle Flow Sounds:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.MuffleFlowSounds, state =>
                {
                    config.MuffleFlowSounds = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_muffleflowsounds").Value}{Menu.GetServerValueString(nameof(config.MuffleFlowSounds))}";
                tick.ToolTip = TextManager.Get("spw_muffleflowsoundstooltip").Value;

                // Muffle Fire Sounds:
                tick = EasySettings.TickBox(list.Content, string.Empty, config.MuffleFireSounds, state =>
                {
                    config.MuffleFireSounds = state;
                    ConfigManager.SaveConfig(config);
                });
                tick.Text = $"{TextManager.Get("spw_mufflefiresounds").Value}{Menu.GetServerValueString(nameof(config.MuffleFireSounds))}";
                tick.ToolTip = TextManager.Get("spw_mufflefiresoundstooltip").Value;

                // Diving LightObstruction Pitch Multiplier
                GUITextBlock textBlockDPM = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4, config.DivingSuitPitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.DivingSuitPitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.DivingSuitPitchMultiplier == defaultConfig.DivingSuitPitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockDPM.Text = $"{TextManager.Get("spw_divingsuitpitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockDPM.Text = $"{TextManager.Get("spw_divingsuitpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.DivingSuitPitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_divingsuitpitchtooltip");

                // Submerged Pitch Multiplier
                GUITextBlock textBlockSPM = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4, config.SubmergedPitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.SubmergedPitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.SubmergedPitchMultiplier == defaultConfig.SubmergedPitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockSPM.Text = $"{TextManager.Get("spw_submergedpitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockSPM.Text = $"{TextManager.Get("spw_submergedpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.SubmergedPitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_submergedpitchtooltip");

                // Muffled Component Pitch Multiplier
                GUITextBlock textBlockMCPM = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4, config.MuffledComponentPitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.MuffledComponentPitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.MuffledComponentPitchMultiplier == defaultConfig.MuffledComponentPitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMCPM.Text = $"{TextManager.Get("spw_muffledcomponentpitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockMCPM.Text = $"{TextManager.Get("spw_muffledcomponentpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledComponentPitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledcomponentpitchtooltip");

                // Unmuffled Component Pitch Multiplier
                GUITextBlock textBlockUCPM = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4, config.UnmuffledComponentPitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.UnmuffledComponentPitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.UnmuffledComponentPitchMultiplier == defaultConfig.UnmuffledComponentPitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockUCPM.Text = $"{TextManager.Get("spw_unmuffledcomponentpitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockUCPM.Text = $"{TextManager.Get("spw_unmuffledcomponentpitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnmuffledComponentPitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_unmuffledcomponentpitchtooltip");

                // Muffled Voice Pitch Multiplier
                GUITextBlock textBlockMVP = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4, config.MuffledVoicePitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.MuffledVoicePitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.MuffledVoicePitchMultiplier == defaultConfig.MuffledVoicePitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMVP.Text = $"{TextManager.Get("spw_muffledvoicepitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockMVP.Text = $"{TextManager.Get("spw_muffledvoicepitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.MuffledVoicePitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledvoicepitchtooltip");

                // Unmuffled Voice Pitch Multiplier
                GUITextBlock textBlockUVP = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0.25f, 4, config.UnmuffledVoicePitchMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.UnmuffledVoicePitchMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.UnmuffledVoicePitchMultiplier == defaultConfig.UnmuffledVoicePitchMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockUVP.Text = $"{TextManager.Get("spw_unmuffledvoicepitch").Value}: {displayValue}% {slider_text}";
                });
                textBlockUVP.Text = $"{TextManager.Get("spw_unmuffledvoicepitch").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.UnmuffledVoicePitchMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_unmuffledvoicepitchtooltip");


                // Advanced Sound Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_advancedsoundsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Custom sounds:
                GUITextBlock textBlockCS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_customsounds").Value}{GetServerDictString(nameof(config.CustomSounds))}");
                GUITextBox soundListCS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.CustomSounds, jsonOptions));
                soundListCS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.CustomSounds = JsonSerializer.Deserialize<HashSet<CustomSound>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockCS.Text = TextManager.Get("spw_customsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockCS.Text = $"{TextManager.Get("spw_customsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                GUIButton button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.CustomSounds = defaultConfig.CustomSounds;
                    ConfigManager.SaveConfig(config);
                    soundListCS.Text = JsonSerializer.Serialize(config.CustomSounds, jsonOptions);
                    return true;
                };


                // Ignored Sounds:
                GUITextBlock textBlockIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_ignoredsounds").Value}{GetServerHashSetString(nameof(config.IgnoredSounds))}");
                GUITextBox soundListIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.IgnoredSounds, jsonOptions), 0.15f);
                soundListIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.IgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockIS.Text = TextManager.Get("spw_ignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockIS.Text = $"{TextManager.Get("spw_ignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.IgnoredSounds = defaultConfig.IgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListIS.Text = JsonSerializer.Serialize(config.IgnoredSounds, jsonOptions);
                    return true;
                };

                // Water Ignored Sounds:
                GUITextBlock textBlockWIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_waterignoredsounds").Value}{GetServerHashSetString(nameof(config.SurfaceIgnoredSounds))}");
                GUITextBox soundListWIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.SurfaceIgnoredSounds, jsonOptions), 0.15f);
                soundListWIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.SurfaceIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockWIS.Text = TextManager.Get("spw_waterignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockWIS.Text = $"{TextManager.Get("spw_waterignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.SurfaceIgnoredSounds = defaultConfig.SurfaceIgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListWIS.Text = JsonSerializer.Serialize(config.SurfaceIgnoredSounds, jsonOptions);
                    return true;
                };

                // Submersion Ignored Sounds:
                GUITextBlock textBlockSIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_submersionignoredsounds").Value}{GetServerHashSetString(nameof(config.SubmersionIgnoredSounds))}");
                GUITextBox soundListSIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.SubmersionIgnoredSounds, jsonOptions), 0.15f);
                soundListSIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.SubmersionIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockSIS.Text = TextManager.Get("spw_submersionignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockSIS.Text = $"{TextManager.Get("spw_submersionignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.SubmersionIgnoredSounds = defaultConfig.SubmersionIgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListSIS.Text = JsonSerializer.Serialize(config.SubmersionIgnoredSounds, jsonOptions);
                    return true;
                };

                // Wall Propagating Sounds:
                GUITextBlock textBlockWPS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_wallpropagatingsounds").Value}{GetServerHashSetString(nameof(config.WallPropagatingSounds))}");
                GUITextBox soundListWPS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.WallPropagatingSounds, jsonOptions), 0.15f);
                soundListWPS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.WallPropagatingSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockWPS.Text = TextManager.Get("spw_wallpropagatingsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockWPS.Text = $"{TextManager.Get("spw_wallpropagatingsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.WallPropagatingSounds = defaultConfig.WallPropagatingSounds;
                    ConfigManager.SaveConfig(config);
                    soundListWPS.Text = JsonSerializer.Serialize(config.WallPropagatingSounds, jsonOptions);
                    return true;
                };

                // Path Ignored Sounds:
                GUITextBlock textBlockPathIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_pathignoredsounds").Value}{GetServerHashSetString(nameof(config.PathIgnoredSounds))}");
                GUITextBox soundListPathIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.PathIgnoredSounds, jsonOptions), 0.15f);
                soundListPathIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.PathIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockPathIS.Text = TextManager.Get("spw_pathignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockPathIS.Text = $"{TextManager.Get("spw_pathignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.PathIgnoredSounds = defaultConfig.PathIgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListPathIS.Text = JsonSerializer.Serialize(config.PathIgnoredSounds, jsonOptions);
                    return true;
                };

                // Pitch Ignored Sounds:
                GUITextBlock textBlockPIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_pitchignoredsounds").Value}{GetServerHashSetString(nameof(config.PitchIgnoredSounds))}");
                GUITextBox soundListPIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.PitchIgnoredSounds, jsonOptions), 0.15f);
                soundListPIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.PitchIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockPIS.Text = TextManager.Get("spw_pitchignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockPIS.Text = $"{TextManager.Get("spw_pitchignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.PitchIgnoredSounds = defaultConfig.PitchIgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListPIS.Text = JsonSerializer.Serialize(config.PitchIgnoredSounds, jsonOptions);
                    return true;
                };

                // Lowpass Forced Sounds:
                GUITextBlock textBlockLFS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_lowpassforcedsounds").Value}{GetServerHashSetString(nameof(config.LowpassForcedSounds))}");
                GUITextBox soundListLFS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.LowpassForcedSounds, jsonOptions), 0.15f);
                soundListLFS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.LowpassForcedSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockLFS.Text = TextManager.Get("spw_lowpassforcedsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockLFS.Text = $"{TextManager.Get("spw_lowpassforcedsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.LowpassForcedSounds = defaultConfig.LowpassForcedSounds;
                    ConfigManager.SaveConfig(config);
                    soundListLFS.Text = JsonSerializer.Serialize(config.LowpassForcedSounds, jsonOptions);
                    return true;
                };

                // Lowpass Ignored Sounds:
                GUITextBlock textBlockLIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_lowpassignoredsounds").Value}{GetServerHashSetString(nameof(config.LowpassIgnoredSounds))}");
                GUITextBox soundListLIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.LowpassIgnoredSounds, jsonOptions), 0.15f);
                soundListLIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.LowpassIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockLIS.Text = TextManager.Get("spw_lowpassignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockLIS.Text = $"{TextManager.Get("spw_lowpassignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.LowpassIgnoredSounds = defaultConfig.LowpassIgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListLIS.Text = JsonSerializer.Serialize(config.LowpassIgnoredSounds, jsonOptions);
                    return true;
                };

                // Container Ignored Sounds:
                GUITextBlock textBlockCIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_containerignoredsounds").Value}{GetServerHashSetString(nameof(config.ContainerIgnoredSounds))}");
                GUITextBox soundListCIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.ContainerIgnoredSounds, jsonOptions), 0.15f);
                soundListCIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.ContainerIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockCIS.Text = TextManager.Get("spw_containerignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockCIS.Text = $"{TextManager.Get("spw_containerignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.ContainerIgnoredSounds = defaultConfig.ContainerIgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListCIS.Text = JsonSerializer.Serialize(config.ContainerIgnoredSounds, jsonOptions);
                    return true;
                };

                // Bubble Ignored Players:
                GUITextBlock textBlockBIP = EasySettings.TextBlock(list, $"{TextManager.Get("spw_bubbleignorednames").Value}{GetServerHashSetString(nameof(config.BubbleIgnoredNames))}");
                textBlockBIP.ToolTip = "hello hello";
                GUITextBox soundListBIP = EasySettings.MultiLineTextBox(list.Content.RectTransform, JsonSerializer.Serialize(config.BubbleIgnoredNames, jsonOptions), 0.15f);
                soundListBIP.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.BubbleIgnoredNames = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockBIP.Text = TextManager.Get("spw_bubbleignorednames").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockBIP.Text = $"{TextManager.Get("spw_bubbleignorednames").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.BubbleIgnoredNames = defaultConfig.BubbleIgnoredNames;
                    ConfigManager.SaveConfig(config);
                    soundListBIP.Text = JsonSerializer.Serialize(config.BubbleIgnoredNames, jsonOptions);
                    return true;
                };
            });
        }
        public static string GetServerValueString(string propertyName, string suffix = "", float divideBy = 1)
        {
            Config? config = SoundproofWalls.ServerConfig;
            if (config == null) { return string.Empty; }

            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName);
            if (propertyInfo == null) { return string.Empty; }

            object value = propertyInfo.GetValue(config);

            if (value is bool boolValue)
            {
                return $" ({TextManager.Get(boolValue ? "spw_enabled" : "spw_disabled").Value})";
            }
            else if (value is float floatValue)
            {
                return $" ({floatValue / divideBy}{suffix})";
            }
            else
            {
                return $" ({value}{suffix})";
            }
        }

        public static string GetServerPercentString(string propertyName)
        {
            Config? config = SoundproofWalls.ServerConfig;
            if (config == null) { return string.Empty; }

            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName);
            if (propertyInfo == null) { return string.Empty; }

            object value = propertyInfo.GetValue(config);

            return $" ({RoundToNearestMultiple((float)value * 100, 1)}%)";
        }

        public static string GetServerDictString(string propertyName)
        {
            Config? config = SoundproofWalls.ServerConfig;
            if (config == null) { return string.Empty; }

            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName);
            if (propertyInfo == null) { return string.Empty; }

            object? value = propertyInfo.GetValue(config);
            if (value == null) { return string.Empty; }

            if (value is System.Collections.IDictionary dict)
            {
                var defaultDictValue = defaultConfig.GetType().GetProperty(propertyName)?.GetValue(defaultConfig) as System.Collections.IDictionary;

                if (defaultDictValue != null && AreDictionariesEqual(dict, defaultDictValue))
                {
                    return $" {TextManager.Get("spw_default").Value}";
                }
                else
                {
                    return $" {TextManager.Get("spw_custom").Value}";
                }
            }

            return string.Empty;
        }

        public static bool AreDictionariesEqual(System.Collections.IDictionary dict1, System.Collections.IDictionary dict2)
        {
            if (dict1.Count != dict2.Count)
                return false;

            foreach (System.Collections.DictionaryEntry kvp in dict1)
            {
                if (!dict2.Contains(kvp.Key))
                    return false;

                if (!Equals(dict2[kvp.Key], kvp.Value))
                    return false;
            }

            return true;
        }

        public static string GetServerHashSetString(string propertyName)
        {
            Config? config = SoundproofWalls.ServerConfig;
            if (config == null) { return string.Empty; }

            PropertyInfo? propertyInfo = config.GetType().GetProperty(propertyName);
            if (propertyInfo == null) { return string.Empty; }

            object? value = propertyInfo.GetValue(config);
            if (value == null) { return string.Empty; }

            if (value is HashSet<string> hashSet)
            {
                var defaultHashSetValue = defaultConfig.GetType().GetProperty(propertyName)?.GetValue(defaultConfig) as HashSet<string>;

                if (defaultHashSetValue != null && hashSet.SetEquals(defaultHashSetValue))
                {
                    return $" {TextManager.Get("spw_default").Value}";
                }
                else
                {
                    return $" {TextManager.Get("spw_custom").Value}";
                }
            }

            return string.Empty;
        }

        public static float RoundToNearestMultiple(float num, float input)
        {
            float rounded = MathF.Round(num / input) * input;
            return MathF.Round(rounded, 2);
        }
    }
}
