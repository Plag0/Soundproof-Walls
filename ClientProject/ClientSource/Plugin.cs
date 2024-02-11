using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Text.RegularExpressions;
using Lidgren.Network;
using System.Net;

namespace SoundproofWalls
{
    public partial class SoundproofWalls : IAssemblyPlugin
    {
        static string? ModPath;
        static bool RoundStarted { get { return GameMain.gameSession?.IsRunning ?? false; } }

        private static Config localConfig = ConfigManager.LoadConfig();
        private static Config? serverConfig = null;
        public static Config Config { get { return serverConfig ?? localConfig; } }
        public static Config LocalConfig { get { return localConfig; } set { localConfig = value; } } //TODO these properties are useless
        public static Config? ServerConfig { get { return serverConfig; } }

        static Hull? EavesdroppedHull = null;

        static Hull? ViewTargetHull = null;
        static float LastViewTargetHullUpdateTime = 0f;

        static bool IsUsingHydrophones;
        static bool IsViewTargetPlayer;
        public static bool EarsInWater;
        public static bool IsWearingDivingSuit;

        static Sound? BubbleSound;
        static Sound? RadioBubbleSound;
        static Sound HydrophoneMovementSound = GameMain.SoundManager.LoadSound("Content/Sounds/Water/SplashLoop.ogg");
        static float LastHydrophonePlayTime = 0f;

        static Hull? CameraHull = null;

        public static bool SoundsLoaded = false;
        public static float SoundsLoadedDelayTime = 3 * 60;

        static bool prevWearingSuit = false;
        static bool prevEarsInWater = false;

        static float LastDrawEavesdroppingTextTime = 0f;
        static float textFade = 0;

        public static Dictionary<SoundChannel, MuffleInfo> SoundChannelMuffleInfo = new Dictionary<SoundChannel, MuffleInfo>();
        static Dictionary<SoundChannel, Character> HydrophoneSoundChannels = new Dictionary<SoundChannel, Character>();
        static Dictionary<Sonar, HydrophoneSwitch> HydrophoneSwitches = new Dictionary<Sonar, HydrophoneSwitch>();

        // Key is the player's voip sound. Value is the bubble sounds.
        static Dictionary<SoundChannel, SoundChannel> BubbleSoundChannels = new Dictionary<SoundChannel, SoundChannel>();

        static HashSet<SoundChannel> PitchedSounds = new HashSet<SoundChannel>();

