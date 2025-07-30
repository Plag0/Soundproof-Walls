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
        static bool HasNotReceivedConfigYet = true; // Has the server recieved any client config
        static float LastConfigRequestTime = 0;
        static string LastConfigString = DISABLED_CONFIG_VALUE;
        static byte LastConfigSenderID = 1;

        public void InitServer()
        {
            GameMain.LuaCs.Networking.Receive(SERVER_RECEIVE_CONFIG, (object[] args) => 
            {
                // Unpack message.
                IReadMessage msg = (IReadMessage)args[0];
                string data = msg.ReadString();
                string configString = DataAppender.RemoveData(data, out bool manualUpdate, out byte configSenderId);

                bool isFirstConfig = HasNotReceivedConfigYet;

                // Prevent automatic updates (when another admin connects to the server) from overriding an existing config unless they manually change a setting.
                if (manualUpdate || HasNotReceivedConfigYet) { LastConfigSenderID = configSenderId; }

                // Return early if automatic update from different config sender.
                if (!manualUpdate && LastConfigSenderID != configSenderId) { return; }

                // Return early if the config recieved is identical.
                if (LastConfigString == configString) { return; }

                // Update values.
                LastConfigString = configString;
                HasNotReceivedConfigYet = false;

                // Send config to all clients.
                IWriteMessage response = GameMain.LuaCs.Networking.Start(CLIENT_RECEIVE_CONFIG);
                response.WriteString(data);
                GameMain.LuaCs.Networking.Send(response);

                if (isFirstConfig && !HasNotReceivedConfigYet)
                {
                    string updaterName = Client.ClientList.FirstOrDefault(client => client.SessionId == configSenderId)?.Name ?? "unknown";
                    LuaCsLogger.Log($"[Soundproof Walls][Server] Sync server started with config from \"{updaterName}\"", Color.LimeGreen);
                }
            });

            // Clients can request the latest version of the config from the server.
            GameMain.LuaCs.Networking.Receive(SERVER_SEND_CONFIG, (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];
                byte requesterId = msg.ReadByte();
                Client? requesterClient = Client.ClientList.FirstOrDefault(client => client.SessionId == requesterId);

                if (requesterClient == null) { return; }

                // No config has been uploaded yet.
                if (HasNotReceivedConfigYet)
                {
                    LuaCsLogger.Log($"[SoundproofWalls][Server] \"{requesterClient.Name}\" requested the server config before it was uploaded", color: Color.Yellow);
                    return;
                }

                string newConfigData = DataAppender.AppendData(LastConfigString, false, LastConfigSenderID);

                // Send new config to requesting client.
                IWriteMessage response = GameMain.LuaCs.Networking.Start(CLIENT_RECEIVE_CONFIG);
                response.WriteString(newConfigData);
                GameMain.LuaCs.Networking.Send(response, requesterClient.Connection);
                LuaCsLogger.Log("Server: sent config to client");
            });

            // Requests the host, or first admin to connect, for their config until it is recieved.
            GameMain.LuaCs.Hook.Add("think", "spw_serverupdate", (object[] args) =>
            {
                if (HasNotReceivedConfigYet && Timing.TotalTime > LastConfigRequestTime + 10)
                {
                    LastConfigRequestTime = (float)Timing.TotalTime;
                    foreach (Client client in Client.ClientList)
                    {
                        if (client.Connection == GameMain.Server.OwnerConnection || client.HasPermission(ClientPermissions.Ban))
                        {
                            IWriteMessage message = GameMain.LuaCs.Networking.Start(CLIENT_SEND_CONFIG);
                            GameMain.LuaCs.Networking.Send(message);
                            //LuaCsLogger.Log("Server: requesting config...");
                            break;
                        }
                    }
                }
                return null;
            });

            try
            {
                // VoipServer CanReceive prefix REPLACEMENT.
                // Used to increase the maximum range a voice can be received so it can be heard through hydrophones.
                harmony.Patch(
                    typeof(VoipServer).GetMethod(nameof(VoipServer.CanReceive), BindingFlags.Static | BindingFlags.NonPublic),
                    new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_VoipServer_CanReceive))));
            }
            catch (Exception ex) { LuaCsLogger.Log($"[SoundproofWalls] Server failed to load CanReceive() patch, {ex}"); }

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

