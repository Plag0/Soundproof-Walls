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
using System.Collections.Concurrent;
using Barotrauma.Extensions;

namespace SoundproofWalls
{
    public partial class SoundproofWalls : IAssemblyPlugin
    {
        // These magic numbers are found in the VoipSound class under the initialization of the muffleFilters and radioFilters arrays.
        public const short VANILLA_VOIP_LOWPASS_FREQUENCY = 800;
        public const short VANILLA_VOIP_BANDPASS_FREQUENCY = 2000;

        public static Config LocalConfig = ConfigManager.LoadConfig();
        public static Config? ServerConfig = null;
        public static Config Config { get { return ServerConfig ?? LocalConfig; } }

        static BiQuad VoipSuitMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.DivingSuitLowpassFrequency);
        static BiQuad VoipEavesdroppingMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.EavesdroppingLowpassFrequency);
        static BiQuad VoipNormalMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.VoiceLowpassFrequency);
        static RadioFilter VoipRadioFilter = new RadioFilter(VoipConfig.FREQUENCY, Config.RadioBandpassFrequency, Config.RadioBandpassQualityFactor, Config.RadioDistortion, Config.RadioStatic, Config.RadioCompressionThreshold, Config.RadioCompressionRatio);

        static SidechainProcessor Sidechain = new SidechainProcessor();

        static bool RoundStarted { get { return GameMain.gameSession?.IsRunning ?? false; } }
        static bool IsUsingCustomTypes { get { return Config.MuffleDivingSuits || Config.MuffleEavesdropping; } }
        static bool IsWearingDivingSuit { get { return Character.Controlled?.LowPassMultiplier < 0.5f; } }
        static bool IsUsingHydrophones { get { return HydrophoneEfficiency > 0.01f && Character.Controlled?.SelectedItem?.GetComponent<Sonar>() is Sonar sonar && HydrophoneSwitches.ContainsKey(sonar) && HydrophoneSwitches[sonar].State; } }
        static bool IsViewTargetPlayer { get { return !Config.FocusTargetAudio || LightManager.ViewTarget as Character == Character.Controlled; } }
        static bool EarsInWater { get { return IsViewTargetPlayer ? Character.Controlled?.AnimController?.HeadInWater == true : LightManager.ViewTarget != null && SoundInWater(LightManager.ViewTarget.Position, ViewTargetHull); } }

        static float hydrophoneEfficiency = 1;
        static float HydrophoneEfficiency { get { return hydrophoneEfficiency; } set { hydrophoneEfficiency = Math.Clamp(value, 0, 1); } }

        static float eavesdroppingEfficiency = 0;
        static float EavesdroppingEfficiency { get { return eavesdroppingEfficiency; } set { eavesdroppingEfficiency = Math.Clamp(value, 0, 1); } }
        static float EavesdroppingTextAlpha = 0;

        static float LastHydrophonePlayTime = 0.1f;
        static float LastConfigUploadTime = 5f;
        static float LastBubbleUpdateTime = 0.2f;

        // Custom sounds.
        static Sound? BubbleSound;
        static Sound? RadioBubbleSound;
        static Sound? HydrophoneMovementSound;
        static Sound? EavesdroppingAmbience;
        static SoundChannel? EavesdroppingAmbienceSoundChannel;
        static Sound[] HydroHydrophoneMovementSounds = new Sound[4];
        static Sound[] EavesdroppingActivationSounds = new Sound[2];

        public static ConcurrentDictionary<SoundChannel, MuffleInfo> SoundChannelMuffleInfo = new ConcurrentDictionary<SoundChannel, MuffleInfo>();
        static ConcurrentDictionary<Client, SoundChannel?> ClientBubbleSoundChannels = new ConcurrentDictionary<Client, SoundChannel?>();
        static ConcurrentDictionary<SoundChannel, bool> PitchedSounds = new ConcurrentDictionary<SoundChannel, bool>();
        static Dictionary<SoundChannel, Character> HydrophoneSoundChannels = new Dictionary<SoundChannel, Character>();
        static Dictionary<Sonar, HydrophoneSwitch> HydrophoneSwitches = new Dictionary<Sonar, HydrophoneSwitch>();
        static HashSet<Sound> SoundsToDispose = new HashSet<Sound>();

        // Expensive or unnecessary sounds that are unlikely to be muffled and so are ignored when reloading sounds as a loading time optimisation.
        static readonly HashSet<string> IgnoredPrefabs = new HashSet<string>
        {
            "Barotrauma/Content/Sounds/Music/",
            "Barotrauma/Content/Sounds/UI/",
            "Barotrauma/Content/Sounds/Ambient/",
            "Barotrauma/Content/Sounds/Hull/",
            "Barotrauma/Content/Sounds/Water/WaterAmbience",
            "Barotrauma/Content/Sounds/RadioStatic",
            "Barotrauma/Content/Sounds/MONSTER_farLayer.ogg",
            "Barotrauma/Content/Sounds/Tinnitus",
            "Barotrauma/Content/Sounds/Heartbeat",
        };

        static Hull? EavesdroppedHull
        {
            get
            {
                Character character = Character.Controlled;

                if (!Config.EavesdroppingKeyOrMouse.IsDown() ||
                    character == null ||
                    character.CurrentHull == null ||
                    character.CurrentSpeed > 0.05 ||
                    character.IsUnconscious ||
                    character.IsDead)
                {
                    return null;
                }

                int expansionAmount = Config.EavesdroppingMaxDistance;
                Limb limb = GetCharacterHead(character);
                Vector2 headPos = limb.WorldPosition;
                headPos.Y = -headPos.Y;

                foreach (Gap gap in character.CurrentHull.ConnectedGaps)
                {
                    if (gap.ConnectedDoor == null || gap.ConnectedDoor.OpenState > 0 || gap.ConnectedDoor.IsBroken) { continue; }

                    Rectangle gWorldRect = gap.WorldRect;
                    Rectangle gapBoundingBox = new Rectangle(
                        gWorldRect.X - expansionAmount / 2,
                        -gWorldRect.Y - expansionAmount / 2,
                        gWorldRect.Width + expansionAmount,
                        gWorldRect.Height + expansionAmount);

                    if (gapBoundingBox.Contains(headPos))
                    {
                        foreach (Hull linkedHull in gap.linkedTo)
                        {
                            if (linkedHull != character.CurrentHull)
                            {
                                return linkedHull;
                            }
                        }
                    }
                }

                return null;
            }
        }
        static Hull? ViewTargetHull
        {
            get
            {
                if (IsViewTargetPlayer)
                {
                    return Character.Controlled.AnimController.GetLimb(LimbType.Head)?.Hull ?? Character.Controlled.CurrentHull;
                }
                else
                {
                    Character? viewedCharacter = LightManager.ViewTarget as Character;
                    if (viewedCharacter != null)
                    {
                        return viewedCharacter.AnimController.GetLimb(LimbType.Head)?.Hull ?? Character.Controlled.CurrentHull;
                    }
                    else
                    {
                        return Hull.FindHull(LightManager.ViewTarget.WorldPosition, Character.Controlled.CurrentHull);
                    }
                }
            }
        }

        public void InitClient()
        {
            EasySettings.SPW = this;

            GameMain.LuaCs.Hook.Add("think", "spw_clientupdate", (object[] args) =>
            {
                SPW_Update();
                return null;
            });

            // StartRound postfix patch.
            // Needed to set up the first hydrophone switches after terminals have loaded in.
            harmony.Patch(
                typeof(GameSession).GetMethod(nameof(GameSession.StartRound), new Type[] { typeof(LevelData), typeof(bool), typeof(SubmarineInfo), typeof(SubmarineInfo) }),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_StartRound))));
            
            // SoundPlayer_PlaySound prefix.
            // Needed to set the new custom range of sounds.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.PlaySound), new Type[] { typeof(Sound), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(Hull), typeof(bool), typeof(bool) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundPlayer_PlaySound))));

            // SoundPlayer_UpdateMusic prefix REPLACEMENT.
            // Ducks music
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateMusic), BindingFlags.NonPublic | BindingFlags.Static),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundPlayer_UpdateMusic))));

            // SoundPlayer_UpdateWaterFlowSounds postfix.
            // For modifying volume based on eavesdropping fade and sidechaining.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateWaterFlowSounds), BindingFlags.NonPublic | BindingFlags.Static),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundPlayer_UpdateWaterFlowSounds))));
            // For modifying volume based on eavesdropping fade and sidechaining.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateFireSounds), BindingFlags.NonPublic | BindingFlags.Static),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundPlayer_UpdateFireSounds))));

            // ItemComponent_PlaySound prefix.
            // Crash-preventative patch for when manually setting sound range below 100%.
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.PlaySound), BindingFlags.NonPublic | BindingFlags.Instance, new Type[] { typeof(ItemSound), typeof(Vector2) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_ItemComponent_PlaySound))));

            // BiQuad prefix.
            // Used for modifying the muffle frequency of standard OggSounds.
            harmony.Patch(
                typeof(BiQuad).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, new Type[] { typeof(int), typeof(double), typeof(double), typeof(double) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_BiQuad))));

            // SoundChannel ctor postfix REPLACEMENT.
            // Implements the custom ExtendedSoundBuffers for SoundChannels made with ExtendedOggSounds.
            harmony.Patch(
                typeof(SoundChannel).GetConstructor(new Type[] { typeof(Sound), typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(string), typeof(bool) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel_Prefix)))) ;

            // Soundchannel Muffle property prefix REPLACEMENT.
            // Switches between the three (when using ExtendedOggSounds) types of muffle buffers.
            harmony.Patch(
                typeof(SoundChannel).GetProperty(nameof(SoundChannel.Muffled)).GetSetMethod(),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel_SetMuffled_Prefix))));

            // VoipSound ApplyFilters prefix REPLACEMENT.
            // Assigns muffle filters and processes gain & pitch for voice.
            harmony.Patch(
                typeof(VoipSound).GetMethod(nameof(VoipSound.ApplyFilters), BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(short[]), typeof(int) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_VoipSound_ApplyFilters_Prefix))));

            // ItemComponent UpdateSounds prefix REPLACEMENT.
            // Updates muffle and other attributes of ItemComponent sounds. Maintainability note: has high contrast with vanilla implementation.
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.UpdateSounds), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_ItemComponent_UpdateSounds))));

            // StatusEffect UpdateAllProjSpecific prefix REPLACEMENT.
            // Updates muffle and other attributes of StatusEffect sounds.
            harmony.Patch(
                typeof(StatusEffect).GetMethod(nameof(StatusEffect.UpdateAllProjSpecific), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_StatusEffect_UpdateAllProjSpecific))));

            // VoipClient SendToServer prefix REPLACEMENT.
            // Plays bubbles on the client's character when they speak underwater.
            // TODO Surely this is better done in an update loop just checking if the client is speaking?
            harmony.Patch(
                typeof(VoipClient).GetMethod(nameof(VoipClient.SendToServer), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_VoipClient_SendToServer))));

            // VoipClient Read prefix REPLACEMENT.
            // Manages the range, muffle flagging, and spectating changes for voice chat. Maintainability note: has VERY high contrast with vanilla implementation.
            harmony.Patch(
                typeof(VoipClient).GetMethod(nameof(VoipClient.Read), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_VoipClient_Read))));

            // UpdateTransform postfix.
            // Essential to the FocusViewTarget setting. Sets SoundManager.ListenerPosition to the position of the viewed target.
            harmony.Patch(
                typeof(Camera).GetMethod(nameof(Camera.UpdateTransform)),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateTransform))));

            // UpdateWaterAmbience prefix REPLACEMENT.
            // Modifies the volume of the water ambience. Maintainability note: a lot of vanilla code being mirrored in this replacement.
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateWaterAmbience), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateWaterAmbience))));

            // Dispose prefix.
            // Auto remove entries in SoundChannelMuffleInfo, as the keys in this dict are SoundChannels.
            harmony.Patch(
                typeof(SoundChannel).GetMethod(nameof(SoundChannel.Dispose)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel_Dispose))));
