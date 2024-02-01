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

namespace SoundproofWalls
{
    public partial class SoundproofWalls : IAssemblyPlugin
    {
        static bool RoundStarted { get { return GameMain.gameSession?.IsRunning ?? false; } }

        private static Config localConfig = ConfigManager.LoadConfig();
        private static Config? serverConfig = null;
        public static Config Config { get { return serverConfig ?? localConfig; } }
        public static Config LocalConfig { get { return localConfig; } }
        public static Config? ServerConfig { get { return serverConfig; } }

        static Hull? EavesdroppedHull = null;

        static Hull? ViewTargetHull = null;
        static float LastViewTargetHullUpdateTime = 0f;

        static bool IsUsingHydrophones;
        static bool IsViewTargetPlayer;
        public static bool EarsInWater;
        public static bool IsWearingDivingSuit;

        static Sound HydrophoneMovementSound = GameMain.SoundManager.LoadSound("Content/Sounds/Water/SplashLoop.ogg");
        static float LastHydrophonePlayTime = 0f;

        static Hull? CameraHull = null;

        public static bool SoundsLoaded = false;
        public static float SoundsLoadedDelayTime = 3 * 60;

        static bool prevWearingSuit = false;
        static bool prevEarsInWater = false;

        static float LastDrawEavesdroppingTextTime = 0f;
        static float textFade = 0;

        static Dictionary<SoundChannel, MuffleInfo> SoundChannelMuffleInfo = new Dictionary<SoundChannel, MuffleInfo>();
        static Dictionary<SoundChannel, Character> HydrophoneSoundChannels = new Dictionary<SoundChannel, Character>();
        static HashSet<SoundChannel> PitchedSounds = new HashSet<SoundChannel>();

        public void InitClient()
        {
            // Lua reload patch
            GameMain.LuaCs.Hook.Add("loaded", "spw_loaded", (object[] args) =>
            {
                UpdateServerConfig();
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
                if (!Config.Enabled) { return null; }
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
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(UpdateWaterFlowMuffling))));