        public void InitClient()
        {
            // Lua reload patch
            GameMain.LuaCs.Hook.Add("loaded", "spw_loaded", (object[] args) =>
            {
                UpdateServerConfig();
                NewHydrophoneSwitches();
                return null;
            });

            GameMain.LuaCs.Hook.Add("client.connected", "spw_client.connected", (object[] args) =>
            {
                UpdateServerConfig();
                return null;
            });

            // Lua reload patch
            GameMain.LuaCs.Hook.Add("stop", "spw_stop", (object[] args) =>
            {
                foreach (var kvp in HydrophoneSwitches)
                {
                    HydrophoneSwitch hydrophoneSwitch = kvp.Value;
                    hydrophoneSwitch.Switch.RemoveFromGUIUpdateList();
                    hydrophoneSwitch.Switch.Visible = false;
                    hydrophoneSwitch.TextBlock.RemoveFromGUIUpdateList();
                    hydrophoneSwitch.TextBlock.Visible = false;

                }
                HydrophoneSwitches.Clear();
                KillSPW();
                return null;
            });

            // Think postfix patch
            harmony.Patch(
                typeof(GameMain).GetMethod(nameof(GameMain.Update), BindingFlags.Instance | BindingFlags.NonPublic),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Update))));

            //StartRound postfix patch
            harmony.Patch(
                typeof(GameSession).GetMethod(nameof(GameSession.StartRound), new Type[] { typeof(LevelData), typeof(bool), typeof(SubmarineInfo), typeof(SubmarineInfo) }),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_StartRound))));

            //EndRound postfix patch
            harmony.Patch(
                typeof(GameSession).GetMethod(nameof(GameSession.EndRound)),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_EndRound))));

            // BiQuad prefix patch
            harmony.Patch(
                typeof(BiQuad).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, new Type[] { typeof(int), typeof(double), typeof(double), typeof(double) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_BiQuad))));

            //PlaySound prefix patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.PlaySound), new Type[] { typeof(Sound), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(Hull), typeof(bool) }),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_PlaySound))));

            // ShouldMuffleSounds prefix and blank replacement patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.ShouldMuffleSound)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_ShouldMuffleSounds))));

            //WaterFlowSounds postfix patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateWaterFlowSounds), BindingFlags.Static | BindingFlags.NonPublic),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateWaterFlowMuffling))));

            //FireSounds postfix patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateFireSounds), BindingFlags.Static | BindingFlags.NonPublic),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateFireMuffling))));

            // SoundChannel postfix patch
            harmony.Patch(
                typeof(SoundChannel).GetConstructor(new Type[] { typeof(Sound), typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(string), typeof(bool) }),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel))));

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

            // VoipSound prefix and replacement patch
            harmony.Patch(
                typeof(Client).GetMethod(nameof(Client.UpdateVoipSound), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateVoipSound))));

            // UpdateTransform postfix patch
            harmony.Patch(
                typeof(Camera).GetMethod(nameof(Camera.UpdateTransform)),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateTransform))));

            // UpdateWaterAmbience prefix patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateWaterAmbience), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_UpdateWaterAmbience))));

            // Dispose postfix patch
            harmony.Patch(
                typeof(SoundChannel).GetMethod(nameof(SoundChannel.Dispose)),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Dispose))));

            // Draw prefix patch
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.Draw)),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Draw))));

            // TogglePauseMenu postfix
            harmony.Patch(
                typeof(GUI).GetMethod(nameof(GUI.TogglePauseMenu)),
                null,
                new HarmonyMethod(typeof(EasySettings).GetMethod(nameof(EasySettings.SPW_TogglePauseMenu))));

            // Clients receiving the host's config.
            GameMain.LuaCs.Networking.Receive("SPW_UpdateConfigClient", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];
                Config? newServerConfig = JsonSerializer.Deserialize<Config>(msg.ReadString());

                bool shouldReloadRoundSounds = ShouldReloadRoundSounds(newServerConfig);

                serverConfig = newServerConfig;

                if (shouldReloadRoundSounds) { ReloadRoundSounds(); }
                SoundChannelMuffleInfo.Clear(); // Clear muffle info objects in case advanced settings were changed.

                LuaCsLogger.Log(TextManager.Get("spw_updateserverconfig").Value, Color.LimeGreen);
            });

            GameMain.LuaCs.Networking.Receive("SPW_DisableConfigClient", (object[] args) =>
            {
                bool shouldReloadRoundSounds = ShouldReloadRoundSounds(LocalConfig);

                serverConfig = null;

                if (shouldReloadRoundSounds) { ReloadRoundSounds(); }
                SoundChannelMuffleInfo.Clear();

                LuaCsLogger.Log(TextManager.Get("spw_disableserverconfig").Value, Color.LimeGreen);
            });

            ModPath = GetModDirectory();
            LoadCustomSounds();
            Menu.LoadMenu();
        }

        class HydrophoneSwitch
        {
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
            NoPath,
            SoundInWater,
            EarsInWater,
            BothInWater
        }

        public class MuffleInfo
        {
            public MuffleReason Reason = MuffleReason.None;
            public float Distance;

            public bool IgnoreWater = false;
            public bool IgnoreSubmersion = false;
            public bool IgnorePitch = false;
            public bool IgnoreLowpass = false;
            public bool IgnoreAll = false;

            public bool Muffled = false;
            public Hull? SoundHull;
            public ItemComponent? ItemComp = null;
            public Client? VoiceOwner = null;
            private SoundChannel Channel;

            public MuffleInfo(SoundChannel channel, Hull? soundHull = null, ItemComponent? itemComp = null, Client? voiceOwner = null, ChatMessageType? messageType = null, bool skipProcess = false, bool dontPitch = false)
            {
                Channel = channel;
                ItemComp = itemComp;
                VoiceOwner = voiceOwner;
                string filename = Channel.Sound.Filename;

                if (skipProcess) 
                { 
                    IgnorePitch = true; 
                }
                else if (Channel.Category == "ui" || SoundIgnoresAll(filename))
                {
                    IgnoreAll = true;
                }
                else
                {
                    IgnoreWater = SoundIgnoresWater(filename);
                    IgnoreSubmersion = SoundIgnoresSubmersion(filename);
                    IgnorePitch = dontPitch || SoundIgnoresPitch(filename);
                    IgnoreLowpass = SoundIgnoresLowpass(filename);
                }

                Update(soundHull, skipProcess: skipProcess);
            }

            public void Update(Hull? soundHull = null, ChatMessageType? messageType = null, bool skipProcess = false)
            {
                Muffled = false;
                Reason = MuffleReason.None;

                if (skipProcess)
                {
                    IgnorePitch = true;
                    Muffled = false;
                    return;
                }

                Character character = Character.Controlled;
                Character? player = VoiceOwner?.Character;
                Limb? playerHead = player?.AnimController?.GetLimb(LimbType.Head);

                Vector2 soundWorldPos = playerHead?.WorldPosition ?? GetSoundChannelPos(Channel);
                SoundHull = soundHull ?? Hull.FindHull(soundWorldPos, player?.CurrentHull ?? character?.CurrentHull);
                Vector2 soundPos = LocalizePosition(soundWorldPos, SoundHull);

                bool canHearUnderwater = IsViewTargetPlayer ? !Config.MuffleSubmergedPlayer : !Config.MuffleSubmergedViewTarget;
                bool canHearIntoWater = !Config.MuffleSubmergedSounds;
                bool soundInWater = SoundInWater(soundPos, SoundHull);
                bool spectating = character == null || LightManager.ViewTarget == null;

                PlayBubbleSounds(player, playerHead, Channel, soundInWater, messageType);

                if (messageType == ChatMessageType.Radio)
                {
                    if (soundInWater && player.OxygenAvailable < 95)
                    {
                        Reason = MuffleReason.SoundInWater;
                        Muffled = true;
                    }

                    return; 
                }

                if (spectating)
                {
                    Distance = Vector3.Distance(GameMain.SoundManager.ListenerPosition, new Vector3(soundWorldPos, 0.0f));
                    Muffled = soundInWater && !IgnoreLowpass && !canHearIntoWater;
                    Reason = Muffled ? MuffleReason.SoundInWater : MuffleReason.None; ;
                    return;
                }

                Hull? listenHull = EavesdroppedHull ?? ViewTargetHull;
                Vector2 listenPos = IsViewTargetPlayer ? character.AnimController.GetLimb(LimbType.Head).Position : LightManager.ViewTarget.Position;
                Vector2 listenWorldPos = IsViewTargetPlayer ? character.AnimController.GetLimb(LimbType.Head).WorldPosition : LightManager.ViewTarget.WorldPosition;

                if (IgnoreAll)
                {
                    Distance = Vector2.Distance(listenPos, soundPos);
                    return;
                }

                // Hydrophone check. Hear outside while sounds inside are muffled.
                if (IsUsingHydrophones && (SoundHull == null || SoundHull.Submarine == LightManager.ViewTarget.Submarine))
                {
                    Distance = SoundHull == null ? Vector2.Distance(listenWorldPos, soundWorldPos) : float.MaxValue;
                    Muffled = Distance == float.MaxValue && !IgnoreLowpass;
                    Reason = Muffled ? MuffleReason.NoPath : MuffleReason.None;
                    return;
                }

                // Gets path to sound. Returns MaxValue if no path or out of range.
                Distance = GetApproximateDistance(listenPos, soundPos, listenHull, SoundHull, Channel.Far);
                if (Distance == float.MaxValue)
                {
                    Muffled = !IgnoreLowpass;
                    Reason = MuffleReason.NoPath;
                    return;
                }

                // Water stuff from here.
                if (!soundInWater && !EarsInWater)
                {
                    Muffled = false;
                    Reason = MuffleReason.None;
                    return;
                }

                Muffled = true;

                if ((soundInWater && !EarsInWater && (IgnoreWater || canHearIntoWater)) ||
                    (EarsInWater && (IgnoreSubmersion || canHearUnderwater)))
                {
                    Muffled = false;
                    Reason = MuffleReason.None;
                    return;
                }

                Reason = soundInWater ? MuffleReason.SoundInWater : MuffleReason.EarsInWater;

                if (soundInWater && EarsInWater)
                {
                    Reason = MuffleReason.BothInWater;
                }

                Muffled = Muffled && !IgnoreLowpass;
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
        public static void PlayBubbleSounds(Character? player, Limb? playerHead, SoundChannel voipChannel, bool soundInWater, ChatMessageType? messageType = null)
        {
            if (playerHead == null) return;

            BubbleSoundChannels.TryGetValue(voipChannel, out SoundChannel? bubbleChannel);
            bool isPlaying = bubbleChannel != null && bubbleChannel.IsPlaying && !bubbleChannel.FadingOutAndDisposing && 
                (messageType == ChatMessageType.Radio && bubbleChannel.Sound.Filename.EndsWith("SPW_RadioBubblesLoopStereo.ogg") || 
                messageType != ChatMessageType.Radio && bubbleChannel.Sound.Filename.EndsWith("SPW_BubblesLoopMono.ogg"));
            bool shouldPlay = soundInWater && player.OxygenAvailable < 95;
            Hull limbHull = playerHead.Hull;

            if (shouldPlay)
            {
                if (!isPlaying)
                {
                    bubbleChannel?.Dispose();

                    if (messageType == ChatMessageType.Radio)
                    {
                        bubbleChannel = RadioBubbleSound?.Play(position: null, gain: 1, freqMult: MathHelper.Lerp(0.8f, 1.2f, MathUtils.InverseLerp(0, 2, player.CurrentSpeed)), muffle: false);
                    }
                    else
                    {
                        bubbleChannel = SoundPlayer.PlaySound(BubbleSound, playerHead.WorldPosition, volume: 1, freqMult: MathHelper.Lerp(1, 4, MathUtils.InverseLerp(0, 2, player.CurrentSpeed)), ignoreMuffling: true);
                    }

                    if (bubbleChannel != null)
                    {
                        bubbleChannel.Looping = true;
                        BubbleSoundChannels[voipChannel] = bubbleChannel;
                    }
                }
                else
                {
                    if (messageType == ChatMessageType.Radio)
                    {
                        bubbleChannel.Position = GameMain.SoundManager.ListenerPosition;
                        bubbleChannel.FrequencyMultiplier = MathHelper.Lerp(1, 2, MathUtils.InverseLerp(0, 2, player.CurrentSpeed));
                    }
                    else
                    {
                        bubbleChannel.Position = new Vector3(playerHead.WorldPosition, 0);
                        bubbleChannel.FrequencyMultiplier = MathHelper.Lerp(1, 4, MathUtils.InverseLerp(0, 2, player.CurrentSpeed));
                    }
                    GameMain.ParticleManager.CreateParticle(
                        "bubbles",
                        playerHead.WorldPosition,
                        velocity: playerHead.LinearVelocity * 10,
                        rotation: 0,
                        limbHull);
                }
            }
            else if (isPlaying)
            {
                bubbleChannel.FadeOutAndDispose();
                BubbleSoundChannels.Remove(voipChannel);
            }
        }

        public static void DisposeAllBubbleChannels()
        {
            foreach (var kvp in BubbleSoundChannels) 
            {
                SoundChannel channel = kvp.Value;
                channel.FadeOutAndDispose();
            }
            BubbleSoundChannels.Clear();
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
                BubbleSound = GameMain.SoundManager.LoadSound(Path.Combine(ModPath, "Content/Sounds/SPW_BubblesLoopMono.ogg"));
                RadioBubbleSound = GameMain.SoundManager.LoadSound(Path.Combine(ModPath, "Content/Sounds/SPW_RadioBubblesLoopStereo.ogg"));
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"Soundproof Walls: Failed to load custom sounds\n{ex.Message}");
            }
        }

        // Called when the client changes a setting.
        public static void UpdateServerConfig()
        {
            if (GameMain.IsMultiplayer && (GameMain.Client.IsServerOwner || GameMain.Client.HasPermission(ClientPermissions.Ban)))
            {
                if (localConfig.SyncSettings)
                {
                    IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_UpdateConfigServer");
                    message.WriteString(JsonSerializer.Serialize(localConfig));
                    GameMain.LuaCs.Networking.Send(message);
                }
                else if (ServerConfig != null)
                {
                    IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_DisableConfigServer");
                    GameMain.LuaCs.Networking.Send(message);
                }
            };
        }

        public static bool ShouldReloadRoundSounds(Config newConfig) // this matters because reloading round sounds will freeze the game.
        {
            double vanillaFreq = 1600;
            Config currentConfig = ServerConfig ?? LocalConfig;
            double currentFreq = currentConfig.Enabled ? currentConfig.GeneralLowpassFrequency : vanillaFreq;
            double newFreq = newConfig.Enabled ? newConfig.GeneralLowpassFrequency : vanillaFreq;

            return currentFreq != newFreq;
        }

        public static void ReloadRoundSounds()
        {
            SoundsLoaded = false;
            foreach (Item item in Item.ItemList)
            {
                foreach (ItemComponent itemComponent in item.Components)
                {
                    foreach (var kvp in itemComponent.sounds)
                    {
                        foreach (ItemSound itemSound in kvp.Value)
                        {
                            Sound newSound = GameMain.SoundManager.LoadSound(itemSound.RoundSound.Filename, itemSound.RoundSound.Stream);
                            itemSound.RoundSound.Sound = newSound;
                            itemComponent.PlaySound(itemSound, itemComponent.Item.WorldPosition);
                        }
                    }

                }
            }

            foreach (StatusEffect statusEffect in StatusEffect.ActiveLoopingSounds)
            {
                foreach (RoundSound roundSound in statusEffect.Sounds)
                {
                    Sound newSound = GameMain.SoundManager.LoadSound(roundSound.Filename, roundSound.Stream);
                    roundSound.Sound = newSound;
                }
            }
            SoundsLoaded = true;
        }
        
        public static void SetupHydrophoneSwitches()
        {
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

            instance.PreventMineralScannerOverlap();
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
                    if (GameMain.Client != null)
                    {
                        instance.unsentChanges = true;
                        instance.correctionTimer = Sonar.CorrectionDelay;
                    }

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

        public static void KillSPW()
        {
            SoundsLoaded = false;
            SoundsLoadedDelayTime = 3 * 60;
            SoundChannelMuffleInfo.Clear();
            PitchedSounds.Clear();
            DisposeAllBubbleChannels();

            foreach (var kvp in HydrophoneSoundChannels)
            {
                kvp.Key.FadeOutAndDispose();
            }
            HydrophoneSoundChannels.Clear();
        }

        public static void PlayHydrophoneSounds()
        {
            if (!Config.Enabled || !IsUsingHydrophones) { return; }

            float range = Config.HydrophoneSoundRange;

            foreach (Character character in Character.CharacterList)
            {
                if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), character.WorldPosition) > range * range || HydrophoneSoundChannels.Any(kvp => kvp.Value == character) || character.CurrentHull != null || character.CurrentSpeed < 0.05 || character.isDead)
                {
                    continue;
                }

                float startingGain = 1 * MathUtils.InverseLerp(0f, 2f, character.CurrentSpeed);
                float speed = Math.Clamp(character.CurrentSpeed, 0f, 10f);
                float freqMult = MathHelper.Lerp(0.25f, 4f, speed / 10f);

                SoundChannel channel = HydrophoneMovementSound.Play(startingGain, range, freqMult, character.WorldPosition, false);

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
                float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, Character.Controlled.WorldPosition);
                channel.Gain = 1 * MathUtils.InverseLerp(0f, 2f, character.CurrentSpeed);

                if (distanceSquared > channel.far * channel.far || channel.Gain < 0.001f || character.CurrentHull != null || character.isDead)
                {
                    HydrophoneSoundChannels.Remove(channel);
                    SoundChannelMuffleInfo.Remove(channel);
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

        public static bool SPW_Draw(ref Camera cam, ref SpriteBatch spriteBatch)
        {
            if (!Config.Enabled) { return true; }
            DrawEavesdroppingText(cam, spriteBatch);
            return true;
        }

        public static void DrawEavesdroppingText(Camera cam, SpriteBatch spriteBatch)
        {
            if (EavesdroppedHull == null && textFade <= 0)
            {
                return;
            }
            else if (EavesdroppedHull == null && !GameMain.Instance.Paused && Timing.TotalTime > LastDrawEavesdroppingTextTime + Timing.Step)
            {
                textFade = Math.Clamp(textFade - 15, 0, 255);
                LastDrawEavesdroppingTextTime = (float)Timing.TotalTime;
            }
            else if (EavesdroppedHull != null && !GameMain.Instance.Paused && Timing.TotalTime > LastDrawEavesdroppingTextTime + Timing.Step)
            {
                textFade = Math.Clamp(textFade + 15, 0, 255);
                LastDrawEavesdroppingTextTime = (float)Timing.TotalTime;
            }

            Character character = Character.Controlled;
            Limb limb = character.AnimController.GetLimb(LimbType.Head) ?? character.AnimController.MainLimb;

            Vector2 position = cam.WorldToScreen(limb.body.DrawPosition + new Vector2(0, 42));
            LocalizedString text = TextManager.Get("spw_listening");
            float size = 1.4f;
            Color color = new Color(224, 214, 164, (int)(textFade));
            GUIFont font = GUIStyle.Font;

            font.DrawString(spriteBatch, text, position, color, 0, Vector2.Zero,
                cam.Zoom / size, 0, 0.001f, Alignment.Center);
        }

        public static bool SPW_ShouldMuffleSounds(ref bool __result)
        {
            if (!Config.Enabled) { return true; }
            __result = true;
            return false;
        }

        public static bool SPW_VoipClient_SendToServer(VoipClient __instance)
        {
            //return true; // Currently disabled.

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
                if (character != null) {
                    Limb playerHead = character.AnimController.GetLimb(LimbType.Head) ?? character.AnimController.MainLimb;
                    Hull limbHull = playerHead.Hull;
                    if (character.OxygenAvailable < 90 && SoundInWater(playerHead.Position, limbHull) && character.SpeechImpediment < 100)
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
            if (!Config.Enabled || !RoundStarted) { return true; }
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
            if (queue.Read(msg, discardData: client.Muted || client.MutedLocally))
            {
                if (client.Muted || client.MutedLocally || (client.Character != null && client.Character.SpeechImpediment >= 100.0f))
                {
                    client.VoipSound?.soundChannel?.Dispose();
                    client.radioNoiseChannel?.Dispose();
                    client.radioNoiseChannel = null;
                    client.VoipSound = null;
                    return false;
                }

                if (client.VoipSound == null)
                {
                    client.VoipSound = new VoipSound(client.Name, GameMain.SoundManager, client.VoipQueue);
                }

                GameMain.SoundManager.ForceStreamUpdate();
                GameMain.NetLobbyScreen?.SetPlayerSpeaking(client);
                GameMain.GameSession?.CrewManager?.SetClientSpeaking(client);
                GameMain.SoundManager.VoipAttenuatedGain = 0.5f;

                bool clientIsPlaying = client.Character != null && !client.Character.IsDead && !client.Character.Removed;

                // Attenuate other sounds when players speak.
                if ((client.VoipSound.CurrentAmplitude * client.VoipSound.Gain * GameMain.SoundManager.GetCategoryGainMultiplier("voip")) > 0.1f)
                {
                    if (clientIsPlaying)
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

                if (!clientIsPlaying) { return false; }

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

                client.Character.ShowSpeechBubble(1.25f, ChatMessage.MessageColor[(int)messageType]);

                // Muffle Info stuff.
                SoundChannel channel = client.VoipSound.soundChannel;
                if (channel == null) { return true; }

                if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
                {
                    muffleInfo = new MuffleInfo(channel, client.Character.CurrentHull, voiceOwner: client, messageType: messageType);
                    SoundChannelMuffleInfo[channel] = muffleInfo;
                }

                muffleInfo.VoiceOwner = client;
                muffleInfo.Update(client.Character.CurrentHull, messageType: messageType);

                client.VoipSound.UseMuffleFilter = muffleInfo.Muffled;
                client.VoipSound.UseRadioFilter = messageType == ChatMessageType.Radio && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters;

                client.RadioNoise = 0.0f;

                // Range.
                if (messageType == ChatMessageType.Radio)
                {
                    client.VoipSound.SetRange(senderRadio.Range * VoipClient.RangeNear * speechImpedimentMultiplier * radioRangeMultiplier, senderRadio.Range * speechImpedimentMultiplier * radioRangeMultiplier);
                    if (distanceFactor > VoipClient.RangeNear && !spectating)
                    {
                        //noise starts increasing exponentially after 40% range
                        client.RadioNoise = MathF.Pow(MathUtils.InverseLerp(VoipClient.RangeNear, 1.0f, distanceFactor), 2);
                    }
                }
                else
                {
                    client.VoipSound.SetRange(ChatMessage.SpeakRange * VoipClient.RangeNear * speechImpedimentMultiplier * localRangeMultiplier, ChatMessage.SpeakRange * speechImpedimentMultiplier * localRangeMultiplier);
                }
            }
            return false;
        }

        public static void SPW_UpdateVoipSound(Client __instance)
        {
            if (!Config.Enabled || __instance?.VoipSound?.soundChannel == null) { return; }

            VoipSound voipSound = __instance.VoipSound;

            if (!SoundChannelMuffleInfo.TryGetValue(voipSound.soundChannel, out MuffleInfo muffleInfo))
            {
                muffleInfo = new MuffleInfo(voipSound.soundChannel);
                SoundChannelMuffleInfo[voipSound.soundChannel] = muffleInfo;
            }
            ProcessVoipSound(voipSound, muffleInfo);
        }

        public static void SPW_Dispose(SoundChannel __instance)
        {
            if (!Config.Enabled) { return; };
            __instance.Looping = false;
            PitchedSounds.Remove(__instance);
            SoundChannelMuffleInfo.Remove(__instance);
            HydrophoneSoundChannels.Remove(__instance);
            if (BubbleSoundChannels.TryGetValue(__instance, out SoundChannel bubbleChannel))
            {
                bubbleChannel.FadeOutAndDispose();
                BubbleSoundChannels.Remove(__instance);
            }
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
            if (!Config.Enabled || !RoundStarted)
            {
                foreach (SoundChannel pitchedChannel in PitchedSounds)
                {
                    pitchedChannel.FrequencyMultiplier = 1.0f;
                }
                PitchedSounds.Clear();

                DisposeAllBubbleChannels();
                return;
            }

            if (!SoundsLoaded)
            {
                SoundsLoadedDelayTime -= 1;
                if (SoundsLoadedDelayTime <= 0)
                {
                    SoundsLoaded = true;
                }
            }

            if (Character.Controlled == null || LightManager.ViewTarget == null || GameMain.Instance.Paused) { return; }

            EavesdroppedHull = GetEavesdroppedHull();
            IsUsingHydrophones = EavesdroppedHull == null && (Character.Controlled?.SelectedItem?.GetComponent<Sonar>() is Sonar sonar && HydrophoneSwitches.ContainsKey(sonar) && HydrophoneSwitches[sonar].State);
            IsViewTargetPlayer = !Config.FocusTargetAudio || LightManager.ViewTarget as Character == Character.Controlled;
            EarsInWater = IsViewTargetPlayer ? Character.Controlled.AnimController.HeadInWater : SoundInWater(LightManager.ViewTarget.Position, ViewTargetHull);
            IsWearingDivingSuit = Character.Controlled?.LowPassMultiplier < 0.5f;

            if (Timing.TotalTime > LastViewTargetHullUpdateTime + 0.05)
            {
                ViewTargetHull = GetViewTargetHull();
                LastViewTargetHullUpdateTime = (float)Timing.TotalTime;
            }

            if (Timing.TotalTime > LastHydrophonePlayTime + 0.1)
            {
                PlayHydrophoneSounds();
                LastHydrophonePlayTime = (float)Timing.TotalTime;
            }
            UpdateHydrophoneSounds();
            UpdateHydrophoneSwitches();
        }

        public static Hull? GetViewTargetHull()
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

        public static bool SPW_BiQuad(BiQuad __instance, ref double frequency, ref double sampleRate)
        {
            if (!Config.Enabled || __instance.GetType() != typeof(LowpassFilter)) { return true; };

            if (SoundsLoaded)
            {
                frequency = Config.VoiceLowpassFrequency;
            }
            else
            {
                frequency = Config.GeneralLowpassFrequency;
            }
            return true;
        }

        public static bool SPW_PlaySound(ref Sound sound, ref float? range, ref Vector2 position, ref Hull hullGuess)
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

        public static void SPW_SoundChannel(SoundChannel __instance)
        {
            SoundChannel channel = __instance;
            if (!Config.Enabled || !RoundStarted || channel == null) { return; }

            MuffleInfo muffleInfo = new MuffleInfo(channel, skipProcess: !channel.Muffled);

            try
            {
                SoundChannelMuffleInfo[channel] = muffleInfo;
            }
            catch (IndexOutOfRangeException ex)
            {
                LuaCsLogger.Log($"Failed to process sound: {ex}");
                SoundChannelMuffleInfo.Clear();
                return;
            }

            channel.Muffled = muffleInfo.Muffled;

            if (channel.Looping)
            {
                ProcessLoopingSound(channel, muffleInfo);
            }
            else
            {
                ProcessSingleSound(channel, muffleInfo);
            }
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

            if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
            {
                muffleInfo = new MuffleInfo(channel, item.CurrentHull, instance);
                SoundChannelMuffleInfo[channel] = muffleInfo;
            }

            muffleInfo.ItemComp = instance;

            if (Timing.TotalTime > instance.lastMuffleCheckTime + 0.2f)
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
                    if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
                    {
                        muffleInfo = new MuffleInfo(channel, skipProcess: statusEffect.ignoreMuffling, dontPitch: true);
                        SoundChannelMuffleInfo[channel] = muffleInfo;
                    }

                    if (doMuffleCheck && !statusEffect.ignoreMuffling)
                    {
                        muffleInfo.Update(skipProcess: statusEffect.ignoreMuffling);
                        channel.Muffled = muffleInfo.Muffled;
                    }

                    statusEffect.soundChannel.Position = new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f);

                    muffleInfo.IgnorePitch = true;
                    ProcessLoopingSound(channel, muffleInfo);
                }
            }
            ActiveLoopingSounds.RemoveWhere(s => s.soundChannel == null);

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

        //TODO merge all of these process functions into one.
        public static void ProcessSingleSound(SoundChannel channel, MuffleInfo muffleInfo)
        {
            if (muffleInfo.IgnoreAll) { return; }

            bool eavesdropped = IsEavesdroppedChannel(channel);
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
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); }

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

            bool eavesdropped = IsEavesdroppedChannel(channel);
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
                PitchedSounds.Add(channel);
            }

            float gainMult = 1;

            gainMult -= (1 - GetCustomGainMultiplier(channel.Sound.Filename));
            if (muffleInfo.Reason == MuffleReason.NoPath) { gainMult -= (1 - Config.MuffledComponentVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); }
            else { gainMult -= (1 - Config.UnmuffledComponentVolumeMultiplier); }

            float distFalloffMult = channel.Muffled ? 0.7f : 1 - MathUtils.InverseLerp(channel.Near, channel.Far, muffleInfo.Distance);
            float targetGain = currentGain * gainMult * distFalloffMult;

            // This is prefferable in vanilla but here it can create an audible pop in when a new sound channel comes into range.
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

            bool eavesdropped = IsEavesdroppedChannel(channel);
            bool hydrophoned = IsUsingHydrophones && muffleInfo.SoundHull?.Submarine != LightManager.ViewTarget?.Submarine;

            if (!muffleInfo.IgnorePitch)
            {
                float freqMult = 1;

                if (muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.MuffledVoicePitchMultiplier); }
                else { freqMult -= (1 - Config.UnmuffledVoicePitchMultiplier); }

                channel.FrequencyMultiplier = Math.Clamp(1 * freqMult, 0.25f, 4);
                PitchedSounds.Add(channel);
            }

            float gainMult = 1;

            if (muffleInfo.Reason == MuffleReason.NoPath) { gainMult -= (1 - Config.MuffledVoiceVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.BothInWater) { gainMult -= (1 - Config.SubmergedVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingVoiceVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); }

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

        public static bool SPW_UpdateWaterAmbience(ref float ambienceVolume)
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
                ambienceVolume *= Config.HydrophoneWaterAmbienceVolumeMultiplier;
            }
            else if (Config.FocusTargetAudio && LightManager.ViewTarget != null && ViewTargetHull == null)
            {
                ambienceVolume *= Config.SubmergedWaterAmbienceVolumeMultiplier;
            }
            else
            {
                ambienceVolume *= Config.UnsubmergedWaterAmbienceVolumeMultiplier;
            }
            return true;
        }

        public static void SPW_UpdateWaterFlowMuffling()
        {
            if (SoundPlayer.FlowSounds.Count == 0)
            {
                return;
            }

            bool wearingSuit = IsWearingDivingSuit;
            bool earsInWater = EarsInWater;
            bool shouldMuffle = (wearingSuit || earsInWater);

            if ((prevWearingSuit != wearingSuit || prevEarsInWater != earsInWater) && Config.Enabled && Config.MuffleFlowSounds)
            {
                foreach (SoundChannel channel in SoundPlayer.flowSoundChannels)
                {
                    if (channel == null) { continue; }
                    channel.Muffled = shouldMuffle;

                    float freqMult = 1;
                    if (shouldMuffle)
                    {
                        if (earsInWater) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                        if (wearingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }
                    }
                    channel.FrequencyMultiplier = Math.Clamp(1 * freqMult, 0.25f, 4);
                }
                prevWearingSuit = wearingSuit;
                prevEarsInWater = earsInWater;
            }
        }

        public static void SPW_UpdateFireMuffling()
        {
            bool wearingSuit = IsWearingDivingSuit;
            bool earsInWater = EarsInWater;
            bool shouldMuffle = (wearingSuit || earsInWater);

            if ((prevWearingSuit != wearingSuit || prevEarsInWater != earsInWater) && Config.Enabled && Config.MuffleFireSounds)
            {
                foreach (SoundChannel channel in SoundPlayer.fireSoundChannels)
                {
                    if (channel == null) { continue; }
                    channel.Muffled = shouldMuffle;

                    float freqMult = 1;
                    if (shouldMuffle)
                    {
                        if (earsInWater) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                        if (wearingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }
                    }
                    channel.FrequencyMultiplier = Math.Clamp(1 * freqMult, 0.25f, 4);
                }
                prevWearingSuit = wearingSuit;
                prevEarsInWater = earsInWater;
            }
        }

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
                if (g.ConnectedDoor != null && !g.ConnectedDoor.IsBroken)
                {
                    // Gap blocked if the door is closed or is curently closing and 90% closed.
                    if ((!g.ConnectedDoor.PredictedState.HasValue && g.ConnectedDoor.IsClosed || g.ConnectedDoor.PredictedState.HasValue && !g.ConnectedDoor.PredictedState.Value) && g.ConnectedDoor.OpenState < 0.1f)
                    {
                        if (distanceMultiplierFromDoors <= 0) { continue; }
                        distanceMultiplier *= distanceMultiplierFromDoors;
                    }
                }
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

        public static Hull? GetEavesdroppedHull()
        {
            Character character = Character.Controlled;

            if (!Config.EavesdroppingKeyOrMouse.IsDown() || character?.CurrentHull == null ||
                character.CurrentSpeed > 0.05 || character.IsUnconscious || character.IsDead)
            {
                return null;
            }
            Limb limb = character.AnimController.GetLimb(LimbType.Head) ?? character.AnimController.MainLimb;
            Vector2 characterHead = limb.WorldPosition;
            characterHead.Y = -characterHead.Y;
            int expansionAmount = Config.EavesdroppingMaxDistance;

            foreach (Gap gap in character.CurrentHull.ConnectedGaps)
            {
                if (gap.ConnectedDoor == null || gap.ConnectedDoor.OpenState > 0 || gap.ConnectedDoor.IsBroken) { continue; }

                Rectangle gWorldRect = gap.WorldRect;
                Rectangle gapBoundingBox = new Rectangle(
                    gWorldRect.X - expansionAmount / 2,
                    -gWorldRect.Y - expansionAmount / 2,
                    gWorldRect.Width + expansionAmount,
                    gWorldRect.Height + expansionAmount);

                if (gapBoundingBox.Contains(characterHead))
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

        public static bool SoundIgnoresWater(string filename)
        {
            string f = filename.ToLower();
            foreach (string sound in Config.WaterIgnoredSounds)
            {
                if (f.Contains(sound.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool SoundIgnoresSubmersion(string filename)
        {
            string exclude = "human";
            string f = filename.ToLower();
            foreach (string sound in Config.SubmersionIgnoredSounds)
            {
                if (f.Contains(sound.ToLower()) && !f.Contains(exclude))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool SoundIgnoresAll(string filename)
        {
            string f = filename.ToLower();
            foreach (string sound in Config.IgnoredSounds)
            {
                if (f.Contains(sound.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool SoundIgnoresPitch(string filename)
        {
            string f = filename.ToLower();
            foreach (string sound in Config.PitchIgnoredSounds)
            {
                if (f.Contains(sound.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool SoundIgnoresLowpass(string filename)
        {
            string f = filename.ToLower();
            foreach (string sound in Config.LowpassIgnoredSounds)
            {
                if (f.Contains(sound.ToLower()))
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

    public static class ConfigManager
    {
        public static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SoundproofWalls_Config.json");
        private static Config _cachedConfig = LoadConfig();

        public static Config LoadConfig()
        {
            if (_cachedConfig != null)
            {
                return _cachedConfig;
            }

            if (!File.Exists(ConfigPath))
            {
                _cachedConfig = new Config();
                SaveConfig(_cachedConfig);
            }
            else
            {
                try
                {
                    string jsonContent = File.ReadAllText(ConfigPath);
                    _cachedConfig = JsonSerializer.Deserialize<Config>(jsonContent) ?? new Config();
                    UpdateConfigFromJson(jsonContent, _cachedConfig);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading config: {ex.Message}");
                    _cachedConfig = new Config();
                }
            }

            return _cachedConfig;
        }

        private static void UpdateConfigFromJson(string jsonContent, Config config)
        {
            var jsonConfig = JsonSerializer.Deserialize<Config>(jsonContent);

            if (jsonConfig == null) return;

            foreach (var property in typeof(Config).GetProperties())
            {
                var jsonValue = property.GetValue(jsonConfig);
                if (jsonValue != null)
                {
                    property.SetValue(config, jsonValue);
                }
            }

            config.EavesdroppingKeyOrMouse = config.ParseEavesdroppingBind();
        }

        public static void SaveConfig(Config config)
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config));
        }
    }
    public class Config
    {

        [JsonIgnore]
        public KeyOrMouse EavesdroppingKeyOrMouse { get; set; } = new KeyOrMouse(MouseButton.SecondaryMouse);
        public KeyOrMouse ParseEavesdroppingBind()
        {
            if (Enum.IsDefined(typeof(Microsoft.Xna.Framework.Input.Keys), EavesdroppingBind))
            {
                return new KeyOrMouse((Microsoft.Xna.Framework.Input.Keys)Enum.Parse(typeof(Microsoft.Xna.Framework.Input.Keys), EavesdroppingBind));
            }
            else if (Enum.IsDefined(typeof(MouseButton), EavesdroppingBind))
            {
                return new KeyOrMouse((MouseButton)Enum.Parse(typeof(MouseButton), EavesdroppingBind));
            }
            return EavesdroppingKeyOrMouse;
        }

        public Config()
        {
            EavesdroppingKeyOrMouse = ParseEavesdroppingBind();
        }

        // General
        public bool Enabled { get; set; } = true;
        public bool SyncSettings { get; set; } = true;
        public bool TalkingRagdolls { get; set; } = true;
        public bool FocusTargetAudio { get; set; } = false;
        public double GeneralLowpassFrequency { get; set; } = 320f;
        public double VoiceLowpassFrequency { get; set; } = 160f;
        public float SoundRangeMultiplier { get; set; } = 1.8f;
        public float VoiceRangeMultiplier { get; set; } = 0.80f;
        public float RadioRangeMultiplier { get; set; } = 0.75f;

        // Volume
        public float MuffledSoundVolumeMultiplier { get; set; } = 0.65f;
        public float MuffledVoiceVolumeMultiplier { get; set; } = 0.80f;
        public float MuffledComponentVolumeMultiplier { get; set; } = 0.75f;
        public float SubmergedVolumeMultiplier { get; set; } = 3f;
        public float UnmuffledComponentVolumeMultiplier { get; set; } = 1f;

        // Eavesdropping
        public string EavesdroppingBind { get; set; } = "SecondaryMouse";
        public float EavesdroppingSoundVolumeMultiplier { get; set; } = 0.75f;
        public float EavesdroppingVoiceVolumeMultiplier { get; set; } = 0.70f;
        public float EavesdroppingPitchMultiplier { get; set; } = 0.85f;
        public int EavesdroppingMaxDistance { get; set; } = 40; // distance in cm from door

        // Hydrophone monitoring
        public float HydrophoneSoundRange { get; set; } = 7500; // range in cm
        public float HydrophoneVolumeMultiplier { get; set; } = 1.1f;
        public float HydrophonePitchMultiplier { get; set; } = 0.65f;

        // Ambience
        public float UnsubmergedWaterAmbienceVolumeMultiplier { get; set; } = 0.3f;
        public float SubmergedWaterAmbienceVolumeMultiplier { get; set; } = 1.1f;
        public float HydrophoneWaterAmbienceVolumeMultiplier { get; set; } = 2f;

        // Advanced settings
        public Dictionary<string, float> SoundVolumeMultipliers { get; set; } = new Dictionary<string, float>
        {
            { "explosion", 1.8f },
            { "weapon", 1.4f },
            { "footstep", 0.8f },
            { "metalimpact", 0.75f }
        };

        public HashSet<string> IgnoredSounds { get; set; } = new HashSet<string>
        {
            "/ambient/",
            "ambience",
            "dropitem",
            "pickitem",
        };

        public HashSet<string> PitchIgnoredSounds { get; set; } = new HashSet<string>
        {
            "deconstructor",
            "alarm",
            "sonar",
            "male",
            "female"
        };

        public HashSet<string> LowpassIgnoredSounds { get; set; } = new HashSet<string>
        {
        };

        // Ignore sounds in water without player - Underwater sounds that are NOT occluded when propagating from water to air.
        public HashSet<string> WaterIgnoredSounds { get; set; } = new HashSet<string>
        {
            "splash",
            "footstep",
            "door",
            "pump",
            "emp",
            "electricaldischarge",
            "sonardecoy",
            "alien"
        };

        // Ignore sounds in water with player - Underwater sounds that can be heard clearly when also underwater.
        public HashSet<string> SubmersionIgnoredSounds { get; set; } = new HashSet<string>
        {
            "/characters/",
            "divingsuit",
            "sonardecoy",
            "alienturret",
            "alien_artifactholderloop"
        };

        // Extra settings
        public bool MuffleSubmergedPlayer { get; set; } = true; // the equivalent of adding all sounds into SubmersionIgnoredSounds
        public bool MuffleSubmergedViewTarget { get; set; } = true; // ^
        public bool MuffleSubmergedSounds { get; set; } = true; // the equivalent of adding all sounds into WaterIgnoredSounds
        public bool MuffleFlowSounds { get; set; } = true;
        public bool MuffleFireSounds { get; set; } = true;
        public float DivingSuitPitchMultiplier { get; set; } = 0.90f;
        public float SubmergedPitchMultiplier { get; set; } = 0.80f;
        public float MuffledVoicePitchMultiplier { get; set; } = 1f;
        public float UnmuffledVoicePitchMultiplier { get; set; } = 1f;
        public float MuffledComponentPitchMultiplier { get; set; } = 1f;
        public float UnmuffledComponentPitchMultiplier { get; set; } = 1f;
        public bool HideSettings { get; set; } = false;
    }

    public static class Menu
    {
        static Config defaultConfig = new Config();
        public static void LoadMenu()
        {
            EasySettings.AddMenu(TextManager.Get("spw_settings").Value, parent =>
            {
                Config config = SoundproofWalls.LocalConfig;
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

                // General Lowpass Freq:
                GUITextBlock textBlockGLF = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 10, 1600, (float)config.GeneralLowpassFrequency, value =>
                {
                    value = RoundToNearestMultiple(value, 10);
                    config.GeneralLowpassFrequency = value;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.GeneralLowpassFrequency == defaultConfig.GeneralLowpassFrequency)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.GeneralLowpassFrequency == 1600)
                    {
                        slider_text = vanilla_preset;
                    }
                    textBlockGLF.Text = $"{TextManager.Get("spw_lowpassfrequency").Value}: {value}Hz {slider_text}";
                }, 10, true);
                textBlockGLF.Text = $"{TextManager.Get("spw_lowpassfrequency").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 10)}Hz{GetServerValueString(nameof(config.GeneralLowpassFrequency), "Hz")}";
                slider.ToolTip = TextManager.Get("spw_lowpassfrequencytooltip");

                // Voice Lowpass Freq:
                GUITextBlock textBlockVLF = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 10, 800, (float)config.VoiceLowpassFrequency, value =>
                {
                    value = RoundToNearestMultiple(value, 10);
                    config.VoiceLowpassFrequency = value;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.VoiceLowpassFrequency == defaultConfig.VoiceLowpassFrequency)
                    {
                        slider_text = default_preset;
                    }
                    else if (config.VoiceLowpassFrequency == 800)
                    {
                        slider_text = vanilla_preset;
                    }
                    textBlockVLF.Text = $"{TextManager.Get("spw_voicelowpassfrequency").Value}: {value}Hz {slider_text}";
                }, 10);
                textBlockVLF.Text = $"{TextManager.Get("spw_voicelowpassfrequency").Value}: {RoundToNearestMultiple(slider.BarScrollValue, 10)}Hz{GetServerValueString(nameof(config.VoiceLowpassFrequency), "Hz")}";
                slider.ToolTip = TextManager.Get("spw_voicelowpassfrequencytooltip");

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


                // Advanced Sound Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_advancedsoundsettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Sound Volume Multipliers:
                GUITextBlock textBlockSVM = EasySettings.TextBlock(list, $"{TextManager.Get("spw_soundvolumemultipliers").Value}{GetServerDictString(nameof(config.SoundVolumeMultipliers))}");
                GUITextBox soundListSVM = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatDictJsonTextBox(JsonSerializer.Serialize(config.SoundVolumeMultipliers)), 0.09f);
                soundListSVM.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.SoundVolumeMultipliers = JsonSerializer.Deserialize<Dictionary<string, float>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockSVM.Text = TextManager.Get("spw_soundvolumemultipliers").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockSVM.Text = $"{TextManager.Get("spw_soundvolumemultipliers").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                GUIButton button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.SoundVolumeMultipliers = defaultConfig.SoundVolumeMultipliers;
                    ConfigManager.SaveConfig(config);
                    soundListSVM.Text = FormatDictJsonTextBox(JsonSerializer.Serialize(config.SoundVolumeMultipliers));
                    return true;
                };


                // Ignored Sounds:
                GUITextBlock textBlockIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_ignoredsounds").Value}{GetServerHashSetString(nameof(config.IgnoredSounds))}");
                GUITextBox soundListIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatDictJsonTextBox(JsonSerializer.Serialize(config.IgnoredSounds)), 0.09f);
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
                    soundListIS.Text = FormatDictJsonTextBox(JsonSerializer.Serialize(config.IgnoredSounds));
                    return true;
                };

                // Pitch Ignored Sounds:
                GUITextBlock textBlockPIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_pitchignoredsounds").Value}{GetServerHashSetString(nameof(config.PitchIgnoredSounds))}");
                GUITextBox soundListPIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatDictJsonTextBox(JsonSerializer.Serialize(config.PitchIgnoredSounds)), 0.09f);
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
                    soundListPIS.Text = FormatDictJsonTextBox(JsonSerializer.Serialize(config.PitchIgnoredSounds));
                    return true;
                };

                // Lowpass Ignored Sounds:
                GUITextBlock textBlockLIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_lowpassignoredsounds").Value}{GetServerHashSetString(nameof(config.LowpassIgnoredSounds))}");
                GUITextBox soundListLIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatDictJsonTextBox(JsonSerializer.Serialize(config.LowpassIgnoredSounds)), 0.09f);
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
                    soundListLIS.Text = FormatDictJsonTextBox(JsonSerializer.Serialize(config.LowpassIgnoredSounds));
                    return true;
                };

                // Water Ignored Sounds:
                GUITextBlock textBlockWIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_waterignoredsounds").Value}{GetServerHashSetString(nameof(config.WaterIgnoredSounds))}");
                GUITextBox soundListWIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatDictJsonTextBox(JsonSerializer.Serialize(config.WaterIgnoredSounds)), 0.09f);
                soundListWIS.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.WaterIgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
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
                    config.WaterIgnoredSounds = defaultConfig.WaterIgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundListWIS.Text = FormatDictJsonTextBox(JsonSerializer.Serialize(config.WaterIgnoredSounds));
                    return true;
                };

                // Submersion Ignored Sounds:
                GUITextBlock textBlockSIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_submersionignoredsounds").Value}{GetServerHashSetString(nameof(config.SubmersionIgnoredSounds))}");
                GUITextBox soundListSIS = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatDictJsonTextBox(JsonSerializer.Serialize(config.SubmersionIgnoredSounds)), 0.09f);
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
                    soundListSIS.Text = FormatDictJsonTextBox(JsonSerializer.Serialize(config.SubmersionIgnoredSounds));
                    return true;
                };


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

                // Diving Suit Pitch Multiplier
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

        public static string FormatDictJsonTextBox(string str)
        {
            string pattern = "},\"";
            string replacement = "},\n\"";

            // Use Regex to replace all occurrences of the pattern in the string
            str = Regex.Replace(str, pattern, replacement);

            // Add newline after first character and before last character, if the string length is more than 1
            if (str.Length > 1)
            {
                str = str.Substring(0, 1) + "\n" + str.Substring(1, str.Length - 2) + "\n" + str.Substring(str.Length - 1);
            }

            return str;
        }

        public static float RoundToNearestMultiple(float num, float input)
        {
            float rounded = MathF.Round(num / input) * input;
            return MathF.Round(rounded, 2);
        }
    }

    // Adapted from EvilFactory's EasySettings Lua code.
    public static class EasySettings
    {
        private static bool ShouldUpdateServerConfig = false;

        private static Config oldLocalConfig = new Config();
        private static Config OldLocalConfig
        {
            get { return oldLocalConfig; }
            set
            {
                // make a copy of the given config.
                string json = JsonSerializer.Serialize(value);
                oldLocalConfig = JsonSerializer.Deserialize<Config>(json);
            }
        }

        private static Dictionary<string, Setting> settings = new Dictionary<string, Setting>();
        public static void AddMenu(string name, Action<GUIFrame> onOpen)
        {
            settings.Add(name, new Setting { Name = name, OnOpen = onOpen });
        }
        public static void SPW_TogglePauseMenu()
        {
            if (GUI.PauseMenuOpen)
            {
                GUIFrame frame = GUI.PauseMenu;
                GUIComponent list = GetChildren(GetChildren(frame)[1])[0];

                foreach (var kvp in settings)
                {
                    Setting value = kvp.Value;
                    GUIButton button = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), list.RectTransform), value.Name, Alignment.Center, "GUIButtonSmall");

                    button.OnClicked = (sender, args) =>
                    {
                        value.OnOpen(frame);
                        return true;
                    };
                }
            }
            else // On menu close
            {
                if (ShouldUpdateServerConfig)
                {
                    SoundproofWalls.UpdateServerConfig();
                    ShouldUpdateServerConfig = false;

                    // Dump muffle info if advanced settings are changed in singleplayer/nosync
                    if (!GameMain.IsMultiplayer || SoundproofWalls.ServerConfig == null)
                    {
                        SoundproofWalls.SoundChannelMuffleInfo.Clear();
                    }

                    // Reload round sounds for singleplayer/nosync
                    if (SoundproofWalls.ShouldReloadRoundSounds(OldLocalConfig) && (!GameMain.IsMultiplayer || SoundproofWalls.ServerConfig == null))
                    {
                        SoundproofWalls.ReloadRoundSounds();
                    }
                }


                OldLocalConfig = SoundproofWalls.LocalConfig;
            }
        }
        public static GUIListBox BasicList(GUIFrame parent, Vector2? size = null)
        {
            GUIFrame menuContent = new GUIFrame(new RectTransform(size ?? new Vector2(0.25f, 0.65f), parent.RectTransform, Anchor.Center));
            GUIListBox menuList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.91f), menuContent.RectTransform, Anchor.Center));
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), menuContent.RectTransform), TextManager.Get("spw_settings").Value, textAlignment: Alignment.Center);
            CloseButton(menuContent);
            ResetAllButton(menuContent);
            return menuList;
        }

        public static GUITickBox TickBox(GUIFrame parent, string text, bool? state, Action<bool> onSelected)
        {
            GUITickBox tickBox = new GUITickBox(new RectTransform(new Vector2(1f, 0.2f), parent.RectTransform), text);
            tickBox.Selected = state ?? true;
            tickBox.OnSelected = (sender) =>
            {
                ShouldUpdateServerConfig = true;
                onSelected(tickBox.State == GUIComponent.ComponentState.Selected);
                return true;
            };
            tickBox.RectTransform.RelativeOffset = new Vector2(0.05f, 0);
            return tickBox;
        }

        public static GUIScrollBar Slider(GUIFrame parent, float min, float max, float? value, Action<float> onSelected, float multiple = 0.01f, bool reloadRoundSounds = false)
        {
            GUIScrollBar scrollBar = new GUIScrollBar(new RectTransform(new Vector2(1f, 0.1f), parent.RectTransform), 0.1f, style: "GUISlider");
            scrollBar.Range = new Vector2(min, max);
            scrollBar.BarScrollValue = value ?? max / 2;
            float startValue = Menu.RoundToNearestMultiple(scrollBar.BarScrollValue, multiple);
            scrollBar.OnMoved = (sender, args) =>
            {
                onSelected(scrollBar.BarScrollValue);
                ShouldUpdateServerConfig = startValue != Menu.RoundToNearestMultiple(scrollBar.BarScrollValue, multiple);
                return true;
            };
            scrollBar.RectTransform.RelativeOffset = new Vector2(0.01f, 0);
            return scrollBar;
        }

        public static GUITextBlock TextBlock(GUIListBox list, string text, float x = 1f, float y = 0.05f, float size = 1, Color? color = null)
        {
            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(x, y), list.Content.RectTransform), text, textAlignment: Alignment.Center, wrap: true);
            textBlock.Enabled = false;
            textBlock.OverrideTextColor(textBlock.TextColor);
            textBlock.TextScale = size;

            if (color.HasValue)
            {
                textBlock.OverrideTextColor((Color)color);
            }
            return textBlock;
        }

        public static GUITextBox MultiLineTextBox(RectTransform rectTransform, string text, float? height)
        {
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(1, height ?? 0.2f), rectTransform));
            GUITextBox textBox = new GUITextBox(new RectTransform(new Vector2(1, 1), listBox.Content.RectTransform), text, wrap: true, style: "GUITextBoxNoBorder");

            textBox.OnSelected += (sender, key) => { UpdateMessageScrollFromCaret(textBox, listBox); };

            string startValue = text;
            textBox.OnTextChangedDelegate = (sender, e) =>
            {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(listBox.Content.Rect.Height, (int)textSize.Y + 10));
                listBox.UpdateScrollBarSize();
                ShouldUpdateServerConfig = startValue != textBox.Text;
                return true;
            };

            textBox.OnEnterPressed = (sender, e) =>
            {
                string str = textBox.Text;
                int caretIndex = textBox.CaretIndex;

                textBox.Text = str.Substring(0, caretIndex) + "\n" + str.Substring(caretIndex);
                textBox.CaretIndex = caretIndex + 1; // Move the caret right after the inserted newline
                return true;
            };

            return textBox;
        }

        public static void UpdateMessageScrollFromCaret(GUITextBox textBox, GUIListBox listBox)
        {
            float caretY = textBox.CaretScreenPos.Y;
            float bottomCaretExtent = textBox.Font.LineHeight * 1.5f;
            float topCaretExtent = -textBox.Font.LineHeight * 0.5f;

            if (caretY + bottomCaretExtent > listBox.Rect.Bottom)
            {
                listBox.ScrollBar.BarScroll = (caretY - textBox.Rect.Top - listBox.Rect.Height + bottomCaretExtent) / (textBox.Rect.Height - listBox.Rect.Height);
            }
            else if (caretY + topCaretExtent < listBox.Rect.Top)
            {
                listBox.ScrollBar.BarScroll = (caretY - textBox.Rect.Top + topCaretExtent) / (textBox.Rect.Height - listBox.Rect.Height);
            }
        }

        public static GUIButton CloseButton(GUIFrame parent)
        {
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), parent.RectTransform, Anchor.BottomRight), TextManager.Get("close").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                GUI.TogglePauseMenu();
                return true;
            };

            return button;
        }

        public static GUIButton ResetAllButton(GUIFrame parent)
        {
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), parent.RectTransform, Anchor.BottomLeft), TextManager.Get("spw_resetall").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                Config newConfig = new Config();
                SoundproofWalls.LocalConfig = newConfig;
                ConfigManager.SaveConfig(newConfig);
                ShouldUpdateServerConfig = true;
                GUI.TogglePauseMenu();
                return true;
            };

            return button;
        }

        public static List<GUIComponent> GetChildren(GUIComponent comp)
        {
            List<GUIComponent> children = new List<GUIComponent>();
            foreach (var child in comp.GetAllChildren())
            {
                children.Add(child);
            }
            return children;
        }
    }
    public class Setting
    {
        public string Name { get; set; }
        public Action<GUIFrame> OnOpen { get; set; }
    }
}
