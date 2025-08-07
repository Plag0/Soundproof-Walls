using Barotrauma;
using HarmonyLib;
using System.Runtime.CompilerServices;

[assembly: IgnoresAccessChecksTo("Barotrauma")]
[assembly: IgnoresAccessChecksTo("DedicatedServer")]
[assembly: IgnoresAccessChecksTo("BarotraumaCore")]

namespace SoundproofWalls
{
    public partial class Plugin : IAssemblyPlugin
    {
        readonly Harmony harmony = new Harmony("plag.barotrauma.soundproofwalls");

        public System.Reflection.MethodInfo? PatchToKeep;

        // Net message names.
        public const string SERVER_RECEIVE_CONFIG = "spw_netmessage_server_receiveconfig";
        public const string SERVER_SEND_CONFIG = "spw_netmessage_server_sendconfig";
        public const string CLIENT_RECEIVE_CONFIG = "spw_netmessage_client_receiveconfig";
        public const string CLIENT_SEND_CONFIG = "spw_netmessage_client_sendconfig";
        // Represents a config with syncing disabled. Sent in place of a serialized Config.
        public const string DISABLED_CONFIG_VALUE = "spw_disabledconfigvalue";

        public void Initialize()
        {
#if SERVER
            InitServer();
#elif CLIENT
            InitClient();
#endif

            // Shared patches:

            // Character SpeechImpediment property prefix and REPLACEMENT.
            // Used to allow ragdolled players to be heard.
            harmony.Patch(
                typeof(Character).GetProperty(nameof(Character.SpeechImpediment)).GetGetMethod(),
                new HarmonyMethod(typeof(Plugin).GetMethod(nameof(SPW_Character_SpeechImpediment))));
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
            // TODO when this works, load extended/reduced sounds early.
        }

        public void Dispose()
        {
            LuaCsLogger.Log("[SoundproofWalls] Shutting down...");

#if CLIENT
            DisposeClient();
#endif
            harmony.UnpatchSelf();

            LuaCsLogger.Log("[SoundproofWalls] Shut down successfully.");
        }

        // Called when disabling the mod from the Soundproof Walls setting menu.
        // Unpatches and disposes everything EXCEPT the pause menu button patch.
        public void PartialDispose()
        {
            LuaCsLogger.Log("[SoundproofWalls] Shutting down...");

#if CLIENT
            DisposeClient();
#endif
            HarmonyUnpatchSelfExceptTogglePauseMenu();

            LuaCsLogger.Log("[SoundproofWalls] Shut down successfully.");
        }

        public void HarmonyUnpatchSelfExceptTogglePauseMenu()
        {
            if (PatchToKeep == null)
            {
                harmony.UnpatchSelf();
                return;
            }

            foreach (var method in Harmony.GetAllPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;

                foreach (var prefix in info.Prefixes)
                {
                    if (prefix.owner == harmony.Id)
                        harmony.Unpatch(method, HarmonyPatchType.Prefix, harmony.Id);
                }

                foreach (var postfix in info.Postfixes)
                {
                    string patchName = postfix.PatchMethod.ToString() ?? "";
                    // Don't remove the TogglePauseMenu patch so the mod can still be enabled/disabled via menu.
                    if (postfix.owner == harmony.Id && !patchName.Contains("TogglePauseMenu"))
                        harmony.Unpatch(method, HarmonyPatchType.Postfix, harmony.Id);
                }

                foreach (var transpiler in info.Transpilers)
                {
                    if (transpiler.owner == harmony.Id)
                        harmony.Unpatch(method, HarmonyPatchType.Postfix, harmony.Id);
                }

                foreach (var finalizer in info.Finalizers)
                {
                    if (finalizer.owner == harmony.Id)
                        harmony.Unpatch(method, HarmonyPatchType.Postfix, harmony.Id);
                }
            }
        }
    }

    public class DataAppender
    {
        private const string Delimiter = "|spw_delimiter|";

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
}
