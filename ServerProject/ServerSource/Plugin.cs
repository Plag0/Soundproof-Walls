using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace SoundproofWalls
{
    public partial class Plugin : IAssemblyPlugin
    {
        static float LastSyncReceiveTime = 0;
        static String LastConfig = "";
        static byte LastConfigOwnerID = (byte)1;

        public void InitServer()
        {
            // VoipServer CanReceive prefix REPLACEMENT.
            // Used to increase the maximum range a voice can be received so it can be heard through hydrophones.
            harmony.Patch(
                typeof(VoipServer).GetMethod(nameof(VoipServer.CanReceive), BindingFlags.Static | BindingFlags.NonPublic),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_VoipServer_CanReceive))));

            GameMain.LuaCs.Networking.Receive("SPW_UpdateConfigServer", (object[] args) => 
            {
                LastSyncReceiveTime = 0f;

                IReadMessage msg = (IReadMessage)args[0];

                string data = msg.ReadString();
                bool manualUpdate = false;
                byte configSenderId = 1;
                string newConfig = DataAppender.RemoveData(data, out manualUpdate, out configSenderId);

                if (manualUpdate)
                {
                    LastConfigOwnerID = configSenderId;
                }
                if (LastConfigOwnerID != configSenderId) { return; }

                LastConfig = newConfig;

                IWriteMessage response = GameMain.LuaCs.Networking.Start("SPW_UpdateConfigClient");
                response.WriteString(data);
                GameMain.LuaCs.Networking.Send(response);
            });

            GameMain.LuaCs.Networking.Receive("SPW_DisableConfigServer", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];

                string data = msg.ReadString();
                bool manualUpdate = false;
                byte configSenderId = 1;
                string _ = DataAppender.RemoveData(data, out manualUpdate, out configSenderId);

                if (manualUpdate)
                {
                    LastConfigOwnerID = configSenderId;
                }

                if (LastConfigOwnerID != configSenderId) { return; }

                LastConfig = string.Empty;

                IWriteMessage response = GameMain.LuaCs.Networking.Start("SPW_DisableConfigClient");
                response.WriteString(data);
                GameMain.LuaCs.Networking.Send(response);
            });

            GameMain.LuaCs.Hook.Add("think", "spw_serverupdate", (object[] args) =>
            {
                if (Timing.TotalTime > LastSyncReceiveTime + 10 && LastConfig != string.Empty)
                {
                    // Keeps the server synced in a dedicated server if no admins are online.
                    string data = DataAppender.AppendData(LastConfig, false, LastConfigOwnerID);
                    IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_UpdateConfigClient");
                    message.WriteString(data);
                    GameMain.LuaCs.Networking.Send(message);

                    LastSyncReceiveTime = (float)Timing.TotalTime;
                }
                return null;
            });
        }

        // Allows the sending of distance voices to clients so they can be picked up with hydrophones.
        // TODO This could be exploited using client-side cheats. But the only benefit of this cheat would be hearing people that are far away and idk why anyone would want that.
        public static bool SPW_VoipServer_CanReceive(Client sender, Client recipient, out float distanceFactor, ref bool __result)
        {
            if (Screen.Selected != GameMain.GameScreen)
            {
                distanceFactor = 0.0f;
                __result = true;
                return false;
            }

            distanceFactor = 0.0f;

            //no-one can hear muted players
            if (sender.Muted) { __result = false; return false; }

            bool recipientSpectating = recipient.Character == null || recipient.Character.IsDead;
            bool senderSpectating = sender.Character == null || sender.Character.IsDead;

            //non-spectators cannot hear spectators, and spectators can always hear spectators
            if (senderSpectating)
            {
                __result = recipientSpectating;
                return false;
            }

            //sender can't speak
            if (sender.Character != null && sender.Character.SpeechImpediment >= 100.0f) { __result = false; return false; }

            //check if the message can be sent via radio
            WifiComponent recipientRadio = null;
            if (!sender.VoipQueue.ForceLocal &&
                ChatMessage.CanUseRadio(sender.Character, out WifiComponent senderRadio) &&
                (recipientSpectating || ChatMessage.CanUseRadio(recipient.Character, out recipientRadio)))
            {
                if (recipientSpectating)
                {
                    if (recipient.SpectatePos == null) { return true; }
                    distanceFactor = MathHelper.Clamp(Vector2.Distance(sender.Character.WorldPosition, recipient.SpectatePos.Value) / senderRadio.Range, 0.0f, 1.0f);
                    __result = distanceFactor < 1.0f;
                    return false;
                }
                else if (recipientRadio != null && recipientRadio.CanReceive(senderRadio))
                {
                    distanceFactor = MathHelper.Clamp(Vector2.Distance(sender.Character.WorldPosition, recipient.Character.WorldPosition) / senderRadio.Range, 0.0f, 1.0f);
                    __result = true;
                    return false;
                }
            }

            float maxHydrophoneRange = 20000;
            float maxAudibleVoiceRange = ChatMessage.SpeakRangeVOIP + maxHydrophoneRange;

            if (recipientSpectating)
            {
                if (recipient.SpectatePos == null) { return true; }
                distanceFactor = MathHelper.Clamp(Vector2.Distance(sender.Character.WorldPosition, recipient.SpectatePos.Value) / maxAudibleVoiceRange, 0.0f, 1.0f);
                __result = distanceFactor < 1.0f;
            }
            else
            {
                //otherwise do a distance check
                float garbleAmount = ChatMessage.GetGarbleAmount(recipient.Character, sender.Character, maxAudibleVoiceRange, obstructionMultiplier: 0);
                distanceFactor = garbleAmount;
                __result = garbleAmount < 1.0f;
            }
            return false;
        }
    }
}

