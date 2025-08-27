using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenAL;
using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Threading.Channels;

namespace SoundproofWalls
{
    public partial class Plugin : IAssemblyPlugin
    {
        public static Plugin Instance;

        // Pointers for convenience.
        public static Config LocalConfig = ConfigManager.LocalConfig;
        public static Config? ServerConfig = ConfigManager.ServerConfig;
        public static Config config => ConfigManager.Config;

        public static SidechainProcessor Sidechain = new SidechainProcessor();
        public static EfxAudioManager? EffectsManager;

        public static ShutdownRequest? ActiveShutdownRequest = null;

        public struct ShutdownRequest
        {
            public bool Partial;
        }

        public enum SoundPath
        {
            EavesdroppingActivation,
            EavesdroppingAmbienceDry,
            EavesdroppingAmbienceWet,

            HydrophoneAmbienceColdCaverns,
            HydrophoneAmbienceEuropanRidge,
            HydrophoneAmbienceAphoticPlateau,
            HydrophoneAmbienceGreatSea,
            HydrophoneAmbienceHydrothermalWastes,
            HydrophoneMovementSmall,
            HydrophoneMovementMedium,
            HydrophoneMovementLarge,

            BubbleLocal,
            BubbleRadio,
        }
        public static string ModPath = Util.GetModDirectory();
        public static readonly Dictionary<SoundPath, string> CustomSoundPaths = new Dictionary<SoundPath, string>
        {
            { SoundPath.EavesdroppingActivation, Path.Combine(ModPath, "Content/Sounds/SPW_EavesdroppingActivation.ogg") },
            { SoundPath.EavesdroppingAmbienceDry, Path.Combine(ModPath, "Content/Sounds/SPW_EavesdroppingAmbienceDryRoom.ogg") },
            { SoundPath.EavesdroppingAmbienceWet, Path.Combine(ModPath, "Content/Sounds/SPW_EavesdroppingAmbienceWetRoom.ogg") },

            { SoundPath.HydrophoneAmbienceColdCaverns, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneAmbienceColdCaverns.ogg") },
            { SoundPath.HydrophoneAmbienceEuropanRidge, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneAmbienceEuropanRidge.ogg") },
            { SoundPath.HydrophoneAmbienceAphoticPlateau, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneAmbienceAphoticPlateau.ogg") },
            { SoundPath.HydrophoneAmbienceGreatSea, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneAmbienceGreatSea.ogg") },
            { SoundPath.HydrophoneAmbienceHydrothermalWastes, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneAmbienceHydrothermalWastes.ogg") },

            { SoundPath.HydrophoneMovementSmall, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneMovementSmall.ogg") },
            { SoundPath.HydrophoneMovementMedium, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneMovementMedium.ogg") },
            { SoundPath.HydrophoneMovementLarge, Path.Combine(ModPath, "Content/Sounds/SPW_HydrophoneMovementLarge.ogg") },

            { SoundPath.BubbleLocal, Path.Combine(ModPath, "Content/Sounds/SPW_BubblesLoopMono.ogg") },
            { SoundPath.BubbleRadio, Path.Combine(ModPath, "Content/Sounds/SPW_RadioBubblesLoopStereo.ogg") },
        };

        public void InitClient()
        {
            Instance = this;

            // Compatibility with Lua mods that mess with with Sound objects.
            LuaUserData.RegisterType(typeof(ExtendedOggSound).FullName);
            LuaUserData.RegisterType(typeof(ExtendedSoundBuffers).FullName);
            LuaUserData.RegisterType(typeof(ReducedOggSound).FullName);
            LuaUserData.RegisterType(typeof(ReducedSoundBuffers).FullName);

            GameMain.LuaCs.Hook.Add("think", "spw_clientupdate", (object[] args) =>
            {
                SPW_Update();
                return null;
            });

            GameMain.LuaCs.Game.AddCommand("spw", TextManager.Get("spw_openmenuhelp").Value, args =>
            {
                Menu.ForceOpenMenu();
            });

            GameMain.LuaCs.Game.AddCommand("spw_welcome", TextManager.Get("spw_openpopuphelp").Value, args =>
            {
                Menu.ShowWelcomePopup();
            });

            GameMain.LuaCs.Game.AddCommand("spw_stats", TextManager.Get("spw_openpopuphelp").Value, args =>
            {
                ModStateManager.SaveState(ModStateManager.State);
                ModStateManager.PrintStats();
            });

            GameMain.LuaCs.Game.AddCommand("spw_help", TextManager.Get("spw_guidehelp").Value, args =>
            {
                ToolBox.OpenFileWithShell("https://steamcommunity.com/workshop/filedetails/discussion/3153737715/4204742223861734474");
            });

            GameMain.LuaCs.Game.AddCommand("spw_report", TextManager.Get("spw_reporthelp").Value, args =>
            {
                ToolBox.OpenFileWithShell("https://steamcommunity.com/workshop/filedetails/discussion/3153737715/4204742223861924530");
            });

            GameMain.LuaCs.Game.AddCommand("spw_workshop", TextManager.Get("spw_openworkshophelp").Value, args =>
            {
                ToolBox.OpenFileWithShell("https://steamcommunity.com/sharedfiles/filedetails/?id=3153737715");
            });

            // SoundManager_Constructor transpiler.
            // Needed to adjust the maximum source count.
            harmony.Patch(
                original: typeof(SoundManager).GetConstructor(Type.EmptyTypes),
                transpiler: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SoundManager_Constructor_Transpiler), BindingFlags.Static | BindingFlags.Public))
            );

            // SoundPlayer_UpdateMusic transpiler.
            // Needed to reflect changes to the maximum source count.
            harmony.Patch(
                original: typeof(SoundPlayer).GetMethod("UpdateMusic", BindingFlags.Static | BindingFlags.NonPublic),
                transpiler: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SoundManager_Constructor_Transpiler), BindingFlags.Static | BindingFlags.Public))
            );

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

            // Turret UpdateProjSpecific postfix.
            // Updates the volume of turret moving and stopping sounds.
            harmony.Patch(
                typeof(Turret).GetMethod(nameof(Turret.UpdateProjSpecific), BindingFlags.Instance | BindingFlags.NonPublic),
                postfix: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_Turret_UpdateProjSpecific))));

