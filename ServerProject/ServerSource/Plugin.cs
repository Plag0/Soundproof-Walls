using Barotrauma;
using Barotrauma.Networking;
using System.Diagnostics;
using System.Text.Json;

namespace SoundproofWalls
{
    public partial class SoundproofWalls : IAssemblyPlugin
    {
        static float LastSyncReceiveTime = 0;
        static String LastConfig = "";
        static byte LastConfigOwnerID = GameMain.Server.ConnectedClients[0]?.SessionId ?? (byte)1;

        public static void InitServer()
        {
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
    }
}

