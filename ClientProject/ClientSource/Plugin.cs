using System.Reflection;
using System.Text.Json;
using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using OpenAL;
using Barotrauma.Extensions;

namespace SoundproofWalls
{
    public partial class Plugin : IAssemblyPlugin
    {
        public static Plugin Instance;

        // Pointers for convenience.
        public static Config LocalConfig = ConfigManager.LocalConfig;
        public static Config? ServerConfig = ConfigManager.ServerConfig;
        public static Config Config { get { return ConfigManager.Config; } }

        public static SidechainProcessor Sidechain = new SidechainProcessor();
        public static EfxAudioManager? EffectsManager;

        public enum SoundPath
        {
            EavesdroppingActivation1,
            EavesdroppingActivation2,
            EavesdroppingAmbienceDry,
            EavesdroppingAmbienceWet,

            HydrophoneMovement1,

            BubbleLocal,
            BubbleRadio,
        }
        private static string modPath = Util.GetModDirectory();
        public static readonly Dictionary<SoundPath, string> CustomSoundPaths = new Dictionary<SoundPath, string>
        {
            { SoundPath.EavesdroppingActivation1, Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingActivation1.ogg") },
            { SoundPath.EavesdroppingActivation2, Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingActivation2.ogg") },
            { SoundPath.EavesdroppingAmbienceDry, Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingAmbienceDryRoom.ogg") },
            { SoundPath.EavesdroppingAmbienceWet, Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingAmbienceWetRoom.ogg") },

            { SoundPath.HydrophoneMovement1, "Content/Sounds/Water/SplashLoop.ogg" },

            { SoundPath.BubbleLocal, Path.Combine(modPath, "Content/Sounds/SPW_BubblesLoopMono.ogg") },
            { SoundPath.BubbleRadio, Path.Combine(modPath, "Content/Sounds/SPW_RadioBubblesLoopStereo.ogg") },
        };

        public void InitClient()
        {
            Instance = this;

            // Compatibility with Lua mods that mess with with Sound objects.
            LuaUserData.RegisterType("SoundproofWalls.ExtendedOggSound");
            LuaUserData.RegisterType("SoundproofWalls.ExtendedSoundBuffers");
            LuaUserData.RegisterType("SoundproofWalls.ReducedOggSound");
            LuaUserData.RegisterType("SoundproofWalls.ReducedSoundBuffers");

            GameMain.LuaCs.Hook.Add("think", "spw_clientupdate", (object[] args) =>
            {
                SPW_Update();
                return null;
            });

            GameMain.LuaCs.Game.AddCommand("spw", TextManager.Get("spw_openmenuhelp").Value, args =>
            {
                Menu.ForceOpenMenu();
            });

            // StartRound postfix patch.
            // Needed to set up the first hydrophone switches after terminals have loaded in.
            harmony.Patch(
                typeof(GameSession).GetMethod(nameof(GameSession.StartRound), new Type[] { typeof(LevelData), typeof(bool), typeof(SubmarineInfo), typeof(SubmarineInfo) }),
                null,
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_StartRound))));

            // SoundManager_ReleaseResources postfix.
            // For releasing and clearing the pool of ExtendedOggSound buffers.
            harmony.Patch(
                typeof(SoundManager).GetMethod(nameof(SoundManager.ReleaseResources), BindingFlags.NonPublic | BindingFlags.Instance),
                null,
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundManager_ReleaseResources_Postfix))));

