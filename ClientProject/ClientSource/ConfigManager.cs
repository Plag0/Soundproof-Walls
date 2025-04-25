using Barotrauma;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace SoundproofWalls
{
    public static class ConfigManager
    {
        public static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SoundproofWalls_Config.json");
        private static Config _cachedConfig = LoadConfig();

        public static Config LocalConfig = LoadConfig();
        public static Config? ServerConfig = null;
        public static Config Config { get { return ServerConfig ?? LocalConfig; } }

        static double LastConfigUploadTime = 5f;

        public static void Update()
        {
            if (Timing.TotalTime > LastConfigUploadTime + 5)
            {
                LastConfigUploadTime = (float)Timing.TotalTime;
                UploadServerConfig(manualUpdate: false);
            }
        }

        public static void UpdateConfig(Config newConfig, Config oldConfig, bool isServerConfigEnabled = false, bool manualUpdate = false, byte configSenderId = 0)
        {
            bool shouldStop = oldConfig.Enabled && !newConfig.Enabled;
            bool shouldStart = !oldConfig.Enabled && newConfig.Enabled;
            bool shouldReloadSounds = Util.ShouldReloadSounds(newConfig: newConfig, oldConfig: oldConfig);
            bool shouldClearMuffleInfo = Util.ShouldClearMuffleInfo(newConfig, oldConfig: oldConfig);
            bool shouldStartAlEffects = !shouldStart && !oldConfig.DynamicFx && newConfig.DynamicFx;
            bool shouldStopAlEffects = !shouldStop && oldConfig.DynamicFx && !newConfig.DynamicFx;

            ServerConfig = isServerConfigEnabled ? newConfig : null;

            if (shouldStartAlEffects) { Plugin.InitDynamicFx(); }
            else if (shouldStopAlEffects) { Plugin.DisposeDynamicFx(); }

            if (shouldStop) { Plugin.Instance?.Dispose(); }
            else if (shouldStart) { Plugin.Instance?.Initialize(); }
            else if (shouldReloadSounds) { Util.ReloadSounds(); }
            else if (shouldClearMuffleInfo) { SoundInfoManager.ClearSoundInfo(); }

            if (manualUpdate && configSenderId != 0)
            {
                string updaterName = GameMain.Client.ConnectedClients.FirstOrDefault(client => client.SessionId == configSenderId)?.Name ?? "unknown";
                if (isServerConfigEnabled)
                {
                    LuaCsLogger.Log($"Soundproof Walls: \"{updaterName}\" {TextManager.Get("spw_updateserverconfig").Value}", Color.LimeGreen);
                }
                else
                {
                    LuaCsLogger.Log($"Soundproof Walls: \"{updaterName}\" {TextManager.Get("spw_disableserverconfig").Value}", Color.MonoGameOrange);
                }
            }
        }

        // Called every 5 seconds or when the client changes a setting.
        public static void UploadServerConfig(bool manualUpdate = false)
        {
            if (!GameMain.IsMultiplayer) { return; }

            foreach (Client client in GameMain.Client.ConnectedClients)
            {
                if (client.IsOwner || client.HasPermission(ClientPermissions.Ban))
                {
                    // TODO I could merge both of these signals into one. I could search the string server-side for the state of the SyncSettings to discern what to do.
                    if (LocalConfig.SyncSettings)
                    {
                        string data = DataAppender.AppendData(JsonSerializer.Serialize(LocalConfig), manualUpdate, GameMain.Client.SessionId);
                        IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_UpdateConfigServer");
                        message.WriteString(data);
                        GameMain.LuaCs.Networking.Send(message);
                    }
                    // Remove the server config for all users.
                    else if (!LocalConfig.SyncSettings && ServerConfig != null)
                    {
                        string data = DataAppender.AppendData("_", manualUpdate, GameMain.Client.SessionId);
                        IWriteMessage message = GameMain.LuaCs.Networking.Start("SPW_DisableConfigServer");
                        message.WriteString(data);
                        GameMain.LuaCs.Networking.Send(message);
                    }

                    return;
                }
            }
        }

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