#if !LINUX
            // Draw prefix.
            // Displays the eavesdropping text.
            // Bug note: a line in this method causes MonoMod to crash on Linux due to an unmanaged PAL_SEHException https://github.com/dotnet/runtime/issues/78271
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.Draw)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Draw))));
#endif
            // TogglePauseMenu postfix.
            // Displays menu button and updates the config when the menu is closed.
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.TogglePauseMenu)),
                null,
                new HarmonyMethod(typeof(EasySettings).GetMethod(nameof(EasySettings.SPW_TogglePauseMenu))));

            // ShouldMuffleSounds prefix REPLACEMENT (blank).
            // Just returns true. Workaround for ignoring muffling on sounds with "dontmuffle" in their XML. 
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.ShouldMuffleSound)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_ShouldMuffleSound))));

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
                    LuaCsLogger.LogError($"Soundproof Walls: Invalid config from host");
                    return;
                }

                UpdateConfig(newConfig: newServerConfig, oldConfig: Config, isServerConfigEnabled: true, manualUpdate: manualUpdate, configSenderId: configSenderId);
            });

            // Clients receiving word that the admin has disabled syncing.
            GameMain.LuaCs.Networking.Receive("SPW_DisableConfigClient", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];

                string data = msg.ReadString();
                bool manualUpdate = false;
                byte configSenderId = 1;
                DataAppender.RemoveData(data, out manualUpdate, out configSenderId);
                UpdateConfig(newConfig: LocalConfig, oldConfig: Config, isServerConfigEnabled: false, manualUpdate: manualUpdate, configSenderId: configSenderId);
            });

            StartupClient(starting: true);
            Menu.LoadMenu();
        }

        public void UpdateConfig(Config newConfig, Config oldConfig, bool isServerConfigEnabled = false, bool manualUpdate = false, byte configSenderId = 0)
        {
            ShouldStopOrStartMod(newConfig: newConfig, oldConfig: oldConfig, out bool shouldStop, out bool shouldStart);
            bool shouldReloadSounds = ShouldReloadSounds(newConfig: newConfig, oldConfig: oldConfig);
            bool shouldClearMuffleInfo = ShouldClearMuffleInfo(newConfig, oldConfig: oldConfig);

            ServerConfig = isServerConfigEnabled ? newConfig : null;

            if (shouldStop) { ShutdownClient(); }
            else if (shouldStart) { StartupClient(); }
            else if (shouldReloadSounds) { ReloadSounds(); }
            else if (shouldClearMuffleInfo) { SoundChannelMuffleInfo.Clear(); }

            if (manualUpdate && configSenderId != 0)
            {
                string updaterName = GameMain.Client.ConnectedClients.FirstOrDefault(client => client.SessionId == configSenderId)?.Name ?? "unknown";
                if (isServerConfigEnabled)
                {
                    LuaCsLogger.Log($"Soundproof Walls: \"{updaterName}\" {TextManager.Get("spw_updateserverconfig").Value}", Color.LimeGreen);
                }  
                else
                {
                    LuaCsLogger.Log($"Soundproof Walls: \"{updaterName}\" {TextManager.Get("spw_disableserverconfig").Value}", Color.MonoGameOrange);
                }
            }
        }

        public static void ShouldStopOrStartMod(Config newConfig, Config oldConfig, out bool shouldStop, out bool shouldStart)
        {
            shouldStop = newConfig.Enabled == false && oldConfig.Enabled == true;
            shouldStart = newConfig.Enabled == true && oldConfig.Enabled == false;
        }

        public static void SPW_StartRound()
        {
            SetupHydrophoneSwitches(firstStartup: true);
        }

        public void ShutdownClient(bool stopping = false)
        {
            // Stop ExtendedOggSounds from being created.
            harmony.Unpatch(typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(string), typeof(bool) }), HarmonyPatchType.Prefix);
            harmony.Unpatch(typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(ContentXElement), typeof(bool), typeof(string) }), HarmonyPatchType.Prefix);

            ResetAllPitchedSounds();
            //DisposeAllBubbleChannels();
            //DisposeAllHydrophoneChannels();
            DisposeAllCustomSounds(); // TODO verify this negates the need for the above two function calls.
            DisposeAllHydrophoneSwitches();

            // Cleans up any ExtendedOggSounds.
            ReloadSounds(stopping: stopping);
        }

        public void StartupClient(bool starting = false)
        {
            // LoadSounds 1 prefix REPLACEMENT.
            // Replaces OggSound with ExtendedOggSound.
            harmony.Patch(
                typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(string), typeof(bool) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_LoadSound1), BindingFlags.Static | BindingFlags.Public)));

            // LoadSounds 2 prefix REPLACEMENT.
            // Replaces OggSound with ExtendedOggSound.
            harmony.Patch(
                typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(ContentXElement), typeof(bool), typeof(string) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_LoadSound2), BindingFlags.Static | BindingFlags.Public)));

            UploadServerConfig();
            LoadCustomSounds();
            SetupHydrophoneSwitches();

            if (Config.Enabled) { ReloadSounds(starting: starting && Config.MuffleDivingSuits); }
        }

        public static bool SPW_LoadSound1(SoundManager __instance, string filename, bool stream, ref Sound __result)
        {
            if (!Config.Enabled || !Config.MuffleDivingSuits) { return true; }
            

            if (__instance.Disabled) { return false; }

            if (!File.Exists(filename))
            {
                throw new System.IO.FileNotFoundException("Sound file \"" + filename + "\" doesn't exist!");
            }

#if DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            Sound newSound = new ExtendedOggSound(__instance, filename, stream, null);
            lock (__instance.loadedSounds)
            {
                __instance.loadedSounds.Add(newSound);
            }
#if DEBUG
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"Loaded sound \"{filename}\" ({sw.ElapsedMilliseconds} ms).");
#endif
            __result = newSound;

            return false;
        }

        public static bool SPW_LoadSound2(SoundManager __instance, ContentXElement element, bool stream, string overrideFilePath, ref Sound __result)
        {
            if (!Config.Enabled || !Config.MuffleDivingSuits) { return true; }
            if (__instance.Disabled) { return false; }

            string filePath = overrideFilePath ?? element.GetAttributeContentPath("file")?.Value ?? "";
            if (!File.Exists(filePath))
            {
                throw new System.IO.FileNotFoundException($"Sound file \"{filePath}\" doesn't exist! Content package \"{(element.ContentPackage?.Name ?? "Unknown")}\".");
            }

            var newSound = new ExtendedOggSound(__instance, filePath, stream, xElement: element)
            {
                BaseGain = element.GetAttributeFloat("volume", 1.0f)
            };
            float range = element.GetAttributeFloat("range", 1000.0f);
            newSound.BaseNear = range * 0.4f;
            newSound.BaseFar = range;

            lock (__instance.loadedSounds)
            {
                __instance.loadedSounds.Add(newSound);
            }
            __result = newSound;

            return false;
        }

        class HydrophoneSwitch
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

        public enum MuffleReason
        {
            None,
            Suit,
            Eavesdropped,
            NoPath,
            SoundInWater,
            EarsInWater,
            BothInWater
        }

        public enum PlayerBubbleSoundState
        {
            DoNotPlayBubbles,
            PlayRadioBubbles,
            PlayLocalBubbles,
        }

        public class MuffleInfo
        {
            public MuffleReason Reason = MuffleReason.None;
            public MuffleReason PreviousReason = MuffleReason.None;
            public float Distance;
            public float GainMult;

            public bool IgnorePath = false;
            public bool IgnoreWater = false;
            public bool IgnoreSubmersion = false;
            public bool IgnorePitch = false;
            public bool IgnoreLowpass = false;
            public bool IgnoreContainer = false;
            public bool IgnoreAll = false;
            public bool PropagateWalls = false;

            public bool Muffled = false;
            public bool Eavesdropped = false;
            
            // For attenuating other sounds.
            public bool IsLoud = false;
            private float SidechainMult = 0;
            private float Release = 60;

            public Hull? SoundHull;
            public ItemComponent? ItemComp = null;
            public Client? VoiceOwner = null;
            private SoundChannel Channel;

            public MuffleInfo(SoundChannel channel, Hull? soundHull = null, ItemComponent? itemComp = null, Client? voiceOwner = null, ChatMessageType? messageType = null, Item? emitter = null, bool dontMuffle = false, bool dontPitch = false)
            {
                Channel = channel;
                ItemComp = itemComp;
                VoiceOwner = voiceOwner;
                string filename = Channel.Sound.Filename;

                if (StringHasKeyword(filename, Config.IgnoredSounds))
                {
                    IgnoreAll = true;
                }
                else
                {
                    IgnoreLowpass = (dontMuffle && !StringHasKeyword(filename, Config.LowpassForcedSounds)) || StringHasKeyword(filename, Config.LowpassIgnoredSounds);
                    IgnorePitch = dontPitch || StringHasKeyword(filename, Config.PitchIgnoredSounds);
                    IgnorePath = IgnoreLowpass || StringHasKeyword(filename, Config.PathIgnoredSounds, include: "Barotrauma/Content/Sounds/Water/Flow");
                    IgnoreWater = IgnoreLowpass || IgnorePath || !Config.MuffleSubmergedSounds || StringHasKeyword(filename, Config.WaterIgnoredSounds);
                    IgnoreSubmersion = IgnoreLowpass || StringHasKeyword(filename, Config.SubmersionIgnoredSounds, exclude: "Barotrauma/Content/Characters/Human/");
                    IgnoreContainer = IgnoreLowpass || StringHasKeyword(filename, Config.ContainerIgnoredSounds);
                    IgnoreAll = IgnoreLowpass && IgnorePitch;
                    PropagateWalls = !IgnoreAll && !IgnoreLowpass && StringHasKeyword(filename, Config.WallPropagatingSounds);
                    
                    GetCustomSoundData(filename, out GainMult, out SidechainMult, out Release);
                    IsLoud = SidechainMult > 0;
                }
                Update(soundHull, messageType: messageType, emitter: emitter);
                PreviousReason = Reason;
            }
            public void Update(Hull? soundHull = null, ChatMessageType? messageType = null, Item? emitter = null)
            {
                Muffled = false;
                Eavesdropped = false;
                Reason = MuffleReason.None;

                UpdateMuffle(soundHull, messageType, emitter);
                UpdateExtendedReason(messageType);

                if (IsLoud && !Muffled)
                {
                    Sidechain.StartRelease(SidechainMult, Release);
                    LuaCsLogger.Log($"{Path.GetFileName(Channel.Sound.Filename)} is a loud sound. Ducking: {SidechainMult} Recovery: {Release}");
                }
                else if (IsLoud && Muffled && (Reason == MuffleReason.Suit || Reason == MuffleReason.Eavesdropped))
                {
                    Sidechain.StartRelease(SidechainMult / 1.2f, Release / 1.2f);
                    LuaCsLogger.Log($"{Path.GetFileName(Channel.Sound.Filename)} is a loud sound. Wearing suit. Ducking: {SidechainMult} Recovery: {Release}");
                }
            }

            private void UpdateExtendedReason(ChatMessageType? messageType)
            {
                double currentMuffleFreq = double.PositiveInfinity;
                if (Muffled) { currentMuffleFreq = messageType != null ? Config.VoiceLowpassFrequency : Config.GeneralLowpassFrequency; }

                double suitFreq = Config.DivingSuitLowpassFrequency;
                double eavesdroppingFreq = Config.EavesdroppingLowpassFrequency;

                if (messageType == ChatMessageType.Radio || currentMuffleFreq <= suitFreq && currentMuffleFreq <= eavesdroppingFreq) { return; }

                bool isSuitMuffled = IsWearingDivingSuit && Config.MuffleDivingSuits && !IgnoreAll;
                bool isEavesdropMuffled = Eavesdropped && Config.MuffleEavesdropping && !IgnoreAll;
                
                MuffleReason reason = Reason;
                if (isSuitMuffled && suitFreq < currentMuffleFreq)
                {
                    currentMuffleFreq = suitFreq;
                    reason = MuffleReason.Suit;
                }
                if (isEavesdropMuffled && eavesdroppingFreq < currentMuffleFreq)
                {
                    reason = MuffleReason.Eavesdropped;
                }

                Reason = reason;
                Muffled = reason != MuffleReason.None && !IgnoreLowpass;
            }

            private void UpdateMuffle(Hull? soundHull = null, ChatMessageType? messageType = null, Item? emitter = null)
            {
                Character character = Character.Controlled;
                Character? player = VoiceOwner?.Character;
                Limb? playerHead = player?.AnimController?.GetLimb(LimbType.Head);
                Vector2 soundWorldPos = playerHead?.WorldPosition ?? GetSoundChannelPos(Channel);
                SoundHull = soundHull ?? Hull.FindHull(soundWorldPos, player?.CurrentHull ?? character?.CurrentHull);
                Vector2 soundPos = LocalizePosition(soundWorldPos, SoundHull);
                bool soundInWater = SoundInWater(soundPos, SoundHull);
                bool soundContained = emitter != null && !IgnoreContainer && IsContainedWithinContainer(emitter);
                bool spectating = character == null || LightManager.ViewTarget == null;

                // Muffle radio comms underwater to make room for bubble sounds.
                if (messageType == ChatMessageType.Radio)
                {
                    bool wearingDivingGear = IsCharacterWearingDivingGear(player);
                    bool oxygenReqMet = wearingDivingGear && player.Oxygen < 11 || !wearingDivingGear && player.OxygenAvailable < 96;
                    bool ignoreBubbles = StringHasKeyword(player.Name, Config.BubbleIgnoredNames) || IgnoreWater;
                    if (oxygenReqMet && soundInWater && !ignoreBubbles)
                    {
                        Reason = MuffleReason.SoundInWater;
                        Muffled = true;
                    }
                    // Return because radio comms aren't muffled under any other circumstances.
                    return;
                }

                if (spectating)
                {
                    Distance = Vector3.Distance(GameMain.SoundManager.ListenerPosition, new Vector3(soundWorldPos, 0.0f));
                    Muffled = !IgnoreAll && ((soundInWater && !IgnoreWater) || soundContained);
                    if (Muffled && soundInWater) { Reason = MuffleReason.SoundInWater; }
                    else if (Muffled && soundContained) { Reason = MuffleReason.NoPath; }
                    else { Reason = MuffleReason.None; }
                    return;
                }

                Hull? eavesdroppedHull = EavesdroppedHull;
                bool isViewTargetPlayer = IsViewTargetPlayer;
                bool IsEavesdropping = eavesdroppedHull != null && EavesdroppingEfficiency >= Config.EavesdroppingThreshold && !IgnorePath;
                
                Hull? listenHull = IsEavesdropping ? eavesdroppedHull : ViewTargetHull;
                Vector2 listenPos = isViewTargetPlayer ? character.AnimController.GetLimb(LimbType.Head)?.Position ?? character.AnimController.MainLimb.Position : LightManager.ViewTarget.Position;
                Vector2 listenWorldPos = isViewTargetPlayer ? character.AnimController.GetLimb(LimbType.Head)?.WorldPosition ?? character.AnimController.MainLimb.WorldPosition : LightManager.ViewTarget.WorldPosition;

                if (IgnoreAll)
                {
                    Distance = Vector2.Distance(listenPos, soundPos);
                    return;
                }

                // Hydrophone check. Muffle sounds inside your own sub while still hearing sounds in other subs/structures.
                if (IsUsingHydrophones &&
                   (SoundHull == null || SoundHull.Submarine == LightManager.ViewTarget?.Submarine))
                {
                    Distance = SoundHull == null ? Vector2.Distance(listenWorldPos, soundWorldPos) : float.MaxValue;
                    Muffled = Distance == float.MaxValue && !IgnoreLowpass;
                    Reason = Muffled ? MuffleReason.NoPath : MuffleReason.None;
                    return;
                }

                // Allow impact sounds from hitting a wall to be heard on the opposite side.
                if (PropagateWalls && 
                    listenHull != SoundHull && 
                    ShouldPropagateToHull(listenHull, soundHull, soundWorldPos, Config.SoundPropagationRange))
                {
                    LuaCsLogger.Log("SOUND HAS PROPAGATED THROUGH THE WALL");
                    Distance = Vector2.Distance(listenPos, soundPos);
                    Muffled = true;
                    Reason = MuffleReason.Suit; // Apply suit muffle instead of heavy muffle.
                    return;
                }

                Distance = GetApproximateDistance(listenPos, soundPos, listenHull, SoundHull, Channel.Far);
                if (Distance == float.MaxValue)
                {
                    if (IgnorePath)
                    {
                        // Switch to the euclidean distance if the sound ignores paths.
                        Distance = Vector2.Distance(listenPos, soundPos);
                        // Add a slight muffle to these sounds.
                        Reason = MuffleReason.Suit;
                        Muffled = !IgnoreLowpass;
                    }
                    else
                    {
                        Muffled = !IgnoreLowpass;
                        Reason = MuffleReason.NoPath;
                        return;
                    }
                }

                // Position of this is important.
                if (IsEavesdropping)
                {
                    Eavesdropped = true;
                }

                // Muffle sounds in containers.
                if (soundContained)
                {
                    Reason = MuffleReason.NoPath;
                    Muffled = !IgnoreLowpass;
                    return;
                }

                // Muffle the annoying vanilla exosuit sound for the wearer.
                if (!Config.MuffleDivingSuits && IsCharacterWearingExoSuit(character) &&
                    LightManager.ViewTarget as Character == character &&
                    Channel.Sound.Filename.EndsWith("WEAPONS_chargeUp.ogg"))
                {
                    Reason = MuffleReason.NoPath;
                    Muffled = true;
                    return;
                }

                bool earsInWater = EarsInWater;
                bool ignoreSubmersion = IgnoreSubmersion || (isViewTargetPlayer ? !Config.MuffleSubmergedPlayer : !Config.MuffleSubmergedViewTarget);

                // Exceptions to water:
                // Neither in water.
                if (!earsInWater && !soundInWater ||
                     // Both in water, but submersion is ignored.
                     earsInWater && soundInWater && ignoreSubmersion ||
                     // Sound is under, ears are above, but water surface is ignored.
                     IgnoreWater && soundInWater && !earsInWater ||
                     // Sound is above, ears are below, but water surface is ignored.
                     IgnoreWater && !soundInWater && earsInWater && ignoreSubmersion)
                {
                    return;
                }

                // Enable muffling because either the sound is on the opposite side of the water's surface,
                // or both the player and sound are submerged.
                Muffled = true;
                Reason = soundInWater ? MuffleReason.SoundInWater : MuffleReason.EarsInWater;

                // Unique Reason, boosts volume if both the player and sound are submerged in the same hull.
                if (soundInWater && earsInWater) { Reason = MuffleReason.BothInWater; }
            }
        }

        public static Vector2 LocalizePosition(Vector2 worldPos, Hull? posHull)
        {
            Vector2 localPos = worldPos;
            if (posHull?.Submarine != null)
            {
                localPos += -posHull.Submarine.WorldPosition + posHull.Submarine.HiddenSubPosition;
            }

            return localPos;
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

        static void ResetAllPitchedSounds()
        {
            foreach (var kvp in PitchedSounds)
            {
                kvp.Key.FrequencyMultiplier = 1.0f;
            }
            PitchedSounds.Clear();
        }

        public static void DisposeAllCustomSounds()
        {
            BubbleSound?.Dispose();
            RadioBubbleSound?.Dispose();
            ClientBubbleSoundChannels.Clear();

            HydrophoneMovementSound?.Dispose();
            HydrophoneSoundChannels.Clear();

            EavesdroppingAmbience?.Dispose();
            foreach (Sound sound in EavesdroppingActivationSounds)
            {
                sound?.Dispose();
            }
        }

        public static void DisposeAllHydrophoneChannels()
        {
            foreach (var kvp in HydrophoneSoundChannels)
            {
                kvp.Key.FadeOutAndDispose();
            }
            HydrophoneSoundChannels.Clear();
        }

        public static void DisposeAllBubbleChannels()
        {
            foreach (var kvp in ClientBubbleSoundChannels)
            {
                StopBubbleSound(kvp.Key);
            }
            ClientBubbleSoundChannels.Clear();
        }

        public static string? GetModDirectory()
        {
            string? path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            while (!path.EndsWith("3153737715") && !path.EndsWith("Soundproof Walls"))
            {
                path = Directory.GetParent(path)?.FullName;
                if (path == null) break;
            }

            return path;
        }

        public static void LoadCustomSounds()
        {
            try
            {
                string? modPath = GetModDirectory();
                HydrophoneMovementSound = GameMain.SoundManager.LoadSound("Content/Sounds/Water/SplashLoop.ogg");
                BubbleSound = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_BubblesLoopMono.ogg"));
                RadioBubbleSound = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_RadioBubblesLoopStereo.ogg"));
                EavesdroppingAmbience = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingAmbience.ogg"));

                Sound eavesdropSound1 = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingActivation1.ogg"));
                Sound eavesdropSound2 = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_EavesdroppingActivation2.ogg"));
                EavesdroppingActivationSounds[0] = eavesdropSound1;
                EavesdroppingActivationSounds[1] = eavesdropSound2;
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"Soundproof Walls: Failed to load custom sounds\n{ex.Message}");
            }
        }

        // Called every 5 seconds or when the client changes a setting.
        public static void UploadServerConfig(bool manualUpdate = false)
        {
            if (!GameMain.IsMultiplayer) { return; }

            foreach (Client client in GameMain.Client.ConnectedClients)
            {
                if (client.IsOwner || client.HasPermission(ClientPermissions.Ban))
                {
                    // TODO I could merge both of these signals into one. I could search the string server-side for the state of the SyncSettings to discern what to do.
                    if (LocalConfig.SyncSettings)
                    {
                        string data = DataAppender.AppendData(JsonSerializer.Serialize(LocalConfig), manualUpdate, GameMain.Client.SessionId);
                        IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_UpdateConfigServer");
                        message.WriteString(data);
                        GameMain.LuaCs.Networking.Send(message);
                    }
                    // Remove the server config for all users.
                    else if (!LocalConfig.SyncSettings && ServerConfig != null)
                    {
                        string data = DataAppender.AppendData("_", manualUpdate, GameMain.Client.SessionId);
                        IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_DisableConfigServer");
                        message.WriteString(data);
                        GameMain.LuaCs.Networking.Send(message);
                    }

                    return;
                }
            }
        }

        // Returns true if there's a mismatch in suit/general lowpass frequencies.
        public static bool ShouldReloadSounds(Config newConfig, Config? oldConfig = null)
        {
            if (oldConfig == null) { oldConfig = Config; }

            double vanillaFreq = SoundPlayer.MuffleFilterFrequency;
            double vanillaSuitFreq = vanillaFreq;

            double oldNormFreq = oldConfig.Enabled ? oldConfig.GeneralLowpassFrequency : vanillaFreq;
            bool oldSuitMuffling = oldConfig.Enabled && oldConfig.MuffleDivingSuits;
            double oldSuitFreq = oldSuitMuffling ? oldConfig.DivingSuitLowpassFrequency : vanillaSuitFreq;

            double newNormFreq = newConfig.Enabled ? newConfig.GeneralLowpassFrequency : vanillaFreq;
            bool newSuitMuffling = newConfig.Enabled && newConfig.MuffleDivingSuits;
            double newSuitFreq = newSuitMuffling ? newConfig.DivingSuitLowpassFrequency : vanillaSuitFreq;

            return oldNormFreq != newNormFreq || oldSuitFreq != newSuitFreq;
        }

        public static bool ShouldClearMuffleInfo(Config newConfig, Config? oldConfig = null)
        {
            if (oldConfig == null) { oldConfig = Config; }

            return !oldConfig.IgnoredSounds.SetEquals(newConfig.IgnoredSounds) ||
                    !oldConfig.PitchIgnoredSounds.SetEquals(newConfig.PitchIgnoredSounds) ||
                    !oldConfig.LowpassIgnoredSounds.SetEquals(newConfig.LowpassIgnoredSounds) ||
                    !oldConfig.ContainerIgnoredSounds.SetEquals(newConfig.ContainerIgnoredSounds) ||
                    !oldConfig.PathIgnoredSounds.SetEquals(newConfig.PathIgnoredSounds) ||
                    !oldConfig.WaterIgnoredSounds.SetEquals(newConfig.WaterIgnoredSounds) ||
                    !oldConfig.SubmersionIgnoredSounds.SetEquals(newConfig.SubmersionIgnoredSounds) ||
                    !oldConfig.BubbleIgnoredNames.SetEquals(newConfig.SubmersionIgnoredSounds);
        }

        private static Sound GetNewSound(Sound oldSound)
        {
            if (oldSound.XElement != null)
            {
                return GameMain.SoundManager.LoadSound(oldSound.XElement, oldSound.Stream, oldSound.Filename);
            }
            else
            {
                return GameMain.SoundManager.LoadSound(oldSound.Filename, oldSound.Stream);
            }
        }

        private static void ReloadRoundSounds(Dictionary<string, Sound> updatedSounds, bool onlyLoadExtendedOggs = false, bool onlyUnloadExtendedOggs = false)
        {
            int t = 0;
            int i = 0;
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (RoundSound roundSound in RoundSound.roundSounds)
            {
                i++;
                Sound? oldSound = roundSound.Sound;

                if (oldSound == null ||
                        (onlyLoadExtendedOggs && oldSound is ExtendedOggSound) ||
                        (onlyUnloadExtendedOggs && oldSound is not ExtendedOggSound))
                { continue; }

                if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                {
                    newSound = GetNewSound(oldSound);
                    updatedSounds.Add(oldSound.Filename, newSound);
                    t++;
                }

                roundSound.Sound = newSound;
                SoundsToDispose.Add(oldSound);
            }

            sw.Stop();
            LuaCsLogger.Log($"Created {t} new round sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }
        private static void AllocateCharacterSounds(Dictionary<string, Sound> updatedSounds, bool onlyLoadExtendedOggs = false, bool onlyUnloadExtendedOggs = false)
        {
            int t = 0;
            int i = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead) { continue; }
                foreach (CharacterSound characterSound in character.sounds)
                {
                    i++;
                    Sound? oldSound = characterSound.roundSound.Sound;

                    if (oldSound == null ||
                        (onlyLoadExtendedOggs && oldSound is ExtendedOggSound) ||
                        (onlyUnloadExtendedOggs && oldSound is not ExtendedOggSound))
                        { continue; }

                    if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                    {
                        newSound = GetNewSound(oldSound);
                        updatedSounds.Add(oldSound.Filename, newSound);
                        t++;
                    }

                    characterSound.roundSound.Sound = newSound;
                    SoundsToDispose.Add(oldSound);
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"Created {t} new character sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }

        private static void AllocateComponentSounds(Dictionary<string, Sound> updatedSounds, bool onlyLoadExtendedOggs = false, bool onlyUnloadExtendedOggs = false)
        {
            int t = 0;
            int i = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (Item item in Item.ItemList)
            {
                foreach (ItemComponent itemComponent in item.Components)
                {
                    foreach (var kvp in itemComponent.sounds)
                    {
                        itemComponent.StopSounds(kvp.Key);
                        foreach (ItemSound itemSound in kvp.Value)
                        {
                            i++;
                            Sound? oldSound = itemSound.RoundSound.Sound;

                            if (oldSound == null ||
                                (onlyLoadExtendedOggs && oldSound is ExtendedOggSound) ||
                                (onlyUnloadExtendedOggs && oldSound is not ExtendedOggSound))
                                { continue; }

                            if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                            {
                                newSound = GetNewSound(oldSound);
                                updatedSounds.Add(oldSound.Filename, newSound);
                                t++;
                            }

                            itemSound.RoundSound.Sound = newSound;
                            SoundsToDispose.Add(oldSound);
                        }
                    }
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"Created {t} new comp sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }

        private static void AllocateStatusEffectSounds(Dictionary<string, Sound> updatedSounds, bool starting = false, bool stopping = false)
        {
            int t = 0;
            int i = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (StatusEffect statusEffect in StatusEffect.ActiveLoopingSounds)
            {
                foreach (RoundSound roundSound in statusEffect.Sounds)
                {
                    i++;
                    Sound? oldSound = roundSound.Sound;

                    if (oldSound == null ||
                        (starting && oldSound is ExtendedOggSound) ||
                        (stopping && oldSound is not ExtendedOggSound))
                        { continue; }

                    if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                    {
                        newSound = GetNewSound(oldSound);
                        updatedSounds.Add(oldSound.Filename, newSound);
                        t++;
                    }

                    roundSound.Sound = newSound;
                    SoundsToDispose.Add(oldSound);
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"Created {t} new status effect sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }

        private static void ReloadPrefabSounds(Dictionary<string, Sound> updatedSounds, bool onlyLoadExtendedOggs = false, bool onlyUnloadExtendedOggs = false)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            int t = 0;
            foreach (SoundPrefab soundPrefab in SoundPrefab.Prefabs)
            {
                Sound oldSound = soundPrefab.Sound;

                if (oldSound == null ||
                    (onlyLoadExtendedOggs && oldSound is ExtendedOggSound) ||
                    (onlyUnloadExtendedOggs && oldSound is not ExtendedOggSound) ||
                    (!onlyUnloadExtendedOggs && StringHasKeyword(oldSound.Filename, IgnoredPrefabs)))
                    { continue; }

                Sound newSound = GetNewSound(oldSound);
                soundPrefab.Sound = newSound;
                SoundsToDispose.Add(oldSound);
                t++;
            }

            sw.Stop();
            LuaCsLogger.Log($"Created {t} new Sound prefab sounds ({sw.ElapsedMilliseconds} ms)");
        }

        private static void ClearSoundsToDispose()
        {
            foreach (Sound sound in SoundsToDispose)
            {
                // Kills channels and removes from loaded sound list.
                sound.Dispose();
            }
            SoundsToDispose.Clear();
        }

        // Compatibility with ReSound.
        private static void StopResound(MoonSharp.Interpreter.DynValue Resound)
        {
            if (Resound.Type == MoonSharp.Interpreter.DataType.Table)
            {
                MoonSharp.Interpreter.Table resoundTable = Resound.Table;
                MoonSharp.Interpreter.DynValue stopFunction = resoundTable.Get("StopMod");
                if (stopFunction.Type == MoonSharp.Interpreter.DataType.Function)
                {
                GameMain.LuaCs.Lua.Call(stopFunction);
                }
            }
        }

        private static void StartResound(MoonSharp.Interpreter.DynValue Resound)
        {
            if (Resound.Type == MoonSharp.Interpreter.DataType.Table)
            {
                MoonSharp.Interpreter.Table resoundTable = Resound.Table;
                MoonSharp.Interpreter.DynValue startFunction = resoundTable.Get("StartMod");
                if (startFunction.Type == MoonSharp.Interpreter.DataType.Function)
                {
                    GameMain.LuaCs.Lua.Call(startFunction);
                }
            }
        }

        static void StopAllPlayingChannels()
        {
            foreach (SoundChannel[] channelGroup in GameMain.SoundManager.playingChannels)
            {
                foreach (SoundChannel channel in channelGroup)
                {
                    if (channel != null) { channel.Dispose(); }
                }
            }
        }

        public static void ReloadSounds(bool starting = false, bool stopping = false)
        {
            LuaCsLogger.Log("Soundproof Walls: ReloadSounds() started running.");
            MoonSharp.Interpreter.DynValue Resound = GameMain.LuaCs.Lua.Globals.Get("Resound");

            // Stop ReSound if SPW is reloaded mid-round or end of round.
            // ReSound has its own code to stop at the end of the round but it needs to happen here and now before SPW.
            if (!starting) { StopResound(Resound); }

            SoundChannelMuffleInfo.Clear();

            StopAllPlayingChannels();

            Dictionary<string, Sound> updatedSounds = new Dictionary<string, Sound>();
            ReloadPrefabSounds(updatedSounds, starting, stopping);
            ReloadRoundSounds(updatedSounds, starting, stopping);
            AllocateStatusEffectSounds(updatedSounds, starting, stopping);
            AllocateCharacterSounds(updatedSounds, starting, stopping);
            AllocateComponentSounds(updatedSounds, starting, stopping);
            LuaCsLogger.Log($"There are {GameMain.SoundManager.LoadedSoundCount} loaded sounds total");
            updatedSounds.Clear();

            ClearSoundsToDispose();

            // We only start ReSound if it has been stopped for a mid-round SPW reload. Otherwise, let ReSound start itself.  
            if (!stopping && !starting) { StartResound(Resound); }
            LuaCsLogger.Log("Soundproof Walls: ReloadSounds() stopped running.");
        }

        public static void SetupHydrophoneSwitches(bool firstStartup = false)
        {
            DisposeAllHydrophoneSwitches();

            if (!Config.Enabled || !Config.HydrophoneSwitchEnabled || !RoundStarted) { return; }

            foreach (Item item in Item.RepairableItems)
            {
                if (item.Tags.Contains("command"))
                {
                    Sonar? sonar = item.GetComponent<Sonar>();
                    if (sonar == null) { continue ; }

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
            if (Config.HydrophoneLegacySwitch)
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

            // If hydrophone efficiency increases back to 1 while not looking at a terminal. TODO could merge this with the above "else if"
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

                container.RectTransform.AbsoluteOffset = new Point(0, -amountRaised*2);

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
            if (!Config.Enabled || !IsUsingHydrophones) { return; }

            float range = Config.HydrophoneSoundRange;

            foreach (Character character in Character.CharacterList)
            {
                if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), character.WorldPosition) > range * range || 
                    HydrophoneSoundChannels.Any(kvp => kvp.Value == character) || 
                    character.CurrentHull != null || 
                    character.CurrentSpeed < 0.05 || 
                    character.isDead)
                {
                    continue;
                }

                float startingGain = 1 * MathUtils.InverseLerp(0f, 2f, character.CurrentSpeed) * HydrophoneEfficiency;
                float speed = Math.Clamp(character.CurrentSpeed, 0f, 10f);
                float freqMult = MathHelper.Lerp(0.25f, 4f, speed / 10f);

                SoundChannel? channel = HydrophoneMovementSound?.Play(startingGain, range, freqMult, character.WorldPosition, false);

                if (channel != null)
                {
                    channel.Looping = true;
                    HydrophoneSoundChannels[channel] = character;
                }
            }
        }

        public static void UpdateHydrophoneSounds()
        {
            if (!Config.Enabled || !IsUsingHydrophones)
            {
                foreach (var kvp in HydrophoneSoundChannels)
                {
                    kvp.Key.FadeOutAndDispose();
                }
                HydrophoneSoundChannels.Clear();
                return;
            }

            foreach (var kvp in HydrophoneSoundChannels)
            {
                SoundChannel channel = kvp.Key;
                Character character = kvp.Value;

                if (channel == null || character == null || Character.Controlled == null) { continue; }

                float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, Character.Controlled.WorldPosition);
                channel.Gain = 1 * MathUtils.InverseLerp(0f, 2f, character.CurrentSpeed) * HydrophoneEfficiency;

                if (distanceSquared > channel.far * channel.far || channel.Gain < 0.001f || character.CurrentHull != null || character.isDead)
                {
                    HydrophoneSoundChannels.Remove(channel);
                    SoundChannelMuffleInfo.TryRemove(channel, out MuffleInfo? _);
                    channel.FadeOutAndDispose();
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

        static Limb GetCharacterHead(Character character)
        {
            // It's weird defaulting to the body but who knows what people might mod into the game.
            return character.AnimController.GetLimb(LimbType.Head) ?? character.AnimController.MainLimb;
        }

        static void UpdateEavesdroppingSounds()
        {
            Sound? sound = EavesdroppingAmbience;
            SoundChannel? channel = EavesdroppingAmbienceSoundChannel;

            bool isPlaying = channel != null && channel.IsPlaying;
            bool shouldPlay = EavesdroppingEfficiency > 0;

            if (sound == null || !shouldPlay && !isPlaying)
            {
                return;
            }
            if (shouldPlay && !isPlaying)
            {
                channel = SoundPlayer.PlaySound(EavesdroppingAmbience, new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), volume: 0.1f, freqMult: MathHelper.Lerp(0.25f, 1, EavesdroppingEfficiency), ignoreMuffling: true);
                if (channel != null) { channel.Looping = true; }
            }
            else if (shouldPlay && isPlaying) 
            {
                channel.FrequencyMultiplier = MathHelper.Lerp(0.25f, 1, Math.Clamp(EavesdroppingEfficiency * 2, 0, 1));
                channel.Gain = MathHelper.Lerp(0.1f, 1, Math.Clamp(EavesdroppingEfficiency * 3, 0, 1));
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

        static void UpdateEavesdropping()
        {

            bool shouldFadeOut = EavesdroppedHull == null || !Config.Enabled;

            UpdateEavesdroppingSounds();

            if (shouldFadeOut && EavesdroppingTextAlpha <= 0 && EavesdroppingEfficiency <= 0) { return; }

            else if (shouldFadeOut)
            {
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha - 15, 0, 255);
                EavesdroppingEfficiency = Math.Clamp(EavesdroppingEfficiency - 0.04f, 0, 1);
            }
            else if (!shouldFadeOut)
            {
                // Play activation sound.
                if (EavesdroppingEfficiency < 0.01 && 
                    EavesdroppingActivationSounds[0] != null && 
                    EavesdroppingActivationSounds[1] != null &&
                    !EavesdroppingActivationSounds[0].IsPlaying() &&
                    !EavesdroppingActivationSounds[1].IsPlaying())
                {
                    Random random = new Random();
                    int randomIndex = random.Next(EavesdroppingActivationSounds.Length);
                    Sound eavesdropSound = EavesdroppingActivationSounds[randomIndex];
                    SoundChannel channel = eavesdropSound.Play(null, 1, muffle: false); //SoundPlayer.PlaySound(eavesdropSound, Character.Controlled.Position, volume: 10, ignoreMuffling: true);

                    // TODO delete this.
                    if (channel != null && Character.Controlled != null)
                    {
                        channel.Gain = 1;
                    }
                }

                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha + 15, 0, 255);
                EavesdroppingEfficiency = Math.Clamp(EavesdroppingEfficiency + 0.01f, 0, 1);
            }
        }

        // Draws the eavesdropping text.
        public static bool SPW_Draw(ref Camera cam, ref SpriteBatch spriteBatch)
        {
            Character character = Character.Controlled;

            if (character == null || cam == null) { return true; }

            Limb limb = GetCharacterHead(character);
            Vector2 position = cam.WorldToScreen(limb.body.DrawPosition + new Vector2(0, 42));
            LocalizedString text = TextManager.Get("spw_listening");
            float size = 1.4f;
            Color color = new Color(224, 214, 164, (int)EavesdroppingTextAlpha);
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
                Character character = __instance.gameClient.Character;
                if (character != null)
                {
                    Limb playerHead = GetCharacterHead(character);
                    Hull limbHull = playerHead.Hull;
                    bool wearingDivingGear = IsCharacterWearingDivingGear(character);
                    bool oxygenReqMet = wearingDivingGear && character.Oxygen < 11 || !wearingDivingGear && character.OxygenAvailable < 96;
                    bool ignoreBubbles = StringHasKeyword(character.Name, Config.BubbleIgnoredNames);
                    if (oxygenReqMet && SoundInWater(playerHead.Position, limbHull) && character.SpeechImpediment < 100 && !ignoreBubbles)
                    {
                        GameMain.ParticleManager.CreateParticle(
                            "bubbles",
                            playerHead.WorldPosition,
                            velocity: playerHead.LinearVelocity * 10,
                            rotation: 0,
                            limbHull);
                    }
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
                //TODO only print when debugging
                DebugConsole.LogError("Failed to find voip queue");
                return false;
            }

            Client client = instance.gameClient.ConnectedClients.Find(c => c.VoipQueue == queue);
            bool clientAlive = client.Character != null && !client.Character.IsDead && !client.Character.Removed;
            bool clientCantSpeak = client.Muted || client.MutedLocally || (clientAlive && client.Character.SpeechImpediment >= 100.0f);

            if (!queue.Read(msg, discardData: clientCantSpeak) || clientCantSpeak)
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

            float speechImpedimentMultiplier = 1.0f - client.Character.SpeechImpediment / 100.0f;
            bool spectating = Character.Controlled == null;
            float localRangeMultiplier = 1 * Config.VoiceRangeMultiplier;
            float radioRangeMultiplier = 1 * Config.RadioRangeMultiplier;
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
                client.VoipSound.UsingRadio = false;
                client.VoipSound.SetRange(ChatMessage.SpeakRangeVOIP * VoipClient.RangeNear * speechImpedimentMultiplier * localRangeMultiplier, ChatMessage.SpeakRangeVOIP * speechImpedimentMultiplier * localRangeMultiplier);
            }

            // Muffle Info stuff.
            SoundChannel channel = client.VoipSound.soundChannel;
            if (channel == null) { return true; }

            bool needsUpdate = true;
            if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
            {
                muffleInfo = new MuffleInfo(channel, client.Character.CurrentHull, voiceOwner: client, messageType: messageType);
                SoundChannelMuffleInfo[channel] = muffleInfo;
                needsUpdate = false;
            }

            if (needsUpdate)
            {
                muffleInfo.Update(client.Character.CurrentHull, messageType: messageType);
            }

            client.VoipSound.UseMuffleFilter = muffleInfo.Muffled;
            client.VoipSound.UseRadioFilter = messageType == ChatMessageType.Radio && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters;

            return false;
        }

        // Get a client's messageType (same implementation seen in VoipClient_Read method).
        public static ChatMessageType GetMessageType(Client client)
        {
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

            return messageType;
        }

        static bool IsCharacterWearingDivingGear(Character character)
        {
            Identifier id = new Identifier("diving");
            Item outerItem = character.Inventory.GetItemInLimbSlot(InvSlotType.OuterClothes);
            Item headItem = character.Inventory.GetItemInLimbSlot(InvSlotType.Head);

            return (outerItem != null && outerItem.HasTag(id)) || (headItem != null && headItem.HasTag(id));
        }

        static bool IsCharacterWearingExoSuit(Character character)
        {
            Identifier id = new Identifier("deepdivinglarge");
            Item outerItem = character.Inventory.GetItemInLimbSlot(InvSlotType.OuterClothes);

            return outerItem != null && outerItem.HasTag(id);
        }

        public static void StopBubbleSound(Client client)
        {
            if (ClientBubbleSoundChannels.TryGetValue(client, out SoundChannel? bubbleChannel) && bubbleChannel != null)
            {
                // The redundancy of these operations is an echo of the infinite bubble bug of old.
                bubbleChannel.FrequencyMultiplier = 1.0f;
                bubbleChannel.Looping = false;
                bubbleChannel.Gain = 0;
                bubbleChannel.Dispose();
                ClientBubbleSoundChannels.TryRemove(client, out SoundChannel? _);
            }
        }

        public static void UpdateBubbleSounds(Client client)
        {
            PlayerBubbleSoundState state = PlayerBubbleSoundState.DoNotPlayBubbles; // Default to not playing.

            Character? player = client.Character;
            Limb? playerHead = player?.AnimController?.GetLimb(LimbType.Head);

            SoundChannel? voiceChannel = client.VoipSound?.soundChannel;

            if (voiceChannel == null || player == null || playerHead == null)
            {
                StopBubbleSound(client);
                return;
            }

            Vector2 soundWorldPos = playerHead.WorldPosition;
            Hull soundHull = Hull.FindHull(soundWorldPos, player.CurrentHull);
            Vector2 soundPos = LocalizePosition(soundWorldPos, soundHull);

            bool soundInWater = SoundInWater(soundPos, soundHull);
            var messageType = GetMessageType(client);

            bool wearingDivingGear = IsCharacterWearingDivingGear(player);
            bool oxygenReqMet = wearingDivingGear && player.Oxygen < 11 || !wearingDivingGear && player.OxygenAvailable < 96;
            bool ignoreBubbles = StringHasKeyword(player.Name, Config.BubbleIgnoredNames);
            bool isPlaying = ClientBubbleSoundChannels.TryGetValue(client, out SoundChannel? currentBubbleChannel) && currentBubbleChannel != null;
            bool soundMatches = true;

            if (isPlaying)
            {
                soundMatches = currentBubbleChannel.Sound.Filename == RadioBubbleSound?.Filename && messageType == ChatMessageType.Radio ||
                               currentBubbleChannel.Sound.Filename == BubbleSound?.Filename && messageType != ChatMessageType.Radio;
            }

            // Check if bubbles should be playing.
            if (soundMatches && soundInWater && oxygenReqMet && !ignoreBubbles)
            {
                state = messageType == ChatMessageType.Radio ? PlayerBubbleSoundState.PlayRadioBubbles : PlayerBubbleSoundState.PlayLocalBubbles;
            }

            if (state == PlayerBubbleSoundState.DoNotPlayBubbles)
            {
                StopBubbleSound(client);
                return;
            }

            if (isPlaying) // Continue playing.
            {
                if (state == PlayerBubbleSoundState.PlayRadioBubbles) // Continue radio
                {
                    currentBubbleChannel.Position = GameMain.SoundManager.ListenerPosition;
                    currentBubbleChannel.FrequencyMultiplier = MathHelper.Lerp(0.85f, 1.15f, MathUtils.InverseLerp(0, 2, player.CurrentSpeed));
                }
                else if (state == PlayerBubbleSoundState.PlayLocalBubbles) // Continue local
                {
                    currentBubbleChannel.Position = new Vector3(playerHead.WorldPosition, 0);
                    currentBubbleChannel.FrequencyMultiplier = MathHelper.Lerp(1, 4, MathUtils.InverseLerp(0, 2, player.CurrentSpeed));
                }

                GameMain.ParticleManager.CreateParticle(
                    "bubbles",
                    playerHead.WorldPosition,
                    velocity: playerHead.LinearVelocity * 10,
                    rotation: 0,
                    playerHead.Hull);
            }

            else // New sound
            {
                SoundChannel? newBubbleChannel = null;
                if (state == PlayerBubbleSoundState.PlayRadioBubbles) // Start radio
                {
                    newBubbleChannel = SoundPlayer.PlaySound(RadioBubbleSound, new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), volume: 0.7f, freqMult: MathHelper.Lerp(0.85f, 1.15f, MathUtils.InverseLerp(0, 2, player.CurrentSpeed)), ignoreMuffling: true);
                }
                else if (state == PlayerBubbleSoundState.PlayLocalBubbles) // Start local
                {
                    newBubbleChannel = SoundPlayer.PlaySound(BubbleSound, playerHead.WorldPosition, volume: 1, range: 350, freqMult: MathHelper.Lerp(1, 4, MathUtils.InverseLerp(0, 2, player.CurrentSpeed)), ignoreMuffling: true);
                }
                
                if (newBubbleChannel != null)
                {
                    newBubbleChannel.Looping = true;
                    ClientBubbleSoundChannels[client] = newBubbleChannel;
                }
            }
        }

        public static bool SPW_VoipSound_ApplyFilters_Prefix(VoipSound __instance, ref short[] buffer, ref int readSamples)
        {
            VoipSound voipSound = __instance;

            // Early return.
            if (!Config.Enabled || voipSound == null || !voipSound.IsPlaying ||
                !SoundChannelMuffleInfo.TryGetValue(voipSound.soundChannel, out MuffleInfo muffleInfo))
            {
                return true; 
            }

            if (voipSound.UseMuffleFilter)
            {
                // Update muffle filters if they have been changed.
                if (VoipNormalMuffleFilter._frequency != Config.VoiceLowpassFrequency)
                {
                    VoipNormalMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.VoiceLowpassFrequency);
                }
                if (VoipSuitMuffleFilter._frequency != Config.DivingSuitLowpassFrequency)
                {
                    VoipSuitMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.DivingSuitLowpassFrequency);
                }
                if (VoipEavesdroppingMuffleFilter._frequency != Config.EavesdroppingLowpassFrequency)
                {
                    VoipEavesdroppingMuffleFilter = new LowpassFilter(VoipConfig.FREQUENCY, Config.EavesdroppingLowpassFrequency);
                }
            }

            BiQuad muffleFilter = VoipNormalMuffleFilter;
            if (muffleInfo.Reason == MuffleReason.Suit)
            {
                muffleFilter = VoipSuitMuffleFilter;
            }
            else if (muffleInfo.Reason == MuffleReason.Eavesdropped)
            {
                muffleFilter = VoipEavesdroppingMuffleFilter;
            }

            // Update radio filter if it has been changed.
            if (voipSound.UseRadioFilter &&
                (VoipRadioFilter.frequency != Config.RadioBandpassFrequency ||
                VoipRadioFilter.q != Config.RadioBandpassQualityFactor ||
                VoipRadioFilter.distortionAmount != Config.RadioDistortion ||
                VoipRadioFilter.staticAmount != Config.RadioStatic ||
                VoipRadioFilter.compressionThreshold != Config.RadioCompressionThreshold ||
                VoipRadioFilter.compressionRatio != Config.RadioCompressionRatio))
            {
                VoipRadioFilter = new RadioFilter(VoipConfig.FREQUENCY, Config.RadioBandpassFrequency, Config.RadioBandpassQualityFactor, Config.RadioDistortion, Config.RadioStatic, Config.RadioCompressionThreshold, Config.RadioCompressionRatio);
            }

            // Vanilla method & changes.
            float finalGain = voipSound.gain * GameSettings.CurrentConfig.Audio.VoiceChatVolume * voipSound.client.VoiceVolume;
            for (int i = 0; i < readSamples; i++)
            {
                float fVal = ToolBox.ShortAudioSampleToFloat(buffer[i]);

                if (finalGain > 1.0f) //TODO: take distance into account?
                {
                    fVal = Math.Clamp(fVal * finalGain, -1f, 1f);
                }

                if (voipSound.UseMuffleFilter)
                {
                    fVal = muffleFilter.Process(fVal);
                }
                if (voipSound.UseRadioFilter)
                {
                    fVal = Math.Clamp(VoipRadioFilter.Process(fVal) * VoipSound.PostRadioFilterBoost, -1f, 1f);
                    //fVal = VoipRadioFilter.Process(fVal);
                }
                buffer[i] = ToolBox.FloatToShortAudioSample(fVal);
            }

            ProcessVoipSound(voipSound, muffleInfo);

            return false;
        }

        // Runs at the start of the SoundChannel disposing method.
        public static void SPW_SoundChannel_Dispose(SoundChannel __instance)
        {
            if (!Config.Enabled) { return; };

            __instance.Looping = false;

            SoundChannelMuffleInfo.TryRemove(__instance, out MuffleInfo? _);
            HydrophoneSoundChannels.Remove(__instance);
            PitchedSounds.TryRemove(__instance, out bool _);
        }

        public static void SPW_Update()
        {
            if (!GameMain.Instance.Paused)
            {
                Sidechain.Process();
                UpdateEavesdropping();
            }

            // Must be above the early return so the config being disabled can be enforced automatically.
            if (Timing.TotalTime > LastConfigUploadTime + 5)
            {
                LastConfigUploadTime = (float)Timing.TotalTime;
                UploadServerConfig(manualUpdate: false);
            }

            if (!Config.Enabled || !RoundStarted) { return; }

            // Bubble sound stuff.
            if (GameMain.IsMultiplayer && Timing.TotalTime > LastBubbleUpdateTime + 0.2f)
            {
                LastBubbleUpdateTime = (float)Timing.TotalTime;

                // In case a client disconnects while their bubble channel is playing.
                foreach (var kvp in ClientBubbleSoundChannels)
                {
                    Client client = kvp.Key;

                    if (!GameMain.Client.ConnectedClients.Contains(client))
                    {
                        StopBubbleSound(client);
                    }
                }

                // Start, stop, or continue bubble sounds.
                foreach (Client client in GameMain.Client.ConnectedClients)
                {
                    UpdateBubbleSounds(client);
                }
            }

            if (Character.Controlled == null || LightManager.ViewTarget == null || GameMain.Instance.Paused) { return; }

            // Hydrophone stuff.
            if (Timing.TotalTime > LastHydrophonePlayTime + 0.1)
            {
                PlayHydrophoneSounds();
                LastHydrophonePlayTime = (float)Timing.TotalTime;
            }
            UpdateHydrophoneSounds();
            UpdateHydrophoneSwitches();
        }

        // Stop the ItemComp PlaySound method from running if the itemSound.Range is too short, meaning the loopingSoundChannel is null.
        public static bool SPW_ItemComponent_PlaySound(ref ItemSound itemSound, ref Vector2 position)
        {
            if (!Config.Enabled || Config.SoundRangeMultiplier >= 1 || !RoundStarted) { return true; }

            float range = itemSound.Range;

            if (!IsUsingHydrophones)
            {
                range *= Config.SoundRangeMultiplier;
            }
            else
            {
                Hull soundHull = Hull.FindHull(position, Character.Controlled?.CurrentHull, true);
                if (soundHull == null || soundHull.Submarine != Character.Controlled?.Submarine)
                {
                    range += Config.HydrophoneSoundRange;
                }
                else
                {
                    range *= Config.SoundRangeMultiplier;
                }
            }

            if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), position) > range * range)
            {
                return false;
            }

            return true;
        }

        // This method is soley patched to duck the music when a loud sound plays. Look at this thing. Maintainability nightmare :(
        public static bool SPW_SoundPlayer_UpdateMusic(float deltaTime)
        {
            if (!Config.Enabled || !Config.Sidechaining) { return true; }

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
                    IEnumerable<BackgroundMusic> suitableNoiseLoops = Screen.Selected == GameMain.GameScreen ?
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
                //nothing should be playing on this channel
                if (SoundPlayer.targetMusic[i] == null)
                {
                    if (SoundPlayer.musicChannel[i] != null && SoundPlayer.musicChannel[i].IsPlaying)
                    {
                        //mute the channel
                        SoundPlayer.musicChannel[i].Gain = MathHelper.Lerp(SoundPlayer.musicChannel[i].Gain, 0.0f, SoundPlayer.MusicLerpSpeed * deltaTime) * (1 - Sidechain.SidechainMultiplier);
                        if (SoundPlayer.musicChannel[i].Gain < 0.01f) { SoundPlayer.DisposeMusicChannel(i); }
                    }
                }
                //something should be playing, but the targetMusic is invalid
                else if (!SoundPlayer.musicClips.Any(mc => mc == SoundPlayer.targetMusic[i]))
                {
                    SoundPlayer.targetMusic[i] = SoundPlayer.GetSuitableMusicClips(SoundPlayer.targetMusic[i].Type, 0.0f).GetRandomUnsynced();
                }
                //something should be playing, but the channel is playing nothing or an incorrect clip
                else if (SoundPlayer.currentMusic[i] == null || SoundPlayer.targetMusic[i] != SoundPlayer.currentMusic[i])
                {
                    //something playing -> mute it first
                    if (SoundPlayer.musicChannel[i] != null && SoundPlayer.musicChannel[i].IsPlaying)
                    {
                        SoundPlayer.musicChannel[i].Gain = MathHelper.Lerp(SoundPlayer.musicChannel[i].Gain, 0.0f, SoundPlayer.MusicLerpSpeed * deltaTime) * (1 - Sidechain.SidechainMultiplier);
                        if (SoundPlayer.musicChannel[i].Gain < 0.01f) { SoundPlayer.DisposeMusicChannel(i); }
                    }
                    //channel free now, start playing the correct clip
                    if (SoundPlayer.currentMusic[i] == null || (SoundPlayer.musicChannel[i] == null || !SoundPlayer.musicChannel[i].IsPlaying))
                    {
                        SoundPlayer.DisposeMusicChannel(i);

                        SoundPlayer.currentMusic[i] = SoundPlayer.targetMusic[i];
                        SoundPlayer.musicChannel[i] = SoundPlayer.currentMusic[i].Sound.Play(0.0f, i == noiseLoopIndex ? "default" : "music");
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
                        SoundPlayer.musicChannel[i] = SoundPlayer.currentMusic[i].Sound.Play(0.0f, i == noiseLoopIndex ? "default" : "music");
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
                    SoundPlayer.musicChannel[i].Gain = MathHelper.Lerp(SoundPlayer.musicChannel[i].Gain, targetGain, SoundPlayer.MusicLerpSpeed * deltaTime) * (1 - Sidechain.SidechainMultiplier);
                }
            }

            // Replace method. Sigh.
            return false;
        }



        public static void SPW_SoundPlayer_PlaySound(ref Sound sound, ref float? range, ref Vector2 position, ref Hull hullGuess)
        {
            if (!Config.Enabled || !RoundStarted || sound == null) { return; }

            range = range ?? sound.BaseFar;

            if (!IsUsingHydrophones)
            {
                range *= Config.SoundRangeMultiplier;
            }
            else
            {
                Hull targetHull = Hull.FindHull(position, hullGuess, true);
                if (targetHull == null || targetHull.Submarine != Character.Controlled?.Submarine)
                {
                    range += Config.HydrophoneSoundRange;
                }
                else
                {
                    range *= Config.SoundRangeMultiplier;
                }
            }
            return;
        }

        // Used to apply the general lowpass frequency to OggSounds when not using the custom ExtendedOggSounds.
        // Patching the OggSound.MuffleBuffer doesn't seem to work, which would be the ideal alternative.
        public static void SPW_BiQuad(BiQuad __instance, ref double frequency, ref double sampleRate, ref double q, ref double gainDb)
        {
            if (!Config.Enabled) { return; };

            // If frequency == vanilla default, we're processing a normal sound, so we replace it with the GeneralLowpassFrequency.
            // Otherwise, it's probably a player's voice meaning it's already at the correct frequency.
            // Note: To avoid an edge case, I made it impossible in the menu for the user to make their voice lowpass freq == vanilla default.
            if (__instance.GetType() == typeof(LowpassFilter) && !IsUsingCustomTypes && frequency == SoundPlayer.MuffleFilterFrequency)
            {
                frequency = Config.GeneralLowpassFrequency;
            }

            // We don't want to modify anything if the vanilla game is constructing a filter.
            else if (__instance.GetType() == typeof(BandpassFilter) && frequency != VANILLA_VOIP_BANDPASS_FREQUENCY)
            {
                q = Config.RadioBandpassQualityFactor;
            }

            return;
        }

        private static bool isMuffleTypeEqual(MuffleReason reasonOne, MuffleReason reasonTwo)
        {
            int muffleTypeOne;
            int muffleTypeTwo;

            if (reasonOne == MuffleReason.None) { muffleTypeOne = 1; }
            else if (reasonOne == MuffleReason.Suit) { muffleTypeOne = 2; }
            else if (reasonOne == MuffleReason.Eavesdropped) { muffleTypeOne = 3; }
            else { muffleTypeOne = 4; }

            if (reasonTwo == MuffleReason.None) { muffleTypeTwo = 1; }
            else if (reasonTwo == MuffleReason.Suit) { muffleTypeTwo = 2; }
            else if (reasonTwo == MuffleReason.Eavesdropped) { muffleTypeTwo = 3; }
            else { muffleTypeTwo = 4; }

            return muffleTypeOne == muffleTypeTwo;
        }

        public static bool SPW_SoundChannel_SetMuffled_Prefix(SoundChannel __instance, bool value)
        {
            SoundChannel instance = __instance;

            // Hand over control to default setter if sound is not extended or has no muffle info.
            if (!Config.Enabled || 
                instance.Sound is not ExtendedOggSound extendedSound || 
                !SoundChannelMuffleInfo.TryGetValue(instance, out MuffleInfo? muffleInfo))
            { 
                return true; 
            }

            // Early return for extended sounds if the type of muffling is the same (none/norm/suit/eavesdropping).
            if (muffleInfo.Muffled == instance.muffled && isMuffleTypeEqual(muffleInfo.Reason, muffleInfo.PreviousReason))
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
            if (extendedSound.Buffers is not { AlBuffer: not 0, AlNormMuffledBuffer: not 0, AlSuitMuffledBuffer: not 0, AlEavesdroppingMuffledBuffer: not 0 }) { return false; }

            uint alBuffer = extendedSound.Buffers.AlBuffer;
            if (muffleInfo.Muffled || extendedSound.Owner.GetCategoryMuffle(instance.Category))
            {
                if (muffleInfo.Reason == MuffleReason.Suit)
                {
                    alBuffer = extendedSound.Buffers.AlSuitMuffledBuffer;
                }
                else if (muffleInfo.Reason == MuffleReason.Eavesdropped)
                {
                    alBuffer = extendedSound.Buffers.AlEavesdroppingMuffledBuffer;
                }
                else
                {
                    alBuffer = extendedSound.Buffers.AlNormMuffledBuffer;
                }
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

            Al.Sourcei(alSource, Al.SampleOffset, playbackPos);
            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                DebugConsole.ThrowError("Failed to reset playback position: " + instance.debugName + ", " + Al.GetErrorString(alError), appendStackTrace: true);
                return false;
            }

            muffleInfo.PreviousReason = muffleInfo.Reason;
            return false;
        }

        static bool MuffleExtendedSound(ExtendedOggSound extendedSound, SoundChannel instance, bool muffle)
        {
            extendedSound.FillAlBuffers();
            if (extendedSound.Buffers is not { AlBuffer: not 0, AlNormMuffledBuffer: not 0, AlSuitMuffledBuffer: not 0, AlEavesdroppingMuffledBuffer: not 0 }) { return false; }

            MuffleInfo muffleInfo = new MuffleInfo(instance, dontMuffle: !muffle);
            SoundChannelMuffleInfo[instance] = muffleInfo;

            instance.muffled = muffleInfo.Muffled;

            uint alBuffer = extendedSound.Buffers.AlBuffer;
            if (muffleInfo.Muffled || extendedSound.Owner.GetCategoryMuffle(instance.Category))
            {
                if (muffleInfo.Reason == MuffleReason.Suit)
                {
                    alBuffer = extendedSound.Buffers.AlSuitMuffledBuffer;
                }
                else if (muffleInfo.Reason == MuffleReason.Eavesdropped)
                {
                    alBuffer = extendedSound.Buffers.AlEavesdroppingMuffledBuffer;
                }
                else
                {
                    alBuffer = extendedSound.Buffers.AlNormMuffledBuffer;
                }
            }

            Al.Sourcei(extendedSound.Owner.GetSourceFromIndex(extendedSound.SourcePoolIndex, instance.ALSourceIndex), Al.Buffer, (int)alBuffer);

            int alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + extendedSound.Owner.GetSourceFromIndex(extendedSound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
            }

            ProcessSingleSound(instance, muffleInfo);

            return true;
        }
        static bool MuffleNormalSound(Sound sound, SoundChannel instance, bool muffle)
        {
            sound.FillAlBuffers();
            if (sound.Buffers is not { AlBuffer: not 0, AlMuffledBuffer: not 0}) { return false; }
            MuffleInfo muffleInfo = new MuffleInfo(instance, dontMuffle: !muffle);
            SoundChannelMuffleInfo[instance] = muffleInfo;

            instance.muffled = muffleInfo.Muffled;

            uint alBuffer = muffleInfo.Muffled || sound.Owner.GetCategoryMuffle(instance.Category) ? sound.Buffers.AlMuffledBuffer : sound.Buffers.AlBuffer;

            Al.Sourcei(sound.Owner.GetSourceFromIndex(sound.SourcePoolIndex, instance.ALSourceIndex), Al.Buffer, (int)alBuffer);

            int alError = Al.GetError();
            if (Al.GetError() != Al.NoError)
            {
                throw new Exception("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + sound.Owner.GetSourceFromIndex(sound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
            }

            ProcessSingleSound(instance, muffleInfo);

            return true;
        }

        public static bool SPW_SoundChannel_Prefix(SoundChannel __instance, Sound sound, float gain, Vector3? position, float freqMult, float near, float far, string category, bool muffle)
        {
            if (!Config.Enabled) { return true; }

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
                if (!instance.IsStream)
                {
                    // Stop the source before detaching the buffer. This must be done because Harmony still runs the original method before this prefix.
                    Al.SourceStop(sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex));
                    int alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to stop source: " + instance.debugName + ", " + Al.GetErrorString(alError));
                    }

                    Al.Sourcei(sound.Owner.GetSourceFromIndex(instance.Sound.SourcePoolIndex, instance.ALSourceIndex), Al.Buffer, 0);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to reset source buffer: " + instance.debugName + ", " + Al.GetErrorString(alError));
                    }

                    SetProperties();

                    bool success = false;
                    if (instance.Sound is ExtendedOggSound extendedSound)
                    {
                        success = MuffleExtendedSound(extendedSound, instance, muffle);
                    }
                    else
                    {
                        success = MuffleNormalSound(sound, instance, muffle);
                    }
                    if (!success) { return false; }

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
                    int alError = Al.GetError();
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

        public static bool SPW_ItemComponent_UpdateSounds(ItemComponent __instance)
        {
            if (!Config.Enabled) { return true; }

            ItemComponent instance = __instance;
            UpdateComponentOneshotSoundChannels(instance);

            Item item = instance.item;
            ItemSound loopingSound = instance.loopingSound;
            SoundChannel channel = instance.loopingSoundChannel;

            if (loopingSound == null || channel == null || !channel.IsPlaying) { return false; }

            bool needsUpdate = true;
            if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
            {
                muffleInfo = new MuffleInfo(channel, item.CurrentHull, instance);
                SoundChannelMuffleInfo[channel] = muffleInfo;
                needsUpdate = false;
            }

            muffleInfo.ItemComp = instance;

            if (needsUpdate && Timing.TotalTime > instance.lastMuffleCheckTime + 0.2f)
            {
                muffleInfo.Update(item.CurrentHull);
                instance.lastMuffleCheckTime = (float)Timing.TotalTime;
            }

            channel.Muffled = muffleInfo.Muffled;
            channel.Position = new Vector3(item.WorldPosition, 0.0f);

            ProcessLoopingSound(channel, muffleInfo);

            return false;
        }

        public static bool SPW_StatusEffect_UpdateAllProjSpecific()
        {
            if (!Config.Enabled) { return true; }

            double LastMuffleCheckTime = StatusEffect.LastMuffleCheckTime;
            HashSet<StatusEffect> ActiveLoopingSounds = StatusEffect.ActiveLoopingSounds;

            bool doMuffleCheck = Timing.TotalTime > LastMuffleCheckTime + 0.2;
            if (doMuffleCheck) { LastMuffleCheckTime = Timing.TotalTime; }
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

                    bool needsUpdate = true;
                    if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
                    {
                        muffleInfo = new MuffleInfo(channel, emitter: statusEffect.soundEmitter as Item, dontMuffle: statusEffect.ignoreMuffling, dontPitch: true);
                        SoundChannelMuffleInfo[channel] = muffleInfo;
                        channel.Muffled = muffleInfo.Muffled;
                        needsUpdate = false;
                    }

                    if (needsUpdate && doMuffleCheck && !statusEffect.ignoreMuffling)
                    {
                        muffleInfo.Update(emitter: statusEffect.soundEmitter as Item);
                        channel.Muffled = muffleInfo.Muffled;
                    }

                    statusEffect.soundChannel.Position = new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f);

                    ProcessLoopingSound(channel, muffleInfo);
                }
            }
            ActiveLoopingSounds.RemoveWhere(s => s.soundChannel == null);

            return false;
        }

        public static bool IsContainedWithinContainer(Item item)
        {
            while (item?.ParentInventory != null)
            {
                if (item.ParentInventory.Owner is Item parent && parent.HasTag("container"))
                {
                    return true;
                }
                item = item.ParentInventory.Owner as Item;
            }
            return false;
        }

        public static void UpdateComponentOneshotSoundChannels(ItemComponent itemComponent)
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

        public static void ProcessSingleSound(SoundChannel channel, MuffleInfo muffleInfo)
        {
            if (muffleInfo.IgnoreAll) { return; }

            bool eavesdropped = muffleInfo.Eavesdropped;
            bool hydrophoned = IsUsingHydrophones && muffleInfo.SoundHull?.Submarine != LightManager.ViewTarget?.Submarine;

            if (!muffleInfo.IgnorePitch)
            {
                float freqMult = 1;

                if (muffleInfo.Reason != MuffleReason.None && muffleInfo.Reason != MuffleReason.BothInWater && muffleInfo.Reason != MuffleReason.Suit) { freqMult -= (1 - GetPitchFromDistance(channel, 0.5f)); }
                else if (muffleInfo.Reason == MuffleReason.BothInWater) { freqMult -= (1 - GetPitchFromDistance(channel)); }
                else if (eavesdropped) { freqMult -= (1 - Config.EavesdroppingPitchMultiplier); }
                else if (hydrophoned) { freqMult -= (1 - Config.HydrophonePitchMultiplier); }

                if (EarsInWater && muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                if (IsWearingDivingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }

                // Multiply self in case sound has built-in pitch. Doesn't multiply into oblivion because we only do this once.
                float originalFreq = channel.FrequencyMultiplier;
                float newFreq = Math.Clamp(originalFreq * freqMult, 0.25f, 4);
                if (newFreq != originalFreq)
                { 
                    channel.FrequencyMultiplier = newFreq;
                    PitchedSounds[channel] = true;
                }
            }

            float gainMult = 1;
            gainMult -= (1 - muffleInfo.GainMult);
            if (muffleInfo.Reason == MuffleReason.NoPath || muffleInfo.Reason == MuffleReason.SoundInWater) { gainMult -= (1 - Config.MuffledSoundVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); gainMult *= HydrophoneEfficiency; }

            // Transitions audio between hulls via attenuation.
            if (Config.EavesdroppingFadeEnabled && EavesdroppingEfficiency > 0)
            {
                if (!eavesdropped) { gainMult *= Math.Clamp(1 - EavesdroppingEfficiency * (1 / Config.EavesdroppingThreshold), 0.1f, 1); }
                else if (eavesdropped) { gainMult *= Math.Clamp(EavesdroppingEfficiency * (1 / Config.EavesdroppingThreshold) - 1, 0.1f, 1); }
            }

            float ducking = 1;
            if (!muffleInfo.IsLoud) { ducking -= Sidechain.SidechainMultiplier; }

            float currentGain = muffleInfo.ItemComp?.GetSoundVolume(muffleInfo.ItemComp?.loopingSound) ?? channel.Gain;
            channel.Gain = currentGain * gainMult * ducking;

            LuaCsLogger.Log($"{Path.GetFileName(channel.Sound.Filename)}: Muffle Reason: {muffleInfo.Reason} currentGain: {currentGain} GainMult: {gainMult} ducking {ducking}");
        }
        public static void ProcessLoopingSound(SoundChannel channel, MuffleInfo muffleInfo)
        {
            float currentGain = muffleInfo.ItemComp?.GetSoundVolume(muffleInfo.ItemComp?.loopingSound) ?? 1;
            float currentFreq = channel.FrequencyMultiplier;

            if (muffleInfo.IgnorePitch && currentFreq != 1)
            {
                channel.FrequencyMultiplier = 1;
            }
            if (muffleInfo.IgnoreAll) 
            {
                if (currentFreq != 1)
                {
                    channel.FrequencyMultiplier = 1;
                }
                channel.Gain = currentGain;
                return; 
            }

            bool eavesdropped = muffleInfo.Eavesdropped;
            bool hydrophoned = IsUsingHydrophones && muffleInfo.SoundHull?.Submarine != LightManager.ViewTarget?.Submarine;

            if (!muffleInfo.IgnorePitch)
            {
                float freqMult = 1;
                if (muffleInfo.Reason != MuffleReason.None && muffleInfo.Reason != MuffleReason.Suit) { freqMult -= (1 - Config.MuffledComponentPitchMultiplier); }
                else if (eavesdropped) { freqMult -= (1 - Config.EavesdroppingPitchMultiplier); }
                else if (hydrophoned) { freqMult -= (1 - Config.HydrophonePitchMultiplier); }
                else { freqMult -= (1 - Config.UnmuffledComponentPitchMultiplier); }

                if (EarsInWater && muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                if (IsWearingDivingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }

                // Multiply by 1 instead of self so we don't multiply into oblivion with repeated calls.
                float newFreq = Math.Clamp(1 * freqMult, 0.25f, 4);
                if (newFreq != currentFreq)
                {
                    channel.FrequencyMultiplier = newFreq;
                    PitchedSounds[channel] = true;
                }
            }

            float gainMult = 1;
            gainMult -= (1 - muffleInfo.GainMult);
            if (muffleInfo.Reason == MuffleReason.NoPath) { gainMult -= (1 - Config.MuffledComponentVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); gainMult *= HydrophoneEfficiency; }
            else { gainMult -= (1 - Config.UnmuffledComponentVolumeMultiplier); }

            // Transitions audio between hulls via attenuation.
            if (Config.EavesdroppingFadeEnabled && EavesdroppingEfficiency > 0)
            {
                if (!eavesdropped) { gainMult *= Math.Clamp(1 - EavesdroppingEfficiency * (1 / Config.EavesdroppingThreshold), 0.1f, 1); }
                else if (eavesdropped) { gainMult *= Math.Clamp(EavesdroppingEfficiency * (1 / Config.EavesdroppingThreshold) - 1, 0.1f, 1); }
            }

            float ducking = 1;
            if (!muffleInfo.IsLoud) { ducking -= Sidechain.SidechainMultiplier; }

            float distFalloffMult = (muffleInfo.Distance == float.MaxValue) ? 0.7f : 1 - MathUtils.InverseLerp(channel.Near, channel.Far, muffleInfo.Distance);
            float targetGain = currentGain * gainMult * distFalloffMult * ducking;
            
            channel.Gain = targetGain;
        }

        public static void ProcessVoipSound(VoipSound voipSound, MuffleInfo muffleInfo)
        {
            SoundChannel channel = voipSound.soundChannel;
            float currentFreq = channel.FrequencyMultiplier;

            // Early return and restore pitch if needed.
            if (muffleInfo.IgnorePitch && currentFreq != 1)
            {
                channel.FrequencyMultiplier = 1;
            }
            if (muffleInfo.IgnoreAll)
            {
                if (currentFreq != 1) 
                { 
                    channel.FrequencyMultiplier = 1;
                }
                return; 
            }

            bool eavesdropped = muffleInfo.Eavesdropped;
            bool hydrophoned = IsUsingHydrophones && muffleInfo.SoundHull?.Submarine != LightManager.ViewTarget?.Submarine;

            if (!muffleInfo.IgnorePitch)
            {
                float freqMult = 1;
                // Voice channels are only muffled by these joke settings.
                if (muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.MuffledVoicePitchMultiplier); }
                else { freqMult -= (1 - Config.UnmuffledVoicePitchMultiplier); }

                // Multiply by 1 instead of self so we don't multiply into oblivion with repeated calls.
                float newFreq = Math.Clamp(1 * freqMult, 0.25f, 4);
                if (newFreq != currentFreq)
                {
                    channel.FrequencyMultiplier = newFreq;
                    PitchedSounds[channel] = true;
                }
            }

            float gainMult = 1;
            gainMult -= (1 - muffleInfo.GainMult);
            if (muffleInfo.Reason == MuffleReason.NoPath) { gainMult -= (1 - Config.MuffledVoiceVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingVoiceVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); gainMult *= HydrophoneEfficiency; }

            // Transitions audio between hulls via attenuation.
            if (Config.EavesdroppingFadeEnabled && EavesdroppingEfficiency > 0)
            {
                if (!eavesdropped) { gainMult *= Math.Clamp(1 - EavesdroppingEfficiency * (1 / Config.EavesdroppingThreshold), 0.1f, 1); }
                else if (eavesdropped) { gainMult *= Math.Clamp(EavesdroppingEfficiency * (1 / Config.EavesdroppingThreshold) - 1, 0.1f, 1); }
            }

            float ducking = 1;
            if (!muffleInfo.IsLoud) { ducking -= Sidechain.SidechainMultiplier; }

            float targetGain = 1 * gainMult * ducking;
            voipSound.Gain = targetGain;
        }

        public static void SPW_UpdateTransform(Camera __instance)
        {
            if (!Config.Enabled || !RoundStarted || Character.Controlled == null) { return; }

            if (Config.FocusTargetAudio && LightManager.ViewTarget != null && LightManager.ViewTarget.Position != Character.Controlled.Position)
            {
                GameMain.SoundManager.ListenerPosition = new Vector3(__instance.TargetPos.X, __instance.TargetPos.Y, -(100 / __instance.Zoom));
            }
        }

        public static bool SPW_UpdateWaterAmbience(ref float ambienceVolume, ref float deltaTime)
        {
            if (!Config.Enabled || !RoundStarted || Character.Controlled == null) { return true; }

            if (Character.Controlled.AnimController.HeadInWater)
            {
                ambienceVolume *= Config.SubmergedWaterAmbienceVolumeMultiplier;
            }
            else if (IsUsingHydrophones)
            {
                ambienceVolume *= Config.HydrophoneWaterAmbienceVolumeMultiplier; ambienceVolume *= HydrophoneEfficiency;
            }
            else if (Config.FocusTargetAudio && LightManager.ViewTarget != null && ViewTargetHull == null)
            {
                ambienceVolume *= Config.SubmergedWaterAmbienceVolumeMultiplier;
            }
            else
            {
                ambienceVolume *= Config.UnsubmergedWaterAmbienceVolumeMultiplier;
            }

            float ducking = 1 - Sidechain.SidechainMultiplier;
            ambienceVolume *= Math.Clamp(1 - EavesdroppingEfficiency * 2.5f, 0, 1) * ducking;

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
                    else if (IsUsingHydrophones && HydrophoneEfficiency < 1)
                    {
                        chn.FrequencyMultiplier = MathHelper.Lerp(0.25f, 1f, HydrophoneEfficiency);
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
            SoundChannel[] channels = SoundPlayer.flowSoundChannels;
            bool enableMuffling = Config.MuffleFlowSounds;

            if (!Config.Enabled)
            {
                foreach (SoundChannel channel in channels)
                {
                    if (channel == null) { continue; }
                    if (channel.Muffled) { channel.Muffled = false; }
                    if (channel.FrequencyMultiplier != 1) { channel.FrequencyMultiplier = 1; }
                }

                return;
            }

            if (!enableMuffling)
            {
                foreach (SoundChannel channel in channels)
                {
                    if (channel == null) { continue; }
                    if (channel.Muffled) { channel.Muffled = false; }
                }
            }

            bool isUsingHydrophones = IsUsingHydrophones;
            bool isWearingsuit = IsWearingDivingSuit;
            bool earsInWater = EarsInWater;

            for (int i = 0; i < SoundPlayer.flowSoundChannels.Count(); i++)
            {
                SoundChannel channel = SoundPlayer.flowSoundChannels[i];
                if (channel == null) { continue; }


                // -- Muffle control --

                bool needsUpdate = true;
                if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo? muffleInfo))
                {
                    muffleInfo = new MuffleInfo(channel);
                    SoundChannelMuffleInfo[channel] = muffleInfo;
                    needsUpdate = false;
                }
                if (needsUpdate) { muffleInfo.Update(); }

                // Reset and skip channel if ignored.
                if (muffleInfo.IgnoreAll)
                {
                    if (channel.Muffled) { channel.Muffled = false; }
                    if (channel.FrequencyMultiplier != 1) { channel.FrequencyMultiplier = 1; }
                    continue;
                }

                channel.Muffled = muffleInfo.Muffled;


                // -- Pitch control --

                float freqMult = 1;
                if (!muffleInfo.IgnorePitch)
                {
                    if (earsInWater) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                    if (isWearingsuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }
                    if (isUsingHydrophones) { freqMult -= (1 - Config.HydrophonePitchMultiplier); }
                }
                float targetFreq = Math.Clamp(1 * freqMult, 0.25f, 4);
                if (targetFreq != channel.FrequencyMultiplier)
                {
                    channel.FrequencyMultiplier = targetFreq;
                    PitchedSounds[channel] = true;
                }


                // -- Volume control --

                float gainMult = 1;
                gainMult -= (1 - muffleInfo.GainMult);
                gainMult -= (1 - Config.FlowSoundVolumeMultiplier);

                // Fades out when eavesdropping.
                if (Config.EavesdroppingFadeEnabled && EavesdroppingEfficiency > 0) {
                    gainMult *= Math.Clamp(1 - EavesdroppingEfficiency, 0.3f, 1);
                }
                else if (EavesdroppingEfficiency > 0)
                {
                    gainMult *= 0.3f;
                }

                float sidechainMult = 1;
                if (!muffleInfo.IsLoud) { sidechainMult -= Sidechain.SidechainMultiplier; }

                float currentGain = Math.Max(SoundPlayer.flowVolumeRight[i], SoundPlayer.flowVolumeLeft[i]);
                float targetGain = currentGain * gainMult * sidechainMult;
                if (targetGain != channel.Gain)
                {
                    channel.Gain = targetGain;
                }
            }
        }

        public static void SPW_SoundPlayer_UpdateFireSounds()
        {

        }

        // TODO Pretty bad. Redo this? Also remember the Config.EstimatePathToFakeSounds value exists.
        public static bool IsPathToFlow()
        {
            Character character = Character.Controlled;
            if (character == null || character.CurrentHull == null) { return true; }

            return GetPathToFlow(character.CurrentHull, new HashSet<Hull>());
        }
        public static bool GetPathToFlow(Hull startHull, HashSet<Hull> connectedHulls)
        {
            Vector2 listenerPos = Character.Controlled.WorldPosition;

            foreach (Gap gap in startHull.ConnectedGaps)
            {
                Vector2 diff = gap.WorldPosition - listenerPos;

                if (Math.Abs(diff.X) >= SoundPlayer.FlowSoundRange && Math.Abs(diff.Y) >= SoundPlayer.FlowSoundRange) { continue; }
                if (gap.Open < 0.01f || gap.LerpedFlowForce.LengthSquared() < 100.0f) { continue; }
                float gapFlow = Math.Abs(gap.LerpedFlowForce.X) + Math.Abs(gap.LerpedFlowForce.Y) * 2.5f;
                if (!gap.IsRoomToRoom) { gapFlow *= 2.0f; }
                if (gapFlow >= 10.0f) { return true; }

                for (int i = 0; i < 2 && i < gap.linkedTo.Count; i++)
                {
                    if (gap.linkedTo[i] is Hull newStartHull && !connectedHulls.Contains(newStartHull))
                    {
                        bool path = GetPathToFlow(newStartHull, connectedHulls);
                        if (path)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool ShouldPropagateToHull(Hull listenerHull, Hull? soundHull, Vector2 soundWorldPos, float soundRadius = 150.0f)
        {
            if (listenerHull == null || listenerHull == soundHull) { return false; }

            Rectangle soundBounds = new Rectangle(
                (int)(soundWorldPos.X - soundRadius),
                (int)(soundWorldPos.Y - soundRadius),
                (int)(soundRadius * 2),
                (int)(soundRadius * 2)
            );

            // Check if the rectangles intersect
            return soundBounds.Intersects(listenerHull.WorldRect);
        }

        // Gets the distance between two localised positions going through gaps. Returns MaxValue if no path or out of range.
        public static float GetApproximateDistance(Vector2 startPos, Vector2 endPos, Hull startHull, Hull endHull, float maxDistance, float distanceMultiplierPerClosedDoor = 0, int numIgnoredWalls = 0)
        {
            if (startHull == null || endHull == null)
            {
                return startHull != endHull ? float.MaxValue : Vector2.Distance(startPos, endPos);
            }
            return GetApproximateHullDistance(startPos, endPos, startHull, endHull, new HashSet<Hull>(), 0.0f, maxDistance, distanceMultiplierPerClosedDoor);
        }

        private static float GetApproximateHullDistance(Vector2 startPos, Vector2 endPos, Hull startHull, Hull endHull, HashSet<Hull> connectedHulls, float distance, float maxDistance, float distanceMultiplierFromDoors = 0)
        {
            if (distance >= maxDistance) { return float.MaxValue; }
            if (startHull == endHull)
            {
                return distance + Vector2.Distance(startPos, endPos);
            }

            connectedHulls.Add(startHull);
            foreach (Gap g in startHull.ConnectedGaps)
            {
                float distanceMultiplier = 1;
                // For doors.
                if (g.ConnectedDoor != null && !g.ConnectedDoor.IsBroken)
                {
                    // Gap blocked if the door is closed or is curently closing and 90% closed.
                    if ((!g.ConnectedDoor.PredictedState.HasValue && g.ConnectedDoor.IsClosed || g.ConnectedDoor.PredictedState.HasValue && !g.ConnectedDoor.PredictedState.Value) && g.ConnectedDoor.OpenState < 0.1f)
                    {
                        if (distanceMultiplierFromDoors <= 0) { continue; }
                        distanceMultiplier *= distanceMultiplierFromDoors;
                    }
                }
                // For holes in hulls.
                else if (g.Open <= 0.33f)
                {
                    continue;
                }

                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    if (g.linkedTo[i] is Hull newStartHull && !connectedHulls.Contains(newStartHull))
                    {
                        Vector2 optimalGapPos = GetGapIntersectionPos(startPos, endPos, g);
                        float dist = GetApproximateHullDistance(optimalGapPos, endPos, newStartHull, endHull, connectedHulls, distance + Vector2.Distance(startPos, optimalGapPos) * distanceMultiplier, maxDistance);
                        if (dist < float.MaxValue)
                        {
                            return dist;
                        }
                    }
                }
            }

            return float.MaxValue;
        }

        public static Vector2 GetGapIntersectionPos(Vector2 startPos, Vector2 endPos, Gap gap)
        {
            Vector2 gapPos = gap.Position;

            if (!gap.IsHorizontal)
            {
                float gapSize = gap.Rect.Width;
                float slope = (endPos.Y - startPos.Y) / (endPos.X - startPos.X);
                float intersectX = (gapPos.Y - startPos.Y + slope * startPos.X) / slope;

                // Clamp the x-coordinate to within the gap's width
                intersectX = Math.Clamp(intersectX, gapPos.X - gapSize / 2, gapPos.X + gapSize / 2);

                return new Vector2(intersectX, gapPos.Y);
            }
            else
            {
                float gapSize = gap.Rect.Height;
                float slope = (endPos.X - startPos.X) / (endPos.Y - startPos.Y);
                float intersectY = (gapPos.X - startPos.X + slope * startPos.Y) / slope;

                // Clamp the y-coordinate to within the gap's height
                intersectY = Math.Clamp(intersectY, gapPos.Y - gapSize / 2, gapPos.Y + gapSize / 2);

                return new Vector2(gapPos.X, intersectY);
            }
        }

        static bool GetCustomSoundData(string inputString, out float gainMult, out float sidechainMult, out float release)
        {
            string s = inputString.ToLower();
            gainMult = 1; sidechainMult = 0; release = 60;

            foreach (var sound in Config.CustomSounds)
            {
                if (s.Contains(sound.Name.ToLower()))
                {
                    bool excluded = false;
                    foreach (string exclusion in sound.Exclusions)
                    {
                        if (s.Contains(exclusion.ToLower())) { excluded = true; }
                    }
                    if (excluded) { continue; }

                    gainMult = sound.GainMultiplier;
                    sidechainMult = sound.SidechainMultiplier * Config.SidechainIntensity;
                    release = sound.Release * Config.SidechainReleaseMultiplier;
                    return true;
                }
            }
            return false;
        }

        public static bool StringHasKeyword(string inputString, HashSet<string> set, string? exclude = null, string? include = null)
        {
            string s = inputString.ToLower();

            if (exclude != null && s.Contains(exclude.ToLower()))
                return false;

            if (include != null && s.Contains(include.ToLower()))
                return true;

            foreach (string keyword in set)
            {
                if (s.Contains(keyword.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        // The maximum return value of this function is 0.25 + startingFreq.
        public static float GetPitchFromDistance(SoundChannel channel, float startingFreq = 0.75f)
        {
            if (Config.MuffledSoundPitchMultiplier == 0) { return 1; }

            // Distance from the sound.
            float distance = Vector3.Distance(GameMain.SoundManager.ListenerPosition, new Vector3(GetSoundChannelPos(channel), 0.0f));
            
            // Ratio is higher the closer the listener is to the sound source.
            float proximityMult = Math.Clamp(1 - distance / channel.Far, 0, 1);

            // Ranges from 0.25 to startingFreq + 0.25
            float targetPitch = 0.25f + startingFreq * proximityMult;

            // Based on config multiplier, return a pitch beteween 1.0 (no effect) and targetPitch
            return MathHelper.Lerp(1.0f, targetPitch, Config.MuffledSoundPitchMultiplier);
        }

        public static Vector2 GetSoundChannelPos(SoundChannel channel)
        {
            return channel.Position.HasValue ? new Vector2(channel.Position.Value.X, channel.Position.Value.Y) : Character.Controlled?.Position ?? Vector2.Zero;
        }

        // Returns true if the given localised position is in water (not accurate when using WorldPositions).
        public static bool SoundInWater(Vector2 soundPos, Hull? soundHull)
        {
            return soundHull == null || soundHull.WaterVolume > 0 && soundPos.Y < soundHull.Surface;
        }
    }
}
