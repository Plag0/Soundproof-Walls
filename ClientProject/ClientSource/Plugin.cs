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

namespace SoundproofWalls
{
    public partial class SoundproofWalls : IAssemblyPlugin
    {
        // The default value of 800 is found in the VoipSound class under the initialization of the muffleFilters list.
        public const short VANILLA_VOIP_LOWPASS_FREQUENCY = 800;
        static readonly FieldInfo? VoipSoundMuffleFiltersField = typeof(VoipSound).GetField("muffleFilters", BindingFlags.NonPublic | BindingFlags.Instance);

        public static Config LocalConfig = ConfigManager.LoadConfig();
        public static Config? ServerConfig = null;
        public static Config Config { get { return ServerConfig ?? LocalConfig; } }

        static bool RoundStarted { get { return GameMain.gameSession?.IsRunning ?? false; } }
        static bool IsWearingDivingSuit { get { return Character.Controlled?.LowPassMultiplier < 0.5f; } }
        static bool IsUsingHydrophones { get { return HydrophoneEfficiency > 0.01f && Character.Controlled?.SelectedItem?.GetComponent<Sonar>() is Sonar sonar && HydrophoneSwitches.ContainsKey(sonar) && HydrophoneSwitches[sonar].State; } }
        static bool IsViewTargetPlayer { get { return !Config.FocusTargetAudio || LightManager.ViewTarget as Character == Character.Controlled; } }
        static bool EarsInWater { get { return IsViewTargetPlayer ? Character.Controlled?.AnimController?.HeadInWater == true : SoundInWater(LightManager.ViewTarget.Position, ViewTargetHull); } }

        static float hydrophoneEfficiency = 1;
        static float HydrophoneEfficiency { get { return hydrophoneEfficiency; } set { hydrophoneEfficiency = Math.Clamp(value, 0, 1); } }
        
        // TODO Try connecting a cool transition sound and its frequency to this efficiency.
        static float eavesdroppingEfficiency = 0;
        static float EavesdroppingEfficiency { get { return eavesdroppingEfficiency; } set { eavesdroppingEfficiency = Math.Clamp(value, 0, 1); } }
        static float EavesdroppingTextAlpha = 0;

        static float LastHydrophonePlayTime = 0.1f;
        static float LastSyncUpdateTime = 5f;
        static float LastBubbleUpdateTime = 0.2f;
        static float LastDrawEavesdroppingTextTime = 0f;

        // Custom sounds.
        static Sound? BubbleSound;
        static Sound? RadioBubbleSound;
        static Sound? HydrophoneMovementSound;
        
        public static ConcurrentDictionary<SoundChannel, MuffleInfo> SoundChannelMuffleInfo = new ConcurrentDictionary<SoundChannel, MuffleInfo>();
        static ConcurrentDictionary<Client, SoundChannel?> ClientBubbleSoundChannels = new ConcurrentDictionary<Client, SoundChannel?>();
        static ConcurrentDictionary<SoundChannel, bool> PitchedSounds = new ConcurrentDictionary<SoundChannel, bool>();
        static Dictionary<SoundChannel, Character> HydrophoneSoundChannels = new Dictionary<SoundChannel, Character>();
        static Dictionary<Sonar, HydrophoneSwitch> HydrophoneSwitches = new Dictionary<Sonar, HydrophoneSwitch>();
        static HashSet<Sound> SoundsToDispose = new HashSet<Sound>();