            //FireSounds postfix patch
            harmony.Patch(
                typeof(SoundPlayer).GetMethod(nameof(SoundPlayer.UpdateFireSounds), BindingFlags.Static | BindingFlags.NonPublic),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(UpdateFireMuffling))));

            // SoundChannel postfix patch
            harmony.Patch(
                typeof(SoundChannel).GetConstructor(new Type[] { typeof(Sound), typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(string), typeof(bool) }),
                null,
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_SoundChannel))));

            // ItemComponent UpdateSounds prefix and replacement patch
            harmony.Patch(
                typeof(ItemComponent).GetMethod(nameof(ItemComponent.UpdateSounds), BindingFlags.Instance | BindingFlags.Public),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(ItemComponent_UpdateSounds))));

            // StatusEffect UpdateAllProjSpecific prefix and replacement patch
            harmony.Patch(
                typeof(StatusEffect).GetMethod(nameof(StatusEffect.UpdateAllProjSpecific), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(StatusEffect_UpdateAllProjSpecific))));

            // VoipClient Read prefix and replacement patch
            harmony.Patch(
            typeof(VoipClient).GetMethod(nameof(VoipClient.Read), BindingFlags.Instance | BindingFlags.Public),
            new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Read))));

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
                new HarmonyMethod(typeof(EasySettings).GetMethod(nameof(EasySettings.TogglePauseMenu))));
            
            // Clients receiving the host's config.
            GameMain.LuaCs.Networking.Receive("SPW_UpdateConfigClient", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];
                Config? newServerConfig = JsonSerializer.Deserialize<Config>(msg.ReadString());

                bool shouldReloadRoundSounds = ShouldReloadRoundSounds(newServerConfig);

                serverConfig = newServerConfig;

                if (shouldReloadRoundSounds) { ReloadRoundSounds(); }
                LuaCsLogger.Log(TextManager.Get("spw_updateserverconfig").Value, Color.LimeGreen);
            });

            GameMain.LuaCs.Networking.Receive("SPW_DisableConfigClient", (object[] args) =>
            {
                bool shouldReloadRoundSounds = ShouldReloadRoundSounds(LocalConfig);

                serverConfig = null;

                if (shouldReloadRoundSounds) { ReloadRoundSounds(); }
                LuaCsLogger.Log(TextManager.Get("spw_disableserverconfig").Value, Color.LimeGreen);
            });
            Menu.LoadMenu();
        }

        // Called when the host updates a setting in their menu.
        public static void UpdateServerConfig()
        {
            if (GameMain.IsMultiplayer && GameMain.Client.IsServerOwner)
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
            LuaCsLogger.Log("Reloading sounds!");
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

        public static void KillSPW()
        {
            SoundsLoaded = false;
            SoundsLoadedDelayTime = 3 * 60;
            PitchedSounds.Clear();
            SoundChannelMuffleInfo.Clear();

            // Looping channels that have a frequency multiplier greater than 1 are not disposed automatically.
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
            __result = false;
            return false;
        }

        public static bool SPW_Read(VoipClient __instance, ref IReadMessage msg)
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
                if (client.Muted || client.MutedLocally) { return false; }
                if (client.VoipSound == null)
                {
                    DebugConsole.Log("Recreating voipsound " + queueId);
                    client.VoipSound = new VoipSound(client.Name, GameMain.SoundManager, client.VoipQueue);
                }
                GameMain.SoundManager.ForceStreamUpdate();
                client.RadioNoise = 0.0f;
                if (client.Character != null && !client.Character.IsDead && !client.Character.Removed && client.Character.SpeechImpediment <= 100.0f)
                {
                    float speechImpedimentMultiplier = 1.0f - client.Character.SpeechImpediment / 100.0f;
                    bool spectating = Character.Controlled == null;
                    float rangeMultiplier = spectating ? 2.0f : 1.0f;
                    WifiComponent senderRadio = null;
                    var messageType =
                        !client.VoipQueue.ForceLocal &&
                        ChatMessage.CanUseRadio(client.Character, out senderRadio) &&
                        ChatMessage.CanUseRadio(Character.Controlled, out var recipientRadio) &&
                        senderRadio.CanReceive(recipientRadio) ?
                            ChatMessageType.Radio : ChatMessageType.Default;

                    client.Character.ShowSpeechBubble(1.25f, ChatMessage.MessageColor[(int)messageType]);

                    client.VoipSound.UseRadioFilter = messageType == ChatMessageType.Radio && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters;
                    client.RadioNoise = 0.0f;
                    if (messageType == ChatMessageType.Radio)
                    {
                        client.VoipSound.SetRange(senderRadio.Range * VoipClient.RangeNear * speechImpedimentMultiplier * rangeMultiplier, senderRadio.Range * speechImpedimentMultiplier * rangeMultiplier);
                        if (distanceFactor > VoipClient.RangeNear && !spectating)
                        {
                            //noise starts increasing exponentially after 40% range
                            client.RadioNoise = MathF.Pow(MathUtils.InverseLerp(VoipClient.RangeNear, 1.0f, distanceFactor), 2);
                        }
                    }
                    else
                    {
                        client.VoipSound.SetRange(ChatMessage.SpeakRange * VoipClient.RangeNear * speechImpedimentMultiplier * rangeMultiplier, ChatMessage.SpeakRange * speechImpedimentMultiplier * rangeMultiplier);
                    }

                    // Changes:
                    if (messageType != ChatMessageType.Radio)
                    {
                        SoundChannel channel = client.VoipSound.soundChannel;
                        Limb limb = client.Character.AnimController.GetLimb(LimbType.Head) ?? client.Character.AnimController.MainLimb;

                        if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
                        {
                            muffleInfo = new MuffleInfo(channel, client.Character.CurrentHull);
                            SoundChannelMuffleInfo[channel] = muffleInfo;
                        }
                        muffleInfo.Update(client.Character.CurrentHull);

                        client.VoipSound.UseMuffleFilter = muffleInfo.Muffled;
                    }
                    else
                    {
                        client.VoipSound.UseMuffleFilter = false;
                    }
                    // End of changes.
                }

                GameMain.NetLobbyScreen?.SetPlayerSpeaking(client);
                GameMain.GameSession?.CrewManager?.SetClientSpeaking(client);

                if ((client.VoipSound.CurrentAmplitude * client.VoipSound.Gain * GameMain.SoundManager.GetCategoryGainMultiplier("voip")) > 0.1f) //TODO: might need to tweak
                {
                    if (client.Character != null && !client.Character.Removed && !client.Character.IsDead)
                    {
                        Vector3 clientPos = new Vector3(client.Character.WorldPosition.X, client.Character.WorldPosition.Y, 0.0f);
                        Vector3 listenerPos = GameMain.SoundManager.ListenerPosition;
                        float attenuationDist = client.VoipSound.Near; // Vanilla was Near * 1.125 (weird magic number?)
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
            }
            return false;
        }

        public static bool SPW_UpdateVoipSound(Client __instance)
        {
            SoundChannel channel = __instance.VoipSound?.soundChannel;
            Character character = __instance.character;

            if (channel == null) { return true; }
            if (!SoundChannelMuffleInfo.TryGetValue(channel, out MuffleInfo muffleInfo))
            {
                muffleInfo = new MuffleInfo(channel, character.CurrentHull);
                SoundChannelMuffleInfo[channel] = muffleInfo;
            }
            ProcessVoipSound(channel, muffleInfo);
            return true; // Disabling this override for now.

            if (!Config.Enabled || !RoundStarted) { return true; }
            Client instance = __instance;
            VoipSound VoipSound = instance.VoipSound;
            SoundChannel radioNoiseChannel = instance.radioNoiseChannel;
            float RadioNoise = instance.RadioNoise;

            if (VoipSound == null || !VoipSound.IsPlaying)
            {
                radioNoiseChannel?.Dispose();
                radioNoiseChannel = null;
                if (VoipSound != null)
                {
                    DebugConsole.Log("Destroying voipsound");
                    VoipSound.Dispose();
                }
                VoipSound = null;
                return false;
            }

            if (Screen.Selected is ModDownloadScreen)
            {
                VoipSound.Gain = 0.0f;
            }

            float gain = 1.0f;
            float noiseGain = 0.0f;
            Vector3? position = null;
            if (character != null && !character.IsDead)
            {
                if (GameSettings.CurrentConfig.Audio.UseDirectionalVoiceChat)
                {
                    position = new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0.0f);
                }
                else
                {
                    //if (!SoundChannelLastDist.TryGetValue(VoipSound.soundChannel, out float dist))
                    //{
                    //dist = Vector3.Distance(new Vector3(character.WorldPosition, 0.0f), GameMain.SoundManager.ListenerPosition);
                    //}
                    //gain = 1.0f - MathUtils.InverseLerp(VoipSound.Near, VoipSound.Far, dist);
                }
                if (RadioNoise > 0.0f)
                {
                    noiseGain = gain * RadioNoise;
                    gain *= 1.0f - RadioNoise;
                }
            }
            VoipSound.SetPosition(position);
            VoipSound.Gain = gain;
            if (noiseGain > 0.0f)
            {
                if (radioNoiseChannel == null || !radioNoiseChannel.IsPlaying)
                {
                    radioNoiseChannel = SoundPlayer.PlaySound("radiostatic");
                    radioNoiseChannel.Category = "voip";
                    radioNoiseChannel.Looping = true;
                }
                radioNoiseChannel.Near = VoipSound.Near;
                radioNoiseChannel.Far = VoipSound.Far;
                radioNoiseChannel.Position = position;
                radioNoiseChannel.Gain = noiseGain;
            }
            else if (radioNoiseChannel != null)
            {
                radioNoiseChannel.Gain = 0.0f;
            }
            return false;
        }

        public static void SPW_Dispose(SoundChannel __instance)
        {
            if (!Config.Enabled) { return; };
            PitchedSounds.Remove(__instance);
            SoundChannelMuffleInfo.Remove(__instance);
            __instance.Looping = false;
            HydrophoneSoundChannels.Remove(__instance);
        }

        public static void SPW_EndRound()
        {
            KillSPW();
        }

        public static void SPW_StartRound()
        {
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

            if (Timing.TotalTime > LastViewTargetHullUpdateTime + 0.05)
            {
                ViewTargetHull = Config.FocusTargetAudio ? Hull.FindHull(LightManager.ViewTarget.WorldPosition, Character.Controlled.CurrentHull, true) : Character.Controlled.CurrentHull;
                LastViewTargetHullUpdateTime = (float)Timing.TotalTime;
            }

            EavesdroppedHull = GetEavesdroppedHull();
            IsUsingHydrophones = EavesdroppedHull == null && Character.Controlled?.SelectedItem?.GetComponentString("Sonar") != null;
            IsViewTargetPlayer = !Config.FocusTargetAudio || LightManager.ViewTarget?.WorldPosition == Character.Controlled?.WorldPosition;
            EarsInWater = IsViewTargetPlayer ? Character.Controlled.AnimController.HeadInWater : SoundInWater(LightManager.ViewTarget.Position, ViewTargetHull);
            IsWearingDivingSuit = Character.Controlled?.LowPassMultiplier < 0.5f;

            if (Timing.TotalTime > LastHydrophonePlayTime + 0.1)
            {
                PlayHydrophoneSounds();
                LastHydrophonePlayTime = (float)Timing.TotalTime;
            }
            UpdateHydrophoneSounds();
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
                if (targetHull == null || targetHull.Submarine != Character.Controlled.Submarine)
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

        public enum MuffleReason
        {
            None,
            Wall,
            Water
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
            private SoundChannel Channel;

            public MuffleInfo(SoundChannel channel, Hull? soundHull = null, bool dontProcess = false, bool dontPitch = false)
            {
                Channel = channel;
                string filename = Channel.Sound.Filename;

                if (dontProcess || Channel.Category == "ui" || SoundIgnoresAll(filename))
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

                Update(soundHull);
            }
            public void Update(Hull? soundHull = null)
            {
                Character character = Character.Controlled;
                bool spectating = character == null || LightManager.ViewTarget == null;
                Vector2 soundPos = GetSoundChannelPos(Channel);

                if (spectating || IgnoreAll)
                {
                    Distance = Vector3.Distance(GameMain.SoundManager.ListenerPosition, new Vector3(soundPos, 0.0f));
                    Muffled = IgnoreAll ? false : !IgnoreLowpass && ShouldMuffleSpectating(soundPos);
                    return;
                }

                Hull? listenHull = EavesdroppedHull ?? ViewTargetHull;
                SoundHull = soundHull ?? Hull.FindHull(soundPos, character.CurrentHull, true);
                Vector2 listenPos = IsViewTargetPlayer ? character.AnimController.GetLimb(LimbType.Head).Position : LightManager.ViewTarget.Position;

                // Localise sound position
                if (SoundHull?.Submarine != null)
                {
                    soundPos += -SoundHull.Submarine.WorldPosition + SoundHull.Submarine.HiddenSubPosition;
                }

                // Hear sounds outside the submarine while sounds inside your submarine are muffled.
                if (IsUsingHydrophones && (SoundHull == null || SoundHull.Submarine == LightManager.ViewTarget.Submarine))
                {
                    Distance = SoundHull == null ? Vector2.Distance(listenPos, soundPos) : float.MaxValue;
                    Muffled = Distance == float.MaxValue && !IgnoreLowpass;
                    Reason = Muffled ? MuffleReason.Wall : MuffleReason.None;
                    return;
                }

                Distance = GetApproximateDistance(listenPos, soundPos, listenHull, SoundHull, Channel.Far);
                if (Distance == float.MaxValue)
                {
                    Muffled = !IgnoreLowpass;
                    Reason = MuffleReason.Wall;
                    return;
                }

                bool soundInWater = SoundInWater(soundPos, SoundHull);
                if (!soundInWater && !EarsInWater)
                {
                    Muffled = false;
                    Reason = MuffleReason.None;
                    return;
                }

                Reason = MuffleReason.Wall;
                Muffled = true;

                bool canHearUnderwater = IsViewTargetPlayer ? !Config.MuffleSubmergedPlayer : !Config.MuffleSubmergedViewTarget;
                bool canHearPastWater = !Config.MuffleSoundsInWater;
                if ((soundInWater && !EarsInWater && (IgnoreWater || canHearPastWater)) ||
                    (soundInWater && EarsInWater && (IgnoreSubmersion || canHearUnderwater)))
                {
                    Reason = MuffleReason.None;
                    Muffled = false;
                }
                else if (soundInWater && EarsInWater)
                {
                    Reason = MuffleReason.Water;
                }
                Muffled = Muffled && !IgnoreLowpass;
            }
        }

        public static void SPW_SoundChannel(SoundChannel __instance)
        {
            SoundChannel channel = __instance;
            if (!Config.Enabled || !RoundStarted || channel == null) { return; }

            MuffleInfo muffleInfo = new MuffleInfo(channel);

            try
            {
                SoundChannelMuffleInfo[channel] = muffleInfo;
            }
            catch (IndexOutOfRangeException ex)
            {
                LuaCsLogger.Log($"Failed to process sound: {ex}");
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
        public static bool ItemComponent_UpdateSounds(ItemComponent __instance)
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
                muffleInfo = new MuffleInfo(channel, item.CurrentHull);
                SoundChannelMuffleInfo[channel] = muffleInfo;
            }

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

        public static bool StatusEffect_UpdateAllProjSpecific()
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
                        muffleInfo = new MuffleInfo(channel, dontProcess: statusEffect.ignoreMuffling, dontPitch: true);
                        SoundChannelMuffleInfo[channel] = muffleInfo;
                    }

                    if (doMuffleCheck && !statusEffect.ignoreMuffling)
                    {
                        muffleInfo.Update();
                        channel.Muffled = muffleInfo.Muffled;
                    }

                    statusEffect.soundChannel.Position = new Vector3(statusEffect.soundEmitter.WorldPosition, 0.0f);

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

                if (muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - GetMuffledFrequencyMultiplier(channel)); }
                else if (eavesdropped) { freqMult -= (1 - Config.EavesdroppingPitchMultiplier); }
                else if (hydrophoned) { freqMult -= (1 - Config.HydrophonePitchMultiplier); }

                if (EarsInWater && muffleInfo.Reason != MuffleReason.None) { freqMult -= (1 - Config.SubmergedPitchMultiplier); }
                if (IsWearingDivingSuit) { freqMult -= (1 - Config.DivingSuitPitchMultiplier); }

                channel.FrequencyMultiplier = Math.Clamp(channel.FrequencyMultiplier * freqMult, 0.25f, 4);
            }

            float gainMult = 1;

            gainMult -= (1 - GetCustomGainMultiplier(channel.Sound.Filename));
            if (muffleInfo.Reason == MuffleReason.Wall) { gainMult -= (1 - Config.WallMuffledSoundVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.Water) { gainMult -= (1 - Config.WaterMuffledSoundVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); }

            channel.Gain *= gainMult;
        }
        public static void ProcessLoopingSound(SoundChannel channel, MuffleInfo muffleInfo)
        {
            if (muffleInfo.IgnoreAll) { return; }
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
            if (muffleInfo.Reason == MuffleReason.Wall) { gainMult -= (1 - Config.WallMuffledComponentVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.Water) { gainMult -= (1 - Config.WaterMuffledComponentVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingSoundVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); }
            else { gainMult -= (1 - Config.UnmuffledComponentVolumeMultiplier); }

            float distFalloffMult = channel.Muffled ? 0.7f : 1 - MathUtils.InverseLerp(channel.Near, channel.Far, muffleInfo.Distance);
            float targetGain = 1 * gainMult * distFalloffMult;
            channel.Gain = targetGain;
        }

        public static void ProcessVoipSound(SoundChannel channel, MuffleInfo muffleInfo)
        {
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

            gainMult -= (1 - GetCustomGainMultiplier(channel.Sound.Filename));
            if (muffleInfo.Reason == MuffleReason.Wall) { gainMult -= (1 - Config.WallMuffledVoiceVolumeMultiplier); }
            else if (muffleInfo.Reason == MuffleReason.Water) { gainMult -= (1 - Config.WaterMuffledVoiceVolumeMultiplier); }
            else if (eavesdropped) { gainMult -= (1 - Config.EavesdroppingVoiceVolumeMultiplier); }
            else if (hydrophoned) { gainMult -= (1 - Config.HydrophoneVolumeMultiplier); }

            //float distFalloffMult = channel.Muffled ? 0.7f : 1 - MathUtils.InverseLerp(channel.Near, channel.Far, muffleInfo.Distance);
            float targetGain = 1 * gainMult; //* distFalloffMult;
            channel.Gain = targetGain;
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

        public static void UpdateWaterFlowMuffling()
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

        public static void UpdateFireMuffling()
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
                        float dist = GetApproximateHullDistance(g.Position, endPos, newStartHull, endHull, connectedHulls, distance + Vector2.Distance(startPos, g.Position) * distanceMultiplier, maxDistance);
                        if (dist < float.MaxValue)
                        {
                            return dist;
                        }
                    }
                }
            }

            return float.MaxValue;
        }

        public static Hull? GetEavesdroppedHull()
        {
            Character character = Character.Controlled;

            if (!Config.EavesdroppingKeyOrMouse.IsDown() || character.CurrentHull == null ||
                character.CurrentSpeed > 0.05 || character.IsUnconscious)
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

        public static float GetMuffledFrequencyMultiplier(SoundChannel channel) //TODO add config option for controlling this multiplier.
        {
            float distance = Vector3.Distance(GameMain.SoundManager.ListenerPosition, new Vector3(GetSoundChannelPos(channel), 0.0f));
            float distanceFromSoundRatio = Math.Clamp(1 - distance / channel.Far, 0, 1);
            return 0.5f * distanceFromSoundRatio + 0.25f;
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

        // Submerged sounds/voices will be muffled for spectators.
        public static bool ShouldMuffleSpectating(Vector2 soundPos)
        {
            Hull soundHull = Hull.FindHull(soundPos, null, true);

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
        public bool FocusTargetAudio { get; set; } = true;
        public double GeneralLowpassFrequency { get; set; } = 360f;
        public double VoiceLowpassFrequency { get; set; } = 180f;
        public float SoundRangeMultiplier { get; set; } = 1.8f;
        public float VoiceRangeMultiplier { get; set; } = 0.75f;

        // Volume
        public float WallMuffledSoundVolumeMultiplier { get; set; } = 3f;
        public float WaterMuffledSoundVolumeMultiplier { get; set; } = 3f;
        public float WallMuffledVoiceVolumeMultiplier { get; set; } = 0.75f;
        public float WaterMuffledVoiceVolumeMultiplier { get; set; } = 3f;
        public float WallMuffledComponentVolumeMultiplier { get; set; } = 0.75f;
        public float WaterMuffledComponentVolumeMultiplier { get; set; } = 3f;
        public float UnmuffledComponentVolumeMultiplier { get; set; } = 1f;

        // Eavesdropping
        public string EavesdroppingBind { get; set; } = "SecondaryMouse";
        public float EavesdroppingSoundVolumeMultiplier { get; set; } = 1.25f;
        public float EavesdroppingVoiceVolumeMultiplier { get; set; } = 0.55f;
        public float EavesdroppingPitchMultiplier { get; set; } = 0.85f;
        public int EavesdroppingMaxDistance { get; set; } = 40; // distance in cm from door

        // Hydrophone monitoring
        public float HydrophoneSoundRange { get; set; } = 7500; // range in cm
        public float HydrophoneVolumeMultiplier { get; set; } = 1.2f;
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
            "/ui/",
            "dropitem",
            "pickitem",
        };

        public HashSet<string> PitchIgnoredSounds { get; set; } = new HashSet<string>
        {
            "voip", //TODO remove this after doing VoipProcessing
            "deconstructor",
            "alarm",
            "sonar",
            "male",
            "female"
        };

        public HashSet<string> LowpassIgnoredSounds { get; set; } = new HashSet<string>
        {
            "explosion",
        };

        // Ignore sounds in water without player - Underwater sounds that are NOT occluded when propagating from water to air.
        public HashSet<string> WaterIgnoredSounds { get; set; } = new HashSet<string>
        {
            "splash",
            "/fire",
            "footstep",
            "metalimpact",
            "door",
            "pump",
            "engine",
            "oxygengenerator.ogg",
            "reactor",
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
        public bool MuffleSoundsInWater { get; set; } = true; // the equivalent of adding all sounds into WaterIgnoredSounds
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

                // Volume Settings:
                EasySettings.TextBlock(list, TextManager.Get("spw_volumesettings").Value, y: 0.1f, size: 1.3f, color: Color.LightYellow);

                // Muffled Voice Volume:
                GUITextBlock textBlockMVV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.WallMuffledVoiceVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.WallMuffledVoiceVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.WallMuffledVoiceVolumeMultiplier == defaultConfig.WallMuffledVoiceVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMVV.Text = $"{TextManager.Get("spw_muffledvoicevolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockMVV.Text = $"{TextManager.Get("spw_muffledvoicevolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.WallMuffledVoiceVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledvoicevolumetooltip");

                // Muffled Sound Volume:
                GUITextBlock textBlockMSV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 2, config.WallMuffledSoundVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.WallMuffledSoundVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.WallMuffledSoundVolumeMultiplier == defaultConfig.WallMuffledSoundVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMSV.Text = $"{TextManager.Get("spw_muffledsoundvolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockMSV.Text = $"{TextManager.Get("spw_muffledsoundvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.WallMuffledSoundVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledsoundvolumetooltip");

                // Muffled Component Volume:
                GUITextBlock textBlockMCV = EasySettings.TextBlock(list, string.Empty);
                slider = EasySettings.Slider(list.Content, 0, 1, config.WallMuffledComponentVolumeMultiplier, value =>
                {
                    float realvalue = RoundToNearestMultiple(value, 0.01f);
                    float displayValue = RoundToNearestMultiple(value * 100, 1);
                    config.WallMuffledComponentVolumeMultiplier = realvalue;
                    ConfigManager.SaveConfig(config);

                    slider_text = string.Empty;
                    if (config.WallMuffledComponentVolumeMultiplier == defaultConfig.WallMuffledComponentVolumeMultiplier)
                    {
                        slider_text = default_preset;
                    }
                    textBlockMCV.Text = $"{TextManager.Get("spw_muffledcomponentvolume").Value}: {displayValue}% {slider_text}";
                });
                textBlockMCV.Text = $"{TextManager.Get("spw_muffledcomponentvolume").Value}: {RoundToNearestMultiple(slider.BarScrollValue * 100, 1)}%{GetServerPercentString(nameof(config.WallMuffledComponentVolumeMultiplier))}";
                slider.ToolTip = TextManager.Get("spw_muffledcomponentvolumetooltip");

                // Unmuffled Component Volume:
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
                GUITextBox soundList = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatDictJsonTextBox(JsonSerializer.Serialize(config.SoundVolumeMultipliers)), 0.09f);
                soundList.OnTextChangedDelegate = (textBox, text) =>
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
                    soundList.Text = FormatDictJsonTextBox(JsonSerializer.Serialize(config.SoundVolumeMultipliers));
                    return true;
                };


                // Ignored Sounds:
                GUITextBlock textBlockIS = EasySettings.TextBlock(list, $"{TextManager.Get("spw_ignoredsounds").Value}{GetServerHashSetString(nameof(config.IgnoredSounds))}");
                soundList = EasySettings.MultiLineTextBox(list.Content.RectTransform, FormatListJsonTextBox(JsonSerializer.Serialize(config.IgnoredSounds)), 0.09f);
                soundList.OnTextChangedDelegate = (textBox, text) =>
                {
                    try
                    {
                        config.IgnoredSounds = JsonSerializer.Deserialize<HashSet<string>>(textBox.Text);
                        ConfigManager.SaveConfig(config);
                        textBlockSVM.Text = TextManager.Get("spw_ignoredsounds").Value;
                    }
                    catch (JsonException)
                    {
                        textBlockSVM.Text = $"{TextManager.Get("spw_ignoredsounds").Value} ({TextManager.Get("spw_invalidinput").Value})";
                    }
                    return true;
                };
                // Reset button:
                button = new GUIButton(new RectTransform(new Vector2(1, 0.2f), list.Content.RectTransform), TextManager.Get("spw_reset").Value, Alignment.Center, "GUIButtonSmall");
                button.OnClicked = (sender, args) =>
                {
                    config.IgnoredSounds = defaultConfig.IgnoredSounds;
                    ConfigManager.SaveConfig(config);
                    soundList.Text = FormatListJsonTextBox(JsonSerializer.Serialize(config.IgnoredSounds));
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

            var stringBuilder = new StringBuilder();

            if (value is System.Collections.IDictionary dict)
            {
                var defaultDictValue = defaultConfig.GetType().GetProperty(propertyName)?.GetValue(defaultConfig) as System.Collections.IDictionary;

                if (defaultDictValue != null && AreDictionariesEqual(dict, defaultDictValue))
                {
                    stringBuilder.Append(TextManager.Get("spw_default").Value);
                }
                else
                {
                    foreach (System.Collections.DictionaryEntry kvp in dict)
                    {
                        stringBuilder.Append($"{kvp.Key}: {kvp.Value}, ");
                    }
                }
            }

            return $"\n{stringBuilder.ToString()}";
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

            var stringBuilder = new StringBuilder();

            if (value is HashSet<string> hashSet)
            {
                var defaultHashSetValue = defaultConfig.GetType().GetProperty(propertyName)?.GetValue(defaultConfig) as HashSet<string>;

                if (defaultHashSetValue != null && hashSet.SetEquals(defaultHashSetValue))
                {
                    stringBuilder.Append(TextManager.Get("spw_default").Value);
                }
                else
                {
                    foreach (string item in hashSet)
                    {
                        stringBuilder.Append($"{item}, ");
                    }
                }
            }

            // Remove the trailing comma and space if the stringBuilder is not empty
            if (stringBuilder.Length > 2)
            {
                stringBuilder.Remove(stringBuilder.Length - 2, 2);
            }

            return $"\n{stringBuilder.ToString()}";
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

        public static string FormatListJsonTextBox(string str)
        {
            string pattern = "\",\"";
            string replacement = ", ";

            str = Regex.Replace(str, pattern, replacement);

            if (str.Length > 4)
            {
                str = str.Substring(2, str.Length - 4);
            }
            else
            {
                str = "";
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
        public static void TogglePauseMenu()
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
                }

                if (SoundproofWalls.ShouldReloadRoundSounds(OldLocalConfig))
                {
                    if (!GameMain.IsMultiplayer)
                    {
                        SoundproofWalls.ReloadRoundSounds();
                    }
                    else if (SoundproofWalls.ServerConfig == null)
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

            textBox.OnTextChangedDelegate = (sender, e) =>
            {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(listBox.Content.Rect.Height, (int)textSize.Y + 10));
                listBox.UpdateScrollBarSize();
                ShouldUpdateServerConfig = true;
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
