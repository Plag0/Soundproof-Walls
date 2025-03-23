using Barotrauma;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace SoundproofWalls
{
    public static class ConfigManager
    {
        public static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SoundproofWalls_Config.json");
        private static Config _cachedConfig = LoadConfig();

        public static Config LoadConfig()
        {
            if (_cachedConfig != null)
            {
                return _cachedConfig;
            }

            // Create default config if file doesn't exist
            if (!File.Exists(ConfigPath))
            {
                _cachedConfig = new Config();
                SaveConfig(_cachedConfig);
            }
            else
            {
                try
                {
                    string jsonContent = File.ReadAllText(ConfigPath);
                    var userConfig = JsonSerializer.Deserialize<Config>(jsonContent);

                    if (userConfig == null)
                    {
                        _cachedConfig = new Config();
                    }
                    else
                    {
                        // Create a new config with default values
                        _cachedConfig = new Config();

                        // Copy existing values from user config to preserve their settings
                        CopyExistingValues(userConfig, _cachedConfig);

                        // Save the updated config which now has both user values and any new default values
                        SaveConfig(_cachedConfig);
                    }

                    // Ensure any derived properties are set correctly
                    _cachedConfig.EavesdroppingKeyOrMouse = _cachedConfig.ParseEavesdroppingBind();
                }
                catch (Exception ex)
                {
                    LuaCsLogger.LogError($"Soundproof Walls: Error loading config: {ex.Message}");
                    _cachedConfig = new Config();
                }
            }
            return _cachedConfig;
        }

        /// <summary>
        /// Copies values from user config to the new config with defaults
        /// </summary>
        private static void CopyExistingValues(Config source, Config destination)
        {
            foreach (var property in typeof(Config).GetProperties())
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

        public static void SaveConfig(Config config)
        {
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                WriteIndented = true
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, options));
        }

        public static Config CloneConfig(Config originalConfig)
        {
            string json = JsonSerializer.Serialize(originalConfig);
            return JsonSerializer.Deserialize<Config>(json);
        }
    }
}
