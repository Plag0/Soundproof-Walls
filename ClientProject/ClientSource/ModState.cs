using Barotrauma;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundproofWalls
{
    public class ModState
    {
        public string Version { get; set; } = "2.0.0";
        public bool FirstLaunch { get; set; } = true;
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
