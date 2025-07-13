using Barotrauma;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace SoundproofWalls
{
    public static class ConfigManager
    {
        public static readonly string ConfigPath = Path.Combine(SaveUtil.DefaultSaveFolder, "ModConfigs/SoundproofWalls_Config.json").Replace('\\', '/');
        private static Config _cachedConfig = LoadConfig();

        public static Config LocalConfig = LoadConfig();
        private static Config? serverConfig = null;
        public static Config? ServerConfig 
        { 
            get => serverConfig; 
            set 
            { 
                serverConfig = value; 
                // Refresh menu if it's open.
                if (Menu.Instance != null) { Menu.Create(); } 
            } 
        }
        public static Client? ServerConfigUploader = null;
        public static Config Config { get { return ServerConfig ?? LocalConfig; } }

        private static float lastRequestTime = 0;

        public static void Update()
        {
            if (GameMain.IsMultiplayer && ServerConfig == null && Timing.TotalTime > lastRequestTime + 10)
            {
                lastRequestTime = (float)Timing.TotalTime;
                IWriteMessage message = GameMain.LuaCs.Networking.Start(Plugin.SERVER_SEND_CONFIG);
                message.WriteByte(GameMain.Client.SessionId);
                GameMain.LuaCs.Networking.Send(message);

            }
        }

        public static void UpdateConfig(Config newConfig, Config oldConfig, bool isServerConfigEnabled = false, bool manualUpdate = false, byte configSenderId = 0)
        {
            bool shouldStop = oldConfig.Enabled && !newConfig.Enabled;
            bool shouldStart = !oldConfig.Enabled && newConfig.Enabled;
            bool shouldReloadSounds = Util.ShouldReloadSounds(newConfig: newConfig, oldConfig: oldConfig);
            bool shouldUpdateAITarget = !GameMain.IsMultiplayer && oldConfig.AITargetSoundRangeMultiplierMaster != newConfig.AITargetSoundRangeMultiplierMaster;
            bool shouldUpdateSoundInfo = Util.ShouldUpdateSoundInfo(newConfig, oldConfig: oldConfig);
            bool shouldStartAlEffects = !shouldStart && !oldConfig.DynamicFx && newConfig.DynamicFx;
            bool shouldStopAlEffects = !shouldStop && oldConfig.DynamicFx && !newConfig.DynamicFx;

            ServerConfig = isServerConfigEnabled ? newConfig : null;

            if (shouldStartAlEffects) { Plugin.InitDynamicFx(); }
            else if (shouldStopAlEffects) { Plugin.DisposeDynamicFx(); }

            if (shouldStop) { Plugin.Instance?.Dispose(); }
            else if (shouldStart) { Plugin.Instance?.Initialize(); }
            else if (shouldReloadSounds) { Util.ReloadSounds(); }
            else if (shouldUpdateSoundInfo) { SoundInfoManager.UpdateSoundInfoMap(); }

            if (shouldUpdateAITarget) { Util.UpdateAITarget(); }

            if (manualUpdate && configSenderId != 0)
            {
                ServerConfigUploader = GameMain.Client.ConnectedClients.FirstOrDefault(client => client.SessionId == configSenderId);
                string uploaderName = ServerConfigUploader?.Name ?? "unknown";
                if (isServerConfigEnabled)
                {
                    LuaCsLogger.Log($"Soundproof Walls: \"{uploaderName}\" {TextManager.Get("spw_updateserverconfig").Value}", Color.LimeGreen);
                }
                else
                {
                    LuaCsLogger.Log($"Soundproof Walls: \"{uploaderName}\" {TextManager.Get("spw_disableserverconfig").Value}", Color.MonoGameOrange);
                }
            }
        }

        public static void UploadClientConfigToServer(bool manualUpdate = false)
        {
            if (!GameMain.IsMultiplayer) { return; }

            Client client = GameMain.Client.MyClient;

            if (client != null && (client.IsOwner || client.HasPermission(ClientPermissions.Ban)))
            {
                string data = DataAppender.AppendData(LocalConfig.SyncSettings ? JsonSerializer.Serialize(LocalConfig) : Plugin.DISABLED_CONFIG_VALUE, manualUpdate, client.SessionId);
                IWriteMessage message = GameMain.LuaCs.Networking.Start(Plugin.SERVER_RECEIVE_CONFIG);
                message.WriteString(data);
                GameMain.LuaCs.Networking.Send(message);
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
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)); // Create ModConfigs folder if it doesn't exist.
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
