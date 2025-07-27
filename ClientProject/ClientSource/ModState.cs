using Barotrauma;
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundproofWalls
{
    public class ModState
    {
        public string Version { get; set; } = "2.0";
        public bool FirstLaunch { get; set; } = true;

        // Stat tracking for fun
        public long TimesInitialized { get; set; } = 0;
        public long LifetimeSoundsPlayed { get; set; } = 0;
        public double TimeSpentPlaying { get; set; } = 0;
        public double TimeSpentEavesdropping { get; set; } = 0;
        public double TimeSpentHydrophones { get; set; } = 0;
    }
    public static class ModStateManager
    {
        public static readonly string ModStatePath = Path.Combine(SaveUtil.DefaultSaveFolder, "ModConfigs/SoundproofWalls_State.json").Replace('\\', '/');
        public static ModState State = LoadState();

        public static ModState LoadState()
        {
            ModState newState = new ModState();

            if (File.Exists(ModStatePath))
            {
                try
                {
                    // Copy existing values to preserve their settings
                    string jsonContent = File.ReadAllText(ModStatePath);
                    ModState? existingState = JsonSerializer.Deserialize<ModState>(jsonContent);
                    if (existingState != null) { CopyExistingValues(existingState, newState); }
                }
                catch (Exception ex)
                {
                    LuaCsLogger.LogError($"[SoundproofWalls] Error loading existing mod state, switching to default...\n{ex.Message}");
                }
            }

            SaveState(newState);
            return newState;
        }

        public static void SaveState(ModState state)
        {
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                WriteIndented = true
            };
            File.WriteAllText(ModStatePath, JsonSerializer.Serialize(state, options));
        }

        public static void PrintStats()
        {
            LuaCsLogger.Log(TextManager.Get("spw_statsheader").Value, color: Menu.ConsolePrimaryColor);
            TimeSpan t = TimeSpan.FromSeconds(State.TimeSpentPlaying);
            string timeSpentPlaying = string.Format("{0:%d}d {0:hh}h {0:mm}m {0:ss}s", t);
            double percentageEavesdropping = State.TimeSpentPlaying > 0 ? Math.Round(State.TimeSpentEavesdropping / State.TimeSpentPlaying * 100, 2) : 0;
            double percentageHydrophones = State.TimeSpentPlaying > 0 ? Math.Round(State.TimeSpentHydrophones / State.TimeSpentPlaying * 100, 2) : 0;
            string soundsPlayed = State.LifetimeSoundsPlayed.ToString("N0", CultureInfo.CurrentUICulture);
            LuaCsLogger.Log(
                TextManager.GetWithVariables("spw_stats", 
                ("[initcount]", State.TimesInitialized.ToString()), 
                ("[playtime]", timeSpentPlaying), 
                ("[percenteavesdrop]", percentageEavesdropping.ToString()), 
                ("[percenthydrophone]", percentageHydrophones.ToString()), 
                ("[soundcount]", soundsPlayed)
            ).Value, 
            color: Menu.ConsoleSecondaryColor);
        }

        private static void CopyExistingValues(ModState source, ModState destination)
        {
            foreach (var property in typeof(ModState).GetProperties())
            {
                if (property.CanWrite) // Skip read-only properties
                {
                    var value = property.GetValue(source);
                    if (value != null) // Only copy non-null values
                    {
                        property.SetValue(destination, value);
                    }
                }
            }
        }
    }
}