            // LoadSounds 1 prefix REPLACEMENT.
            // Replaces OggSound with ExtendedOggSound.
            harmony.Patch(
                typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(string), typeof(bool) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_LoadCustomOggSound), BindingFlags.Static | BindingFlags.Public, new Type[] { typeof(SoundManager), typeof(string), typeof(bool), typeof(Sound).MakeByRefType() })));

            // LoadSounds 2 prefix REPLACEMENT.
            // Replaces OggSound with ExtendedOggSound.
            harmony.Patch(
                typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(ContentXElement), typeof(bool), typeof(string) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_LoadCustomOggSound), BindingFlags.Static | BindingFlags.Public, new Type[] { typeof(SoundManager), typeof(ContentXElement), typeof(bool), typeof(string), typeof(Sound).MakeByRefType() })));

            // SoundBuffer_RequestAlBuffers prefix.
            // Crash-preventative patch for missing vanilla check.
            harmony.Patch(
                typeof(SoundBuffers).GetMethod(nameof(SoundBuffers.RequestAlBuffers), BindingFlags.Public | BindingFlags.Instance),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundBuffers_RequestAlBuffers_Prefix))));

            // SoundPlayer_PlaySound prefix.
            // Needed to set the new custom range of sounds.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.PlaySound), new Type[] { typeof(Sound), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(Hull), typeof(bool), typeof(bool) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundPlayer_PlaySound))));
            
            // ItemComponent_PlaySound prefix REPLACEMENT.
            // Increase range of component sounds.
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.PlaySound), BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(ActionType), typeof(Character) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_ItemComponent_PlaySound), BindingFlags.Static | BindingFlags.Public, new Type[] { typeof(ItemComponent), typeof(ActionType), typeof(Character) })));

            // ItemComponent_PlaySound prefix REPLACEMENT.
            // Increase range of component sounds.
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.PlaySound), BindingFlags.NonPublic | BindingFlags.Instance, new Type[] { typeof(ItemSound), typeof(Vector2) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_ItemComponent_PlaySound), BindingFlags.Static | BindingFlags.Public, new Type[] { typeof(ItemComponent), typeof(ItemSound), typeof(Vector2) })));

            // SoundPlayer_UpdateMusic prefix REPLACEMENT.
            // Ducks music when sidechaining and removes white noise tracks.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateMusic), BindingFlags.NonPublic | BindingFlags.Static),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundPlayer_UpdateMusic))));

            // SoundPlayer_UpdateWaterFlowSounds postfix.
            // For modifying volume based on eavesdropping fade and sidechaining.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateWaterFlowSounds), BindingFlags.NonPublic | BindingFlags.Static),
                null,
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundPlayer_UpdateWaterFlowSounds))));

            // BiQuad prefix.
            // Used for modifying the muffle frequency of standard OggSounds.
            harmony.Patch(
                typeof(BiQuad).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, new Type[] { typeof(int), typeof(double), typeof(double), typeof(double) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_BiQuad))));

            // SoundChannel ctor postfix REPLACEMENT.
            // Implements the custom ExtendedSoundBuffers for SoundChannels made with ExtendedOggSounds.
            harmony.Patch(
                typeof(SoundChannel).GetConstructor(new Type[] { typeof(Sound), typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(Identifier), typeof(bool) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundChannel_Prefix)))) ;

            // Soundchannel Muffle property prefix REPLACEMENT.
            // Switches between the five (when using ExtendedOggSounds) types of buffers.
            harmony.Patch(
                typeof(SoundChannel).GetProperty(nameof(SoundChannel.Muffled)).GetSetMethod(),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundChannel_SetMuffled_Prefix))));

            // Soundchannel FadeOutAndDispose prefix REPLACEMENT.
            // Removes the vanilla fade out and dispose functionality and just redirects to normal Dipose() to avoid infinite looping bugs.
            harmony.Patch(
                typeof(SoundChannel).GetMethod(nameof(SoundChannel.FadeOutAndDispose)),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundChannel_FadeOutAndDispose))));

            // VoipSound ApplyFilters prefix REPLACEMENT.
            // Assigns muffle filters and processes gain & pitch for voice.
            harmony.Patch(
                typeof(VoipSound).GetMethod(nameof(VoipSound.ApplyFilters), BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(short[]), typeof(int) }),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_VoipSound_ApplyFilters_Prefix))));

            // Client UpdateVoipSound prefix REPLACEMENT.
            // For adding range.
            harmony.Patch(
                typeof(Client).GetMethod(nameof(Client.UpdateVoipSound), BindingFlags.Public | BindingFlags.Instance),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_Client_UpdateVoipSound))));

            // ItemComponent UpdateSounds prefix REPLACEMENT.
            // Updates muffle and other attributes of ItemComponent sounds. Maintainability note: has high contrast with vanilla implementation.
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.UpdateSounds), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_ItemComponent_UpdateSounds))));

            // StatusEffect UpdateAllProjSpecific prefix REPLACEMENT.
            // Updates muffle and other attributes of StatusEffect sounds.
            harmony.Patch(
                typeof(StatusEffect).GetMethod(nameof(StatusEffect.UpdateAllProjSpecific), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_StatusEffect_UpdateAllProjSpecific))));

            // VoipClient SendToServer prefix REPLACEMENT.
            // Plays bubbles on the client's character when they speak underwater.
            // TODO Surely this is better done in an update loop just checking if the client is speaking?
            harmony.Patch(
                typeof(VoipClient).GetMethod(nameof(VoipClient.SendToServer), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_VoipClient_SendToServer))));

            // VoipClient Read prefix REPLACEMENT.
            // Manages the range, muffle flagging, and spectating changes for voice chat. Maintainability note: has VERY high contrast with vanilla implementation.
            harmony.Patch(
                typeof(VoipClient).GetMethod(nameof(VoipClient.Read), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_VoipClient_Read))));

            // UpdateTransform postfix.
            // Essential to the FocusViewTarget setting. Sets SoundManager.ListenerPosition to the position of the viewed target.
            harmony.Patch(
                typeof(Camera).GetMethod(nameof(Camera.UpdateTransform)),
                null,
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_UpdateTransform))));

            // UpdateWaterAmbience prefix REPLACEMENT.
            // Modifies the volume of the water ambience. Maintainability note: a lot of vanilla code being mirrored in this replacement.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateWaterAmbience), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_UpdateWaterAmbience))));


            // UpdateFireSounds prefix REPLACEMENT.
            // Fixes a bug in the vanilla code that caused gain attenuation for large fires to drop off dramatically.
            // Also adds channels to channelInfoMap for modifying volume based on eavesdropping fade and sidechaining.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateFireSounds), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundPlayer_UpdateFireSounds))));

            // Dispose prefix.
            // Auto remove entries in SourceInfoMap, as the keys in this dict are SoundChannels.
            harmony.Patch(
                typeof(SoundChannel).GetMethod(nameof(SoundChannel.Dispose)),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_SoundChannel_Dispose))));

            // MoveCamera postfix.
            // Zooms in slightly when eavesdropping.
            harmony.Patch(
                typeof(Camera).GetMethod(nameof(Camera.MoveCamera)),
                null,
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_MoveCamera))));

            // Draw prefix.
            // Displays the eavesdropping text, eavesdropping vignette, and processing mode tooltip.
            // Bug note: a line in this method causes MonoMod to crash on Linux due to an unmanaged PAL_SEHException https://github.com/dotnet/runtime/issues/78271
            // Bug note update: from my testing this doesn't seem to apply anymore as of June 2025
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.Draw)),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_Draw))));

            // TogglePauseMenu postfix.
            // Displays menu button and updates the config when the menu is closed.
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.TogglePauseMenu)),
                null,
                new HarmonyMethod(typeof(Menu).GetMethod(nameof(Menu.HandlePauseMenuToggle))));

            // ShouldMuffleSounds prefix REPLACEMENT (blank).
            // Just returns true. Workaround for ignoring muffling on sounds with "dontmuffle" in their XML. 
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.ShouldMuffleSound)),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_ShouldMuffleSound))));

            // Clients receiving the admin's config.
            GameMain.LuaCs.Networking.Receive("SPW_UpdateConfigClient", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];

                string data = msg.ReadString();
                bool manualUpdate = false;
                byte configSenderId = 1;
                string newConfig = DataAppender.RemoveData(data, out manualUpdate, out configSenderId);

                Config? newServerConfig = JsonSerializer.Deserialize<Config>(newConfig);

                if (newServerConfig == null)
                {
                    LuaCsLogger.LogError($"[SoundproofWalls] Invalid config from host");
                    return;
                }

                ConfigManager.UpdateConfig(newConfig: newServerConfig, oldConfig: Config, isServerConfigEnabled: true, manualUpdate: manualUpdate, configSenderId: configSenderId);
            });

            // Clients receiving word that the admin has disabled syncing.
            GameMain.LuaCs.Networking.Receive("SPW_DisableConfigClient", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];

                string data = msg.ReadString();
                bool manualUpdate = false;
                byte configSenderId = 1;
                DataAppender.RemoveData(data, out manualUpdate, out configSenderId);
                ConfigManager.UpdateConfig(newConfig: LocalConfig, oldConfig: Config, isServerConfigEnabled: false, manualUpdate: manualUpdate, configSenderId: configSenderId);
            });

            InitDynamicFx();
            ConfigManager.UploadServerConfig();

            HydrophoneManager.Setup();
            EavesdropManager.Setup();
            BubbleManager.Setup();

            // Ensure all sounds have been loaded with the correct muffle buffer.
            if (Config.Enabled)
            { 
                Util.ReloadSounds(starting: true);
                SoundInfoManager.UpdateSoundInfoMap();
            }
        }

        public static void InitDynamicFx()
        {
            if (!Config.Enabled || !Config.DynamicFx) { return; }

            Util.StopPlayingChannels();

            EffectsManager?.Dispose();
            EffectsManager = null;

            if (!AlEffects.Initialize(GameMain.SoundManager.alcDevice))
            {
                DebugConsole.LogError("[SoundproofWalls] Failed to initialize AlEffects!");
                return;
            }

            EffectsManager = new EfxAudioManager();
            if (EffectsManager == null || !EffectsManager.IsInitialized)
            {
                DebugConsole.LogError("[SoundproofWalls] Failed to initialize EffectsManager!");
            }
        }

        public static void DisposeDynamicFx()
        {
            Util.StopPlayingChannels();

            EffectsManager?.Dispose();
            AlEffects.Cleanup();
        }

        public void DisposeClient()
        {
            // Unpatch these earlier to stop custom sound types from being created when reloading sounds
            // (this shouldn't be necessary but there was weird behaviour with the early return that SHOULD handle this inside the LoadSound functions.
            harmony.Unpatch(typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(string), typeof(bool) }), HarmonyPatchType.Prefix);
            harmony.Unpatch(typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(ContentXElement), typeof(bool), typeof(string) }), HarmonyPatchType.Prefix);

            ChannelInfoManager.ResetAllPitchedChannels();
            SoundInfoManager.ClearSoundInfo();

            HydrophoneManager.Dispose();
            EavesdropManager.Dispose();
            BubbleManager.Dispose();

            DisposeDynamicFx();

            // Cleans up any ExtendedOggSounds.
            Util.ReloadSounds(stopping: true);
        }

        public static void SPW_StartRound()
        {
            HydrophoneManager.SetupHydrophoneSwitches(firstStartup: true);
            SoundPathfinder.InitializeGraph(Character.Controlled?.Submarine);
        }

        // This patch might not exist when the base method is called, but it's here in principal to prevent memory leaks.
        public static void SPW_SoundManager_ReleaseResources_Postfix()
        {
            ExtendedSoundBuffers.ClearPool();
        }

        // This patch implements a fix missing in vanilla that can cause a crash otherwise.
        public static bool SPW_SoundBuffers_RequestAlBuffers_Prefix(SoundBuffers __instance)
        {
            if (__instance.AlBuffer != 0 || __instance.AlMuffledBuffer != 0)
            {
                return false; 
            }
            return true;
        }

        public static void SPW_Update()
        {
            if (GameMain.Instance.Paused || !Config.Enabled) return;
            ConfigManager.Update();
            Listener.Update();
            HydrophoneManager.Update();
            EavesdropManager.Update();
            BubbleManager.Update();
            ChannelInfoManager.Update();
            Sidechain.Update();
            EffectsManager?.Update();
        }

        public static bool SPW_LoadCustomOggSound(SoundManager __instance, string filename, bool stream, ref Sound __result)
        {
            bool badFilename = !filename.IsNullOrEmpty() && (Config.StaticFx && Util.StringHasKeyword(filename, SoundInfoManager.IgnoredPrefabs) || Util.StringHasKeyword(filename, CustomSoundPaths.Values.ToHashSet()));
            if (badFilename ||
                !Config.Enabled ||
                Config.ClassicFx ||
                Config.DynamicFx && !Config.RemoveUnusedBuffers)
            { return true; } // Run original method.

            if (__instance.Disabled) { return false; }

            if (!File.Exists(filename))
            {
                throw new System.IO.FileNotFoundException("Sound file \"" + filename + "\" doesn't exist!");
            }

#if DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            if (Config.DynamicFx)
            {
                Sound newSound = new ReducedOggSound(__instance, filename, stream, null);
                lock (__instance.loadedSounds)
                {
                    __instance.loadedSounds.Add(newSound);
                }
                __result = newSound;
            }
            else if (Config.StaticFx)
            {
                Sound newSound = new ExtendedOggSound(__instance, filename, stream, null);
                lock (__instance.loadedSounds)
                {
                    __instance.loadedSounds.Add(newSound);
                }
                __result = newSound;
            }
#if DEBUG
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"Loaded sound \"{filename}\" ({sw.ElapsedMilliseconds} ms).");
#endif

            return false;
        }

        public static bool SPW_LoadCustomOggSound(SoundManager __instance, ContentXElement element, bool stream, string overrideFilePath, ref Sound __result)
        {
            if (!Config.Enabled || Config.ClassicFx || Config.DynamicFx && !Config.RemoveUnusedBuffers) { return true; }
            if (__instance.Disabled) { return false; }

            string filePath = overrideFilePath ?? element.GetAttributeContentPath("file")?.Value ?? "";

            if (!filePath.IsNullOrEmpty() && 
                (Config.StaticFx && Util.StringHasKeyword(filePath, SoundInfoManager.IgnoredPrefabs) ||
                Util.StringHasKeyword(filePath, CustomSoundPaths.Values.ToHashSet())))
            { return true; }

            if (!File.Exists(filePath))
            {
                throw new System.IO.FileNotFoundException($"Sound file \"{filePath}\" doesn't exist! Content package \"{(element.ContentPackage?.Name ?? "Unknown")}\".");
            }

            float range = element.GetAttributeFloat("range", 1000.0f);
            if (Config.DynamicFx)
            {
                var newSound = new ReducedOggSound(__instance, filePath, stream, xElement: element)
                {
                    BaseGain = element.GetAttributeFloat("volume", 1.0f)
                };
                newSound.BaseNear = range * 0.4f;
                newSound.BaseFar = range;
                lock (__instance.loadedSounds)
                {
                    __instance.loadedSounds.Add(newSound);
                }
                __result = newSound;
            }
            else if (Config.StaticFx)
            {
                var newSound = new ExtendedOggSound(__instance, filePath, stream, xElement: element)
                {
                    BaseGain = element.GetAttributeFloat("volume", 1.0f)
                };
                newSound.BaseNear = range * 0.4f;
                newSound.BaseFar = range;
                lock (__instance.loadedSounds)
                {
                    __instance.loadedSounds.Add(newSound);
                }
                __result = newSound;
            }

            return false;
        }

        public static void SPW_MoveCamera(Camera __instance, float deltaTime, bool allowMove = true, bool allowZoom = true, bool allowInput = true, bool? followSub = null)
        {
            if (!Config.Enabled || !Config.EavesdroppingEnabled || !allowZoom) { return; }

            // Sync with the text for simplicity.
            float activationMult = EavesdropManager.EavesdroppingTextAlpha / 255;

            float zoomAmount = __instance.DefaultZoom - ((1 - Config.EavesdroppingZoomMultiplier) * activationMult);

            __instance.globalZoomScale = zoomAmount;
        }

        public static bool SPW_Draw(ref Camera cam, ref SpriteBatch spriteBatch)
        {
            // Processing mode tooltip.
            GUIFrame? frame = Menu.currentMenuFrame;
            if (frame != null && GUIComponent.toolTipBlock != null && GUIComponent.toolTipBlock.Text == TextManager.Get("spw_effectprocessingmodetooltip"))
            {
                float padding = 30;
                RichString menuText = "";
                if (Menu.NewLocalConfig.ClassicFx) { menuText = TextManager.Get("spw_vanillafxtooltip"); }
                else if (Menu.NewLocalConfig.StaticFx) { menuText = TextManager.Get("spw_staticfxtooltip"); }
                else if (Menu.NewLocalConfig.DynamicFx) { menuText = TextManager.Get("spw_dynamicfxtooltip"); }
                GUIComponent.DrawToolTip(spriteBatch, menuText, new Vector2(frame.Rect.X + frame.Rect.Width + padding, frame.Rect.Y));
            }

            Character character = Character.Controlled;
            if (character == null || cam == null) { return true; }

            // Eavesdropping vignette.
            int vignetteOpacity = (int)(EavesdropManager.EavesdroppingTextAlpha * Config.EavesdroppingVignetteOpacityMultiplier);
            EavesdropManager.Vignette.Color = new Color(0, 0, 0, vignetteOpacity);
            EavesdropManager.Vignette.Draw(spriteBatch);

            // Eavesdropping text.
            Limb limb = Util.GetCharacterHead(character);
            Vector2 position = cam.WorldToScreen(limb.body.DrawPosition + new Vector2(0, 40));
            LocalizedString text = TextManager.Get("spw_listening");
            float size = 1.6f;
            Color color = new Color(224, 214, 164, (int)EavesdropManager.EavesdroppingTextAlpha);
            GUIFont font = GUIStyle.Font;
            font.DrawString(spriteBatch, text, position, color, 0, Vector2.Zero,
                cam.Zoom / size, 0, 0.001f, Alignment.Center);

            return true;
        }

        // Workaround for ignoring sounds with "dontmuffle" in their XML. 
        // In the PlaySound() methods, the condition "muffle = !ignoreMuffling && ShouldMuffleSound()" is used to determine muffle.
        // By making one of the two operands constant, we effectively rule it out, making the "muffle" variable a direct reference to "!ignoreMuffling".
        // This is how we can know if a sound is tagged with "dontmuffle" in their XML (SoundChannel.Sound.XElement is not viable due to often being null).
        public static bool SPW_ShouldMuffleSound(ref bool __result)
        {
            if (!Config.Enabled) { return true; }
            __result = true;
            return false;
        }

        public static bool SPW_VoipClient_SendToServer(VoipClient __instance)
        {
            if (!Config.Enabled) { return true; };
            if (GameSettings.CurrentConfig.Audio.VoiceSetting == VoiceMode.Disabled)
            {
                if (VoipCapture.Instance != null)
                {
                    __instance.storedBufferID = VoipCapture.Instance.LatestBufferID;
                    VoipCapture.Instance.Dispose();
                }
                return false; ;
            }
            else
            {
                try
                {
                    if (VoipCapture.Instance == null) { VoipCapture.Create(GameSettings.CurrentConfig.Audio.VoiceCaptureDevice, __instance.storedBufferID); }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"VoipCature.Create failed: {e.Message} {e.StackTrace.CleanupStackTrace()}");
                    var config = GameSettings.CurrentConfig;
                    config.Audio.VoiceSetting = VoiceMode.Disabled;
                    GameSettings.SetCurrentConfig(config);
                }
                if (VoipCapture.Instance == null || VoipCapture.Instance.EnqueuedTotalLength <= 0) { return false; }
            }

            if (DateTime.Now >= __instance.lastSendTime + VoipConfig.SEND_INTERVAL)
            {
                // Additions to original method start here.

                // Create bubble particles around local character.
                Character character = __instance.gameClient.Character;
                if (BubbleManager.ShouldPlayBubbles(character))
                {
                    Limb playerHead = Util.GetCharacterHead(character);
                    Hull limbHull = playerHead.Hull;
                    GameMain.ParticleManager.CreateParticle(
                        "bubbles",
                        playerHead.WorldPosition,
                        velocity: playerHead.LinearVelocity * 10,
                        rotation: 0,
                        limbHull);
                }

                // Additions to original method end here.

                IWriteMessage msg = new WriteOnlyMessage();

                msg.WriteByte((byte)ClientPacketHeader.VOICE);
                msg.WriteByte((byte)VoipCapture.Instance.QueueID);
                VoipCapture.Instance.Write(msg);

                __instance.netClient.Send(msg, DeliveryMethod.Unreliable);

                __instance.lastSendTime = DateTime.Now;
            }

            return false;
        }

        public static bool SPW_VoipClient_Read(VoipClient __instance, ref IReadMessage msg)
        {
            if (!Config.Enabled) { return true; }
            
            VoipClient instance = __instance;
            byte queueId = msg.ReadByte();
            float distanceFactor = msg.ReadRangedSingle(0.0f, 1.0f, 8);
            VoipQueue queue = instance.queues.Find(q => q.QueueID == queueId);
            
            if (queue == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Couldn't find VoipQueue with id " + queueId.ToString() + "!", GUIStyle.Red);
#endif
                return false;
            }

            Client client = instance.gameClient.ConnectedClients.Find(c => c.VoipQueue == queue);
            bool clientAlive = client.Character != null && !client.Character.IsDead && !client.Character.Removed;
            bool clientCantSpeak = client.Muted || client.MutedLocally || (clientAlive && client.Character.SpeechImpediment >= 100.0f);

            if (clientCantSpeak || !queue.Read(msg, discardData: clientCantSpeak))
            {
                return false;
            }

            if (client.VoipSound == null)
            {
                DebugConsole.Log("Recreating voipsound " + queueId);
                client.VoipSound = new VoipSound(client, GameMain.SoundManager, client.VoipQueue);
            }

            GameMain.SoundManager.ForceStreamUpdate();
            GameMain.NetLobbyScreen?.SetPlayerSpeaking(client);
            GameMain.GameSession?.CrewManager?.SetClientSpeaking(client);
            client.RadioNoise = 0.0f;

            // Attenuate other sounds when players speak.
            if ((client.VoipSound.CurrentAmplitude * client.VoipSound.Gain * GameMain.SoundManager.GetCategoryGainMultiplier("voip")) > 0.1f)
            {
                if (clientAlive)
                {
                    Vector3 clientPos = new Vector3(client.Character.WorldPosition, 0.0f);
                    Vector3 listenerPos = GameMain.SoundManager.ListenerPosition;
                    float attenuationDist = client.VoipSound.Near * 1.125f;
                    if (Vector3.DistanceSquared(clientPos, listenerPos) < attenuationDist * attenuationDist)
                    {
                        GameMain.SoundManager.VoipAttenuatedGain = 0.5f;
                    }
                }
                else
                {
                    GameMain.SoundManager.VoipAttenuatedGain = 0.5f;
                }
            }

            if (!clientAlive) // Stop here if the speaker is spectating or in lobby.
            {
                client.VoipSound.UseMuffleFilter = false;
                return false; 
            }

            bool spectating = Character.Controlled == null;
            WifiComponent senderRadio = null;

            var messageType = ChatMessageType.Default;
            if (!spectating)
            {
                messageType =
                    !client.VoipQueue.ForceLocal &&
                    ChatMessage.CanUseRadio(client.Character, out senderRadio) &&
                    ChatMessage.CanUseRadio(Character.Controlled, out var recipientRadio) &&
                    senderRadio.CanReceive(recipientRadio) ?
                        ChatMessageType.Radio : ChatMessageType.Default;
            }
            else
            {
                messageType =
                    !client.VoipQueue.ForceLocal &&
                    ChatMessage.CanUseRadio(client.Character, out senderRadio) ?
                        ChatMessageType.Radio : ChatMessageType.Default;
            }

            client.Character.ShowTextlessSpeechBubble(1.25f, ChatMessage.MessageColor[(int)messageType]);

            // Range.
            float localRangeMultiplier = 1 * Config.VoiceRangeMultiplier;
            float radioRangeMultiplier = 1 * Config.RadioRangeMultiplier;
            float speechImpedimentMultiplier = 1.0f - client.Character.SpeechImpediment / 100.0f;
            if (messageType == ChatMessageType.Radio)
            {
                client.VoipSound.UsingRadio = true;
                client.VoipSound.SetRange(senderRadio.Range * VoipClient.RangeNear * speechImpedimentMultiplier * radioRangeMultiplier, senderRadio.Range * speechImpedimentMultiplier * radioRangeMultiplier);
                if (distanceFactor > VoipClient.RangeNear && !spectating)
                {
                    //noise starts increasing exponentially after 40% range
                    client.RadioNoise = MathF.Pow(MathUtils.InverseLerp(VoipClient.RangeNear, 1.0f, distanceFactor), 2);
                }
            }
            else
            {
                float rangeFar = ChatMessage.SpeakRangeVOIP * Config.VoiceRangeMultiplier;
                if (Listener.IsUsingHydrophones) { rangeFar += Config.HydrophoneSoundRange; }
                client.VoipSound.UsingRadio = false;
                client.VoipSound.SetRange(rangeFar * VoipClient.RangeNear * speechImpedimentMultiplier * localRangeMultiplier, rangeFar * speechImpedimentMultiplier * localRangeMultiplier);
            }

            // Sound Info stuff.
            SoundChannel channel = client.VoipSound.soundChannel;
            Hull? clientHull = client.Character.CurrentHull;
            //CrossThread.RequestExecutionOnMainThread(() => ChannelInfoManager.EnsureUpdateVoiceInfo(Channel, clientHull, speakingClient: client, messageType: messageType));
            client.VoipSound.UseMuffleFilter = true; // default to muffled to stop pops.
            ChannelInfoManager.EnsureUpdateVoiceInfo(channel, clientHull, speakingClient: client, messageType: messageType);
            return false;
        }

        public static bool SPW_Client_UpdateVoipSound(Client __instance)
        {
            Client instance = __instance;
            if (instance.VoipSound == null || !instance.VoipSound.IsPlaying)
            {
                instance.radioNoiseChannel?.Dispose();
                instance.radioNoiseChannel = null;
                if (instance.VoipSound != null)
                {
                    DebugConsole.Log("Destroying voipsound");
                    instance.VoipSound.Dispose();
                }
                instance.VoipSound = null;
                return false;
            }

            if (Screen.Selected is ModDownloadScreen)
            {
                instance.VoipSound.Gain = 0.0f;
            }

            float rangeFar = instance.VoipSound.Far * Config.VoiceRangeMultiplier;
            float rangeNear = instance.VoipSound.Near * Config.VoiceRangeMultiplier;
            float maxAudibleRange = ChatMessage.SpeakRangeVOIP * Config.VoiceRangeMultiplier;
            if (Listener.IsUsingHydrophones)
            {
                rangeFar += Config.HydrophoneSoundRange;
                rangeNear += Config.HydrophoneSoundRange;
                maxAudibleRange += Config.HydrophoneSoundRange;
            }

            float gain = 1.0f;
            float noiseGain = 0.0f;
            Vector3? position = null;
            if (instance.character != null && !instance.character.IsDead)
            {
                if (GameSettings.CurrentConfig.Audio.UseDirectionalVoiceChat)
                {
                    position = new Vector3(instance.character.WorldPosition.X, instance.character.WorldPosition.Y, 0.0f);
                }
                else
                {
                    float dist = Vector3.Distance(new Vector3(instance.character.WorldPosition, 0.0f), GameMain.SoundManager.ListenerPosition);
                    gain = 1.0f - MathUtils.InverseLerp(rangeNear, rangeFar, dist);
                }
                if (!instance.VoipSound.UsingRadio)
                {
                    //emulate the "garbling" of the text chat
                    //this in a sense means the volume diminishes exponentially when close to the maximum range of the sound
                    //(diminished by both the garbling and the distance attenuation)

                    //which is good, because we want the voice chat to become unintelligible close to the max range,
                    //and we need to heavily reduce the volume to do that (otherwise it's just quiet, but still intelligible)
                    float garbleAmount = ChatMessage.GetGarbleAmount(Character.Controlled, instance.character, maxAudibleRange);
                    gain *= 1.0f - garbleAmount;
                }
                if (instance.RadioNoise > 0.0f)
                {
                    noiseGain = gain * instance.RadioNoise;
                    gain *= 1.0f - instance.RadioNoise;
                }
            }
            instance.VoipSound.SetPosition(position);
            instance.VoipSound.Gain = gain;
            if (noiseGain > 0.0f)
            {
                if (instance.radioNoiseChannel == null || !instance.radioNoiseChannel.IsPlaying)
                {
                    instance.radioNoiseChannel = SoundPlayer.PlaySound("radiostatic");
                    instance.radioNoiseChannel.Category = SoundManager.SoundCategoryVoip;
                    instance.radioNoiseChannel.Looping = true;
                }
                instance.radioNoiseChannel.Near = rangeNear;
                instance.radioNoiseChannel.Far = rangeFar;
                instance.radioNoiseChannel.Position = position;
                instance.radioNoiseChannel.Gain = noiseGain;
            }
            else if (instance.radioNoiseChannel != null)
            {
                instance.radioNoiseChannel.Gain = 0.0f;
            }

            return false;
        }

        private static BiQuad voipLightMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.LightLowpassFrequency);
        private static BiQuad voipMediumMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.MediumLowpassFrequency);
        private static BiQuad voipHeavyMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.VoiceHeavyLowpassFrequency);
        private static RadioFilter voipCustomRadioFilter = new RadioFilter(VoipConfig.FREQUENCY, Config.RadioBandpassFrequency, Config.RadioBandpassQualityFactor, Config.RadioDistortion, Config.RadioStatic, Config.RadioCompressionThreshold, Config.RadioCompressionRatio);
        private static BiQuad voipVanillaRadioFilter = new BandpassFilter(VoipConfig.FREQUENCY, ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY);
        public static bool SPW_VoipSound_ApplyFilters_Prefix(VoipSound __instance, ref short[] buffer, ref int readSamples)
        {
            VoipSound voipSound = __instance;
            Client client = voipSound.client;

            if (!Config.Enabled || 
                voipSound == null || 
                !voipSound.IsPlaying ||
                client == null ||
                client.Character == null) 
            { return true; }

            SoundChannel channel = voipSound.soundChannel;
            Hull? clientHull = client.Character.CurrentHull;
            ChannelInfo soundInfo = ChannelInfoManager.EnsureUpdateChannelInfo(channel, soundHull: clientHull, speakingClient: client);

            // Update muffle filters if needed.
            if (voipSound.UseMuffleFilter)
            {
                if (voipHeavyMuffleFilter._frequency != Config.VoiceHeavyLowpassFrequency)
                {
                    voipHeavyMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.VoiceHeavyLowpassFrequency);
                }
                if (voipLightMuffleFilter._frequency != Config.LightLowpassFrequency)
                {
                    voipLightMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.LightLowpassFrequency);
                }
                if (voipMediumMuffleFilter._frequency != Config.MediumLowpassFrequency)
                {
                    voipMediumMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.MediumLowpassFrequency);
                }
            }

            // Select the muffle filter to use.
            BiQuad muffleFilter = voipHeavyMuffleFilter;
            if (soundInfo.LightMuffle) muffleFilter = voipLightMuffleFilter;
            else if (soundInfo.MediumMuffle)muffleFilter = voipMediumMuffleFilter;

            // Update custom radio filter if any settings for it have been changed.
            if (voipSound.UseRadioFilter &&
                Config.RadioCustomFilterEnabled &&
                (voipCustomRadioFilter.frequency != Config.RadioBandpassFrequency ||
                voipCustomRadioFilter.q != Config.RadioBandpassQualityFactor ||
                voipCustomRadioFilter.distortionAmount != Config.RadioDistortion ||
                voipCustomRadioFilter.staticAmount != Config.RadioStatic ||
                voipCustomRadioFilter.compressionThreshold != Config.RadioCompressionThreshold ||
                voipCustomRadioFilter.compressionRatio != Config.RadioCompressionRatio))
            {
                voipCustomRadioFilter = new RadioFilter(VoipConfig.FREQUENCY, Config.RadioBandpassFrequency, Config.RadioBandpassQualityFactor, Config.RadioDistortion, Config.RadioStatic, Config.RadioCompressionThreshold, Config.RadioCompressionRatio);
            }

            // Vanilla method & changes.

            // Sets voipSound.gain to the raw volume without voice config settings applied (seems to be what it's for in vanilla)
            // and updates the soundchannel gain with the voice config settings applied.
            voipSound.Gain = soundInfo.Gain;

            for (int i = 0; i < readSamples; i++)
            {
                float fVal = ToolBox.ShortAudioSampleToFloat(buffer[i]);

                if (voipSound.UseMuffleFilter && !Config.DynamicFx) // DynamicFx processes muffle differently.
                {
                    fVal = muffleFilter.Process(fVal);
                }
                if (voipSound.UseRadioFilter)
                {
                    if (Config.RadioCustomFilterEnabled)
                    {
                        fVal = Math.Clamp(voipCustomRadioFilter.Process(fVal), -1f, 1f);
                    }
                    else
                    {
                        fVal = Math.Clamp(voipVanillaRadioFilter.Process(fVal) * VoipSound.PostRadioFilterBoost, -1f, 1f);
                    }
                }
                buffer[i] = ToolBox.FloatToShortAudioSample(fVal);
            }

            return false;
        }

        // Runs at the start of the SoundChannel disposing method.
        public static void SPW_SoundChannel_Dispose(SoundChannel __instance)
        {
            if (!Config.Enabled) { return; };

            SoundChannel channel = __instance;

            uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
            EffectsManager?.UnregisterSource(sourceId);

            channel.Looping = false; // This isn't actually needed, it is more of a superstitious line of code...

            ChannelInfoManager.RemoveChannelInfo(channel);
            ChannelInfoManager.RemovePitchedChannel(channel);
            HydrophoneManager.RemoveHydrophoneChannel(channel);
            BubbleManager.RemoveBubbleSound(channel);
        }

        public static void SPW_SoundPlayer_PlaySound(ref Sound sound, ref float? range, ref Vector2 position, ref Hull hullGuess)
        {
            if (!Config.Enabled || !Util.RoundStarted || sound == null) { return; }

            range = range ?? sound.BaseFar;
            float rangeMult = Config.SoundRangeMultiplierMaster * SoundInfoManager.EnsureGetSoundInfo(sound).RangeMult;
            range *= rangeMult;

            if (Listener.IsUsingHydrophones)
            {
                Hull targetHull = Hull.FindHull(position, hullGuess, true);
                if (targetHull == null || targetHull.Submarine != Character.Controlled?.Submarine)
                {
                    range += Config.HydrophoneSoundRange;
                }
            }
            return;
        }

        public static bool SPW_ItemComponent_PlaySound(ItemComponent __instance, ItemSound itemSound, Vector2 position)
        {
            if (!Config.Enabled) { return true; }

            Sound? sound = itemSound.RoundSound?.Sound;
            float individualRangeMult = 1;
            if (sound != null) { individualRangeMult = SoundInfoManager.EnsureGetSoundInfo(sound).RangeMult; }
            
            float range = itemSound.Range;
            float rangeMult = Config.LoopingSoundRangeMultiplierMaster * individualRangeMult;
            range *= rangeMult;

            if (Listener.IsUsingHydrophones)
            {
                Hull soundHull = Hull.FindHull(position, Character.Controlled?.CurrentHull, true);
                if (soundHull == null || soundHull.Submarine != Character.Controlled?.Submarine)
                {
                    range += Config.HydrophoneSoundRange;
                }
            }

            if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), position) > range * range)
            {
                return false;
            }

            if (itemSound.OnlyPlayInSameSub && __instance.item.Submarine != null && Character.Controlled != null)
            {
                if (Character.Controlled.Submarine == null || !Character.Controlled.Submarine.IsEntityFoundOnThisSub(__instance.item, includingConnectedSubs: true)) { return false; }
            }

            if (itemSound.Loop)
            {
                if (__instance.loopingSoundChannel != null && __instance.loopingSoundChannel.Sound != itemSound.RoundSound.Sound)
                {
                    __instance.loopingSoundChannel.FadeOutAndDispose(); __instance.loopingSoundChannel = null;
                }
                if (__instance.loopingSoundChannel == null || !__instance.loopingSoundChannel.IsPlaying)
                {
                    float volume = __instance.GetSoundVolume(itemSound);
                    if (volume <= 0.0001f) { return false; }
                    __instance.loopingSound = itemSound;
                    __instance.loopingSoundChannel = SoundPlayer.PlaySound(__instance.loopingSound.RoundSound, position, volume: 0.01f, hullGuess: __instance.item.CurrentHull);
                    if (__instance.loopingSoundChannel != null)
                    {
                        __instance.loopingSoundChannel.Looping = true;
                        __instance.loopingSoundChannel.Near = range * Config.LoopingComponentSoundNearMultiplier;
                        __instance.loopingSoundChannel.Far = range;
                    }
                }
            }
            else
            {
                float volume = __instance.GetSoundVolume(itemSound);
                if (volume <= 0.0001f) { return false; }
                var channel = SoundPlayer.PlaySound(itemSound.RoundSound, position, volume, hullGuess: __instance.item.CurrentHull);
                if (channel != null) { __instance.playingOneshotSoundChannels.Add(channel); }
            }

            return false;
        }

        public static bool SPW_ItemComponent_PlaySound(ItemComponent __instance, ActionType type, Character user)
        {
            if (!Config.Enabled) { return true; }

            if (!__instance.hasSoundsOfType[(int)type]) { return false; }
            if (GameMain.Client?.MidRoundSyncing ?? false) { return false; }

            //above the top boundary of the level (in an inactive respawn shuttle?)
            if (__instance.item.Submarine != null && __instance.item.Submarine.IsAboveLevel)
            {
                return false;
            }

            if (__instance.loopingSound != null)
            {
                Sound? sound = __instance.loopingSound.RoundSound?.Sound;
                float individualRangeMult = 1;
                if (sound != null) { individualRangeMult = SoundInfoManager.EnsureGetSoundInfo(sound).RangeMult; }

                float range = __instance.loopingSound.Range;
                float rangeMult = Config.LoopingSoundRangeMultiplierMaster * individualRangeMult;
                range *= rangeMult;

                if (Listener.IsUsingHydrophones)
                {
                    Hull soundHull = Hull.FindHull(__instance.item.WorldPosition, __instance.item.CurrentHull, true);
                    if (soundHull == null || soundHull.Submarine != Character.Controlled?.Submarine)
                    {
                        range += Config.HydrophoneSoundRange;
                    }
                }

                if (Vector3.DistanceSquared(GameMain.SoundManager.ListenerPosition, new Vector3(__instance.item.WorldPosition, 0.0f)) > range * range ||
                    (__instance.GetSoundVolume(__instance.loopingSound)) <= 0.0001f)
                {
                    if (__instance.loopingSoundChannel != null)
                    {
                        __instance.loopingSoundChannel.FadeOutAndDispose();
                        __instance.loopingSoundChannel = null;
                        __instance.loopingSound = null;
                    }
                    return false;
                }

                if (__instance.loopingSoundChannel != null && __instance.loopingSoundChannel.Sound != __instance.loopingSound.RoundSound.Sound)
                {
                    __instance.loopingSoundChannel.FadeOutAndDispose();
                    __instance.loopingSoundChannel = null;
                    __instance.loopingSound = null;
                }

                if (__instance.loopingSoundChannel == null || !__instance.loopingSoundChannel.IsPlaying)
                {
                    __instance.loopingSoundChannel = __instance.loopingSound.RoundSound.Sound.Play(
                        new Vector3(__instance.item.WorldPosition, 0.0f),
                        0.01f,
                        __instance.loopingSound.RoundSound.GetRandomFrequencyMultiplier(),
                        SoundPlayer.ShouldMuffleSound(Character.Controlled, __instance.item.WorldPosition, __instance.loopingSound.Range, Character.Controlled?.CurrentHull));
                    if (__instance.loopingSoundChannel != null)
                    {
                        __instance.loopingSoundChannel.Looping = true;
                        __instance.item.CheckNeedsSoundUpdate(__instance);
                        __instance.loopingSoundChannel.Near = range * Config.LoopingComponentSoundNearMultiplier;
                        __instance.loopingSoundChannel.Far = range;
                    }
                }

                // Looping sound with manual selection mode should be changed if value of ManuallySelectedSound has changed
                // Otherwise the sound won't change until the sound condition (such as being active) is disabled and re-enabled
                if (__instance.loopingSoundChannel != null && __instance.loopingSoundChannel.IsPlaying && __instance.soundSelectionModes[type] == SoundSelectionMode.Manual)
                {
                    var playingIndex = __instance.sounds[type].IndexOf(__instance.loopingSound);
                    var shouldBePlayingIndex = Math.Clamp(__instance.ManuallySelectedSound, 0, __instance.sounds[type].Count);
                    if (playingIndex != shouldBePlayingIndex)
                    {
                        __instance.loopingSoundChannel.FadeOutAndDispose();
                        __instance.loopingSoundChannel = null;
                        __instance.loopingSound = null;
                    }
                }
                return false;
            }

            var matchingSounds = __instance.sounds[type];
            if (__instance.loopingSoundChannel == null || !__instance.loopingSoundChannel.IsPlaying)
            {
                SoundSelectionMode soundSelectionMode = __instance.soundSelectionModes[type];
                int index;
                if (soundSelectionMode == SoundSelectionMode.CharacterSpecific && user != null)
                {
                    index = user.ID % matchingSounds.Count;
                }
                else if (soundSelectionMode == SoundSelectionMode.ItemSpecific)
                {
                    index = __instance.item.ID % matchingSounds.Count;
                }
                else if (soundSelectionMode == SoundSelectionMode.All)
                {
                    foreach (ItemSound sound in matchingSounds)
                    {
                        __instance.PlaySound(sound, __instance.item.WorldPosition);
                    }
                    return false;
                }
                else if (soundSelectionMode == SoundSelectionMode.Manual)
                {
                    index = Math.Clamp(__instance.ManuallySelectedSound, 0, matchingSounds.Count - 1);
                }
                else
                {
                    index = Rand.Int(matchingSounds.Count);
                }

                __instance.PlaySound(matchingSounds[index], __instance.item.WorldPosition);
                __instance.item.CheckNeedsSoundUpdate(__instance);
            }
            return false;
        }

            // This method replacement is unfortunately needed to remove the terrible white noise biome loops and duck music for loud sounds.
            public static bool SPW_SoundPlayer_UpdateMusic(float deltaTime)
        {
            if (!Config.Enabled) { return true; }

            bool sidechaining = Config.SidechainingEnabled && Config.SidechainingDucksMusic;

            if (SoundPlayer.musicClips == null || (GameMain.SoundManager?.Disabled ?? true)) { return false; }

            if (SoundPlayer.OverrideMusicType != null && SoundPlayer.OverrideMusicDuration.HasValue)
            {
                SoundPlayer.OverrideMusicDuration -= deltaTime;
                if (SoundPlayer.OverrideMusicDuration <= 0.0f)
                {
                    SoundPlayer.OverrideMusicType = Identifier.Empty;
                    SoundPlayer.OverrideMusicDuration = null;
                }
            }

            int noiseLoopIndex = 1;

            SoundPlayer.updateMusicTimer -= deltaTime;
            if (SoundPlayer.updateMusicTimer <= 0.0f)
            {
                //find appropriate music for the current situation
                Identifier currentMusicType = SoundPlayer.GetCurrentMusicType();
                float currentIntensity = GameMain.GameSession?.EventManager != null ?
                    GameMain.GameSession.EventManager.MusicIntensity * 100.0f : 0.0f;

                IEnumerable<BackgroundMusic> suitableMusic = SoundPlayer.GetSuitableMusicClips(currentMusicType, currentIntensity);
                int mainTrackIndex = 0;
                if (suitableMusic.None())
                {
                    SoundPlayer.targetMusic[mainTrackIndex] = null;
                }
                //switch the music if nothing playing atm or the currently playing clip is not suitable anymore
                else if (SoundPlayer.targetMusic[mainTrackIndex] == null || SoundPlayer.currentMusic[mainTrackIndex] == null || !SoundPlayer.currentMusic[mainTrackIndex].IsPlaying() || !suitableMusic.Any(m => m == SoundPlayer.currentMusic[mainTrackIndex]))
                {
                    if (currentMusicType == "default")
                    {
                        if (SoundPlayer.previousDefaultMusic == null)
                        {
                            SoundPlayer.targetMusic[mainTrackIndex] = SoundPlayer.previousDefaultMusic = suitableMusic.GetRandomUnsynced();
                        }
                        else
                        {
                            SoundPlayer.targetMusic[mainTrackIndex] = SoundPlayer.previousDefaultMusic;
                        }
                    }
                    else
                    {
                        SoundPlayer.targetMusic[mainTrackIndex] = suitableMusic.GetRandomUnsynced();
                    }
                }

                if (Level.Loaded != null && (Level.Loaded.Type == LevelData.LevelType.LocationConnection || Level.Loaded.GenerationParams.PlayNoiseLoopInOutpostLevel))
                {
                    Identifier biome = Level.Loaded.LevelData.Biome.Identifier;
                    if (Level.Loaded.IsEndBiome && GameMain.GameSession?.Campaign is CampaignMode campaign)
                    {
                        //don't play end biome music in the path leading up to the end level(s)
                        if (!campaign.Map.EndLocations.Contains(Level.Loaded.StartLocation))
                        {
                            biome = Level.Loaded.StartLocation.Biome.Identifier;
                        }
                    }

                    // Find background noise loop for the current biome
                    // SPW - Disable white noise here.
                    IEnumerable<BackgroundMusic> suitableNoiseLoops = (Screen.Selected == GameMain.GameScreen && !Config.DisableWhiteNoise) ?
                        SoundPlayer.GetSuitableMusicClips(biome, currentIntensity) :
                        Enumerable.Empty<BackgroundMusic>();

                    if (suitableNoiseLoops.Count() == 0)
                    {
                        SoundPlayer.targetMusic[noiseLoopIndex] = null;
                    }
                    // Switch the noise loop if nothing playing atm or the currently playing clip is not suitable anymore
                    else if (SoundPlayer.targetMusic[noiseLoopIndex] == null || SoundPlayer.currentMusic[noiseLoopIndex] == null || !suitableNoiseLoops.Any(m => m == SoundPlayer.currentMusic[noiseLoopIndex]))
                    {
                        SoundPlayer.targetMusic[noiseLoopIndex] = suitableNoiseLoops.GetRandomUnsynced();
                    }
                }
                else
                {
                    SoundPlayer.targetMusic[noiseLoopIndex] = null;
                }

                IEnumerable<BackgroundMusic> suitableTypeAmbiences = SoundPlayer.GetSuitableMusicClips($"{currentMusicType}ambience".ToIdentifier(), currentIntensity);
                int typeAmbienceTrackIndex = 2;
                if (suitableTypeAmbiences.None())
                {
                    SoundPlayer.targetMusic[typeAmbienceTrackIndex] = null;
                }
                // Switch the type ambience if nothing playing atm or the currently playing clip is not suitable anymore
                else if (SoundPlayer.targetMusic[typeAmbienceTrackIndex] == null || SoundPlayer.currentMusic[typeAmbienceTrackIndex] == null || !SoundPlayer.currentMusic[typeAmbienceTrackIndex].IsPlaying() || suitableTypeAmbiences.None(m => m == SoundPlayer.currentMusic[typeAmbienceTrackIndex]))
                {
                    SoundPlayer.targetMusic[typeAmbienceTrackIndex] = suitableTypeAmbiences.GetRandomUnsynced();
                }

                IEnumerable<BackgroundMusic> suitableIntensityMusic = Enumerable.Empty<BackgroundMusic>();
                BackgroundMusic mainTrack = SoundPlayer.targetMusic[mainTrackIndex];
                if (mainTrack is not { MuteIntensityTracks: true } && Screen.Selected == GameMain.GameScreen)
                {
                    float intensity = currentIntensity;
                    if (mainTrack?.ForceIntensityTrack != null)
                    {
                        intensity = mainTrack.ForceIntensityTrack.Value;
                    }
                    suitableIntensityMusic = SoundPlayer.GetSuitableMusicClips("intensity".ToIdentifier(), intensity);
                }
                //get the appropriate intensity layers for current situation
                int intensityTrackStartIndex = 3;
                for (int i = intensityTrackStartIndex; i < SoundPlayer.MaxMusicChannels; i++)
                {
                    //disable targetmusics that aren't suitable anymore
                    if (SoundPlayer.targetMusic[i] != null && !suitableIntensityMusic.Any(m => m == SoundPlayer.targetMusic[i]))
                    {
                        SoundPlayer.targetMusic[i] = null;
                    }
                }

                foreach (BackgroundMusic intensityMusic in suitableIntensityMusic)
                {
                    //already playing, do nothing
                    if (SoundPlayer.targetMusic.Any(m => m != null && m == intensityMusic)) { continue; }

                    for (int i = intensityTrackStartIndex; i < SoundPlayer.MaxMusicChannels; i++)
                    {
                        if (SoundPlayer.targetMusic[i] == null)
                        {
                            SoundPlayer.targetMusic[i] = intensityMusic;
                            break;
                        }
                    }
                }

                SoundPlayer.LogCurrentMusic();
                SoundPlayer.updateMusicTimer = SoundPlayer.UpdateMusicInterval;
                if (mainTrack != null)
                {
                    SoundPlayer.updateMusicTimer += mainTrack.MinimumPlayDuration;
                }
            }

            bool muteBackgroundMusic = false;
            for (int i = 0; i < SoundManager.SourceCount; i++)
            {
                SoundChannel playingSoundChannel = GameMain.SoundManager.GetSoundChannelFromIndex(SoundManager.SourcePoolIndex.Default, i);
                if (playingSoundChannel is { MuteBackgroundMusic: true, IsPlaying: true })
                {
                    muteBackgroundMusic = true;
                    break;
                }
            }

            int activeTrackCount = SoundPlayer.targetMusic.Count(m => m != null);
            for (int i = 0; i < SoundPlayer.MaxMusicChannels; i++)
            {
                //nothing should be playing on this Channel
                if (SoundPlayer.targetMusic[i] == null)
                {
                    if (SoundPlayer.musicChannel[i] != null && SoundPlayer.musicChannel[i].IsPlaying)
                    {
                        //mute the Channel
                        SoundPlayer.musicChannel[i].Gain = MathHelper.Lerp(SoundPlayer.musicChannel[i].Gain, 0.0f, SoundPlayer.MusicLerpSpeed * deltaTime);
                        if (sidechaining) { SoundPlayer.musicChannel[i].Gain *= 1 - (Sidechain.SidechainMultiplier * Config.SidechainMusicDuckMultiplier); }
                        if (SoundPlayer.musicChannel[i].Gain < 0.01f) { SoundPlayer.DisposeMusicChannel(i); }
                    }
                }
                //something should be playing, but the targetMusic is invalid
                else if (!SoundPlayer.musicClips.Any(mc => mc == SoundPlayer.targetMusic[i]))
                {
                    SoundPlayer.targetMusic[i] = SoundPlayer.GetSuitableMusicClips(SoundPlayer.targetMusic[i].Type, 0.0f).GetRandomUnsynced();
                }
                //something should be playing, but the Channel is playing nothing or an incorrect clip
                else if (SoundPlayer.currentMusic[i] == null || SoundPlayer.targetMusic[i] != SoundPlayer.currentMusic[i])
                {
                    //something playing -> mute it first
                    if (SoundPlayer.musicChannel[i] != null && SoundPlayer.musicChannel[i].IsPlaying)
                    {
                        SoundPlayer.musicChannel[i].Gain = MathHelper.Lerp(SoundPlayer.musicChannel[i].Gain, 0.0f, SoundPlayer.MusicLerpSpeed * deltaTime);
                        if (sidechaining) { SoundPlayer.musicChannel[i].Gain *= 1 - (Sidechain.SidechainMultiplier * Config.SidechainMusicDuckMultiplier); }
                        if (SoundPlayer.musicChannel[i].Gain < 0.01f) { SoundPlayer.DisposeMusicChannel(i); }
                    }
                    //Channel free now, start playing the correct clip
                    if (SoundPlayer.currentMusic[i] == null || (SoundPlayer.musicChannel[i] == null || !SoundPlayer.musicChannel[i].IsPlaying))
                    {
                        SoundPlayer.DisposeMusicChannel(i);

                        SoundPlayer.currentMusic[i] = SoundPlayer.targetMusic[i];
                        SoundPlayer.musicChannel[i] = SoundPlayer.currentMusic[i].Sound.Play(0.0f, i == noiseLoopIndex ? SoundManager.SoundCategoryDefault : SoundManager.SoundCategoryMusic);
                        if (SoundPlayer.targetMusic[i].ContinueFromPreviousTime)
                        {
                            SoundPlayer.musicChannel[i].StreamSeekPos = SoundPlayer.targetMusic[i].PreviousTime;
                        }
                        else if (SoundPlayer.targetMusic[i].StartFromRandomTime)
                        {
                            SoundPlayer.musicChannel[i].StreamSeekPos =
                                (int)(SoundPlayer.musicChannel[i].MaxStreamSeekPos * Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced));
                        }
                        SoundPlayer.musicChannel[i].Looping = true;
                    }
                }
                else
                {
                    //playing something, lerp volume up
                    if (SoundPlayer.musicChannel[i] == null || !SoundPlayer.musicChannel[i].IsPlaying)
                    {
                        SoundPlayer.musicChannel[i]?.Dispose();
                        SoundPlayer.musicChannel[i] = SoundPlayer.currentMusic[i].Sound.Play(0.0f, i == noiseLoopIndex ? SoundManager.SoundCategoryDefault : SoundManager.SoundCategoryMusic);
                        SoundPlayer.musicChannel[i].Looping = true;
                    }
                    float targetGain = SoundPlayer.targetMusic[i].Volume;
                    if (muteBackgroundMusic)
                    {
                        targetGain = 0.0f;
                    }
                    if (SoundPlayer.targetMusic[i].DuckVolume)
                    {
                        targetGain *= (float)Math.Sqrt(1.0f / activeTrackCount);
                    }
                    SoundPlayer.musicChannel[i].Gain = MathHelper.Lerp(SoundPlayer.musicChannel[i].Gain, targetGain, SoundPlayer.MusicLerpSpeed * deltaTime);
                    if (sidechaining) { SoundPlayer.musicChannel[i].Gain *= 1 - (Sidechain.SidechainMultiplier * Config.SidechainMusicDuckMultiplier); }
                }
            }

            // Replace method. Sigh.
            return false;
        }

        // Used to apply the general lowpass frequency to OggSounds when not using the custom ExtendedOggSounds.
        // Patching the OggSound.MuffleBufferHeavy doesn't seem to work, which would be the ideal alternative.
        public static void SPW_BiQuad(BiQuad __instance, ref double frequency, ref double sampleRate, ref double q, ref double gainDb)
        {
            if (!Config.Enabled) { return; };

            // If frequency == vanilla default, we're processing a normal sound, so we replace it with the HeavyLowpassFrequency.
            // Otherwise, it's probably a player's voice meaning it's already at the correct frequency.
            // Note: To avoid an edge case, I made it impossible in the menu for the user to make their voice lowpass freq == vanilla default.
            if (__instance.GetType() == typeof(LowpassFilter) && !Config.StaticFx && frequency == SoundPlayer.MuffleFilterFrequency)
            {
                frequency = Config.HeavyLowpassFrequency;
            }

            // We don't want to modify anything if the vanilla game is constructing a filter.
            else if (__instance.GetType() == typeof(BandpassFilter) && frequency != ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY)
            {
                q = Config.RadioBandpassQualityFactor;
            }

            return;
        }

        public static bool SPW_SoundChannel_FadeOutAndDispose(SoundChannel __instance)
        {
            __instance.Dispose();
            return false;
        }

        public static bool SPW_SoundChannel_SetMuffled_Prefix(SoundChannel __instance, bool value)
        {
            SoundChannel instance = __instance;

            // Hand over control to default setter if sound is not extended or has no sound info.
            uint sourceId = instance.Sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex);
            if (!Config.Enabled ||
                instance.Sound is not ExtendedOggSound extendedSound ||
                !ChannelInfoManager.TryGetChannelInfo(instance, out ChannelInfo? soundInfo) ||
                soundInfo == null)
            { 
                return true; 
            }

            // Real time processing doesn't use the muffled property so it is essentially turned off.
            if (Config.DynamicFx || instance.Sound is ReducedOggSound)
            {
                return false;
            }
            
            instance.muffled = value;

            if (instance.ALSourceIndex < 0) { return false; }

            if (!instance.IsPlaying) { return false; }

            if (instance.IsStream) { return false; }

            uint alSource = instance.Sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex);
            
            Al.GetSourcei(alSource, Al.SampleOffset, out int playbackPos);
            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.ThrowError("Failed to get source's playback position: " + instance.debugName + ", " + Al.GetErrorString(alError), appendStackTrace: true);
                return false;
            }
            
            Al.SourceStop(alSource);
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.ThrowError("Failed to stop source: " + instance.debugName + ", " + Al.GetErrorString(alError), appendStackTrace: true);
                return false;
            }

            instance.Sound.FillAlBuffers();
            if (extendedSound.Buffers is not { AlBuffer: not 0, AlReverbBuffer: not 0, AlHeavyMuffledBuffer: not 0, AlLightMuffledBuffer: not 0, AlMediumMuffledBuffer: not 0 }) { return false; }

            uint alBuffer = extendedSound.Buffers.AlBuffer;
            if (soundInfo.Muffled || extendedSound.Owner.GetCategoryMuffle(instance.Category))
            {
                if (soundInfo.LightMuffle)
                {
                    alBuffer = extendedSound.Buffers.AlLightMuffledBuffer;
                }
                else if (soundInfo.MediumMuffle)
                {
                    alBuffer = extendedSound.Buffers.AlMediumMuffledBuffer;
                }
                else
                {
                    alBuffer = extendedSound.Buffers.AlHeavyMuffledBuffer;
                }
            }
            else if (soundInfo.StaticShouldUseReverbBuffer)
            {
                alBuffer = extendedSound.Buffers.AlReverbBuffer;
                soundInfo.StaticIsUsingReverbBuffer = true;
            }
            else
            {
                soundInfo.StaticIsUsingReverbBuffer = false;
            }

            Al.Sourcei(alSource, Al.Buffer, (int)alBuffer);

            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.ThrowError("Failed to bind buffer to source: " + instance.debugName + ", " + Al.GetErrorString(alError), appendStackTrace: true);
                return false;
            }

            Al.SourcePlay(alSource);
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.ThrowError("Failed to replay source: " + instance.debugName + ", " + Al.GetErrorString(alError), appendStackTrace: true);
                return false;
            }

            // Verify playback pos for sounds that are using an elongated reverb buffer
            // as they may have had their buffer size changed mid-play.
            if (soundInfo.StaticIsUsingReverbBuffer && (instance.Looping || Config.UpdateNonLoopingSounds))
            {
                bool foundPos = false;
                long maxIterations = extendedSound.TotalSamples + (long)(extendedSound.SampleRate * Config.StaticReverbDuration);
                int samples = extendedSound.SampleRate / 10; // Move 0.1 seconds left to see if valid.
                while (!foundPos && maxIterations > 0)
                {
                    Al.Sourcei(alSource, Al.SampleOffset, playbackPos);
                    alError = Al.GetError();
                    if (alError == Al.NoError) { foundPos = true; }
                    else
                    {
                        playbackPos -= samples;
                        maxIterations -= samples;
                    }
                }
            }
            else
            {
                Al.Sourcei(alSource, Al.SampleOffset, playbackPos);
            }
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                // This error still happens for StaticFx reverb buffers despite the above solutions of moving the playbackpos... I'm just turning it off because it doesn't actually matter anyway.
                if (!Config.StaticFx) { DebugConsole.ThrowError("Failed to reset playback position: " + instance.debugName + ", " + Al.GetErrorString(alError), appendStackTrace: true); }
                return false;
            }

            // Replace method.
            return false;
        }

        static bool BindReducedSoundBuffers(ReducedOggSound reducedSound, SoundChannel instance, uint sourceId, bool muffle, bool isClone)
        {
            reducedSound.FillAlBuffers();
            if (reducedSound.Buffers is not { AlBuffer: not 0 }) { return false; }

            if (!isClone)
            {
                ChannelInfoManager.EnsureUpdateChannelInfo(instance, dontMuffle: !muffle);
            }

            uint alBuffer = reducedSound.Buffers.AlBuffer;

            Al.Sourcei(sourceId, Al.Buffer, (int)alBuffer);

            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + reducedSound.Owner.GetSourceFromIndex(reducedSound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
            }

            return true;
        }

        static bool BindExtendedSoundBuffers(ExtendedOggSound extendedSound, SoundChannel instance, uint sourceId, bool muffle, bool isClone)
        {
            extendedSound.FillAlBuffers();
            if (isClone || extendedSound.Buffers is not { AlBuffer: not 0, AlReverbBuffer: not 0, AlHeavyMuffledBuffer: not 0, AlLightMuffledBuffer: not 0, AlMediumMuffledBuffer: not 0 }) { return false; }

            ChannelInfo soundInfo = ChannelInfoManager.EnsureUpdateChannelInfo(instance, dontMuffle: !muffle);

            uint alBuffer = extendedSound.Buffers.AlBuffer;

            if (soundInfo.Muffled || extendedSound.Owner.GetCategoryMuffle(instance.Category))
            {
                if (soundInfo.LightMuffle)
                {
                    alBuffer = extendedSound.Buffers.AlLightMuffledBuffer;
                }
                else if (soundInfo.MediumMuffle)
                {
                    alBuffer = extendedSound.Buffers.AlMediumMuffledBuffer;
                }
                else
                {
                    alBuffer = extendedSound.Buffers.AlHeavyMuffledBuffer;
                }
            }
            else if (soundInfo.StaticShouldUseReverbBuffer)
            {
                alBuffer = extendedSound.Buffers.AlReverbBuffer;
                soundInfo.StaticIsUsingReverbBuffer = true;
            }
            else
            {
                soundInfo.StaticIsUsingReverbBuffer = false;
            }

            Al.Sourcei(sourceId, Al.Buffer, (int)alBuffer);

            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + extendedSound.Owner.GetSourceFromIndex(extendedSound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
            }

            return true;
        }

        static bool BindVanillaSoundBuffers(Sound sound, SoundChannel instance, uint sourceId, bool muffle, bool isClone)
        {
            sound.FillAlBuffers();
            if (sound.Buffers is not { AlBuffer: not 0, AlMuffledBuffer: not 0}) { return false; }

            bool shouldMuffle = false;
            if (!isClone) 
            {
                ChannelInfo soundInfo = ChannelInfoManager.EnsureUpdateChannelInfo(instance, dontMuffle: !muffle);
                shouldMuffle = soundInfo.Muffled;
            }

            uint alBuffer = shouldMuffle || sound.Owner.GetCategoryMuffle(instance.Category) ? sound.Buffers.AlMuffledBuffer : sound.Buffers.AlBuffer;

            Al.Sourcei(sourceId, Al.Buffer, (int)alBuffer);

            int alError = Al.GetError();
            if (Al.GetError() != Al.NoError)
            {
                throw new Exception("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + sound.Owner.GetSourceFromIndex(sound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
            }

            return true;
        }

        public static bool SPW_SoundChannel_Prefix(SoundChannel __instance, Sound sound, float gain, Vector3? position, float freqMult, float near, float far, Identifier category, bool muffle)
        {
            if (!Config.StaticFx && sound is ExtendedOggSound ||
                !Config.DynamicFx && sound is ReducedOggSound ||
                Config.ClassicFx && (sound is ExtendedOggSound || sound is ReducedOggSound) ||
                !Config.Enabled && (sound is ExtendedOggSound || sound is ReducedOggSound))
            {
                sound.Dispose();
                return false;
            }
            else if (!Config.Enabled)
            { 
                return true; 
            }
            SoundChannel instance = __instance;
            instance.Sound = sound;
            instance.debugName = sound == null ?
            "SoundChannel (null)" :
                $"SoundChannel ({(string.IsNullOrEmpty(sound.Filename) ? "filename empty" : sound.Filename)})";
            instance.IsStream = sound.Stream;
            instance.FilledByNetwork = sound is VoipSound;
            instance.decayTimer = 0;
            instance.streamSeekPos = 0; instance.reachedEndSample = false;
            instance.buffersToRequeue = 4;

            bool isClone = freqMult == ChannelInfoManager.CLONE_FREQ_MULT_CODE;
            if (isClone) { freqMult = 1; }

            if (instance.IsStream)
            {
                // Access readonly fields via reflection
                var fieldInfo = typeof(SoundChannel).GetField("mutex", BindingFlags.NonPublic | BindingFlags.Instance);
                fieldInfo?.SetValue(__instance, new object());
            }

#if !DEBUG
            try
            {
#endif
            if (instance.mutex != null) { Monitor.Enter(instance.mutex); }
            if (sound.Owner.CountPlayingInstances(sound) < sound.MaxSimultaneousInstances)
            {
                instance.ALSourceIndex = sound.Owner.AssignFreeSourceToChannel(instance);
            }

            if (instance.ALSourceIndex >= 0)
            {
                uint sourceId = sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex);
                int alError = Al.GetError();

                if (!instance.IsStream)
                {
                    
                    // Reset buffer.
                    Al.Sourcei(sourceId, Al.Buffer, 0);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to reset source buffer: " + instance.debugName + ", " + Al.GetErrorString(alError));
                    }

                    SetProperties();

                    bool success = false;
                    if (instance.Sound is ReducedOggSound reducedSound)
                    {
                        success = BindReducedSoundBuffers(reducedSound, instance, sourceId, muffle, isClone);
                    }
                    else if (instance.Sound is ExtendedOggSound extendedSound)
                    {
                        success = BindExtendedSoundBuffers(extendedSound, instance, sourceId, muffle, isClone);
                    }
                    else
                    {
                        success = BindVanillaSoundBuffers(sound, instance, sourceId, muffle, isClone);
                    }
                    if (!success) { return false; }

                    // Play sound.
                    Al.SourcePlay(instance.Sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex));
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to play source: " + instance.debugName + ", " + Al.GetErrorString(alError));
                    }
                }
                else
                {
                    uint alBuffer = 0;
                    Al.Sourcei(sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex), Al.Buffer, (int)alBuffer);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to reset source buffer: " + instance.debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.Sourcei(sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex), Al.Looping, Al.False);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to set stream looping state: " + instance.debugName + ", " + Al.GetErrorString(alError));
                    }

                    // Modify readonly fields via reflection.
                    var streamShortBuffer = typeof(SoundChannel).GetField("streamShortBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
                    streamShortBuffer?.SetValue(instance, new short[SoundChannel.STREAM_BUFFER_SIZE]);

                    var streamBuffers = typeof(SoundChannel).GetField("streamBuffers", BindingFlags.NonPublic | BindingFlags.Instance);
                    uint[] streamBuffersArray = new uint[4];
                    streamBuffers?.SetValue(instance, streamBuffersArray);

                    var unqueuedBuffers = typeof(SoundChannel).GetField("unqueuedBuffers", BindingFlags.NonPublic | BindingFlags.Instance);
                    unqueuedBuffers?.SetValue(instance, new uint[4]);

                    var streamBufferAmplitudes = typeof(SoundChannel).GetField("streamBufferAmplitudes", BindingFlags.NonPublic | BindingFlags.Instance);
                    streamBufferAmplitudes?.SetValue(instance, new float[4]);

                    for (int i = 0; i < 4; i++)
                    {
                        Al.GenBuffer(out streamBuffersArray[i]);

                        alError = Al.GetError();
                        if (alError != Al.NoError)
                        {
                            throw new Exception("Failed to generate stream buffers: " + instance.debugName + ", " + Al.GetErrorString(alError));
                        }

                        if (!Al.IsBuffer(streamBuffersArray[i]))
                        {
                            throw new Exception("Generated streamBuffer[" + i.ToString() + "] is invalid! " + instance.debugName);
                        }
                    }
                    instance.Sound.Owner.InitUpdateChannelThread();
                    SetProperties();
                }
            }