        // Expensive sounds that are unlikely to be muffled and so are ignored when ReloadSounds() is called.
        static readonly HashSet<string> IgnoredPrefabs = new HashSet<string>
        { 
            "Barotrauma/Content/Sounds/Music/",
            "Barotrauma/Content/Sounds/UI/",
            "Barotrauma/Content/Sounds/Ambient/",
            "Barotrauma/Content/Sounds/Hull/",
            "Barotrauma/Content/Sounds/Water/WaterAmbience",
            "Barotrauma/Content/Sounds/Water/BlackSmoker.ogg",
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
            GameMain.LuaCs.Hook.Add("loaded", "spw_loaded", (object[] args) =>
            {
                UpdateServerConfig();
                NewHydrophoneSwitches();
                return null;
            });

            GameMain.LuaCs.Hook.Add("stop", "spw_stop", (object[] args) =>
            {
                KillSPW();
                return null;
            });

            GameMain.LuaCs.Hook.Add("think", "spw_update", (object[] args) =>
            {
                SPW_Update();
                return null;
            });

            // StartRound postfix patch
            harmony.Patch(
                typeof(GameSession).GetMethod(nameof(GameSession.StartRound), new Type[] { typeof(LevelData), typeof(bool), typeof(SubmarineInfo), typeof(SubmarineInfo) }),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_StartRound))));

            // EndRound postfix patch
            harmony.Patch(
                typeof(GameSession).GetMethod(nameof(GameSession.EndRound)),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_EndRound))));

            // LoadSounds 1 prefix patch. Replaces OggSound with ExtendedOggSound.
            harmony.Patch(
                typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(string), typeof(bool) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_LoadSound1), BindingFlags.Static | BindingFlags.Public)));

            // LoadSounds 2 prefix patch. Replaces OggSound with ExtendedOggSound.
            harmony.Patch(
                typeof(SoundManager).GetMethod(nameof(SoundManager.LoadSound), BindingFlags.Instance | BindingFlags.Public, new Type[] { typeof(ContentXElement), typeof(bool), typeof(string) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_LoadSound2), BindingFlags.Static | BindingFlags.Public)));

            // SoundPlayer_PlaySound prefix patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.PlaySound), new Type[] { typeof(Sound), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(Hull), typeof(bool), typeof(bool) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundPlayer_PlaySound))));

            // ItemComponent_PlaySound prefix patch
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.PlaySound), BindingFlags.NonPublic | BindingFlags.Instance, new Type[] { typeof(ItemSound), typeof(Vector2) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_ItemComponent_PlaySound))));

            // SoundChannel ctor postfix replacement patch
            harmony.Patch(
                typeof(SoundChannel).GetConstructor(new Type[] { typeof(Sound), typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(string), typeof(bool) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel_Prefix))),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel_Postfix))));

            // Soundchannel Muffle property prefix replacement patch
            harmony.Patch(
                typeof(SoundChannel).GetProperty(nameof(SoundChannel.Muffled)).GetSetMethod(),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel_SetMuffled_Prefix))));

            // BiQuad prefix patch. Used for changing the muffle frequency if using standard OggSounds.
            harmony.Patch(
                typeof(BiQuad).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, new Type[] { typeof(int), typeof(double), typeof(double), typeof(double) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_BiQuad))));

            // VoipSound ApplyFilters prefix patch. Assigns muffle filters and processes gain & pitch.
            harmony.Patch(
                typeof(VoipSound).GetMethod(nameof(VoipSound.ApplyFilters), BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(short[]), typeof(int) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_VoipSound_ApplyFilters_Prefix))));

            // ItemComponent UpdateSounds prefix and replacement patch
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.UpdateSounds), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_ItemComponent_UpdateSounds))));

            // StatusEffect UpdateAllProjSpecific prefix and replacement patch
            harmony.Patch(
                typeof(StatusEffect).GetMethod(nameof(StatusEffect.UpdateAllProjSpecific), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_StatusEffect_UpdateAllProjSpecific))));

            // VoipClient SendToServer prefix and replacement patch
            harmony.Patch(
                typeof(VoipClient).GetMethod(nameof(VoipClient.SendToServer), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_VoipClient_SendToServer))));

            // VoipClient Read prefix and replacement patch
            harmony.Patch(
                typeof(VoipClient).GetMethod(nameof(VoipClient.Read), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_VoipClient_Read))));

            // UpdateTransform postfix patch
            harmony.Patch(
                typeof(Camera).GetMethod(nameof(Camera.UpdateTransform)),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateTransform))));

            // UpdateWaterAmbience prefix patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateWaterAmbience), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateWaterAmbience))));

            // Dispose prefix patch
            harmony.Patch(
                typeof(SoundChannel).GetMethod(nameof(SoundChannel.Dispose)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Dispose))));