            // StatusEffect UpdateAllProjSpecific prefix REPLACEMENT.
            // Updates muffle and other attributes of StatusEffect sounds.
            harmony.Patch(
                typeof(StatusEffect).GetMethod(nameof(StatusEffect.UpdateAllProjSpecific), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_StatusEffect_UpdateAllProjSpecific))));

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

            // Draw postfix.
            // Displays the eavesdropping text, eavesdropping vignette, and eavesdropping sprite overlay
            // Bug note: a line in this method causes MonoMod to crash on Linux due to an unmanaged PAL_SEHException https://github.com/dotnet/runtime/issues/78271
            // Bug note update: from my testing this doesn't seem to apply anymore as of June 2025
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.Draw)),
                prefix: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_Draw_Prefix))));
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.Draw)),
                postfix: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_Draw_Postfix))));

            // Sonar_Draw postfix.
            // Displays active hydrophone sectors.
            harmony.Patch(
                original: typeof(Sonar).GetMethod(nameof(Sonar.DrawSonar), BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: new HarmonyMethod(typeof(HydrophoneManager).GetMethod(nameof(HydrophoneManager.DrawHydrophoneSprites))));

            // Sonar_DrawBlip prefix.
            // Disables sonar blips when using hydrophones.
            harmony.Patch(
                original: typeof(Sonar).GetMethod(nameof(Sonar.DrawBlip), BindingFlags.NonPublic | BindingFlags.Instance),
                prefix: new HarmonyMethod(typeof(HydrophoneManager).GetMethod(nameof(HydrophoneManager.DrawSonarBlips))));

            // Sonar_DrawDockingPorts prefix.
            // Disables drawing of docking ports when using hydrophones.
            harmony.Patch(
                original: typeof(Sonar).GetMethod(nameof(Sonar.DrawDockingPorts), BindingFlags.NonPublic | BindingFlags.Instance),
                prefix: new HarmonyMethod(typeof(HydrophoneManager).GetMethod(nameof(HydrophoneManager.DrawDockingPorts))));

            // Sonar_DrawOwnSubmarineBorders prefix.
            // Disables drawing of submarine borders when using hydrophones.
            harmony.Patch(
                original: typeof(Sonar).GetMethod(nameof(Sonar.DrawOwnSubmarineBorders), BindingFlags.NonPublic | BindingFlags.Instance),
                prefix: new HarmonyMethod(typeof(HydrophoneManager).GetMethod(nameof(HydrophoneManager.DrawSubBorders))));

            // TogglePauseMenu postfix.
            // Displays menu button and updates the config when the menu is closed.
            // Don't unpatch this because otherwise the config won't save when renabling SPW and closing menu with esc after disabling it.
            PatchToKeep = harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.TogglePauseMenu)),
                null,
                new HarmonyMethod(typeof(Menu).GetMethod(nameof(Menu.SPW_TogglePauseMenu))));

            // ShouldMuffleSounds prefix REPLACEMENT (blank).
            // Just returns true. Workaround for ignoring muffling on sounds with "dontmuffle" in their XML. 
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.ShouldMuffleSound)),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_ShouldMuffleSound))));

            // Server requests client to send their config.
            GameMain.LuaCs.Networking.Receive(CLIENT_SEND_CONFIG, (object[] args) =>
            {
                ConfigManager.UploadClientConfigToServer();
            });

            // Clients receiving the server config.
            GameMain.LuaCs.Networking.Receive(CLIENT_RECEIVE_CONFIG, (object[] args) =>
            {
                // Unpack message.
                IReadMessage msg = (IReadMessage)args[0];
                string data = msg.ReadString();
                string configString = DataAppender.RemoveData(data, out bool manualUpdate, out byte configSenderId);

                bool useServerConfig = configString != DISABLED_CONFIG_VALUE; // configString will be equal to DISABLED_CONFIG_VALUE if syncing is disabled.
                //LuaCsLogger.Log($"Client: received config from server. is valid: {useServerConfig}");

                Config? newConfig = null;
                try
                {
                    newConfig = useServerConfig ? JsonSerializer.Deserialize<Config>(configString) : LocalConfig;
                } catch (Exception e) { DebugConsole.LogError($"[SoundproofWalls] Failed to deserialize server config, {e}"); }

                if (newConfig == null)
                {
                    DebugConsole.LogError("[SoundproofWalls] Error detected in server config - switching to local config");
                    newConfig = LocalConfig;
                    useServerConfig = false;
                }

                ConfigManager.UpdateConfig(newConfig: newConfig, oldConfig: config, isServerConfigEnabled: useServerConfig, manualUpdate: manualUpdate, configSenderId: configSenderId);
            });

            InitDynamicFx();

            ConfigManager.Setup();
            HydrophoneManager.Setup();
            EavesdropManager.Setup();
            BubbleManager.Setup();

            // Ensure all sounds have been loaded with the correct muffle buffer.
            if (config.Enabled)
            { 
                BufferManager.TriggerBufferReload(starting: true);
                SoundInfoManager.UpdateSoundInfoMap();

                Util.ResizeSoundManagerPools(config.MaxSourceCount);
            }

            LuaCsLogger.Log(TextManager.GetWithVariable("initmessage", "[version]", ModStateManager.State.Version).Value, color: Color.LimeGreen);
            LuaCsLogger.Log(TextManager.Get("initmessagefollowup").Value, color: Color.LightGreen);
            
            ModStateManager.State.TimesInitialized++;
            ModStateManager.SaveState(ModStateManager.State);
        }

        public static void InitDynamicFx()
        {
            if (!config.Enabled || !config.DynamicFx) { return; }

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

        public void TriggerShutdown(bool partial = false, bool reloadBuffers = false)
        {
            if (ActiveShutdownRequest.HasValue) return;

            if (reloadBuffers)
            {
                BufferManager.TriggerBufferReload();
            }

            ActiveShutdownRequest = new ShutdownRequest
            {
                Partial = partial
            };
        }

        public void DisposeClient(bool fullStop = false)
        {
            ModStateManager.SaveState(ModStateManager.State); // Save stats

            Util.ResizeSoundManagerPools(SoundManager.SourceCount); // Reset to default source count.

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

            // If we're quitting to menu we don't have time to wait for the reload request and instead call directly.
            // This should ONLY be called when fully stopping the mod. Otherwise ReloadBuffers has already called.
            if (fullStop)
            {
                BufferManager.ReloadBuffers(new BufferManager.ReloadRequest() { Stopping = true }); ;
            }

            Menu.Dispose();
        }

        public static void SPW_StartRound()
        {
            HydrophoneManager.SetupHydrophoneSwitches(firstStartup: true);
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
            if (!GameMain.Instance.Paused && config.Enabled)
            {
                ConfigManager.Update();
                Listener.Update();
                HydrophoneManager.Update();
                EavesdropManager.Update();
                BubbleManager.Update();
                ChannelInfoManager.Update();
                Sidechain.Update();
                EffectsManager?.Update();
                PerformanceProfiler.Instance.Update();
            }

            BufferManager.Update();

            if (ActiveShutdownRequest.HasValue && !BufferManager.ActiveReloadRequest.HasValue)
            {
                if (ActiveShutdownRequest.Value.Partial) { Instance.PartialDispose(); }
                else { Instance.Dispose(); }
                ActiveShutdownRequest = null;
            }
        }

        public static IEnumerable<CodeInstruction> SoundManager_Constructor_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {

            FieldInfo dynamicCountField = typeof(ChannelInfoManager).GetField(nameof(ChannelInfoManager.SourceCount), BindingFlags.Public | BindingFlags.Static);

            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                // Check for instructions that load the integer constant '32'.
                bool isOriginalSourceCount = (codes[i].opcode == OpCodes.Ldc_I4_S && (sbyte)codes[i].operand == 32) ||
                                             (codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 32);

                if (isOriginalSourceCount)
                {
                    // Replace the "load constant 32" instruction with dynamicCountField.
                    codes[i] = new CodeInstruction(OpCodes.Ldsfld, dynamicCountField);
                }
            }

            return codes.AsEnumerable();
        }

        public static bool SPW_LoadCustomOggSound(SoundManager __instance, string filename, bool stream, ref Sound __result)
        {
            bool badFilename = !string.IsNullOrEmpty(filename) && (config.StaticFx && Util.StringHasKeyword(filename, SoundInfoManager.IgnoredPrefabs) || Util.StringHasKeyword(filename, CustomSoundPaths.Values.ToHashSet()));
            if (badFilename ||
                !config.Enabled ||
                config.ClassicFx ||
                config.DynamicFx && !config.RemoveUnusedBuffers)
            { return true; } // Run original method.

            if (__instance.Disabled) { return false; }

            if (!File.Exists(filename))
            {
                DebugConsole.LogError("[SoundproofWalls] Sound file \"" + filename + "\" doesn't exist!");
                return false;
            }

#if DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            if (config.DynamicFx)
            {
                Sound newSound = new ReducedOggSound(__instance, filename, stream, null);
                lock (__instance.loadedSounds)
                {
                    __instance.loadedSounds.Add(newSound);
                }
                __result = newSound;
            }
            else if (config.StaticFx)
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
            if (!config.Enabled || config.ClassicFx || config.DynamicFx && !config.RemoveUnusedBuffers) { return true; }
            if (__instance.Disabled) { return false; }

            string filePath = overrideFilePath ?? element.GetAttributeContentPath("file")?.Value ?? "";

            if (!string.IsNullOrEmpty(filePath) && 
                (config.StaticFx && Util.StringHasKeyword(filePath, SoundInfoManager.IgnoredPrefabs) ||
                Util.StringHasKeyword(filePath, CustomSoundPaths.Values.ToHashSet())))
            { return true; }

            if (!File.Exists(filePath))
            {
                DebugConsole.LogError($"[SoundproofWalls] Sound file \"{filePath}\" doesn't exist! Content package \"{(element.ContentPackage?.Name ?? "Unknown")}\".");
                return false;
            }

            float range = element.GetAttributeFloat("range", 1000.0f);
            if (config.DynamicFx)
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
            else if (config.StaticFx)
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
            if (!config.Enabled || !config.EavesdroppingEnabled || !allowZoom) { return; }

            // Sync with the text for simplicity.
            float activationMult = EavesdropManager.EavesdroppingTextAlpha / 255;

            float zoomAmount = __instance.DefaultZoom - ((1 - config.EavesdroppingZoomMultiplier) * activationMult);

            __instance.globalZoomScale = zoomAmount;
        }

        public static bool SPW_Draw_Prefix(ref Camera cam, ref SpriteBatch spriteBatch)
        {
            EavesdropManager.Draw(spriteBatch, cam);
            return true;
        }

        public static void SPW_Draw_Postfix(ref Camera cam, ref SpriteBatch spriteBatch)
        {
            PerformanceProfiler.Instance.Draw(spriteBatch);
            BufferManager.DrawBufferReloadText(spriteBatch);
        }

        // Workaround for ignoring sounds with "dontmuffle" in their XML. 
        // In the PlaySound() methods, the condition "muffle = !ignoreMuffling && ShouldMuffleSound()" is used to determine muffle.
        // By making one of the two operands constant, we effectively rule it out, making the "muffle" variable a direct reference to "!ignoreMuffling".
        // This is how we can know if a sound is tagged with "dontmuffle" in their XML (SoundChannel.Sound.XElement is not viable due to often being null).
        public static bool SPW_ShouldMuffleSound(ref bool __result)
        {
            if (!config.Enabled) { return true; }
            __result = true;
            return false;
        }

        public static bool SPW_VoipClient_Read(VoipClient __instance, ref IReadMessage msg)
        {
            if (!config.Enabled) { return true; }
            
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

            if (!queue.Read(msg, discardData: client.Muted || client.MutedLocally))
            {
                return false;
            }

            if (clientCantSpeak)
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
                client.VoipSound.UseRadioFilter = false;
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
            float speechImpedimentMultiplier = 1.0f - client.Character.SpeechImpediment / 100.0f;
            if (messageType == ChatMessageType.Radio)
            {
                client.VoipSound.UsingRadio = true;
                float radioRange = senderRadio.Range * speechImpedimentMultiplier * config.VoiceRadioRangeMultiplier;
                client.VoipSound.SetRange(near: radioRange * VoipClient.RangeNear, far: radioRange);
                if (distanceFactor > VoipClient.RangeNear && !spectating)
                {
                    //noise starts increasing exponentially after 40% range
                    client.RadioNoise = MathF.Pow(MathUtils.InverseLerp(VoipClient.RangeNear, 1.0f, distanceFactor), 2);
                }
            }
            else
            {
                client.VoipSound.UsingRadio = false;
                float rangeMult = config.VoiceLocalRangeMultiplier;
                float baseRange = ChatMessage.SpeakRangeVOIP;
                float hydrophoneAddedRange = Listener.IsUsingHydrophones ? config.HydrophoneSoundRange : 0;
                float targetFar = (baseRange + hydrophoneAddedRange) * speechImpedimentMultiplier * rangeMult;
                float targetNear = targetFar * VoipClient.RangeNear;

                if (config.ScreamMode)
                {
                    float maxRange = config.ScreamModeMaxRange * rangeMult;
                    targetFar = maxRange * client.VoipSound.CurrentAmplitude;
                    targetFar = Math.Max(targetFar, config.ScreamModeMinRange);
                    if (targetFar > ChannelInfoManager.ScreamModeTrailingRangeFar)
                    {
                        ChannelInfoManager.ScreamModeTrailingRangeFar = targetFar;
                    }

                    targetFar = (ChannelInfoManager.ScreamModeTrailingRangeFar + hydrophoneAddedRange) * speechImpedimentMultiplier;
                    targetNear = ChannelInfoManager.ScreamModeTrailingRangeFar * VoipClient.RangeNear;
                }
                client.VoipSound.SetRange(targetNear, targetFar);

                //LuaCsLogger.Log($"CurrentAmplitude {client.VoipSound.CurrentAmplitude} localRangeMultiplier {localRangeMultiplier} targetNear: {targetNear} targetFar: {targetFar} channelFar {client.VoipSound.soundChannel.Far}");
            }

            // Sound Info stuff.
            SoundChannel channel = client.VoipSound.soundChannel;
            Hull? clientHull = client.Character.CurrentHull;
            ChannelInfoManager.EnsureUpdateVoiceInfo(channel, speakingClient: client, messageType: messageType, soundHull: clientHull);
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

            float rangeFar = instance.VoipSound.Far;
            float rangeNear = instance.VoipSound.Near;
            float maxAudibleRange = ChatMessage.SpeakRangeVOIP * config.VoiceLocalRangeMultiplier;
            if (Listener.IsUsingHydrophones)
            {
                maxAudibleRange += config.HydrophoneSoundRange;
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
            //instance.VoipSound.Gain = gain;
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

        private static float voipLastMinLowpassFrequency = -1;
        private static float voipLastMuffleStrength = -1;
        private static BiQuad voipDynamicMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, 24000); // Temp value.
        private static BiQuad voipLightMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, config.VoiceLightLowpassFrequency);
        private static BiQuad voipMediumMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, config.VoiceMediumLowpassFrequency);
        private static BiQuad voipHeavyMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, config.VoiceHeavyLowpassFrequency);
        private static RadioFilter voipCustomRadioFilter = new RadioFilter(VoipConfig.FREQUENCY, config.RadioBandpassFrequency, 
                                                                config.RadioBandpassQualityFactor, config.RadioDistortionDrive, 
                                                                config.RadioDistortionThreshold, config.RadioStatic, 
                                                                config.RadioCompressionThreshold, config.RadioCompressionRatio);
        private static BiQuad voipVanillaRadioFilter = new BandpassFilter(VoipConfig.FREQUENCY, ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY);
        public static bool SPW_VoipSound_ApplyFilters_Prefix(VoipSound __instance, ref short[] buffer, ref int readSamples)
        {
            VoipSound voipSound = __instance;
            Client client = voipSound.client;

            if (!config.Enabled || 
                voipSound == null || 
                !voipSound.IsPlaying ||
                client == null ||
                client.Character == null) 
            { return true; }

            SoundChannel channel = voipSound.soundChannel;
            Hull? clientHull = client.Character.CurrentHull;
            ChannelInfo channelInfo = ChannelInfoManager.EnsureUpdateChannelInfo(channel, soundHull: clientHull, speakingClient: client);

            // Update muffle filters if needed.
            if (voipSound.UseMuffleFilter && config.DynamicFx && 
                (voipLastMuffleStrength != channelInfo.MuffleStrength || voipLastMinLowpassFrequency != config.VoiceMinLowpassFrequency))
            {
                double lowpassFrequency = Util.GetCompensatedBiquadFrequency(channelInfo.MuffleStrength, config.VoiceMinLowpassFrequency, VoipConfig.FREQUENCY);
                voipDynamicMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, lowpassFrequency);
                voipLastMuffleStrength = channelInfo.MuffleStrength;
                voipLastMinLowpassFrequency = config.VoiceMinLowpassFrequency;
            }
            else if (voipSound.UseMuffleFilter && !config.DynamicFx)
            {
                if (voipHeavyMuffleFilter._frequency != config.VoiceHeavyLowpassFrequency)
                {
                    voipHeavyMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, config.VoiceHeavyLowpassFrequency);
                }
                if (voipLightMuffleFilter._frequency != config.VoiceLightLowpassFrequency)
                {
                    voipLightMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, config.VoiceLightLowpassFrequency);
                }
                if (voipMediumMuffleFilter._frequency != config.VoiceMediumLowpassFrequency)
                {
                    voipMediumMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, config.VoiceMediumLowpassFrequency);
                }
            }

            // Select the muffle filter to use.
            BiQuad muffleFilter;
            if (config.DynamicFx)
            {
                muffleFilter = voipDynamicMuffleFilter;
            }
            else
            {
                muffleFilter = voipHeavyMuffleFilter;
                if (channelInfo.LightMuffle) muffleFilter = voipLightMuffleFilter;
                else if (channelInfo.MediumMuffle) muffleFilter = voipMediumMuffleFilter;
            }

            // Update custom radio filter if any settings for it have been changed.
            if (voipSound.UseRadioFilter &&
                config.RadioCustomFilterEnabled &&
                (voipCustomRadioFilter.frequency != config.RadioBandpassFrequency ||
                voipCustomRadioFilter.q != config.RadioBandpassQualityFactor ||
                voipCustomRadioFilter.distortionDrive != config.RadioDistortionDrive ||
                voipCustomRadioFilter.distortionThreshold != config.RadioDistortionThreshold ||
                voipCustomRadioFilter.staticAmount != config.RadioStatic ||
                voipCustomRadioFilter.compressionThreshold != config.RadioCompressionThreshold ||
                voipCustomRadioFilter.compressionRatio != config.RadioCompressionRatio))
            {
                voipCustomRadioFilter = new RadioFilter(VoipConfig.FREQUENCY, config.RadioBandpassFrequency, 
                    config.RadioBandpassQualityFactor, config.RadioDistortionDrive, config.RadioDistortionThreshold, 
                    config.RadioStatic, config.RadioCompressionThreshold, config.RadioCompressionRatio);
            }

            // Vanilla method & changes.

            // This voipSound.Gain property applies Baro's CurrentConfig.Audio.VoiceChatVolume and client.VoiceVolume to channel.Gain.
            // The soundInfo.Gain property returns channel.gain
            //voipSound.Gain = soundInfo.Gain;

            for (int i = 0; i < readSamples; i++)
            {
                float fVal = ToolBox.ShortAudioSampleToFloat(buffer[i]);

                if (voipSound.UseMuffleFilter)
                {
                    fVal = muffleFilter.Process(fVal);
                }
                if (voipSound.UseRadioFilter)
                {
                    fVal *= ConfigManager.Config.VoiceRadioVolumeMultiplier;

                    if (config.RadioCustomFilterEnabled)
                    {
                        fVal = Math.Clamp(voipCustomRadioFilter.Process(fVal) * ConfigManager.Config.RadioPostFilterBoost, -1f, 1f);
                    }
                    else
                    {
                        fVal = Math.Clamp(voipVanillaRadioFilter.Process(fVal) * VoipSound.PostRadioFilterBoost, -1f, 1f);
                    }
                }
                else
                {
                    fVal *= ConfigManager.Config.VoiceLocalVolumeMultiplier;
                }
                buffer[i] = ToolBox.FloatToShortAudioSample(fVal);
            }

            return false;
        }

        // Runs at the start of the SoundChannel disposing method.
        public static void SPW_SoundChannel_Dispose(SoundChannel __instance)
        {
            if (!config.Enabled) { return; };

            SoundChannel channel = __instance;

            try
            {
                if (channel.mutex != null) { Monitor.Enter(channel.mutex); }

                if (channel.Sound != null)
                {
                    uint sourceId = channel.Sound.Owner.GetSourceFromIndex(channel.Sound.SourcePoolIndex, channel.ALSourceIndex);
                    EffectsManager?.UnregisterSource(sourceId);
                }
                else
                {
                    LuaCsLogger.LogError($"[SoundproofWalls] Warning: \"{channel.debugName}\" was missing a Sound object and may not have been disposed correctly.");
                }

                ChannelInfoManager.RemoveChannelInfo(channel);
                ChannelInfoManager.RemovePitchedChannel(channel);
                BubbleManager.RemoveBubbleSound(channel);
            }
            finally
            {
                if (channel.mutex != null) { Monitor.Exit(channel.mutex); }
            }
        }

        public static void SPW_SoundPlayer_PlaySound(ref Sound sound, ref float? range, ref Vector2 position, ref Hull hullGuess)
        {
            if (!config.Enabled || !Util.RoundStarted || sound == null) { return; }

            range = range ?? sound.BaseFar;
            float rangeMult = config.SoundRangeMultiplierMaster * SoundInfoManager.EnsureGetSoundInfo(sound).RangeMult;
            range *= rangeMult;

            if (Listener.IsUsingHydrophones)
            {
                Hull targetHull = Hull.FindHull(position, hullGuess, true);
                if (targetHull == null || targetHull.Submarine != Character.Controlled?.Submarine)
                {
                    range += config.HydrophoneSoundRange;
                }
            }
            return;
        }

        public static bool SPW_ItemComponent_PlaySound(ItemComponent __instance, ItemSound itemSound, Vector2 position)
        {
            if (!config.Enabled) { return true; }

            Sound? sound = itemSound.RoundSound?.Sound;
            float individualRangeMult = 1;
            if (sound != null) { individualRangeMult = SoundInfoManager.EnsureGetSoundInfo(sound).RangeMult; }
            
            float range = itemSound.Range;
            float rangeMult = config.LoopingSoundRangeMultiplierMaster * individualRangeMult;
            range *= rangeMult;

            if (Listener.IsUsingHydrophones)
            {
                Hull soundHull = Hull.FindHull(position, Character.Controlled?.CurrentHull, true);
                if (soundHull == null || soundHull.Submarine != Character.Controlled?.Submarine)
                {
                    range += config.HydrophoneSoundRange;
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
                        if (__instance.loopingSoundChannel.Sound == null)
                        {
                            __instance.loopingSoundChannel.Dispose();
                            return false;
                        }
                        __instance.loopingSoundChannel.Looping = true;
                        __instance.loopingSoundChannel.Near = range * config.LoopingComponentSoundNearMultiplier;
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
            if (!config.Enabled) { return true; }

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
                float rangeMult = config.LoopingSoundRangeMultiplierMaster * individualRangeMult;
                range *= rangeMult;

                if (Listener.IsUsingHydrophones)
                {
                    Hull soundHull = Hull.FindHull(__instance.item.WorldPosition, __instance.item.CurrentHull, true);
                    if (soundHull == null || soundHull.Submarine != Character.Controlled?.Submarine)
                    {
                        range += config.HydrophoneSoundRange;
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
                        __instance.loopingSoundChannel.Near = range * config.LoopingComponentSoundNearMultiplier;
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
            if (!config.Enabled) { return true; }

            bool sidechaining = config.SidechainingEnabled && config.SidechainMusic;

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
                    IEnumerable<BackgroundMusic> suitableNoiseLoops = (Screen.Selected == GameMain.GameScreen && !config.DisableWhiteNoise) ?
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
                        if (sidechaining) { SoundPlayer.musicChannel[i].Gain *= 1 - (Sidechain.SidechainMultiplier * config.SidechainIntensityMaster * config.SidechainMusicMultiplier); }
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
                        if (sidechaining) { SoundPlayer.musicChannel[i].Gain *= 1 - (Sidechain.SidechainMultiplier * config.SidechainIntensityMaster * config.SidechainMusicMultiplier); }
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
                    if (sidechaining) { SoundPlayer.musicChannel[i].Gain *= 1 - (Sidechain.SidechainMultiplier * config.SidechainIntensityMaster * config.SidechainMusicMultiplier); }
                }
            }

            // Replace method. Sigh.
            return false;
        }

        // Used to apply the general lowpass frequency to OggSounds when not using the custom ExtendedOggSounds.
        // Patching the OggSound.MuffleBufferHeavy doesn't seem to work, which would be the ideal alternative.
        public static void SPW_BiQuad(BiQuad __instance, ref double frequency, ref double sampleRate, ref double q, ref double gainDb)
        {
            if (!config.Enabled) { return; };

            if (__instance.GetType() == typeof(LowpassFilter))
            {
                // It's not possible for the user to make their voice lowpass freq == vanilla default.
                bool constructedByVoice = frequency != SoundPlayer.MuffleFilterFrequency;
                // Apply the ClassicMuffleFrequency on Classic and Dynamic mode. Dynamic mode doesn't need it but it makes switching to classic instant.
                if (!config.StaticFx && !constructedByVoice)
                {
                    frequency = config.ClassicMuffleFrequency;
                }
            }

            // We don't want to modify anything if the vanilla game is constructing a filter.
            else if (__instance.GetType() == typeof(BandpassFilter))
            {
                bool constructedByMod = frequency != ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY;
                if (config.RadioCustomFilterEnabled && constructedByMod) 
                {
                    q = config.RadioBandpassQualityFactor;
                }
            }

            return;
        }

        public static bool SPW_SoundChannel_FadeOutAndDispose(SoundChannel __instance)
        {
            // Run the original method if this setting is disabled. Og method can help with sounds cutting off too abruptly
            if (!config.DisableVanillaFadeOutAndDispose) { return true; }

            __instance.Dispose();
            return false;
        }

        public static bool SPW_SoundChannel_SetMuffled_Prefix(SoundChannel __instance, bool value)
        {
            // Don't try to set buffers while waiting for them to be reloaded. In most cases this doesn't matter, but
            // if the user was on Dynamic mode + reduced buffers, switching to another mode will crash the game as we apply buffers that don't exist yet
            if (BufferManager.ActiveReloadRequest.HasValue)
            {
                return false;
            }

            SoundChannel instance = __instance;

            // Hand over control to default setter if sound is not extended or has no sound info.
            uint sourceId = instance.Sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex);
            if (!config.Enabled ||
                instance.Sound is not ExtendedOggSound extendedSound ||
                !ChannelInfoManager.TryGetChannelInfo(instance, out ChannelInfo? soundInfo) ||
                soundInfo == null)
            { 
                return true; 
            }

            // Real time processing doesn't use the muffled property so it is essentially turned off.
            if (config.DynamicFx || instance.Sound is ReducedOggSound)
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
                DebugConsole.LogError("Failed to get source's playback position: " + instance.debugName + ", " + Al.GetErrorString(alError));
                return false;
            }
            
            Al.SourceStop(alSource);
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.LogError("Failed to stop source: " + instance.debugName + ", " + Al.GetErrorString(alError));
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
                DebugConsole.LogError("Failed to bind buffer to source: " + instance.debugName + ", " + Al.GetErrorString(alError));
                return false;
            }

            Al.SourcePlay(alSource);
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.LogError("Failed to replay source: " + instance.debugName + ", " + Al.GetErrorString(alError));
                return false;
            }

            // Verify playback pos for sounds that are using a reverb buffer.
            // Reverb buffers are longer. Taking the playback pos and applying it to the shorter muffle buffer can cause errors if not handled here.
            if (config.StaticFx && config.StaticReverbEnabled && (instance.Looping || config.UpdateNonLoopingSounds))
            {
                bool foundPos = false;
                long maxIterations = extendedSound.TotalSamples + (long)(extendedSound.SampleRate * config.StaticReverbDuration);
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
                DebugConsole.LogError("Failed to reset playback position: " + instance.debugName + ", " + Al.GetErrorString(alError));
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
                DebugConsole.LogError("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + reducedSound.Owner.GetSourceFromIndex(reducedSound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
                return false;
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
                DebugConsole.LogError("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + extendedSound.Owner.GetSourceFromIndex(extendedSound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
                return false;
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
                DebugConsole.LogError("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + sound.Owner.GetSourceFromIndex(sound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
                return false;
            }

            return true;
        }

        public static bool SPW_SoundChannel_Prefix(SoundChannel __instance, Sound sound, float gain, Vector3? position, float freqMult, float near, float far, Identifier category, bool muffle)
        {
            if (!BufferManager.ActiveReloadRequest.HasValue && (
                !config.StaticFx && sound is ExtendedOggSound ||
                !config.DynamicFx && sound is ReducedOggSound ||
                config.ClassicFx && (sound is ExtendedOggSound || sound is ReducedOggSound) ||
                !config.Enabled && (sound is ExtendedOggSound || sound is ReducedOggSound)))
            {
                sound.Dispose();
                return false;
            }
            else if (!config.Enabled)
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
                        DebugConsole.LogError("Failed to reset source buffer: " + instance.debugName + ", " + Al.GetErrorString(alError));
                        return false;
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
                        DebugConsole.LogError("Failed to play source: " + instance.debugName + ", " + Al.GetErrorString(alError));
                        return false;
                    }
                }
                else
                {
                    uint alBuffer = 0;
                    Al.Sourcei(sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex), Al.Buffer, (int)alBuffer);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        DebugConsole.LogError("Failed to reset source buffer: " + instance.debugName + ", " + Al.GetErrorString(alError));
                        return false;
                    }

                    Al.Sourcei(sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex), Al.Looping, Al.False);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        DebugConsole.LogError("Failed to set stream looping state: " + instance.debugName + ", " + Al.GetErrorString(alError));
                        return false;
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
                            DebugConsole.LogError("Failed to generate stream buffer: " + instance.debugName + ", " + Al.GetErrorString(alError));
                            return false;
                        }

                        if (!Al.IsBuffer(streamBuffersArray[i]))
                        {
                            DebugConsole.LogError("Generated streamBuffer[" + i.ToString() + "] is not a valid buffer: " + instance.debugName);
                            return false;
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
                // Sometimes fire sizes can be negative values? Thank you baro devs
                if (fs.Size.X < 0 || fs.Size.Y < 0) { return; }

                // 1. Find the closest horizontal point on the fire's surface to the listener.
                // Clamp the listener's X position to the fire's horizontal bounds.
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
            if (!config.Enabled) { return true; }

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

        public static void SPW_Turret_UpdateProjSpecific(Turret __instance)
        {   
            if (!config.Enabled) { return; }

            if (__instance.moveSoundChannel != null)
            {
                __instance.moveSoundChannel.Position = new Vector3(__instance.Item.WorldPosition, 0);
                ChannelInfoManager.EnsureUpdateChannelInfo(__instance.moveSoundChannel, soundHull: __instance.Item.CurrentHull);
            }
            if (__instance.chargeSoundChannel != null)
            {
                __instance.chargeSoundChannel.Position = new Vector3(__instance.Item.WorldPosition, 0);
                ChannelInfoManager.EnsureUpdateChannelInfo(__instance.chargeSoundChannel, soundHull: __instance.Item.CurrentHull);
            }
        }

        public static bool SPW_StatusEffect_UpdateAllProjSpecific()
        {
            if (!config.Enabled) { return true; }

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
                    ChannelInfoManager.EnsureUpdateChannelInfo(channel, statusEffect: statusEffect, dontMuffle: statusEffect.ignoreMuffling);
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
            if (!config.Enabled || !Util.RoundStarted || Character.Controlled == null) { return; }

            if (Listener.IsCharacter)
            {
                GameMain.SoundManager.ListenerPosition = new Vector3(Listener.WorldPos.X, Listener.WorldPos.Y, 0);
            }
            else if (config.FocusTargetAudio && LightManager.ViewTarget != null)
            {
                GameMain.SoundManager.ListenerPosition = new Vector3(Listener.WorldPos.X, Listener.WorldPos.Y, -(100 / __instance.Zoom));
            }
            else
            {
                GameMain.SoundManager.ListenerPosition = new Vector3(__instance.TargetPos.X, __instance.TargetPos.Y, -(100 / __instance.Zoom));
            }
        }

        public static bool SPW_UpdateWaterAmbience(ref float ambienceVolume, ref float deltaTime)
        {
            if (!config.Enabled || !Util.RoundStarted || Character.Controlled == null) { return true; }

            if (Listener.IsSubmerged)
            {
                ambienceVolume *= Listener.IsWearingDivingSuit || !Listener.IsCharacter ? config.SubmergedSuitWaterAmbienceVolumeMultiplier : config.SubmergedNoSuitWaterAmbienceVolumeMultiplier;
            }
            else if (Listener.IsUsingHydrophones)
            {
                ambienceVolume *= 0;
            }
            else
            {
                ambienceVolume *= Listener.IsWearingDivingSuit ? config.UnsubmergedSuitWaterAmbienceVolumeMultiplier : config.UnsubmergedNoSuitWaterAmbienceVolumeMultiplier;
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
                    chn = sound.Play(volume, SoundManager.SoundCategoryWaterAmbience);
                    if (chn != null)
                    {
                        if (chn.Sound == null)
                        {
                            chn.Dispose();
                            return;
                        }
                        chn.Looping = true;
                        SoundPlayer.waterAmbienceChannels.Add(chn);
                    }
                }
                else
                {
                    float diff = volume - chn.Gain;
                    float snapThreshold = 0.1f * config.WaterAmbienceTransitionSpeedMultiplier;
                    if (Math.Abs(diff) < snapThreshold)
                    {
                        chn.Gain = volume;
                    }
                    else
                    {
                        chn.Gain += dt * Math.Sign(diff) * config.WaterAmbienceTransitionSpeedMultiplier;
                    }

                    if (chn.Gain < 0.01f)
                    {
                        chn.FadeOutAndDispose();
                        SoundPlayer.waterAmbienceChannels.Remove(chn);
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

            updateWaterAmbience(SoundPlayer.waterAmbienceIn.Sound, ambienceVolume * (1.0f - movementSoundVolume) * insideSubFactor * SoundPlayer.waterAmbienceIn.Volume * config.WaterAmbienceInVolumeMultiplier);
            updateWaterAmbience(SoundPlayer.waterAmbienceMoving.Sound, ambienceVolume * movementSoundVolume * insideSubFactor * SoundPlayer.waterAmbienceMoving.Volume * config.WaterAmbienceMovingVolumeMultiplier);
            updateWaterAmbience(SoundPlayer.waterAmbienceOut.Sound, ambienceVolume * (1.0f - insideSubFactor) * SoundPlayer.waterAmbienceOut.Volume * config.WaterAmbienceOutVolumeMultiplier);
            return false;
        }

        public static void SPW_SoundPlayer_UpdateWaterFlowSounds()
        {
            if (!config.Enabled) { return; }

            Listener.UpdateHullsWithLeaks();

            for (int i = 0; i < SoundPlayer.flowSoundChannels.Count(); i++)
            {
                SoundChannel channel = SoundPlayer.flowSoundChannels[i];
                if (channel == null || GameMain.Instance.Paused) { continue; }
                ChannelInfoManager.EnsureUpdateChannelInfo(channel);
            }
        }
    }
}
