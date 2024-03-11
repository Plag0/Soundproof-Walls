using System.Runtime.CompilerServices;
using Barotrauma;
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

        public class DataAppender
        {
            private const string Delimiter = "|";

            public static string AppendData(string originalString, bool boolData, byte byteData)
            {
                return $"{originalString}{Delimiter}{boolData}{Delimiter}{byteData}";
            }

            public static string RemoveData(string data, out bool boolData, out byte byteData)
            {
                string[] parts = data.Split(new[] { Delimiter }, StringSplitOptions.None);

                if (parts.Length < 3)
                {
                    throw new FormatException("The appended string does not contain enough parts to extract data.");
                }

                string originalString = string.Join(Delimiter, parts.Take(parts.Length - 2));

                if (bool.TryParse(parts[parts.Length - 2], out bool tempBoolData) && byte.TryParse(parts.Last(), out byte tempByteData))
                {
                    boolData = tempBoolData;
                    byteData = tempByteData;
                }
                else
                {
                    throw new FormatException("Failed to parse the appended data.");
                }

                return originalString;
            }
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
