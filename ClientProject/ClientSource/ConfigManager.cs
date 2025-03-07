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
                    _cachedConfig = JsonSerializer.Deserialize<Config>(jsonContent) ?? new Config();
                    UpdateConfigFromJson(jsonContent, _cachedConfig);
                    SaveConfig(_cachedConfig);
                }
                catch (Exception ex)
                {
                    LuaCsLogger.LogError($"Soundproof Walls: Error loading config: {ex.Message}");
                    _cachedConfig = new Config();
                }
            }

            return _cachedConfig;
        }

        private static void UpdateConfigFromJson(string jsonContent, Config config)
        {
            var jsonConfig = JsonSerializer.Deserialize<Config>(jsonContent);

            if (jsonConfig == null) return;

            foreach (var property in typeof(Config).GetProperties())
            {
                var jsonValue = property.GetValue(jsonConfig);
                if (jsonValue != null)
                {
                    property.SetValue(config, jsonValue);
                }
            }

            config.EavesdroppingKeyOrMouse = config.ParseEavesdroppingBind();
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
