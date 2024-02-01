using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;

[assembly: IgnoresAccessChecksTo(assemblyName: "Barotrauma")]
[assembly: IgnoresAccessChecksTo(assemblyName: "DedicatedServer")]

namespace SoundproofWalls
{
    public partial class SoundproofWalls : IAssemblyPlugin
    {
        readonly Harmony harmony = new Harmony("plag.barotrauma.soundproofwalls");
        public void Initialize()
        {
#if SERVER
            InitServer();
#elif CLIENT
            InitClient();
#endif
        }

        public void OnLoadCompleted()
        {

        }

        public void PreInitPatching() 
        { 

        }
        
        public void Dispose()
        {
            harmony.UnpatchAll();
        }
    }
}
