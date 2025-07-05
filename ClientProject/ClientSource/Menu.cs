// Menu.cs
using Barotrauma;
using Microsoft.Xna.Framework;
using System.Text.Json;

namespace SoundproofWalls
{
    public static class Menu
    {
        public static Config defaultConfig = new Config();
        public static JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private static GUIButton? settingsButton = null;

        /// <summary>
        /// A reference to the main frame of the settings window, if it's open.
        /// Managed by the SoundproofWallsMenu class.
        /// </summary>
        public static GUIFrame? currentMenuFrame = null;

        /// <summary>
        /// A reference to the settings currently being edited in the menu, if it's open.
        /// This safely gets the value from the active menu instance.
        /// </summary>
        public static Config? NewLocalConfig => SoundproofWallsMenu.Instance?.unsavedConfig;


        public static void ForceOpenMenu()
        {
            if (!GUI.PauseMenuOpen)
            {
                GUI.TogglePauseMenu();
            }

            if (GUI.PauseMenuOpen)
            {
                SoundproofWallsMenu.Create();
            }
        }

        // *** NEW: This method is now a proxy for the real implementation. ***
        public static void ForceOpenWelcomePopUp()
        {
            // This call forwards the request to the SoundproofWallsMenu class,
            // which is responsible for creating all UI elements.
            SoundproofWallsMenu.ShowWelcomePopup();
        }

        public static void SPW_TogglePauseMenu()
        {
            if (GUI.PauseMenuOpen)
            {
                // PAUSE MENU IS OPENING
                GUIFrame pauseMenuFrame = GUI.PauseMenu;
                if (pauseMenuFrame == null) return;

                GUIComponent? pauseMenuList = pauseMenuFrame.FindChild("PauseMenuList");
                if (pauseMenuList == null)
                {
                    var frameChildren = GetChildren(pauseMenuFrame);
                    if (frameChildren.Count > 1)
                    {
                        var secondChildChildren = GetChildren(frameChildren[1]);
                        if (secondChildChildren.Count > 0)
                        {
                            pauseMenuList = secondChildChildren[0];
                        }
                    }
                }

                if (pauseMenuList == null)
                {
                    LuaCsLogger.LogError("[SoundproofWalls] Failed to find pause menu list to add settings button.");
                    return;
                }

                if (ConfigManager.LocalConfig.HideSettingsButton) { return; }

                bool buttonExists = false;
                foreach (var child in pauseMenuList.Children)
                {
                    if (child is GUIButton btn && btn.UserData as string == "SoundproofWallsSettings")
                    {
                        buttonExists = true;
                        settingsButton = btn;
                        break;
                    }
                }

                if (!buttonExists)
                {
                    string buttonText = TextManager.Get("spw_settings").Value;
                    settingsButton = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), pauseMenuList.RectTransform), buttonText, Alignment.Center, "GUIButtonSmall")
                    {
                        UserData = "SoundproofWallsSettings",
                        OnClicked = (sender, args) =>
                        {
                            SoundproofWallsMenu.Create();
                            return true;
                        }
                    };
                }
            }
            else
            {
                // PAUSE MENU IS CLOSING
                SoundproofWallsMenu.Instance?.Close();
            }
        }

        public static List<GUIComponent> GetChildren(GUIComponent comp)
        {
            var children = new List<GUIComponent>();
            foreach (var child in comp.GetAllChildren()) { children.Add(child); }
            return children;
        }
    }
}