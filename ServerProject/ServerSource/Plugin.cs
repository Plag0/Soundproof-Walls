using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma;
using Barotrauma.Networking;

namespace SoundproofWalls
{
    public partial class SoundproofWalls : IAssemblyPlugin
    {
        public static void InitServer()
        {
            GameMain.LuaCs.Networking.Receive("SPW_UpdateConfigServer", (object[] args) =>
            {
                IReadMessage msg = (IReadMessage)args[0];
                IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_UpdateConfigClient");
                message.WriteString(msg.ReadString());
                GameMain.LuaCs.Networking.Send(message);
            });

            GameMain.LuaCs.Networking.Receive("SPW_DisableConfigServer", (object[] args) =>
            {
                IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_DisableConfigClient");
                GameMain.LuaCs.Networking.Send(message);
            });
        }
    }
}