#if !LINUX
            // Draw prefix patch
            // A line in this method causes MonoMod to crash on Linux due to an unmanaged PAL_SEHException
            // https://github.com/dotnet/runtime/issues/78271
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.Draw)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Draw))));
#endif
            // TogglePauseMenu postfix
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.TogglePauseMenu)),
                null,
                new HarmonyMethod(typeof(EasySettings).GetMethod(nameof(EasySettings.SPW_TogglePauseMenu))));

            // ShouldMuffleSounds prefix and blank replacement patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.ShouldMuffleSound)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_ShouldMuffleSound))));

            // Clients receiving the host's config.
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

                bool shouldReloadSounds = ShouldReloadSounds(newConfig: newServerConfig, oldConfig: Config);
                bool shouldClearMuffleInfo = ShouldClearMuffleInfo(newServerConfig);
                
                ServerConfig = newServerConfig; 

                if (shouldReloadSounds) { ReloadSounds(); }
                if (shouldClearMuffleInfo) { SoundChannelMuffleInfo.Clear(); }

                if (manualUpdate)
                {
                    string updaterName = GameMain.Client.ConnectedClients.FirstOrDefault(client => client.SessionId == configSenderId)?.Name ?? "unknown";
                    LuaCsLogger.Log($"Soundproof Walls: \"{updaterName}\" {TextManager.Get("spw_updateserverconfig").Value}", Color.LimeGreen);
                }
            });

            GameMain.LuaCs.Networking.Receive("SPW_DisableConfigClient", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];

                string data = msg.ReadString();
                bool manualUpdate = false;
                byte configSenderId = 1;
                DataAppender.RemoveData(data, out manualUpdate, out configSenderId);

                bool shouldReloadSounds = ShouldReloadSounds(newConfig: LocalConfig, oldConfig: Config);

                ServerConfig = null;

                if (shouldReloadSounds) { ReloadSounds(); }
                SoundChannelMuffleInfo.Clear();

                if (manualUpdate)
                {
                    string updaterName = GameMain.Client.ConnectedClients.FirstOrDefault(client => client.SessionId == configSenderId)?.Name ?? "unknown";
                    LuaCsLogger.Log($"Soundproof Walls: \"{updaterName}\" {TextManager.Get("spw_disableserverconfig").Value}", Color.MonoGameOrange);
                }
            });

            LoadBubbleSounds();
            ReloadSounds(onlyLoadMissingExtendedOggs: true);
            Menu.LoadMenu();
        }

        public static bool SPW_LoadSound1(SoundManager __instance, string filename, bool stream, ref Sound __result)
        {
            if (!Config.Enabled) { return true; }

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
            if (!Config.Enabled) { return true; }

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

            public bool IgnorePath = false;
            public bool IgnoreWater = false;
            public bool IgnoreSubmersion = false;
            public bool IgnorePitch = false;
            public bool IgnoreLowpass = false;
            public bool IgnoreContainer = false;
            public bool IgnoreAll = false;

            public bool Muffled = false;
            public bool MuffledBySuit = false;
            public bool Eavesdropped = false;
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

                if (Channel.Category == "ui" || IsFilenameInSet(filename, Config.IgnoredSounds))
                {
                    IgnoreAll = true;
                }
                else
                {
                    IgnoreLowpass = dontMuffle || IsFilenameInSet(filename, Config.LowpassIgnoredSounds);
                    IgnorePitch = dontPitch || IsFilenameInSet(filename, Config.PitchIgnoredSounds);
                    IgnorePath = IgnoreLowpass || IsFilenameInSet(filename, Config.PathIgnoredSounds, include: "Barotrauma/Content/Sounds/Water/Flow");
                    IgnoreWater = IgnoreLowpass || !Config.MuffleSubmergedSounds || IsFilenameInSet(filename, Config.WaterIgnoredSounds);
                    IgnoreSubmersion = IgnoreLowpass || IsViewTargetPlayer ? !Config.MuffleSubmergedPlayer : !Config.MuffleSubmergedViewTarget || IsFilenameInSet(filename, Config.SubmersionIgnoredSounds, exclude: "Barotrauma/Content/Characters/Human/");
                    IgnoreContainer = IgnoreLowpass || IsFilenameInSet(filename, Config.ContainerIgnoredSounds);
                    IgnoreAll = IgnoreLowpass && IgnorePitch;
                }

                Update(soundHull, messageType: messageType, emitter: emitter);
                PreviousReason = Reason;
            }
            public void Update(Hull? soundHull = null, ChatMessageType? messageType = null, Item? emitter = null)
            {
                Muffled = false;
                MuffledBySuit = false;
                Eavesdropped = false;
                Reason = MuffleReason.None;

                UpdateMuffle(soundHull, messageType, emitter);
                UpdateSuitMuffle(messageType);
            }

            private void UpdateSuitMuffle(ChatMessageType? messageType)
            {
                bool isVoice = messageType != null;
                bool isRadio = messageType == ChatMessageType.Radio;
                bool isSuitMuffled = IsWearingDivingSuit && Config.MuffleDivingSuits && !IgnoreAll;
                // Sound isn't muffled or the suit muffle is stronger than the current one.
                bool suitMufflePriority = !Muffled || Config.DivingSuitLowpassFrequency <= Config.GeneralLowpassFrequency;
                
                if ((isVoice && isSuitMuffled && !isRadio) ||
                   (!isVoice && isSuitMuffled && suitMufflePriority))
                {
                    Reason = MuffleReason.Suit;
                    Muffled = !IgnoreLowpass;
                    MuffledBySuit = Muffled;
                }
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
                    if (oxygenReqMet && soundInWater && !PlayerIgnoresBubbles(player.Name) && !IgnoreWater)
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
                Hull? listenHull = eavesdroppedHull ?? ViewTargetHull;
                Vector2 listenPos = IsViewTargetPlayer ? character.AnimController.GetLimb(LimbType.Head)?.Position ?? character.AnimController.MainLimb.Position : LightManager.ViewTarget.Position;
                Vector2 listenWorldPos = IsViewTargetPlayer ? character.AnimController.GetLimb(LimbType.Head)?.WorldPosition ?? character.AnimController.MainLimb.WorldPosition : LightManager.ViewTarget.WorldPosition;

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

                // Use the euclidean distance if the sound ignores paths.
                Distance = IgnorePath ? 
                    Vector2.Distance(listenPos, soundPos) : 
                    GetApproximateDistance(listenPos, soundPos, listenHull, SoundHull, Channel.Far);

                if (listenHull == eavesdroppedHull) { Eavesdropped = true; }

                if (Distance == float.MaxValue)
                {
                    Muffled = !IgnoreLowpass;
                    Reason = MuffleReason.NoPath;
                    return;
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

                // Exceptions to water:
                    // Neither in water.
                if (!earsInWater && !soundInWater                                    ||
                     // Both in water, but submersion is ignored.
                     earsInWater &&  soundInWater  &&  IgnoreSubmersion              ||
                     // Sound is under, ears are above, but water surface is ignored.
                     IgnoreWater &&  soundInWater  && !earsInWater ||
                     // Sound is above, ears are below, but water surface is ignored.
                     IgnoreWater && !soundInWater  && earsInWater && IgnoreSubmersion)
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

        public static void LoadBubbleSounds()
        {
            try
            {
                string? modPath = GetModDirectory();
                HydrophoneMovementSound = GameMain.SoundManager.LoadSound("Content/Sounds/Water/SplashLoop.ogg");
                BubbleSound = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_BubblesLoopMono.ogg"));
                RadioBubbleSound = GameMain.SoundManager.LoadSound(Path.Combine(modPath, "Content/Sounds/SPW_RadioBubblesLoopStereo.ogg"));
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"Soundproof Walls: Failed to load bubble sounds\n{ex.Message}");
            }
        }

        // Called every 5 seconds or when the client changes a setting.
        public static void UpdateServerConfig(bool manualUpdate = false)
        {
            if (!GameMain.IsMultiplayer) { return; }

            foreach (Client client in GameMain.Client.ConnectedClients)
            {
                if (client.IsOwner || client.HasPermission(ClientPermissions.Ban))
                {
                    if (LocalConfig.SyncSettings)
                    {
                        string data = DataAppender.AppendData(JsonSerializer.Serialize(LocalConfig), manualUpdate, GameMain.Client.SessionId);
                        IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_UpdateConfigServer");
                        message.WriteString(data);
                        GameMain.LuaCs.Networking.Send(message);
                    }
                    // Remove the server config for all users.
                    else if (ServerConfig != null)
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
        public static bool ShouldReloadSounds(Config newConfig, Config oldConfig)
        {
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

        public static bool ShouldClearMuffleInfo(Config newConfig)
        {
            Config currentConfig = ServerConfig ?? LocalConfig;
            return  !currentConfig.IgnoredSounds.SetEquals(newConfig.IgnoredSounds) ||
                    !currentConfig.PitchIgnoredSounds.SetEquals(newConfig.PitchIgnoredSounds) ||
                    !currentConfig.LowpassIgnoredSounds.SetEquals(newConfig.LowpassIgnoredSounds) ||
                    !currentConfig.ContainerIgnoredSounds.SetEquals(newConfig.ContainerIgnoredSounds) ||
                    !currentConfig.PathIgnoredSounds.SetEquals(newConfig.PathIgnoredSounds) ||
                    !currentConfig.WaterIgnoredSounds.SetEquals(newConfig.WaterIgnoredSounds) ||
                    !currentConfig.SubmersionIgnoredSounds.SetEquals(newConfig.SubmersionIgnoredSounds) ||
                    !currentConfig.BubbleIgnoredNames.SetEquals(newConfig.SubmersionIgnoredSounds);
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

        private static void ReloadCharacterSounds(bool onlyLoadMissingExtendedOggs = false)
        {
            int t = 0;
            int i = 0;
            Dictionary<string, Sound> updatedSounds = new Dictionary<string, Sound>();

            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead) { continue; }
                foreach (CharacterSound characterSound in character.sounds)
                {
                    i++;
                    Sound? oldSound = characterSound.roundSound.Sound;

                    if (oldSound == null || (onlyLoadMissingExtendedOggs && oldSound is ExtendedOggSound)) continue;

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
            LuaCsLogger.Log($"Created {t} new character sounds. Scanned {i}");
        }

        private static void ReloadComponentSounds(bool onlyLoadMissingExtendedOggs = false)
        {
            int t = 0;
            int i = 0;
            Dictionary<string, Sound> updatedSounds = new Dictionary<string, Sound>();

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

                            if (oldSound == null || (onlyLoadMissingExtendedOggs && oldSound is ExtendedOggSound)) continue;

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
            LuaCsLogger.Log($"Created {t} new comp sounds. Scanned {i}");
        }

        private static void ReloadStatusEffectSounds(bool onlyLoadMissingExtendedOggs = false)
        {
            int t = 0;
            Dictionary<string, Sound> updatedSounds = new Dictionary<string, Sound>();

            foreach (StatusEffect statusEffect in StatusEffect.ActiveLoopingSounds)
            {
                foreach (RoundSound roundSound in statusEffect.Sounds)
                {
                    Sound? oldSound = roundSound.Sound;

                    if (oldSound == null || (onlyLoadMissingExtendedOggs && oldSound is ExtendedOggSound)) continue;

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
            LuaCsLogger.Log($"Created {t} new status effect sounds");
        }

        private static void ReloadPrefabSounds(bool onlyLoadMissingExtendedOggs = false)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            int t = 0;
            foreach (SoundPrefab soundPrefab in SoundPrefab.Prefabs)
            {
                Sound oldSound = soundPrefab.Sound;

                if (oldSound == null || (onlyLoadMissingExtendedOggs && oldSound is ExtendedOggSound) || (!onlyLoadMissingExtendedOggs && IsFilenameInSet(oldSound.Filename, IgnoredPrefabs))) continue;

                soundPrefab.Sound = GetNewSound(oldSound);
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

        // onlyLoadMissingExtendedOggs will not reload a sound if it is already an ExtendedOggSound.
        public static void ReloadSounds(bool onlyLoadMissingExtendedOggs = false)
        {
            MoonSharp.Interpreter.DynValue Resound = GameMain.LuaCs.Lua.Globals.Get("Resound");
            StopResound(Resound);

            // Arranged from least to most sounds.
            ReloadStatusEffectSounds(onlyLoadMissingExtendedOggs);
            ReloadCharacterSounds(onlyLoadMissingExtendedOggs);
            ReloadComponentSounds(onlyLoadMissingExtendedOggs);
            ReloadPrefabSounds(onlyLoadMissingExtendedOggs);

            ClearSoundsToDispose();
            LuaCsLogger.Log($"LoadedSounds: {GameMain.SoundManager.LoadedSounds.Count}");

            StartResound(Resound);
        }
        
        public static void SetupHydrophoneSwitches()
        {
            if (!Config.HydrophoneSwitchEnabled) { return; }

            foreach (Item item in Item.RepairableItems)
            {
                if (item.Tags.Contains("command"))
                {
                    Sonar? sonar = item.GetComponent<Sonar>();
                    if (sonar == null) { continue ; }

                    if (sonar.HasMineralScanner) 
                    {
                        MakeRoomForHydrophoneSwitchMineralScanner(sonar);
                    }
                    else
                    {
                        MakeRoomForHydrophoneSwitchDefault(sonar);
                    }

                    AddHydrophoneSwitchToGUI(sonar);
                }
            }
        }

        public static void NewHydrophoneSwitches()
        {
            if (!Config.HydrophoneSwitchEnabled) { return; }

            foreach (Item item in Item.RepairableItems)
            {
                if (item.Tags.Contains("command"))
                {
                    Sonar? sonar = item.GetComponent<Sonar>();
                    if (sonar == null) { continue; }

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

        public static void UpdateHydrophoneSwitchesLegacy()
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

        // Called at the end of a round or when Lua is reloaded.
        public static void KillSPW()
        {
            SoundChannelMuffleInfo.Clear();
            ResetAllPitchedSounds();
            //DisposeAllBubbleChannels();
            //DisposeAllHydrophoneChannels();
            DisposeAllCustomSounds(); // TODO verify this negates the need for the above two function calls.
            DisposeAllHydrophoneSwitches();

            LuaCsLogger.Log("Successfully killed Soundproof Walls.");
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

        static void UpdateEavesdroppingFade()
        {
            bool shouldFadeOut = !Config.Enabled || EavesdroppedHull == null;
            bool isPaused = GameMain.Instance.Paused;

            if (shouldFadeOut && EavesdroppingTextAlpha <= 0 && EavesdroppingEfficiency <= 0) { return; }

            else if (shouldFadeOut && !isPaused)
            {
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha - 15, 0, 255);
                EavesdroppingEfficiency = Math.Clamp(EavesdroppingEfficiency - 0.02f, 0, 100);
            }
            else if (!shouldFadeOut && !isPaused)
            {
                EavesdroppingTextAlpha = Math.Clamp(EavesdroppingTextAlpha + 15, 0, 255);
                EavesdroppingEfficiency = Math.Clamp(EavesdroppingEfficiency + 0.01f, 0, 100);
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
                Character character = __instance.gameClient.Character;
                if (character != null)
                {
                    Limb playerHead = GetCharacterHead(character);
                    Hull limbHull = playerHead.Hull;
                    bool wearingDivingGear = IsCharacterWearingDivingGear(character);
                    bool oxygenReqMet = wearingDivingGear && character.Oxygen < 11 || !wearingDivingGear && character.OxygenAvailable < 96;

                    if (oxygenReqMet && SoundInWater(playerHead.Position, limbHull) && character.SpeechImpediment < 100 && !PlayerIgnoresBubbles(character.Name))
                    {
                        GameMain.ParticleManager.CreateParticle(
                            "bubbles",
                            playerHead.WorldPosition,
                            velocity: playerHead.LinearVelocity * 10,
                            rotation: 0,
                            limbHull);
                    }
                }

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
                DebugConsole.Log("Failed to find voip queue");
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
                bubbleChannel.FrequencyMultiplier = 1.0f;
                bubbleChannel.Looping = false;
                bubbleChannel.Gain = 0; // Might be overkill.
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

            bool isPlaying = ClientBubbleSoundChannels.TryGetValue(client, out SoundChannel? currentBubbleChannel) && currentBubbleChannel != null;
            bool soundMatches = true;

            if (isPlaying)
            {
                soundMatches = currentBubbleChannel.Sound.Filename == RadioBubbleSound?.Filename && messageType == ChatMessageType.Radio ||
                               currentBubbleChannel.Sound.Filename == BubbleSound?.Filename && messageType != ChatMessageType.Radio;
            }

            // Check if bubbles should be playing.
            if (soundMatches && soundInWater && oxygenReqMet && !PlayerIgnoresBubbles(player.Name))
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

        public static void SPW_VoipSound_ApplyFilters_Prefix(VoipSound __instance)
        {
            VoipSound voipSound = __instance;
            var muffleFiltersField = VoipSoundMuffleFiltersField;

            if (!Config.Enabled || voipSound == null || !voipSound.IsPlaying ||
                !SoundChannelMuffleInfo.TryGetValue(voipSound.soundChannel, out MuffleInfo muffleInfo))
            {
                var mf = muffleFiltersField?.GetValue(voipSound) as BiQuad[];
                if (mf == null || mf.Length != 1 || mf[0]._frequency != VANILLA_VOIP_LOWPASS_FREQUENCY)
                {
                    muffleFiltersField?.SetValue(voipSound, new BiQuad[] { new LowpassFilter(VoipConfig.FREQUENCY, VANILLA_VOIP_LOWPASS_FREQUENCY) });
                }
                return; 
            }

            // Modify readonly fields via reflection
            var muffleFilters = muffleFiltersField?.GetValue(voipSound) as BiQuad[];

            // Player's voice is currently muffled by listener's suit.
            if (voipSound.UseMuffleFilter && muffleInfo.Reason == MuffleReason.Suit)
            {
                // Muffled by something else + suit filter does not exist or suit/voice lowpass has changed.
                if (muffleInfo.Distance == float.MaxValue &&
                    (muffleFilters == null || muffleFilters.Length < 2 ||
                    muffleFilters[1]._frequency != Config.DivingSuitLowpassFrequency ||
                    muffleFilters[0]._frequency != Config.VoiceLowpassFrequency))
                {

                }
                // Not muffled by anything else + suit filter does not exist or suit lowpass has changed.
                else if (muffleInfo.Distance != float.MaxValue &&
                    (muffleFilters == null || muffleFilters.Length < 2 ||
                    muffleFilters[1]._frequency != Config.DivingSuitLowpassFrequency))
                {
                    muffleFiltersField?.SetValue(voipSound, new BiQuad[] { new LowpassFilter(VoipConfig.FREQUENCY, Config.DivingSuitLowpassFrequency) });
                }
            }
            // Player's voice is not muffled by listener's suit.
            else
            {
                // Modify if suit filter still exists or voice lowpass has changed.
                if (muffleFilters == null || muffleFilters.Length != 1 ||
                    muffleFilters[0]._frequency != Config.VoiceLowpassFrequency)
                {
                    muffleFiltersField?.SetValue(voipSound, new BiQuad[] { new LowpassFilter(VoipConfig.FREQUENCY, Config.VoiceLowpassFrequency) });
                }
            }

            ProcessVoipSound(voipSound, muffleInfo);
        }

        // Old method of applying muffle filters. Used if MuffleDivingSuit is disabled and ExtendedOggSounds are not being used.
        public static bool SPW_BiQuad(BiQuad __instance, ref double frequency, ref double sampleRate)
        {
            if (!Config.Enabled || !Config.MuffleDivingSuits || __instance.GetType() != typeof(LowpassFilter)) { return true; };

            if (frequency == SoundPlayer.MuffleFilterFrequency)
            {
                frequency = Config.GeneralLowpassFrequency;
            }
            else
            {
                frequency = Config.VoiceLowpassFrequency;
            }
            return true;
        }

        // Runs at the start of the SoundChannel disposing method.
        public static void SPW_Dispose(SoundChannel __instance)
        {
            if (!Config.Enabled) { return; };

            __instance.Looping = false;

            SoundChannelMuffleInfo.TryRemove(__instance, out MuffleInfo? _);
            HydrophoneSoundChannels.Remove(__instance);
            PitchedSounds.TryRemove(__instance, out bool _);
        }

        public static void SPW_EndRound()
        {
            KillSPW();
        }

        public static void SPW_StartRound()
        {
            SetupHydrophoneSwitches();
        }

        public static void SPW_Update()
        {
            MuffleFakeSounds(Config.MuffleFlowSounds, SoundPlayer.flowSoundChannels);
            MuffleFakeSounds(Config.MuffleFireSounds, SoundPlayer.fireSoundChannels);

            UpdateEavesdroppingFade();

            // Must be above the early return so the config being disabled can be enforced automatically.
            if (Timing.TotalTime > LastSyncUpdateTime + 5)
            {
                LastSyncUpdateTime = (float)Timing.TotalTime;
                UpdateServerConfig(manualUpdate: false);
            }

            if (!Config.Enabled || !RoundStarted)
            {
                ResetAllPitchedSounds();
                DisposeAllBubbleChannels();
                DisposeAllHydrophoneChannels();
                return;
            }

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

        public static bool SPW_SoundPlayer_PlaySound(ref Sound sound, ref float? range, ref Vector2 position, ref Hull hullGuess)
        {
            if (!Config.Enabled || !RoundStarted || sound == null) { return true; }

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
            return true;
        }

        private static bool isMuffleTypeEqual(MuffleReason reasonOne, MuffleReason reasonTwo)
        {
            int muffleTypeOne;
            int muffleTypeTwo;

            if (reasonOne == MuffleReason.None) { muffleTypeOne = 1; }
            else if (reasonOne == MuffleReason.Suit) { muffleTypeOne = 2; }
            else { muffleTypeOne = 3; }

            if (reasonTwo == MuffleReason.None) { muffleTypeTwo = 1; }
            else if (reasonTwo == MuffleReason.Suit) { muffleTypeTwo = 2; }
            else { muffleTypeTwo = 3; }

            return muffleTypeOne == muffleTypeTwo;
        }

        public static bool SPW_SoundChannel_SetMuffled_Prefix(SoundChannel __instance, bool value)
        {
            SoundChannel instance = __instance;

            if (!Config.Enabled) { return true; }

            if (instance.Sound is not ExtendedOggSound extendedSound)
            {
                ReloadSounds(onlyLoadMissingExtendedOggs: true);
                LuaCsLogger.Log("Missing ExtendedOgg in SetMuffled_Prefix. Reloaded all sounds.");
                return true;
            }

            if (!SoundChannelMuffleInfo.TryGetValue(instance, out MuffleInfo? muffleInfo)) { return false; }

            if (muffleInfo.Muffled == instance.muffled && isMuffleTypeEqual(muffleInfo.Reason, muffleInfo.PreviousReason)) { return false; }
            
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
            if (extendedSound.Buffers is not { AlBuffer: not 0, AlNormMuffledBuffer: not 0, AlSuitMuffledBuffer: not 0 }) { return false; }

            uint alBuffer;
            if (muffleInfo.Muffled)
            {
                alBuffer = muffleInfo.MuffledBySuit ? extendedSound.Buffers.AlSuitMuffledBuffer : extendedSound.Buffers.AlNormMuffledBuffer;
            }
            else
            {
                alBuffer = extendedSound.Buffers.AlBuffer;
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

        public static bool SPW_SoundChannel_Prefix(SoundChannel __instance, Sound sound, float gain, Vector3? position, float freqMult, float near, float far, string category, bool muffle)
        {
            SoundChannel instance = __instance;

            if (!Config.Enabled) { return true; }

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

                    if (instance.Sound is not ExtendedOggSound extendedSound)
                    {
                        ReloadSounds(onlyLoadMissingExtendedOggs: true);
                        LuaCsLogger.Log("Missing ExtendedOgg in SoundChannel_Prefix. Reloaded all sounds.");
                        return true;
                    }

                    extendedSound.FillAlBuffers();
                    if (extendedSound.Buffers is not { AlBuffer: not 0, AlNormMuffledBuffer: not 0, AlSuitMuffledBuffer: not 0 }) { return false; }

                    SetProperties();

                    MuffleInfo muffleInfo = new MuffleInfo(instance, dontMuffle: !muffle);
                    SoundChannelMuffleInfo[instance] = muffleInfo;

                    instance.muffled = muffleInfo.Muffled;

                    uint alBuffer;
                    if (muffleInfo.Muffled || extendedSound.Owner.GetCategoryMuffle(category))
                    {
                        alBuffer = muffleInfo.MuffledBySuit ? extendedSound.Buffers.AlSuitMuffledBuffer : extendedSound.Buffers.AlNormMuffledBuffer;
                    }
                    else
                    {
                        alBuffer = extendedSound.Buffers.AlBuffer;
                    }

                    Al.Sourcei(extendedSound.Owner.GetSourceFromIndex(extendedSound.SourcePoolIndex, instance.ALSourceIndex), Al.Buffer, (int)alBuffer);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        throw new Exception("Failed to bind buffer to source (" + instance.ALSourceIndex.ToString() + ":" + extendedSound.Owner.GetSourceFromIndex(extendedSound.SourcePoolIndex, instance.ALSourceIndex) + "," + alBuffer.ToString() + "): " + instance.debugName + ", " + Al.GetErrorString(alError));
                    }

                    ProcessSingleSound(instance, muffleInfo);

                    Al.SourcePlay(extendedSound.Owner.GetSourceFromIndex(extendedSound.SourcePoolIndex, instance.ALSourceIndex));
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

        // Old method for creating muffleInfos. Still used for non-ExtendedOggSounds.
        // TODO just move this functionality into the prefix to save on a Muffled property setter call.
        public static void SPW_SoundChannel_Postfix(SoundChannel __instance)
        {
            SoundChannel channel = __instance;

            if (!Config.Enabled || !RoundStarted || channel == null || channel.IsStream || channel.Sound is ExtendedOggSound) { return; }

            MuffleInfo muffleInfo = new MuffleInfo(channel, dontMuffle: !channel.Muffled);

            SoundChannelMuffleInfo[channel] = muffleInfo;

            channel.Muffled = muffleInfo.Muffled;

            ProcessSingleSound(channel, muffleInfo);
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

                if (muffleInfo.Reason != MuffleReason.None && muffleInfo.Reason != MuffleReason.BothInWater) { freqMult -= (1 - GetMuffledFrequencyMultiplier(channel, 0.5f)); }
                else if (muffleInfo.Reason == MuffleReason.BothInWater) { freqMult -= (1 - GetMuffledFrequencyMultiplier(channel)); }
                else if (eavesdropped) { freqMult -= (1 - Config.EavesdroppingPitchMultiplier); }
                else if (hydrophoned) { freqMult -= (1 - Config.HydrophonePitchMultiplier); }

                if (EarsInWater && muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                if (IsWearingDivingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }

                channel.FrequencyMultiplier = Math.Clamp(channel.FrequencyMultiplier * freqMult, 0.25f, 4);
            }

            float gainMult = 1;

            gainMult -= (1 - GetCustomGainMultiplier(channel.Sound.Filename));
            if (muffleInfo.Reason == MuffleReason.NoPath) { gainMult -= (1 - Config.MuffledSoundVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); gainMult *= EavesdroppingEfficiency; }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); gainMult *= HydrophoneEfficiency; }

            channel.Gain *= gainMult;
        }
        public static void ProcessLoopingSound(SoundChannel channel, MuffleInfo muffleInfo)
        {
            float currentGain = muffleInfo.ItemComp?.GetSoundVolume(muffleInfo.ItemComp?.loopingSound) ?? 1;

            if (muffleInfo.IgnorePitch)
            {
                channel.FrequencyMultiplier = 1;
            }
            if (muffleInfo.IgnoreAll) 
            {
                channel.Gain = currentGain;
                return; 
            }

            bool eavesdropped = muffleInfo.Eavesdropped;
            bool hydrophoned = IsUsingHydrophones && muffleInfo.SoundHull?.Submarine != LightManager.ViewTarget?.Submarine;

            if (!muffleInfo.IgnorePitch)
            {
                float freqMult = 1;

                if (muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.MuffledComponentPitchMultiplier); }
                else if (eavesdropped) { freqMult -= (1 - Config.EavesdroppingPitchMultiplier); }
                else if (hydrophoned) { freqMult -= (1 - Config.HydrophonePitchMultiplier); }
                else { freqMult -= (1 - Config.UnmuffledComponentPitchMultiplier); }

                if (EarsInWater && muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                if (IsWearingDivingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }


                channel.FrequencyMultiplier = Math.Clamp(1 * freqMult, 0.25f, 4);
                PitchedSounds[channel] = true;
            }

            float gainMult = 1;

            gainMult -= (1 - GetCustomGainMultiplier(channel.Sound.Filename));
            if (muffleInfo.Reason == MuffleReason.NoPath) { gainMult -= (1 - Config.MuffledComponentVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); gainMult *= EavesdroppingEfficiency; }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); gainMult *= HydrophoneEfficiency; }
            else { gainMult -= (1 - Config.UnmuffledComponentVolumeMultiplier); }

            float distFalloffMult = (muffleInfo.Muffled & !muffleInfo.MuffledBySuit) ? 0.7f : 1 - MathUtils.InverseLerp(channel.Near, channel.Far, muffleInfo.Distance);
            float targetGain = currentGain * gainMult * distFalloffMult;

            // This is preferable in vanilla but here it can create an audible pop in when a new sound channel comes into range.
            //float gainDiff = targetGain - channel.Gain;
            //channel.Gain += Math.Abs(gainDiff) < 0.1f ? gainDiff : Math.Sign(gainDiff) * 0.1f;

            channel.Gain = targetGain;
        }

        public static void ProcessVoipSound(VoipSound voipSound, MuffleInfo muffleInfo)
        {
            SoundChannel channel = voipSound.soundChannel;

            if (muffleInfo.IgnorePitch)
            {
                channel.FrequencyMultiplier = 1;
            }

            if (muffleInfo.IgnoreAll) { return; }

            bool eavesdropped = muffleInfo.Eavesdropped;
            bool hydrophoned = IsUsingHydrophones && muffleInfo.SoundHull?.Submarine != LightManager.ViewTarget?.Submarine;

            if (!muffleInfo.IgnorePitch)
            {
                float freqMult = 1;

                if (muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.MuffledVoicePitchMultiplier); }
                else { freqMult -= (1 - Config.UnmuffledVoicePitchMultiplier); }

                channel.FrequencyMultiplier = Math.Clamp(1 * freqMult, 0.25f, 4);
                PitchedSounds[channel] = true;
            }

            float gainMult = 1;

            if (muffleInfo.Reason == MuffleReason.NoPath) { gainMult -= (1 - Config.MuffledVoiceVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingVoiceVolumeMultiplier); gainMult *= EavesdroppingEfficiency; }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); gainMult *= HydrophoneEfficiency; }

            float targetGain = 1 * gainMult;
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
            else if (EavesdroppedHull != null)
            {
                ambienceVolume *= Config.UnsubmergedWaterAmbienceVolumeMultiplier / 2;
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

        // TODO implement proper prediction systems for if the sounds should be muffled via path.
        private static void MuffleFakeSounds(bool enabled, SoundChannel[] channels)
        {
            if (!enabled || !Config.Enabled)
            {
                foreach (SoundChannel channel in channels)
                {
                    if (channel == null) { continue; }
                    if (channel.Muffled) { channel.Muffled = false; }
                    if (channel.FrequencyMultiplier != 1) { channel.FrequencyMultiplier = 1; }
                }

                return;
            }

            foreach (SoundChannel channel in channels)
            {
                if (channel == null) { continue; }

                bool needsUpdate = true;
                if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo? muffleInfo))
                {
                    muffleInfo = new MuffleInfo(channel);
                    SoundChannelMuffleInfo[channel] = muffleInfo;
                    needsUpdate = false;
                }

                // Otherwise these sounds are specifically ignored.
                muffleInfo.IgnoreLowpass = false;

                if (needsUpdate)
                {
                    muffleInfo.Update();
                }

                channel.Muffled = muffleInfo.Muffled;

                float freqMult = 1;
                if (!muffleInfo.IgnorePitch)
                {
                    if (EarsInWater) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                    if (IsWearingDivingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }
                    if (IsUsingHydrophones) { freqMult -= (1 - Config.HydrophonePitchMultiplier); }
                }
                channel.FrequencyMultiplier = Math.Clamp(1 * freqMult, 0.25f, 4);
            }
        }


        // TODO Pretty bad. Create two systems (flow & fire) to use in MuffleFakeSounds. Also, remember the Config.EstimatePathToFakeSounds value.
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

        // Gets the distance between two localised positions going through gaps. Returns MaxValue if no path or out of range.
        public static float GetApproximateDistance(Vector2 startPos, Vector2 endPos, Hull startHull, Hull endHull, float maxDistance, float distanceMultiplierPerClosedDoor = 0)
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

        public static bool IsFilenameInSet(string filename, HashSet<string> set, string? exclude = null, string? include = null)
        {
            string f = filename.ToLower();
            foreach (string keyword in set)
            {
                if (exclude != null && f.Contains(exclude)) continue;

                if (include != null && f.Contains(include) || f.Contains(keyword.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        public static float GetCustomGainMultiplier(string filename)
        {
            string f = filename.ToLower();
            foreach (var kvp in Config.SoundVolumeMultipliers)
            {
                if (f.Contains(kvp.Key.ToLower()))
                {
                    return kvp.Value;
                }
            }

            return 1.0f;
        }

        public static bool PlayerIgnoresBubbles(string playername)
        {
            string n = playername.ToLower();
            foreach (string name in Config.BubbleIgnoredNames)
            {
                if (n.Contains(name.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        //TODO add config option for controlling the strength of this with a multiplier.
        public static float GetMuffledFrequencyMultiplier(SoundChannel channel, float startingFreq = 0.75f)
        {
            float distance = Vector3.Distance(GameMain.SoundManager.ListenerPosition, new Vector3(GetSoundChannelPos(channel), 0.0f));
            float distanceFromSoundRatio = Math.Clamp(1 - distance / channel.Far, 0, 1);
            return startingFreq * distanceFromSoundRatio + 0.25f;
        }

        public static bool IsEavesdroppedChannel(SoundChannel channel)
        {
            if (EavesdroppedHull == null) { return false; }
            Hull soundHull = Hull.FindHull(GetSoundChannelPos(channel), Character.Controlled.CurrentHull, true);
            return soundHull == EavesdroppedHull;
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

        // Submerged sounds/voices will be muffled for spectators. Not used anymore.
        public static bool ShouldMuffleSpectating(Vector2 soundPos, Hull soundHull)
        {
            if (soundHull == null) { return true; }

            if (soundHull.Submarine != null)
            {
                soundPos += -soundHull.Submarine.WorldPosition + soundHull.Submarine.HiddenSubPosition;
            }

            return SoundInWater(soundPos, soundHull);
        }
    }
}
