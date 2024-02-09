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

            // SpeechImpediment property prefix and replacement patch
            harmony.Patch(
                typeof(Character).GetProperty(nameof(Character.SpeechImpediment)).GetGetMethod(),
                new HarmonyMethod(typeof(SoundproofWalls).GetMethod(nameof(SPW_Character_SpeechImpediment))));
        }


        // Allow players to speak while ragdolling.
        public static bool SPW_Character_SpeechImpediment(Character __instance, ref float __result)
        {
#if CLIENT
            if (!Config.Enabled || !Config.TalkingRagdolls) { return true; }
#endif
            bool isKnockedDown = __instance.CharacterHealth.StunTimer > 1f ? true : __instance.IsIncapacitated;

            if (!__instance.CanSpeak || __instance.IsUnconscious || isKnockedDown)
            {
                __result = 100.0f;
            }
            else
            {
                __result = __instance.speechImpediment;
            }

            return false;
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