#if !DEBUG
            }
            catch
            {
                throw;
            }
            finally
            {
#endif
            if (instance.mutex != null) { Monitor.Exit(instance.mutex); }
#if !DEBUG
            }
#endif

            void SetProperties()
            {
                instance.Position = position;
                instance.Gain = gain;
                instance.FrequencyMultiplier = freqMult;
                instance.Looping = false;
                instance.Near = near;
                instance.Far = far;
                instance.Category = category;
            }

            instance.Sound.Owner.Update();

            // Replaces method.
            return false;
        }

        public static bool SPW_SoundPlayer_UpdateFireSounds(float deltaTime)
        {
            if (!ConfigManager.Config.Enabled) { return true; }

            // Reset volume accumulators.
            for (int i = 0; i < SoundPlayer.fireVolumeLeft.Length; i++)
            {
                SoundPlayer.fireVolumeLeft[i] = 0.0f;
                SoundPlayer.fireVolumeRight[i] = 0.0f;
            }

            // Get the listener's position.
            Vector2 listenerPos = new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);

            // Accumulate volume from all active fire sources.
            foreach (Hull hull in Hull.HullList)
            {
                foreach (FireSource fs in hull.FireSources)
                {
                    AddFireVolume(fs, listenerPos);
                }
                foreach (FireSource fs in hull.FakeFireSources)
                {
                    AddFireVolume(fs, listenerPos);
                }
            }

            // Update or create sound channels based on the calculated volumes.
            for (int i = 0; i < SoundPlayer.fireVolumeLeft.Length; i++)
            {
                // If volume is negligible, fade out and remove the sound.
                if (SoundPlayer.fireVolumeLeft[i] < 0.05f && SoundPlayer.fireVolumeRight[i] < 0.05f)
                {
                    if (SoundPlayer.fireSoundChannels[i] != null)
                    {
                        SoundPlayer.fireSoundChannels[i].FadeOutAndDispose();
                        SoundPlayer.fireSoundChannels[i] = null;
                    }
                }
                else // Otherwise, play the sound with updated properties.
                {
                    // The sound system uses the difference between right and left volumes to pan the sound.
                    // A positive difference pans right, a negative difference pans left.
                    Vector2 soundPos = new Vector2(GameMain.SoundManager.ListenerPosition.X + (SoundPlayer.fireVolumeRight[i] - SoundPlayer.fireVolumeLeft[i]) * 100, GameMain.SoundManager.ListenerPosition.Y);

                    if (SoundPlayer.fireSoundChannels[i] == null || !SoundPlayer.fireSoundChannels[i].IsPlaying)
                    {
                        SoundPlayer.fireSoundChannels[i] = SoundPlayer.GetSound(SoundPlayer.fireSoundTags[i])?.Play(1.0f, SoundPlayer.FireSoundRange, soundPos);
                        if (SoundPlayer.fireSoundChannels[i] == null) { continue; }
                        SoundPlayer.fireSoundChannels[i].Looping = true;
                    }

                    // The gain is the loudest of the two channels.
                    SoundPlayer.fireSoundChannels[i].Gain = Math.Max(SoundPlayer.fireVolumeRight[i], SoundPlayer.fireVolumeLeft[i]);
                    // The position is updated to handle panning.
                    SoundPlayer.fireSoundChannels[i].Position = new Vector3(soundPos, 0.0f);
                }
            }

            void AddFireVolume(FireSource fs, Vector2 listenerPos)
            {
                // 1. Find the closest horizontal point on the fire's surface to the listener.
                // This is key to fixing the vanilla bug. clamp the listener's X position to the fire's horizontal bounds.
                float fireLeftEdgeX = fs.WorldPosition.X;
                float fireRightEdgeX = fs.WorldPosition.X + fs.Size.X;
                float closestX = Math.Clamp(listenerPos.X, fireLeftEdgeX, fireRightEdgeX);

                // 2. The sound's origin for distance calculation is this closest point at the fire's vertical center.
                Vector2 soundSourcePos = new Vector2(closestX, fs.WorldPosition.Y + fs.Size.Y / 2.0f);

                // 3. Calculate the actual distance to this correct point.
                float dist = Vector2.Distance(listenerPos, soundSourcePos);

                // 4. Calculate volume falloff based on the correct distance.
                float distFalloff = dist / SoundPlayer.FireSoundRange;

                // If the sound is out of range, it contributes no volume.
                if (distFalloff >= 0.99f) return;

                float baseVolume = (1.0f - distFalloff);

                // 5. Determine panning. We need to calculate left/right volumes that the
                // sound system can use to create a positional effect.
                // Note: The original system uses 'fireVolumeLeft' for sounds on the right, and 'fireVolumeRight' for sounds on the left?

                float fireCenterX = fs.WorldPosition.X + fs.Size.X / 2.0f;
                float halfWidth = fs.Size.X / 2.0f;

                // Calculate a pan value from -1.0 (left) to 1.0 (right) based on listener's position relative to the fire's center.
                float pan = (listenerPos.X - fireCenterX) / (halfWidth + 0.001f); // Add epsilon to prevent div by zero
                pan = Math.Clamp(pan, -1.0f, 1.0f);

                float rightChannelVolume = 0.0f;
                float leftChannelVolume = 0.0f;

                // Distribute the baseVolume into the two channels based on the pan value.
                // This creates a smooth panning effect across the surface of the fire.
                if (pan < 0) // Listener is on the left half of the fire
                {
                    leftChannelVolume = baseVolume;
                    rightChannelVolume = baseVolume * (1.0f + pan); // Volume on the right channel fades out as pan approaches -1
                }
                else // Listener is on the right half of the fire
                {
                    rightChannelVolume = baseVolume;
                    leftChannelVolume = baseVolume * (1.0f - pan); // Volume on the left channel fades out as pan approaches 1
                }

                // 6. Add the calculated volumes to the correct global accumulators.
                // Remember: fireVolumeLeft is for right-panned sounds, fireVolumeRight is for left-panned sounds.
                SoundPlayer.fireVolumeLeft[0] += rightChannelVolume;
                SoundPlayer.fireVolumeRight[0] += leftChannelVolume;

                // Apply same logic for the larger fire sound layers, scaled by the fire's size.
                if (fs.Size.X > SoundPlayer.FireSoundLargeLimit)
                {
                    float factor = (fs.Size.X - SoundPlayer.FireSoundLargeLimit) / SoundPlayer.FireSoundLargeLimit;
                    SoundPlayer.fireVolumeLeft[2] += rightChannelVolume * factor;
                    SoundPlayer.fireVolumeRight[2] += leftChannelVolume * factor;
                }
                else if (fs.Size.X > SoundPlayer.FireSoundMediumLimit)
                {
                    float factor = (fs.Size.X - SoundPlayer.FireSoundMediumLimit) / SoundPlayer.FireSoundMediumLimit;
                    SoundPlayer.fireVolumeLeft[1] += rightChannelVolume * factor;
                    SoundPlayer.fireVolumeRight[1] += leftChannelVolume * factor;
                }
            }

            // Add channels to channelInfoMap for modifying volume based on eavesdropping fade and sidechaining.
            for (int i = 0; i < SoundPlayer.fireSoundChannels.Count(); i++)
            {
                SoundChannel channel = SoundPlayer.fireSoundChannels[i];
                if (channel == null) { continue; }
                ChannelInfoManager.EnsureUpdateChannelInfo(channel);
            }

            return false;
        }

        public static bool SPW_ItemComponent_UpdateSounds(ItemComponent __instance)
        {
            if (!Config.Enabled) { return true; }

            ItemComponent instance = __instance;
            UpdateComponentOneshotSoundChannels(instance);

            Item item = instance.item;
            ItemSound loopingSound = instance.loopingSound;
            SoundChannel channel = instance.loopingSoundChannel;

            if (loopingSound == null || channel == null || !channel.IsPlaying) { return false; }

            channel.Position = new Vector3(item.WorldPosition, 0.0f);

            ChannelInfoManager.EnsureUpdateChannelInfo(channel, itemComp: instance);

            return false;
        }

        public static bool SPW_StatusEffect_UpdateAllProjSpecific()
        {
            if (!Config.Enabled) { return true; }

            HashSet<StatusEffect> ActiveLoopingSounds = StatusEffect.ActiveLoopingSounds;

            foreach (StatusEffect statusEffect in ActiveLoopingSounds)
            {
                if (statusEffect.soundChannel == null) { continue; }

                //stop looping sounds if the statuseffect hasn't been applied in 0.1
                //= keeping the sound looping requires continuously applying the statuseffect
                if (Timing.TotalTime > statusEffect.loopStartTime + 0.1 && !StatusEffect.DurationList.Any(e => e.Parent == statusEffect))
                {
                    statusEffect.soundChannel.FadeOutAndDispose();
                    statusEffect.soundChannel = null;
                }
                else if (statusEffect.soundEmitter is { Removed: false })
                {
                    // Changes
                    SoundChannel channel = statusEffect.soundChannel;
                    ChannelInfoManager.EnsureUpdateChannelInfo(channel, statusEffect: statusEffect, dontMuffle: statusEffect.ignoreMuffling, dontPitch: true);
                    statusEffect.soundChannel.Position = new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f);
                }
            }
            ActiveLoopingSounds.RemoveWhere(s => s.soundChannel == null);

            return false;
        }

        private static void UpdateComponentOneshotSoundChannels(ItemComponent itemComponent)
        {
            List<SoundChannel> playingOneshotSoundChannels = itemComponent.playingOneshotSoundChannels;
            Item item = itemComponent.item;

            for (int i = 0; i < playingOneshotSoundChannels.Count; i++)
            {
                if (!playingOneshotSoundChannels[i].IsPlaying)
                {
                    playingOneshotSoundChannels[i].Dispose();
                    playingOneshotSoundChannels[i] = null;
                }
            }
            playingOneshotSoundChannels.RemoveAll(ch => ch == null);
            foreach (SoundChannel channel in playingOneshotSoundChannels)
            {
                channel.Position = new Vector3(item.WorldPosition, 0.0f);
            }
        }

        public static void SPW_UpdateTransform(Camera __instance)
        {
            if (!Config.Enabled || !Util.RoundStarted || Character.Controlled == null) { return; }

            if (Config.FocusTargetAudio && LightManager.ViewTarget != null && LightManager.ViewTarget.Position != Character.Controlled.Position)
            {
                GameMain.SoundManager.ListenerPosition = new Vector3(__instance.TargetPos.X, __instance.TargetPos.Y, -(100 / __instance.Zoom));
            }
        }

        public static bool SPW_UpdateWaterAmbience(ref float ambienceVolume, ref float deltaTime)
        {
            if (!Config.Enabled || !Util.RoundStarted || Character.Controlled == null) { return true; }

            if (Character.Controlled.AnimController.HeadInWater)
            {
                ambienceVolume *= Config.SubmergedWaterAmbienceVolumeMultiplier;
            }
            else if (Listener.IsUsingHydrophones)
            {
                ambienceVolume *= Config.HydrophoneWaterAmbienceVolumeMultiplier; ambienceVolume *= HydrophoneManager.HydrophoneEfficiency;
            }
            else if (Config.FocusTargetAudio && LightManager.ViewTarget != null && Listener.CurrentHull == null)
            {
                ambienceVolume *= Config.SubmergedWaterAmbienceVolumeMultiplier;
            }
            else
            {
                ambienceVolume *= Config.UnsubmergedWaterAmbienceVolumeMultiplier;
            }

            float ducking = 1 - Sidechain.SidechainMultiplier;
            ambienceVolume *= Math.Clamp(1 - EavesdropManager.Efficiency * 2.5f, 0, 1) * ducking;

            float dt = deltaTime;
            // Method Replacement:

            if (GameMain.SoundManager.Disabled || GameMain.GameScreen?.Cam == null) { return false; }

            //how fast the sub is moving, scaled to 0.0 -> 1.0
            float movementSoundVolume = 0.0f;

            float insideSubFactor = 0.0f;
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub == null || sub.Removed) { continue; }
                float movementFactor = (sub.Velocity == Vector2.Zero) ? 0.0f : sub.Velocity.Length() / 10.0f;
                movementFactor = MathHelper.Clamp(movementFactor, 0.0f, 1.0f);

                if (Character.Controlled == null || Character.Controlled.Submarine != sub)
                {
                    float dist = Vector2.Distance(GameMain.GameScreen.Cam.WorldViewCenter, sub.WorldPosition);
                    movementFactor /= Math.Max(dist / 1000.0f, 1.0f);
                    insideSubFactor = Math.Max(1.0f / Math.Max(dist / 1000.0f, 1.0f), insideSubFactor);
                }
                else
                {
                    insideSubFactor = 1.0f;
                }

                if (Character.Controlled != null && Character.Controlled.PressureTimer > 0.0f && !Character.Controlled.IsDead)
                {
                    //make the sound lerp to the "outside" sound when under pressure
                    insideSubFactor -= Character.Controlled.PressureTimer / 100.0f;
                }

                movementSoundVolume = Math.Max(movementSoundVolume, movementFactor);
                if (!MathUtils.IsValid(movementSoundVolume))
                {
                    string errorMsg = "Failed to update water ambience volume - submarine's movement value invalid (" + movementSoundVolume + ", sub velocity: " + sub.Velocity + ")";
                    DebugConsole.Log(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.UpdateWaterAmbience:InvalidVolume", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    movementSoundVolume = 0.0f;
                }
                if (!MathUtils.IsValid(insideSubFactor))
                {
                    string errorMsg = "Failed to update water ambience volume - inside sub value invalid (" + insideSubFactor + ")";
                    DebugConsole.Log(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("SoundPlayer.UpdateWaterAmbience:InvalidVolume", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    insideSubFactor = 0.0f;
                }
            }

            void updateWaterAmbience(Sound sound, float volume)
            {
                SoundChannel chn = SoundPlayer.waterAmbienceChannels.FirstOrDefault(c => c.Sound == sound);
                if (Level.Loaded != null)
                {
                    volume *= Level.Loaded.GenerationParams.WaterAmbienceVolume;
                }
                if (chn is null || !chn.IsPlaying)
                {
                    if (volume < 0.01f) { return; }
                    if (chn is not null) { SoundPlayer.waterAmbienceChannels.Remove(chn); }
                    chn = sound.Play(volume, "waterambience");
                    chn.Looping = true;
                    SoundPlayer.waterAmbienceChannels.Add(chn);
                }
                else
                {
                    float diff = volume - chn.Gain;
                    float snapThreshold = 0.1f * Config.WaterAmbienceTransitionSpeedMultiplier;
                    if (Math.Abs(diff) < snapThreshold)
                    {
                        chn.Gain = volume;
                    }
                    else
                    {
                        chn.Gain += dt * Math.Sign(diff) * Config.WaterAmbienceTransitionSpeedMultiplier;
                    }

                    if (chn.Gain < 0.01f)
                    {
                        chn.FadeOutAndDispose();
                    }
                    if (Character.Controlled != null && Character.Controlled.PressureTimer > 0.0f && !Character.Controlled.IsDead)
                    {
                        //make the sound decrease in pitch when under pressure
                        chn.FrequencyMultiplier = MathHelper.Clamp(Character.Controlled.PressureTimer / 200.0f, 0.75f, 1.0f);
                    }
                    else if (Listener.IsUsingHydrophones && HydrophoneManager.HydrophoneEfficiency < 1)
                    {
                        chn.FrequencyMultiplier = MathHelper.Lerp(0.25f, 1f, HydrophoneManager.HydrophoneEfficiency);
                    }
                    else
                    {
                        chn.FrequencyMultiplier = Math.Min(chn.frequencyMultiplier + dt, 1.0f);
                    }
                }
            }

            updateWaterAmbience(SoundPlayer.waterAmbienceIn.Sound, ambienceVolume * (1.0f - movementSoundVolume) * insideSubFactor);
            updateWaterAmbience(SoundPlayer.waterAmbienceMoving.Sound, ambienceVolume * movementSoundVolume * insideSubFactor);
            updateWaterAmbience(SoundPlayer.waterAmbienceOut.Sound, 1.0f - insideSubFactor);

            return false;
        }

        public static void SPW_SoundPlayer_UpdateWaterFlowSounds()
        {
            if (!Config.Enabled) { return; }

            Listener.UpdateHullsWithLeaks();

            for (int i = 0; i < SoundPlayer.flowSoundChannels.Count(); i++)
            {
                SoundChannel channel = SoundPlayer.flowSoundChannels[i];
                if (channel == null) { continue; }
                ChannelInfoManager.EnsureUpdateChannelInfo(channel);
            }
        }
    }
}
