#nullable enable
using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Globalization;
using System.Text.Json;

namespace SoundproofWalls
{
    public class Menu
    {
        private const int TAB_ICON_SIZE = 64;
        private const string TAB_ICON_SHEET = "Content/UI/TabIcons.png";

        private static readonly Color MenuFlashColor = new Color(47, 74, 57);
        private static readonly Color MenuDefaultValueColor = new Color(71, 133, 114);
        private static readonly Color MenuVanillaValueColor = Color.LightGray;

        private static readonly Color PopupPrimaryColor = new Color(73, 164, 137); // Main frame text
        private static readonly Color PopupSecondaryColor = new Color(203, 193, 149); // Sub headings
        private static readonly Color PopupAccentColor = new Color(204, 204, 204); // Body text
        private static readonly Color PopupLinkColor = Color.LightBlue * 0.8f; // Link text   

        public static readonly Color ConsolePrimaryColor = Color.SpringGreen;
        public static readonly Color ConsoleSecondaryColor = Color.LightSkyBlue;

        private static readonly Config defaultConfig = new Config();
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static GUIButton? menuButton = null;
        public static Menu? Instance { get; private set; }
        // Keeps track of the last tab the user was on and how scrolled down it they were.
        private static Tab lastTab = Tab.General;
        private static Dictionary<Tab, float> scrollPositions = new Dictionary<Tab, float>() 
        { 
            { Tab.General, 0 }, 
            { Tab.DynamicFx, 0 },
            { Tab.StaticFx, 0 },
            { Tab.Voice, 0 },
            { Tab.Muffle, 0 },
            { Tab.Volume, 0 },
            { Tab.Eavesdropping, 0 },
            { Tab.Hydrophones, 0 },
            { Tab.Ambience, 0 },
            { Tab.Pitch, 0 },
            { Tab.Advanced, 0 },
        };

        public enum Tab
        {
            General,
            DynamicFx,
            StaticFx,
            Voice,
            Muffle,
            Volume,
            Eavesdropping,
            Hydrophones,
            Ambience,
            Pitch,
            Advanced
        }

        public Tab CurrentTab { get; private set; }

        private static Config unsavedConfig;
        private Dictionary<ContentPackage, HashSet<CustomSound>> unsavedModdedCustomSounds;

        private readonly GUIFrame mainFrame;
        private readonly GUILayoutGroup tabber;
        private readonly GUIFrame contentFrame;
        private readonly Dictionary<Tab, (GUIButton Button, GUIFrame Content)> tabContents;

        private Action<KeyOrMouse>? currentKeybindSetter;
        private GUIButton? selectedKeybindButton;
        private bool keybindBoxSelectedThisFrame;

        public static void Create(bool startAtDefaultValues = false, bool startAtUnsavedValues = false, Color? flashColor = null)
        {
            Instance?.Close();
            Instance = new Menu(startAtDefaultValues, startAtUnsavedValues, flashColor);
        }

        public static void ShowWelcomePopup()
        {
            if (!GUI.PauseMenuOpen)
            {
                GUI.TogglePauseMenu();
            }
            if (!GUI.PauseMenuOpen)
            {
                return; // If the pause menu didn't open, we can't show the popup.
            }

            var popupFrame = new GUIFrame(new RectTransform(new Vector2(0.55f, 0.65f), GUI.PauseMenu.RectTransform, Anchor.Center), color: new Color(255, 255, 255, 255));
            var mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), popupFrame.RectTransform, Anchor.Center), isHorizontal: false)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            // 1. Header Section
            var headerSection = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.18f), mainLayout.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.05f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerSection.RectTransform), TextManager.Get("spw_popupheader"), textAlignment: Alignment.Center, font: GUIStyle.LargeFont, textColor: PopupSecondaryColor);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerSection.RectTransform), TextManager.Get("spw_popupheadersubtext"), textAlignment: Alignment.Center, font: GUIStyle.Font, wrap: true, textColor: PopupAccentColor);

            // 2. Body Section
            var bodyList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.70f), mainLayout.RectTransform), style: "InnerFrame")
            {
                CanBeFocused = true,
                OnSelected = (_, __) => false,
                Spacing = 4,
            };

            Spacer(bodyList.Content);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), bodyList.Content.RectTransform), TextManager.Get("spw_popupbodyheader"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center, textColor: PopupSecondaryColor) { CanBeFocused = false };

            // Add content sections to the body.
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading1"), TextManager.Get("spw_popupbodysubtext1"));
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading2"), TextManager.Get("spw_popupbodysubtext2"));
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading3"), TextManager.Get("spw_popupbodysubtext3"));
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading4"), TextManager.Get("spw_popupbodysubtext4"));
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading5"), TextManager.Get("spw_popupbodysubtext5"));
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading6"), TextManager.Get("spw_popupbodysubtext6"));
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading7"), TextManager.Get("spw_popupbodysubtext7"));
            CreateWelcomeContentSection(bodyList.Content, TextManager.Get("spw_popupbodysubheading8"), TextManager.Get("spw_popupbodysubtext8"));

            Spacer(bodyList.Content, size: 0.1f); // Double size
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), bodyList.Content.RectTransform), TextManager.Get("spw_popupbodyconclusion"), wrap: true, textAlignment: Alignment.Center, textColor: PopupAccentColor) { CanBeFocused = false };
            Spacer(bodyList.Content);

            // 3. Footer Section
            var footerSection = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.12f), mainLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = 5
            };

            // Left footer column
            var leftFooter = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1.0f), footerSection.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), leftFooter.RectTransform), TextManager.Get("spw_footertext1"), textAlignment: Alignment.Center, wrap: true, textColor: PopupPrimaryColor);
            CreatePopupLink(leftFooter, TextManager.Get("spw_footerlink1"), "https://steamcommunity.com/sharedfiles/filedetails/changelog/3153737715");

            // Middle footer column
            var middleFooter = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1.0f), footerSection.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), middleFooter.RectTransform), TextManager.Get("spw_footertext2"), textAlignment: Alignment.Center, wrap: true, textColor: PopupPrimaryColor);
            CreatePopupLink(middleFooter, TextManager.Get("spw_footerlink2"), "https://ko-fi.com/plag");

            // Right footer column
            var rightFooter = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1.0f), footerSection.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), rightFooter.RectTransform), TextManager.Get("spw_footertext3"), textAlignment: Alignment.Center, wrap: true, textColor: PopupPrimaryColor);
            CreatePopupLink(rightFooter, TextManager.Get("spw_footerlink3"), "https://github.com/Plag0/Soundproof-Walls");

            // 4. Close Button Section
            var buttonSection = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), mainLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.10f
            };

            new GUIButton(new RectTransform(new Vector2(0.5f, 0.95f), buttonSection.RectTransform, Anchor.BottomCenter), TextManager.Get("Close"))
            {
                OnClicked = (btn, data) =>
                {
                    if (popupFrame.Parent != null)
                    {
                        popupFrame.Parent.RemoveChild(popupFrame);
                    }
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.5f, 0.95f), buttonSection.RectTransform, Anchor.BottomCenter), TextManager.Get("spw_popupbutton"))
            {
                OnClicked = (btn, data) =>
                {
                    if (popupFrame.Parent != null)
                    {
                        popupFrame.Parent.RemoveChild(popupFrame);
                    }
                    ForceOpenMenu();
                    return true;
                }
            };

            ModStateManager.State.SeenWelcomePopup = true;
            ModStateManager.SaveState(ModStateManager.State);
        }

        private static void CreateWelcomeContentSection(GUIFrame parent, LocalizedString header, LocalizedString body)
        {
            var sectionLayout = new GUILayoutGroup(parent.RectTransform, isHorizontal: false)
            {
                AbsoluteSpacing = 25, // Spacing between the header and its body text
                Stretch = true
            };
            Spacer(parent);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), sectionLayout.RectTransform), header, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Left, textColor: PopupSecondaryColor) { CanBeFocused = false };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), sectionLayout.RectTransform), body, wrap: true, textAlignment: Alignment.TopLeft, textColor: PopupAccentColor) { CanBeFocused = false, TextOffset = (14, 0) };
        }

        private static void CreatePopupLink(GUILayoutGroup parent, LocalizedString text, string url)
        {
            var linkText = new GUITextBlock(new RectTransform(Vector2.Zero, parent.RectTransform) { IsFixedSize = true }, text, font: GUIStyle.SmallFont, textAlignment: Alignment.Center, textColor: PopupLinkColor);
            linkText.RectTransform.NonScaledSize = new Point((int)GUIStyle.SmallFont.MeasureString(text).X, linkText.Rect.Height);

            (Rectangle Rect, bool MouseOn) getHoverRect()
            {
                var textRect = linkText.Rect;
                bool mouseOn = textRect.Contains(PlayerInput.LatestMousePosition);
                return (textRect, mouseOn);
            }

            new GUICustomComponent(new RectTransform(Vector2.One, linkText.RectTransform),
                onUpdate: (dt, component) =>
                {
                    var (_, mouseOn) = getHoverRect();
                    if (mouseOn && PlayerInput.PrimaryMouseButtonClicked())
                    {
                        try
                        {
                            ToolBox.OpenFileWithShell(url);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.LogError($"Failed to open the url {url}, {e}");
                        }
                    }
                },
                onDraw: (sb, component) =>
                {
                    var (rect, mouseOn) = getHoverRect();
                    Color color = mouseOn ? GUIStyle.Green : PopupLinkColor;
                    linkText.TextColor = color;
                    GUI.DrawLine(sb, new Vector2(rect.Left, rect.Bottom), new Vector2(rect.Right, rect.Bottom), color);
                });
        }

        public static void ForceOpenMenu()
        {
            if (!GUI.PauseMenuOpen)
            {
                GUI.TogglePauseMenu();
            }

            if (GUI.PauseMenuOpen)
            {
                Create();
            }
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
                        break;
                    }
                }

                if (!buttonExists)
                {
                    string buttonText = TextManager.GetWithVariable("spw_modname", "[version]", ModState.Version).Value;
                    menuButton = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), pauseMenuList.RectTransform), buttonText, Alignment.Center, "GUIButtonSmall")
                    {
                        UserData = "SoundproofWallsSettings",
                        OnClicked = (sender, args) =>
                        {
                            Create();
                            return true;
                        }
                    };
                }
            }
            else
            {
                // PAUSE MENU IS CLOSING
                Instance?.Close();
            }
        }

        public static void Dispose()
        {
            if (menuButton?.Parent != null)
            {
                menuButton.Parent.RemoveChild(menuButton);
            }
            menuButton = null;
        }

        public static List<GUIComponent> GetChildren(GUIComponent comp)
        {
            var children = new List<GUIComponent>();
            foreach (var child in comp.GetAllChildren()) { children.Add(child); }
            return children;
        }

        private Menu(bool startAtDefaultValues = false, bool startAtUnsavedValues = false, Color? flashColor = null)
        {
            if (GUI.PauseMenu == null) { return; }

            if (startAtUnsavedValues)
            {
                unsavedConfig = ConfigManager.CloneConfig(unsavedConfig ?? ConfigManager.LocalConfig);
            }
            else if (startAtDefaultValues)
            {
                unsavedConfig = ConfigManager.CloneConfig(defaultConfig);
            }
            else 
            {
                unsavedConfig = ConfigManager.CloneConfig(ConfigManager.LocalConfig);
            }

            unsavedConfig.EavesdroppingKeyOrMouse = unsavedConfig.ParseEavesdroppingBind();

            // Clone the modded custom sounds list.
            var targetDict = startAtDefaultValues ? ConfigManager.DefaultModdedCustomSounds : ConfigManager.ModdedCustomSounds;
            unsavedModdedCustomSounds = targetDict.ToDictionary(
                entry => entry.Key, 
                entry => new HashSet<CustomSound>(entry.Value.Select(sound => JsonSerializer.Deserialize<CustomSound>(JsonSerializer.Serialize(sound)))));

            mainFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.6f), GUI.PauseMenu.RectTransform, Anchor.Center), color: new Color(255, 255, 255, 255));

            var mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), mainFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.03f,
                Stretch = true
            };

            new GUICustomComponent(new RectTransform(Vector2.Zero, mainLayout.RectTransform), onUpdate: (deltaTime, component) =>
            {
                // If we are not waiting for a keybind, do nothing.
                if (currentKeybindSetter == null) { return; }

                // This flag's only job is to ignore input for the single frame after selecting a button.
                if (keybindBoxSelectedThisFrame)
                {
                    keybindBoxSelectedThisFrame = false;
                    return;
                }

                // Check for all possible inputs and call the central setter function.
                var pressedKeys = PlayerInput.GetKeyboardState.GetPressedKeys();
                if (pressedKeys.Any())
                {
                    if (pressedKeys.Contains(Keys.Escape)) { ClearKeybindSetter(); } // Escape just clears, doesn't set.
                    else { CallKeybindSetter(pressedKeys.First()); }
                }
                else if (PlayerInput.PrimaryMouseButtonClicked()) { CallKeybindSetter(MouseButton.PrimaryMouse, clear: selectedKeybindButton != GUI.MouseOn as GUIButton); }
                else if (PlayerInput.SecondaryMouseButtonClicked()) { CallKeybindSetter(MouseButton.SecondaryMouse); }
                else if (PlayerInput.MidButtonClicked()) { CallKeybindSetter(MouseButton.MiddleMouse); }
                else if (PlayerInput.Mouse4ButtonClicked()) { CallKeybindSetter(MouseButton.MouseButton4); }
                else if (PlayerInput.Mouse5ButtonClicked()) { CallKeybindSetter(MouseButton.MouseButton5); }
            });

            // --- Left Column (Info Panel) ---
            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), mainLayout.RectTransform), isHorizontal: false);
            
            var iconContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), leftColumn.RectTransform, scaleBasis: ScaleBasis.BothWidth), style: null);
            var iconSprite = new Sprite(Path.Combine(Plugin.ModPath, "Content/UI/SoundproofWallsIcon.png"), sourceRectangle: null);
            new GUIImage(new RectTransform(Vector2.One, iconContainer.RectTransform), iconSprite, scaleToFit: GUIImage.ScalingMode.ScaleToFitSmallestExtent);

            // Info Text (middle, fills remaining space)
            var infoTextContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.45f), leftColumn.RectTransform), style: null);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.95f), infoTextContainer.RectTransform, Anchor.Center), GetInfoPanelText(), wrap: true, textAlignment: Alignment.TopLeft, font: GUIStyle.SmallFont) { CanBeFocused = false };
            
            var linksContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), isHorizontal: true)
            {
                Stretch = true, // Fill the horizontal space
                ChildAnchor = Anchor.Center // Center the child layout group
            };

            var actualLinksLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1.0f), linksContainer.RectTransform) { IsFixedSize = true }, isHorizontal: true)
            {
                AbsoluteSpacing = 10
            };

            CreateLink(actualLinksLayout, "Changelog", "https://steamcommunity.com/sharedfiles/filedetails/changelog/3153737715");
            AddLinkSeparator(actualLinksLayout);
            CreateLink(actualLinksLayout, "Donate", "https://ko-fi.com/plag");
            AddLinkSeparator(actualLinksLayout);
            CreateLink(actualLinksLayout, "GitHub", "https://github.com/Plag0/Soundproof-Walls");

            // --- Right Column (Settings Panel) ---
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1.0f), mainLayout.RectTransform), isHorizontal: false)
            {
                RelativeSpacing = 0.03f,
                Stretch = true
            };

            tabber = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), rightColumn.RectTransform), isHorizontal: true) { Stretch = true, AbsoluteSpacing = 20 };
            tabContents = new Dictionary<Tab, (GUIButton Button, GUIFrame Content)>();

            contentFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.8f), rightColumn.RectTransform), style: "InnerFrame");

            var bottom = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), rightColumn.RectTransform), isHorizontal: true) { Stretch = true, RelativeSpacing = 0.02f };

            CreateGeneralTab();
            CreateDynamicFxTab();
            CreateStaticFxTab();
            CreateVoiceTab();
            CreateMuffleTab();
            CreateVolumeTab();
            CreateEavesdroppingTab();
            CreateHydrophonesTab();
            CreateAmbienceTab();
            CreatePitchTab();
            CreateAdvancedTab();
            CreateBottomButtons(bottom);

            if (!ConfigManager.LocalConfig.RememberMenuTabAndScroll) { lastTab = Tab.General; }

            SelectTab(lastTab, scrollPositions[lastTab]);
            mainLayout.Recalculate();

            mainFrame.Flash(flashColor ?? MenuFlashColor, flashDuration: 0.45f);
        }

        private float GetScrollPos(Tab tab)
        {
            if (!ConfigManager.Config.RememberMenuTabAndScroll) { return 0; }

            foreach (var child in tabContents[tab].Content.Children)
            {
                var list = child as GUIListBox;
                if (list != null)
                {
                    return list.BarScroll;
                }
            }

            return 0;
        }

        public void Close()
        {
            scrollPositions[CurrentTab] = GetScrollPos(CurrentTab);

            mainFrame.Parent.RemoveChild(mainFrame);
            if (Instance == this) { Instance = null; }
        }

        #region Tab Management
        private void SwitchContent(GUIFrame newContent, float targetScroll)
        {
            foreach (var child in contentFrame.Children)
            {
                child.Visible = false;
            }
            newContent.Visible = true;

            // Load scroll position.
            foreach (var child in newContent.Children)
            {
                var list = child as GUIListBox;
                if (list != null)
                {
                    list.BarScroll = targetScroll;
                    break;
                }
            }
        }

        public void SelectTab(Tab tab, float lastScroll = 0)
        {
            CurrentTab = tab;
            lastTab = CurrentTab;
            if (!ConfigManager.Config.RememberMenuTabAndScroll) { lastScroll = 0; }
            SwitchContent(tabContents[tab].Content, lastScroll);
            foreach (var child in tabber.Children)
            {
                if (child is GUIButton btn) { btn.Selected = btn == tabContents[tab].Button; }
            }
        }

        private void AddButtonToTabber(Tab tab, Rectangle sourceRect)
        {
            var button = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), tabber.RectTransform), "", style: "GUITabButton")
            {
                ToolTip = TextManager.Get($"spw_{tab.ToString().ToLower()}tab"),
                OnClicked = (b, _) =>
                {
                    scrollPositions[CurrentTab] = GetScrollPos(CurrentTab); // Save scroll pos.
                    SelectTab(tab, scrollPositions[tab]);
                    return false;
                },
                Color = new Color(0, 0, 0, 0),
            };

            // Make the buttons square.
            new GUICustomComponent(new RectTransform(Vector2.Zero, button.RectTransform), onUpdate: (deltaTime, component) =>
            {
                int height = component.RectTransform.Parent.Rect.Height;
                // Set the width to be the same as the height
                component.RectTransform.Parent.NonScaledSize = new Point(height, height);
            });

            var iconSprite = new Sprite(Path.Combine(Plugin.ModPath, TAB_ICON_SHEET), sourceRect);

            new GUIImage(
                new RectTransform(Vector2.One, button.RectTransform, scaleBasis: ScaleBasis.Smallest),
                iconSprite,
                sourceRect: null,
                scaleToFit: GUIImage.ScalingMode.ScaleToFitSmallestExtent)
            {
                CanBeFocused = false,
                // Same colours found in style.xml for SettingsMenuAtlas.
                Color = new Color(169, 212, 187, 255),
                HoverColor = new Color(220, 220, 220, 255),
                SelectedColor = new Color(255, 255, 255, 255),
                PressedColor = new Color(100, 100, 100, 255),
                DisabledColor = new Color(125, 125, 125, 125)
            };

            var content = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.95f), contentFrame.RectTransform, Anchor.Center), style: null);
            tabContents.Add(tab, (button, content));
        }

        private GUIFrame GetTabContentFrame(Tab tab)
        {
            if (tabContents.TryGetValue(tab, out var tabContent))
            {
                return tabContent.Content;
            }
            throw new InvalidOperationException($"Tab content for {tab} not found.");
        }
        #endregion

        #region UI Creation Helpers
        private string GetInfoPanelText()
        {
            string text = $"Soundproof Walls v{ModState.Version} by Plag\n\n";
            if (GameMain.IsMultiplayer)
            {
                // Syncing text.
                if (ConfigManager.ServerConfig != null)
                {
                    text += ConfigManager.ServerConfigUploader == GameMain.Client.MyClient ? 
                        TextManager.Get("spw_syncingenableduploader").Value : 
                        TextManager.GetWithVariable("spw_syncingenabled", "[name]", ConfigManager.ServerConfigUploader?.Name ?? "unknown").Value;
                }
                else
                {
                    text += TextManager.Get("spw_syncingdisabled").Value;
                }

                text += "\n\n";

                // Permission text.
                Client client = GameMain.Client.MyClient;
                bool isDedicatedServer = !GameMain.Client.ConnectedClients.Any(c => c.IsOwner);
                if (isDedicatedServer)
                {
                    text += client.HasPermission(ClientPermissions.Ban) ? TextManager.Get("spw_editpermissionadmin").Value : TextManager.Get("spw_editpermissionnotadmin").Value;
                }
                else
                {
                    text += client.IsOwner ? TextManager.Get("spw_editpermissionhost").Value : TextManager.Get("spw_editpermissionnothost").Value;
                }
            }
            else
            {
                string configPath = ConfigManager.ConfigPath;
                if (!Environment.UserName.IsNullOrEmpty()) // Hide username in path.
                {
                    configPath = configPath.Replace(Environment.UserName, "username");
                }
                text += TextManager.GetWithVariable("spw_offlinemode", "[path]", configPath).Value;
            }

            return text;
        }

        private void CreateLink(GUILayoutGroup parent, LocalizedString text, string url)
        {
            var linkText = new GUITextBlock(new RectTransform(Vector2.Zero, parent.RectTransform) { IsFixedSize = true }, text, font: GUIStyle.SmallFont, textColor: PopupLinkColor);
            linkText.RectTransform.NonScaledSize = new Point((int)GUIStyle.SmallFont.MeasureString(text).X, linkText.Rect.Height);

            (Rectangle Rect, bool MouseOn) getHoverRect()
            {
                const int horizontalPadding = 6;
                const int verticalPadding = 4;

                var hoverRect = linkText.Rect;
                hoverRect.X += 10;
                hoverRect.Y += 0;
                hoverRect.Inflate(horizontalPadding, verticalPadding);
                bool mouseOn = hoverRect.Contains(PlayerInput.LatestMousePosition);

                var textSize = linkText.Font.MeasureString(linkText.Text);
                var textTopLeft = linkText.Rect.Location.ToVector2() + linkText.TextPos;
                var textRect = new Rectangle(textTopLeft.ToPoint(), textSize.ToPoint());

                return (textRect, mouseOn);
            }

            new GUICustomComponent(new RectTransform(Vector2.One, linkText.RectTransform),
                onUpdate: (dt, component) =>
                {
                    var (_, mouseOn) = getHoverRect();
                    if (mouseOn && PlayerInput.PrimaryMouseButtonClicked())
                    {
                        try
                        {
                            ToolBox.OpenFileWithShell(url);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.LogError($"Failed to open the url {url}, {e}");
                        }
                    }
                },
                onDraw: (sb, component) =>
                {
                    var (rect, mouseOn) = getHoverRect();
                    Color color = mouseOn ? GUIStyle.Green : PopupLinkColor;
                    linkText.TextColor = color;
                    GUI.DrawLine(sb, new Vector2(rect.Left, rect.Bottom - 9), new Vector2(rect.Right, rect.Bottom - 9), color);
                });
        }

        private void AddLinkSeparator(GUILayoutGroup parent)
        {
            var separator = new GUITextBlock(new RectTransform(Vector2.Zero, parent.RectTransform) { IsFixedSize = true }, "|", font: GUIStyle.SmallFont);
            separator.RectTransform.NonScaledSize = new Point((int)GUIStyle.SmallFont.MeasureString("|").X, separator.Rect.Height);
            separator.TextColor *= 0.8f;
        }
        #endregion

        #region UI Helpers
        private static RectTransform NewItemRectT(GUIFrame parent)
            => new RectTransform((1.0f, 0.1f), parent.RectTransform);

        private static GUIListBox NewList(GUIFrame parent)
        {
            GUIListBox settings =
                new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform), style: null)
                {
                    CanBeFocused = true,
                    OnSelected = (_, __) => false,
                    Spacing = 4,
                };
            return settings;
        }

        private static GUIFrame NewListContent(GUIFrame parent)
        {
            GUIListBox settings =
                new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform), style: null)
                {
                    CanBeFocused = true,
                    OnSelected = (_, __) => false,
                    Spacing = 4,
                };
            return settings.Content;
        }

        private static void Spacer(GUIFrame parent, float size = 0.05f)
        {
            new GUIFrame(new RectTransform((1.0f, size), parent.RectTransform), style: null) { CanBeFocused = false };
        }

        private static GUITextBlock SpacerLabel(GUIFrame parent, LocalizedString str, float size = 0.08f)
        {
            var frame = new GUIFrame(new RectTransform((1.0f, size), parent.RectTransform), style: null) { CanBeFocused = false };
            return new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.8f), frame.RectTransform), str, font: GUIStyle.HotkeyFont, textColor: new Color(Color.White * 0.8f, 125), textAlignment: Alignment.BottomCenter) { CanBeFocused = false };
        }

        private static GUITextBlock Label(GUIFrame parent, LocalizedString str)
        {
            return new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform), str, font: GUIStyle.SubHeadingFont) { CanBeFocused = false };
        }

        private string RawValue(float v) => $"{MathF.Round(v, 2)}";
        private string RawValueZeroInfinity(float v) => $"{(v <= 0 ? "∞" : MathF.Round(v, 2))}";
        private string RawValueZeroInstant(float v) => $"{(v <= 0 ? TextManager.Get("spw_instant") : MathF.Round(v, 2))}";
        private string RawValuePrecise(float v) => $"{MathF.Round(v, 3)}";
        private string Percentage(float v)
        {
            string str = $"{MathF.Round(v, 2).ToString("P0", CultureInfo.CurrentUICulture)}";
            if (CultureInfo.CurrentUICulture == CultureInfo.InvariantCulture) { str = str.Replace(" %", "%"); }
            return str;
        }
        private string PercentageOneDisabled(float v)
        {
            string str;
            if (v == 1)
            {
                str = TextManager.Get("spw_disabled").Value;
            }
            else
            {
                str = $"{MathF.Round(v, 2).ToString("P0", CultureInfo.CurrentUICulture)}";
                if (CultureInfo.CurrentUICulture == CultureInfo.InvariantCulture) { str = str.Replace(" %", "%"); }
            }
            return str;
        }
        private string Hertz(float v) => $"{MathF.Round(v).ToString("N0", CultureInfo.CurrentUICulture)} Hz";
        private string Seconds(float v) => $"{MathF.Round(v, 2)} {TextManager.Get("spw_seconds")}"; // Custom localization for seconds, e.g., English is 15s but Russian is 15с.
        private string SecondsOneTick(float v) => $"{( v > Timing.Step ? $"{MathF.Round(v, 2)} {TextManager.Get("spw_seconds")}" : $"{TextManager.Get("spw_pertick")}")}";
        private string PlusSeconds(float v) => $"+{MathF.Round(v, 2)} {TextManager.Get("spw_seconds")}";
        private string Centimeters(float v) => $"{MathF.Round(v).ToString("N0", CultureInfo.CurrentUICulture)} cm";
        private string CentimetersZeroInfinity(float v) => $"{(v <= 0 ? "∞" : MathF.Round(v).ToString("N0", CultureInfo.CurrentUICulture))} cm";
        private string CentimetersSq(float v) => $"{MathF.Round(v).ToString("N0", CultureInfo.CurrentUICulture)} cm²";
        private string PlusMeters(float v) => $"+{MathF.Round(v / 100).ToString("N0", CultureInfo.CurrentUICulture)} m";
        private string Curve(float v)
        {
            string str = $"{MathF.Round(v, 2)}";
            if (v == 1) { str += $" ({TextManager.Get("spw_linear").Value})"; }
            else if (v < 1) { str += $" ({TextManager.Get("spw_concave").Value})"; }
            else if (v > 1) { str += $" ({TextManager.Get("spw_convex").Value})"; }
            return str;
        }

        private string BoolFormatter(bool b) => TextManager.Get(b ? "spw_enabled" : "spw_disabled").Value;

        private string FormatSettingText<T>(T localValue, T serverValue, Func<T, string> formatter)
        {
            string localFormatted = formatter(localValue);

            // Show server value.
            if (GameMain.IsMultiplayer && ConfigManager.ServerConfig != null && !EqualityComparer<T>.Default.Equals(localValue, serverValue))
            {
                localFormatted += $" ({formatter(serverValue)})";
            }

            return localFormatted;
        }

        private Color GetSettingColor<T>(T localValue, GUIComponentStyle componentStyle, object? defaultValue = null, object? vanillaValue = null, bool highPrecision = false)
        {
            Color color = componentStyle.TextColor;
            if (defaultValue != null && ValuesEqual(localValue, defaultValue, highPrecision))
            {
                color = MenuDefaultValueColor;
            }
            else if (vanillaValue != null && ValuesEqual(localValue, vanillaValue, highPrecision))
            {
                color = MenuVanillaValueColor;
            }
            return color;
        }

        private bool ValuesEqual<T>(T localValue, object otherValue, bool highPrecision = false)
        {
            // Handle boolean comparison
            if (localValue is bool localBool && otherValue is bool otherBool)
            {
                return localBool == otherBool;
            }

            // Handle numeric comparison (float, double, int)
            if (IsNumericType(typeof(T)) && IsNumericType(otherValue.GetType()))
            {
                try
                {
                    double epsilon = highPrecision ? 0.0005 : 0.005;
                    double localDouble = Convert.ToDouble(localValue);
                    double otherDouble = Convert.ToDouble(otherValue);
                    return Math.Abs(localDouble - otherDouble) < epsilon;
                }
                catch
                {
                    return false;
                }
            }

            // Fallback to direct comparison
            return localValue?.Equals(otherValue) ?? false;
        }

        private bool IsNumericType(Type type)
        {
            return type == typeof(int) || type == typeof(float) || type == typeof(double) ||
                   type == typeof(decimal) || type == typeof(long) || type == typeof(short) ||
                   type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
                   type == typeof(ushort) || type == typeof(sbyte);
        }

        private string FormatTextBoxLabel(LocalizedString label, bool localValue, bool serverValue, Func<bool, string> formatter)
        {
            string text = label.Value;
            if (GameMain.IsMultiplayer && ConfigManager.ServerConfig != null && localValue != serverValue)
            {
                text += $" ({formatter(serverValue)})";
            }
            return text;
        }

        private static GUITickBox Tickbox(GUIFrame parent, LocalizedString label, LocalizedString tooltip, bool currentValue, Action<bool> setter)
        {
            var tickbox = new GUITickBox(NewItemRectT(parent), label)
            {
                Selected = currentValue,
                OnSelected = (tb) =>
                {
                    setter(tb.Selected);
                    return true;
                }
            };
            tickbox.ToolTip = tooltip;
            return tickbox;
        }

        private GUIDropDown DropdownWithServerInfo<T>(
            GUIFrame parent,
            IReadOnlyList<T> values,
            T localValue,
            T serverValue,
            Action<T> setter,
            Func<T, string> formatter,
            Func<T, LocalizedString>? tooltipFunc,
            LocalizedString? mainTooltip = null) where T : IEquatable<T>
        {
            var dropdown = new GUIDropDown(NewItemRectT(parent), elementCount: values.Count)
            {
                ToolTip = mainTooltip
            };
            foreach (var option in values)
            {
                dropdown.AddItem(
                    text: formatter(option),
                    userData: option,
                    toolTip: tooltipFunc?.Invoke(option)
                );
            }
            dropdown.Select(values.IndexOf(localValue));

            Action updateButtonText = () =>
            {
                T currentLocal = (T)dropdown.SelectedData;
                dropdown.button.Text =
                    $"{formatter(currentLocal)}{(GameMain.IsMultiplayer && ConfigManager.ServerConfig != null && !EqualityComparer<T>.Default.Equals(localValue, serverValue) ? $" ({formatter(serverValue)})" : "")}";
            };

            updateButtonText();

            dropdown.OnSelected = (dd, userData) =>
            {
                T selectedValue = (T)userData;
                setter(selectedValue);
                updateButtonText();
                return true;
            };

            return dropdown;
        }

        private static (GUIScrollBar slider, GUITextBlock label) Slider(GUIFrame parent, Vector2 range, float stepSize, Func<float, string> labelFunc, Func<float, GUIComponentStyle, Color> colorFunc, float currentValue, Action<float> setter, LocalizedString? tooltip = null)
        {
            var layout = new GUILayoutGroup(NewItemRectT(parent), isHorizontal: true);
            var slider = new GUIScrollBar(new RectTransform((0.72f, 1.0f), layout.RectTransform), style: "GUISlider")
            {
                Range = range,
                BarScrollValue = currentValue,
                Step = 1.0f / (float)((range.Y - range.X) / stepSize),
                BarSize = 0.07f,
            };
            if (tooltip != null)
            {
                slider.ToolTip = tooltip;
            }

            int decimalPlaces = GetDecimalPlaces(stepSize);

            var label = new GUITextBlock(new RectTransform((0.28f, 1.0f), layout.RectTransform),
                labelFunc(currentValue), wrap: true, textAlignment: Alignment.Center);
            label.TextColor = colorFunc(currentValue, label.Style);

            slider.OnMoved = (sb, val) =>
            {
                float roundedValue = MathF.Round(sb.BarScrollValue, decimalPlaces);
                label.Text = labelFunc(roundedValue);
                label.TextColor = colorFunc(roundedValue, label.Style);
                setter(roundedValue);
                return true;
            };
            return (slider, label);
        }

        private static (GUIScrollBar slider, GUITextBlock label) PowerSlider(
            GUIFrame parent,
            Vector2 range,
            float stepSize,
            Func<float, string> labelFunc,
            Func<float, GUIComponentStyle, Color> colorFunc,
            float currentValue,
            Action<float> setter,
            LocalizedString? tooltip = null,
            float curveFactor = MathF.E,
            float bannedValue = -1)
        {
            var layout = new GUILayoutGroup(NewItemRectT(parent), isHorizontal: true);

            float normalizedValue = (currentValue - range.X) / (range.Y - range.X);
            float initialScrollPos = (float)Math.Pow(normalizedValue, 1.0f / curveFactor);

            var slider = new GUIScrollBar(new RectTransform((0.72f, 1.0f), layout.RectTransform), style: "GUISlider")
            {
                Range = new Vector2(0.0f, 1.0f),
                BarScroll = initialScrollPos,
                Step = 1.0f / (float)((range.Y - range.X) / stepSize),
                BarSize = 0.07f,
            };

            if (tooltip != null)
            {
                slider.ToolTip = tooltip;
            }

            int decimalPlaces = GetDecimalPlaces(stepSize);

            var label = new GUITextBlock(new RectTransform((0.28f, 1.0f), layout.RectTransform),
                labelFunc(currentValue), wrap: true, textAlignment: Alignment.Center);
            label.TextColor = colorFunc(currentValue, label.Style);

            slider.OnMoved = (sb, val) =>
            {
                float scrollPos = sb.BarScroll;
                if (scrollPos == bannedValue) { scrollPos += stepSize; }
                float curvedPos = (float)Math.Pow(scrollPos, curveFactor);
                float linearValue = range.X + (range.Y - range.X) * curvedPos;

                float roundedValue = MathF.Round(linearValue / stepSize, decimalPlaces) * stepSize;
                roundedValue = Math.Clamp(roundedValue, range.X, range.Y);

                label.Text = labelFunc(roundedValue);
                label.TextColor = colorFunc(roundedValue, label.Style);
                setter(roundedValue);
                return true;
            };

            return (slider, label);
        }

        private static int GetDecimalPlaces(float stepSize)
        {
            if (stepSize >= 1.0f) return 0;
            return (int)Math.Ceiling(-Math.Log10(stepSize));
        }

        private void ClearKeybindSetter()
        {
            currentKeybindSetter = null;
            if (selectedKeybindButton != null)
            {
                selectedKeybindButton.Selected = false;
                selectedKeybindButton = null;
            }
        }

        private void CallKeybindSetter(KeyOrMouse v, bool clear = true)
        {
            if (currentKeybindSetter == null) { return; }
            currentKeybindSetter.Invoke(v);

            if (clear)
            {
                ClearKeybindSetter();
            }
        }

        private void CreateKeybindControl(GUIFrame parent, LocalizedString title, Func<string> valueNameGetter, Action<KeyOrMouse> valueSetter)
        {
            var layout = new GUILayoutGroup(NewItemRectT(parent), isHorizontal: true);

            new GUITextBlock(
                new RectTransform((0.6f, 1.0f), layout.RectTransform),
                title,
                textAlignment: Alignment.CenterLeft,
                wrap: true
            );

            var keybindBox = new GUIButton(
                new RectTransform((0.4f, 1.0f), layout.RectTransform),
                valueNameGetter(),
                style: "GUITextBoxNoIcon"
            )
            {
                ToolTip = TextManager.Get("spw_eavesdroppingbindtooltip"),
                OnClicked = (btn, obj) =>
                {
                    if (btn.Selected)
                    {
                        CallKeybindSetter(MouseButton.PrimaryMouse);
                    }
                    else
                    {
                        ClearKeybindSetter();

                        // Set the state to "listening for input".
                        keybindBoxSelectedThisFrame = true;
                        currentKeybindSetter = v =>
                        {
                            valueSetter(v);
                            btn.Text = valueNameGetter();
                        };
                        selectedKeybindButton = btn;
                        btn.Selected = true;
                    }
                    return true;
                }
            };
        }

        private void CreateJsonTextBox<T>(
            GUIListBox parentListBox,
            LocalizedString labelText,
            LocalizedString tooltip,
            Func<Config, HashSet<T>> getter,
            Action<HashSet<T>> setter,
            Func<T, string> itemFormatter)
        {
            GUIFrame parent = parentListBox.Content;
            var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), isHorizontal: true);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1.0f), topRow.RectTransform), labelText, font: GUIStyle.SubHeadingFont);
            var resetButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), topRow.RectTransform), TextManager.Get("spw_reset"), style: "GUIButtonSmall");

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.25f), parent.RectTransform));
            var textBox = new GUITextBox(new RectTransform(Vector2.One, listBox.Content.RectTransform), "", wrap: true, style: "GUITextBoxNoBorder");
            listBox.ScrollBarEnabled = false;

            // shitty fix for the textbox blocking tab buttons
            new GUICustomComponent(new RectTransform(Vector2.Zero, listBox.Content.RectTransform), onUpdate: (deltaTime, component) =>
            {
                bool mouseInParentListBox = parentListBox.Rect.Contains(PlayerInput.MousePosition);
                bool mouseInListBox = listBox.Rect.Contains(PlayerInput.MousePosition);
                textBox.Visible = !mouseInListBox || mouseInParentListBox;
            });

            Action updateSize = () =>
            {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                listBox.RectTransform.NonScaledSize = new Point(listBox.Rect.Width, (int)textSize.Y + GUI.IntScale(20));
                
                // TODO Modify textbox here in some way so it doesn't block menu tabs when scrolled down?

                parentListBox.scrollBarNeedsRecalculation = true;
            };

            resetButton.OnClicked = (btn, data) =>
            {
                var defaultSet = getter(Menu.defaultConfig);
                setter(defaultSet);
                textBox.Text = JsonSerializer.Serialize(defaultSet, jsonOptions);
                return true;
            };

            textBox.OnTextChangedDelegate = (sender, e) =>
            {
                textBox.SetText(textBox.Text, store: true);
                updateSize();

                try
                {
                    var deserializedList = JsonSerializer.Deserialize<List<T>>(textBox.Text) ?? new List<T>();
                    var newSet = new HashSet<T>(deserializedList, getter(unsavedConfig).Comparer);
                    setter(newSet);
                    label.TextColor = GUIStyle.TextColorNormal;
                }
                catch (JsonException)
                {
                    label.TextColor = GUIStyle.Red;
                }
                return true;
            };

            textBox.OnEnterPressed = (sender, e) =>
            {
                int caretIndex = textBox.CaretIndex;
                textBox.Text = textBox.Text.Substring(0, caretIndex) + "\n" + textBox.Text.Substring(caretIndex);
                textBox.CaretIndex = caretIndex + 1;
                updateSize();
                return true;
            };

            var localSet = getter(unsavedConfig);
            var serverSet = getter(ConfigManager.ServerConfig ?? unsavedConfig);

            var (fullTooltip, diffCount) = GenerateServerDiffTooltip(localSet, serverSet, tooltip, itemFormatter);

            textBox.ToolTip = resetButton.ToolTip = label.ToolTip = fullTooltip;
            label.Text = diffCount > 0 ? $"{labelText} ({diffCount})" : labelText;

            textBox.Text = JsonSerializer.Serialize(localSet, jsonOptions);

            // Delayed update to recalculate size. Without this, the text box is cut off at the top and bottom. Not long enough for slower systems.
            bool needsTextUpdate = true;
            new GUICustomComponent(new RectTransform(Vector2.Zero, listBox.Content.RectTransform), onUpdate: (deltaTime, component) =>
            {
                if (needsTextUpdate)
                {
                    textBox.Text = textBox.Text;
                    needsTextUpdate = false;
                }
            });
        }

        /// <summary>
        /// Compares a local and server HashSet of any type, generates a formatted tooltip, and returns the number of differences.
        /// </summary>
        private (string fullTooltip, int diffCount) GenerateServerDiffTooltip<T>(
            HashSet<T> localSet,
            HashSet<T> serverSet,
            LocalizedString baseTooltip,
            Func<T, string> itemFormatter)
        {
            if (!GameMain.IsMultiplayer || ConfigManager.ServerConfig == null)
            {
                return (baseTooltip.Value, 0);
            }

            var differences = serverSet.Except(localSet).ToList();

            if (differences.Count == 0)
            {
                return (baseTooltip.Value, 0);
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(baseTooltip);
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(TextManager.GetWithVariable("spw_serverdiffheader", "[count]", differences.Count.ToString()));

            for (int i = 0; i < differences.Count; i++)
            {
                sb.AppendLine();
                sb.Append($"{i + 1}. {itemFormatter(differences[i])}");
            }

            return (sb.ToString(), differences.Count);
        }

        private void CreateModdedCustomSoundsJsonTextBox(
            GUIListBox parentListBox,
            LocalizedString labelText,
            LocalizedString tooltip,
            Func<Dictionary<ContentPackage, HashSet<CustomSound>>, HashSet<CustomSound>> getter,
            Action<HashSet<CustomSound>> setter)
        {
            GUIFrame parent = parentListBox.Content;
            var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), isHorizontal: true);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1.0f), topRow.RectTransform), labelText, font: GUIStyle.SubHeadingFont);
            var resetButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), topRow.RectTransform), TextManager.Get("spw_reset"), style: "GUIButtonSmall");

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.25f), parent.RectTransform));
            var textBox = new GUITextBox(new RectTransform(Vector2.One, listBox.Content.RectTransform), "", wrap: true, style: "GUITextBoxNoBorder");
            listBox.ScrollBarEnabled = false;

            // shitty fix for the textbox blocking tab buttons
            new GUICustomComponent(new RectTransform(Vector2.Zero, listBox.Content.RectTransform), onUpdate: (deltaTime, component) =>
            {
                bool mouseInParentListBox = parentListBox.Rect.Contains(PlayerInput.MousePosition);
                bool mouseInListBox = listBox.Rect.Contains(PlayerInput.MousePosition);
                textBox.Visible = !mouseInListBox || mouseInParentListBox;
            });

            Action updateSize = () =>
            {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                listBox.RectTransform.NonScaledSize = new Point(listBox.Rect.Width, (int)textSize.Y + GUI.IntScale(20));

                parentListBox.scrollBarNeedsRecalculation = true;
            };

            resetButton.OnClicked = (btn, data) =>
            {
                var defaultSet = getter(ConfigManager.DefaultModdedCustomSounds);
                setter(defaultSet);
                textBox.Text = JsonSerializer.Serialize(defaultSet, jsonOptions);
                return true;
            };

            textBox.OnTextChangedDelegate = (sender, e) =>
            {
                textBox.SetText(textBox.Text, store: true);
                updateSize();

                try
                {
                    var deserializedList = JsonSerializer.Deserialize<List<CustomSound>>(textBox.Text) ?? new List<CustomSound>();
                    var newSet = new HashSet<CustomSound>(deserializedList, getter(unsavedModdedCustomSounds).Comparer);
                    setter(newSet);
                    label.TextColor = GUIStyle.TextColorNormal;
                }
                catch (JsonException)
                {
                    label.TextColor = GUIStyle.Red;
                }
                return true;
            };

            textBox.OnEnterPressed = (sender, e) =>
            {
                int caretIndex = textBox.CaretIndex;
                textBox.Text = textBox.Text.Substring(0, caretIndex) + "\n" + textBox.Text.Substring(caretIndex);
                textBox.CaretIndex = caretIndex + 1;
                updateSize();
                return true;
            };

            var localSet = getter(unsavedModdedCustomSounds);
            textBox.ToolTip = resetButton.ToolTip = label.ToolTip = tooltip;
            label.Text = labelText;
            textBox.Text = JsonSerializer.Serialize(localSet, jsonOptions);

            // Delayed update to recalculate size.
            bool needsTextUpdate = true;
            new GUICustomComponent(new RectTransform(Vector2.Zero, listBox.Content.RectTransform), onUpdate: (deltaTime, component) =>
            {
                if (needsTextUpdate)
                {
                    textBox.Text = textBox.Text;
                    needsTextUpdate = false;
                }
            });
        }
        #endregion

        #region Tab Creation
        private void CreateGeneralTab()
        {
            var iconRect = new Rectangle(0 * TAB_ICON_SIZE, 0 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE); // Top-left icon (0,0)
            AddButtonToTabber(Tab.General, iconRect);
            var content = GetTabContentFrame(Tab.General);
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_enablemod"),
                localValue: unsavedConfig.Enabled, 
                serverValue: ConfigManager.ServerConfig?.Enabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_enablemodtooltip"), 
                currentValue: unsavedConfig.Enabled,
                setter: v => unsavedConfig.Enabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_syncsettings"),
                localValue: unsavedConfig.SyncSettings, 
                serverValue: ConfigManager.ServerConfig?.SyncSettings ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_syncsettingstooltip"),
                currentValue: unsavedConfig.SyncSettings,
                setter: v => unsavedConfig.SyncSettings = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_focustargetaudio"),
                localValue: unsavedConfig.FocusTargetAudio, 
                serverValue: ConfigManager.ServerConfig?.FocusTargetAudio ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_focustargetaudiotooltip"),
                currentValue: unsavedConfig.FocusTargetAudio,
                setter: v => unsavedConfig.FocusTargetAudio = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_attenuatewithapproximatedistance"),
                localValue: unsavedConfig.AttenuateWithApproximateDistance, 
                serverValue: ConfigManager.ServerConfig?.AttenuateWithApproximateDistance ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_attenuatewithapproximatedistancetooltip"),
                currentValue: unsavedConfig.AttenuateWithApproximateDistance,
                setter: v => unsavedConfig.AttenuateWithApproximateDistance = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_effectprocessingmode"));
            var effectModeOptions = new[] { Config.EFFECT_PROCESSING_DYNAMIC, Config.EFFECT_PROCESSING_STATIC, Config.EFFECT_PROCESSING_CLASSIC };

            string EffectModeFormatter(uint mode) => mode switch
            {
                Config.EFFECT_PROCESSING_DYNAMIC => TextManager.Get("spw_dynamicfx").Value,
                Config.EFFECT_PROCESSING_STATIC => TextManager.Get("spw_staticfx").Value,
                Config.EFFECT_PROCESSING_CLASSIC => TextManager.Get("spw_vanillafx").Value,
                _ => ""
            };

            DropdownWithServerInfo(
                parent: settingsFrame,
                values: effectModeOptions,
                localValue: unsavedConfig.EffectProcessingMode,
                serverValue: ConfigManager.ServerConfig?.EffectProcessingMode ?? default,
                setter: v => unsavedConfig.EffectProcessingMode = v,
                formatter: EffectModeFormatter,
                tooltipFunc: value => value switch
                {
                    Config.EFFECT_PROCESSING_DYNAMIC => TextManager.Get("spw_dynamicfxtooltip"),
                    Config.EFFECT_PROCESSING_STATIC => TextManager.Get("spw_staticfxtooltip"),
                    Config.EFFECT_PROCESSING_CLASSIC => TextManager.Get("spw_vanillafxtooltip"),
                    _ => ""
                },
                mainTooltip: TextManager.Get("spw_effectprocessingmodetooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_soundrange"));
            Slider(settingsFrame, (0, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue, 
                    serverValue: ConfigManager.ServerConfig?.SoundRangeMultiplierMaster ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SoundRangeMultiplierMaster,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.SoundRangeMultiplierMaster,
                setter: v => unsavedConfig.SoundRangeMultiplierMaster = v,
                TextManager.Get("spw_soundrangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loopingsoundrange"));
            Slider(settingsFrame, (0, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoopingSoundRangeMultiplierMaster ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoopingSoundRangeMultiplierMaster,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.LoopingSoundRangeMultiplierMaster,
                setter: v => unsavedConfig.LoopingSoundRangeMultiplierMaster = v,
                TextManager.Get("spw_loopingsoundrangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_outdoorsoundrange"));
            Slider(settingsFrame, (0, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.OutdoorSoundRangeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.OutdoorSoundRangeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.OutdoorSoundRangeMultiplier,
                setter: v => unsavedConfig.OutdoorSoundRangeMultiplier = v,
                TextManager.Get("spw_outdoorsoundrangetooltip")
            );
        }

        private void CreateDynamicFxTab()
        {
            var iconRect = new Rectangle(0 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.DynamicFx, iconRect);
            var content = GetTabContentFrame(Tab.DynamicFx);
            GUIFrame settingsFrame = NewListContent(content);

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorymuffle"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_occludesounds"),
                localValue: unsavedConfig.OccludeSounds, 
                serverValue: ConfigManager.ServerConfig?.OccludeSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_occludesoundstooltip"),
                currentValue: unsavedConfig.OccludeSounds,
                setter: v => unsavedConfig.OccludeSounds = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_overmuffle"),
                localValue: unsavedConfig.OverMuffle, 
                serverValue: ConfigManager.ServerConfig?.OverMuffle ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_overmuffletooltip"),
                currentValue: unsavedConfig.OverMuffle,
                setter: v => unsavedConfig.OverMuffle = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_dynamicmufflestrengthmaster"));
            PowerSlider(settingsFrame, (0, 1.5f), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicMuffleStrengthMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicMuffleStrengthMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicMuffleStrengthMultiplier,
                setter: v => unsavedConfig.DynamicMuffleStrengthMultiplier = v,
                tooltip: TextManager.Get("spw_dynamicmufflestrengthmastertooltip"),
                curveFactor: 0.4f
            );

            Label(settingsFrame, TextManager.Get("spw_overmufflestrength"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.OverMuffleStrengthMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.OverMuffleStrengthMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.OverMuffleStrengthMultiplier,
                setter: v => unsavedConfig.OverMuffleStrengthMultiplier = v,
                tooltip: TextManager.Get("spw_overmufflestrengthtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_maxocclusions"));
            Slider(settingsFrame, (0, 5), 1,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MaxOcclusions ?? default,
                    formatter: RawValueZeroInfinity),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MaxOcclusions,
                    vanillaValue: null),
                currentValue: unsavedConfig.MaxOcclusions,
                setter: v => unsavedConfig.MaxOcclusions = (int)v,
                TextManager.Get("spw_maxocclusionstooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryreverb"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverb"),
                localValue: unsavedConfig.DynamicReverbEnabled, 
                serverValue: ConfigManager.ServerConfig?.DynamicReverbEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbtooltip"),
                currentValue: unsavedConfig.DynamicReverbEnabled,
                setter: v => unsavedConfig.DynamicReverbEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverbbloom"),
                localValue: unsavedConfig.DynamicReverbBloom,
                serverValue: ConfigManager.ServerConfig?.DynamicReverbBloom ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbbloomtooltip"),
                currentValue: unsavedConfig.DynamicReverbBloom,
                setter: v => unsavedConfig.DynamicReverbBloom = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverbwatersubtractsarea"),
                localValue: unsavedConfig.DynamicReverbWaterSubtractsArea, 
                serverValue: ConfigManager.ServerConfig?.DynamicReverbWaterSubtractsArea ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbwatersubtractsareatooltip"),
                currentValue: unsavedConfig.DynamicReverbWaterSubtractsArea,
                setter: v => unsavedConfig.DynamicReverbWaterSubtractsArea = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbminarea"));
            Slider(settingsFrame, (0, 1_000_000), 10_000,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbMinArea ?? default,
                    formatter: CentimetersSq),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbMinArea,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbMinArea,
                setter: v => unsavedConfig.DynamicReverbMinArea = (int)v,
                TextManager.Get("spw_dynamicreverbminareatooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbareasizemultiplier"));
            Slider(settingsFrame, (0, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbAreaSizeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbAreaSizeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbAreaSizeMultiplier,
                setter: v => unsavedConfig.DynamicReverbAreaSizeMultiplier = v,
                TextManager.Get("spw_dynamicreverbareasizemultipliertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbwetroomareasizemultiplier"));
            Slider(settingsFrame, (0, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbWetRoomAreaSizeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbWetRoomAreaSizeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbWetRoomAreaSizeMultiplier,
                setter: v => unsavedConfig.DynamicReverbWetRoomAreaSizeMultiplier = v,
                TextManager.Get("spw_dynamicreverbwetroomareasizemultipliertooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbairtargetgain"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbAirTargetGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbAirTargetGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbAirTargetGain,
                setter: v => unsavedConfig.DynamicReverbAirTargetGain = v,
                TextManager.Get("spw_dynamicreverbairtargetgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbairduration"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbAirDurationMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbAirDurationMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbAirDurationMultiplier,
                setter: v => unsavedConfig.DynamicReverbAirDurationMultiplier = v,
                TextManager.Get("spw_dynamicreverbairdurationtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbairgainhf"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbAirGainHf ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbAirGainHf,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbAirGainHf,
                setter: v => unsavedConfig.DynamicReverbAirGainHf = v,
                TextManager.Get("spw_dynamicreverbairgainhftooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbairamplitudethreshold"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DyanmicReverbAirAmplitudeThreshold ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DyanmicReverbAirAmplitudeThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.DyanmicReverbAirAmplitudeThreshold,
                setter: v => unsavedConfig.DyanmicReverbAirAmplitudeThreshold = v,
                TextManager.Get("spw_dynamicreverbairamplitudethresholdtooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbwatertargetgain"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbWaterTargetGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbWaterTargetGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbWaterTargetGain,
                setter: v => unsavedConfig.DynamicReverbWaterTargetGain = v,
                TextManager.Get("spw_dynamicreverbwatertargetgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbwaterdiffusion"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbWaterDiffusion ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbWaterDiffusion,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbWaterDiffusion,
                setter: v => unsavedConfig.DynamicReverbWaterDiffusion = v,
                TextManager.Get("spw_dynamicreverbwaterdiffusiontooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbwaterduration"));
            Slider(settingsFrame, (0, 1.2f), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbWaterDurationMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbWaterDurationMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbWaterDurationMultiplier,
                setter: v => unsavedConfig.DynamicReverbWaterDurationMultiplier = v,
                TextManager.Get("spw_dynamicreverbwaterdurationtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbwatergainhf"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicReverbWaterGainHf ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicReverbWaterGainHf,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicReverbWaterGainHf,
                setter: v => unsavedConfig.DynamicReverbWaterGainHf = v,
                TextManager.Get("spw_dynamicreverbwatergainhftooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbwateramplitudethreshold"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DyanmicReverbWaterAmplitudeThreshold ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DyanmicReverbWaterAmplitudeThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.DyanmicReverbWaterAmplitudeThreshold,
                setter: v => unsavedConfig.DyanmicReverbWaterAmplitudeThreshold = v,
                TextManager.Get("spw_dynamicreverbwateramplitudethresholdtooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryloudsounddistortionair"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_loudsounddistortionair"),
                localValue: unsavedConfig.LoudSoundDistortionAirEnabled,
                serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionAirEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_loudsounddistortiontooltipair"),
                currentValue: unsavedConfig.LoudSoundDistortionAirEnabled,
                setter: v => unsavedConfig.LoudSoundDistortionAirEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortionmaxmuffle"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionAirMaxMuffleThreshold ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionAirMaxMuffleThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionAirMaxMuffleThreshold,
                setter: v => unsavedConfig.LoudSoundDistortionAirMaxMuffleThreshold = v,
                TextManager.Get("spw_loudsounddistortionmaxmuffletooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetgain"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionAirTargetGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionAirTargetGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionAirTargetGain,
                setter: v => unsavedConfig.LoudSoundDistortionAirTargetGain = v,
                TextManager.Get("spw_loudsounddistortiontargetgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetedge"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionAirTargetEdge ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionAirTargetEdge,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionAirTargetEdge,
                setter: v => unsavedConfig.LoudSoundDistortionAirTargetEdge = v,
                TextManager.Get("spw_loudsounddistortiontargetedgetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetfrequency"));
            PowerSlider(settingsFrame, (80, 24000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionAirTargetFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionAirTargetFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionAirTargetFrequency,
                setter: v => unsavedConfig.LoudSoundDistortionAirTargetFrequency = (int)v,
                tooltip: TextManager.Get("spw_loudsounddistortiontargetfrequencytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortionlowpassfrequency"));
            PowerSlider(settingsFrame, (80, 24000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionAirLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionAirLowpassFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionAirLowpassFrequency,
                setter: v => unsavedConfig.LoudSoundDistortionAirLowpassFrequency = (int)v,
                tooltip: TextManager.Get("spw_loudsounddistortionlowpassfrequencytooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryloudsounddistortionwater"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_loudsounddistortionwater"),
                localValue: unsavedConfig.LoudSoundDistortionWaterEnabled,
                serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionWaterEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_loudsounddistortiontooltipwater"),
                currentValue: unsavedConfig.LoudSoundDistortionWaterEnabled,
                setter: v => unsavedConfig.LoudSoundDistortionWaterEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortionmaxmuffle"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionWaterMaxMuffleThreshold ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionWaterMaxMuffleThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionWaterMaxMuffleThreshold,
                setter: v => unsavedConfig.LoudSoundDistortionWaterMaxMuffleThreshold = v,
                TextManager.Get("spw_loudsounddistortionmaxmuffletooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetgain"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionWaterTargetGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionWaterTargetGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionWaterTargetGain,
                setter: v => unsavedConfig.LoudSoundDistortionWaterTargetGain = v,
                TextManager.Get("spw_loudsounddistortiontargetgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetedge"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionWaterTargetEdge ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionWaterTargetEdge,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionWaterTargetEdge,
                setter: v => unsavedConfig.LoudSoundDistortionWaterTargetEdge = v,
                TextManager.Get("spw_loudsounddistortiontargetedgetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetfrequency"));
            PowerSlider(settingsFrame, (80, 24000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionWaterTargetFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionWaterTargetFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionWaterTargetFrequency,
                setter: v => unsavedConfig.LoudSoundDistortionWaterTargetFrequency = (int)v,
                tooltip: TextManager.Get("spw_loudsounddistortiontargetfrequencytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortionlowpassfrequency"));
            PowerSlider(settingsFrame, (80, 24000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionWaterLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionWaterLowpassFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionWaterLowpassFrequency,
                setter: v => unsavedConfig.LoudSoundDistortionWaterLowpassFrequency = (int)v,
                tooltip: TextManager.Get("spw_loudsounddistortionlowpassfrequencytooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryexperimental"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_removeunusedbuffers"),
                localValue: unsavedConfig.RemoveUnusedBuffers,
                serverValue: ConfigManager.ServerConfig?.RemoveUnusedBuffers ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_removeunusedbufferstooltip"),
                currentValue: unsavedConfig.RemoveUnusedBuffers,
                setter: v => unsavedConfig.RemoveUnusedBuffers = v);
        }

        private void CreateStaticFxTab()
        {
            var iconRect = new Rectangle(1 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.StaticFx, iconRect);
            var content = GetTabContentFrame(Tab.StaticFx);
            GUIFrame settingsFrame = NewListContent(content);

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorymuffle"));

            Label(settingsFrame, TextManager.Get("spw_heavylowpassfrequency"));
            PowerSlider(settingsFrame, (10, 3200), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HeavyLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HeavyLowpassFrequency,
                    vanillaValue: SoundPlayer.MuffleFilterFrequency),
                currentValue: unsavedConfig.HeavyLowpassFrequency,
                setter: v => unsavedConfig.HeavyLowpassFrequency = (int)v,
                tooltip: TextManager.Get("spw_heavylowpassfrequencytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_mediumlowpassfrequency"));
            PowerSlider(settingsFrame, (10, 3200), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MediumLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MediumLowpassFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.MediumLowpassFrequency,
                setter: v => unsavedConfig.MediumLowpassFrequency = (int)v,
                tooltip: TextManager.Get("spw_mediumlowpassfrequencytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_lightlowpassfrequency"));
            PowerSlider(settingsFrame, (10, 3200), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LightLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LightLowpassFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.LightLowpassFrequency,
                setter: v => unsavedConfig.LightLowpassFrequency = (int)v,
                tooltip: TextManager.Get("spw_lightlowpassfrequencytooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryreverb"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_staticreverb"),
                localValue: unsavedConfig.StaticReverbEnabled,
                serverValue: ConfigManager.ServerConfig?.StaticReverbEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_staticreverbtooltip"),
                currentValue: unsavedConfig.StaticReverbEnabled,
                setter: v => unsavedConfig.StaticReverbEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_staticreverbalwaysloudsounds"),
                localValue: unsavedConfig.StaticReverbAlwaysOnLoudSounds,
                serverValue: ConfigManager.ServerConfig?.StaticReverbAlwaysOnLoudSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_staticreverbalwaysloudsoundstooltip"),
                currentValue: unsavedConfig.StaticReverbAlwaysOnLoudSounds,
                setter: v => unsavedConfig.StaticReverbAlwaysOnLoudSounds = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_staticreverbduration"));
            Slider(settingsFrame, (0.01f, 15), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StaticReverbDuration ?? default,
                    formatter: Seconds),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StaticReverbDuration,
                    vanillaValue: null),
                currentValue: unsavedConfig.StaticReverbDuration,
                setter: v => unsavedConfig.StaticReverbDuration = v,
                TextManager.Get("spw_staticreverbdurationtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_staticreverbwetdrymix"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StaticReverbWetDryMix ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StaticReverbWetDryMix,
                    vanillaValue: null),
                currentValue: unsavedConfig.StaticReverbWetDryMix,
                setter: v => unsavedConfig.StaticReverbWetDryMix = v,
                TextManager.Get("spw_staticreverbwetdrymixtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_staticreverbdamping"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StaticReverbDamping ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StaticReverbDamping,
                    vanillaValue: null),
                currentValue: unsavedConfig.StaticReverbDamping,
                setter: v => unsavedConfig.StaticReverbDamping = v,
                TextManager.Get("spw_staticreverbdampingtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_staticreverbminarea"));
            Slider(settingsFrame, (0, 1_000_000), 10_000,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StaticReverbMinArea ?? default,
                    formatter: CentimetersSq),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StaticReverbMinArea,
                    vanillaValue: null),
                currentValue: unsavedConfig.StaticReverbMinArea,
                setter: v => unsavedConfig.StaticReverbMinArea = (int)v,
                TextManager.Get("spw_staticreverbminareatooltip")
            );
        }

        private void CreateVoiceTab()
        {
            var iconRect = new Rectangle(1 * TAB_ICON_SIZE, 0 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Voice, iconRect);
            var content = GetTabContentFrame(Tab.Voice);
            GUIListBox settingsList = NewList(content);
            GUIFrame settingsFrame = settingsList.Content;

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorygeneral"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_talkingragdolls"),
                localValue: unsavedConfig.TalkingRagdolls,
                serverValue: ConfigManager.ServerConfig?.TalkingRagdolls ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_talkingragdollstooltip"),
                currentValue: unsavedConfig.TalkingRagdolls,
                setter: v => unsavedConfig.TalkingRagdolls = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_directionalradio"),
                localValue: unsavedConfig.DirectionalRadio,
                serverValue: ConfigManager.ServerConfig?.DirectionalRadio ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_directionalradiotooltip"),
                currentValue: unsavedConfig.DirectionalRadio,
                setter: v => unsavedConfig.DirectionalRadio = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hearlocalvoiceonfocusedtarget"),
                localValue: unsavedConfig.HearLocalVoiceOnFocusedTarget,
                serverValue: ConfigManager.ServerConfig?.HearLocalVoiceOnFocusedTarget ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hearlocalvoiceonfocusedtargettooltip"),
                currentValue: unsavedConfig.HearLocalVoiceOnFocusedTarget,
                setter: v => unsavedConfig.HearLocalVoiceOnFocusedTarget = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_voicerange"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceLocalRangeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceLocalRangeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceLocalRangeMultiplier,
                setter: v => unsavedConfig.VoiceLocalRangeMultiplier = v,
                TextManager.Get("spw_voicerangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiorange"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceRadioRangeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceRadioRangeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceRadioRangeMultiplier,
                setter: v => unsavedConfig.VoiceRadioRangeMultiplier = v,
                TextManager.Get("spw_radiorangetooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_voicevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceLocalVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceLocalVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceLocalVolumeMultiplier,
                setter: v => unsavedConfig.VoiceLocalVolumeMultiplier = v,
                TextManager.Get("spw_voicevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiovolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceRadioVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceRadioVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceRadioVolumeMultiplier,
                setter: v => unsavedConfig.VoiceRadioVolumeMultiplier = v,
                TextManager.Get("spw_radiovolumetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorydynamicfx"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverblocal"),
                localValue: unsavedConfig.VoiceLocalReverb,
                serverValue: ConfigManager.ServerConfig?.VoiceLocalReverb ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverblocaltooltip"),
                currentValue: unsavedConfig.VoiceLocalReverb,
                setter: v => unsavedConfig.VoiceLocalReverb = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverbradio"),
                localValue: unsavedConfig.VoiceRadioReverb,
                serverValue: ConfigManager.ServerConfig?.VoiceRadioReverb ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbradiotooltip"),
                currentValue: unsavedConfig.VoiceRadioReverb,
                setter: v => unsavedConfig.VoiceRadioReverb = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_drowningdistortion"),
                localValue: unsavedConfig.DrowningRadioDistortion,
                serverValue: ConfigManager.ServerConfig?.DrowningRadioDistortion ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_drowningdistortiontooltip"),
                currentValue: unsavedConfig.DrowningRadioDistortion,
                setter: v => unsavedConfig.DrowningRadioDistortion = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_voiceminlowpassfrequency"));
            PowerSlider(settingsFrame, (10, 1000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceMinLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceMinLowpassFrequency,
                    vanillaValue: ChannelInfoManager.VANILLA_VOIP_LOWPASS_FREQUENCY),
                currentValue: unsavedConfig.VoiceMinLowpassFrequency,
                setter: v => unsavedConfig.VoiceMinLowpassFrequency = (int)v,
                TextManager.Get("spw_voiceminlowpassfrequencytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_voicedynamicmufflemultiplier"));
            PowerSlider(settingsFrame, (0, 1.1f), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceDynamicMuffleMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceDynamicMuffleMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceDynamicMuffleMultiplier,
                setter: v => unsavedConfig.VoiceDynamicMuffleMultiplier = v,
                TextManager.Get("spw_voicedynamicmufflemultipliertooltip"),
                curveFactor: 0.4f
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorymuffle"));

            Label(settingsFrame, TextManager.Get("spw_voiceheavylowpassfrequency"));
            PowerSlider(settingsFrame, (10, 3200), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceHeavyLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceHeavyLowpassFrequency,
                    vanillaValue: ChannelInfoManager.VANILLA_VOIP_LOWPASS_FREQUENCY),
                currentValue: unsavedConfig.VoiceHeavyLowpassFrequency,
                setter: v => unsavedConfig.VoiceHeavyLowpassFrequency = (int)v,
                TextManager.Get("spw_voiceheavylowpassfrequencytooltip"),
                bannedValue: SoundPlayer.MuffleFilterFrequency
            );

            Label(settingsFrame, TextManager.Get("spw_voicemediumlowpassfrequency"));
            PowerSlider(settingsFrame, (10, 3200), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceMediumLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceMediumLowpassFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceMediumLowpassFrequency,
                setter: v => unsavedConfig.VoiceMediumLowpassFrequency = (int)v,
                TextManager.Get("spw_voicemediumlowpassfrequencytooltip"),
                bannedValue: SoundPlayer.MuffleFilterFrequency
            );

            Label(settingsFrame, TextManager.Get("spw_voicelightlowpassfrequency"));
            PowerSlider(settingsFrame, (10, 3200), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceLightLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceLightLowpassFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceLightLowpassFrequency,
                setter: v => unsavedConfig.VoiceLightLowpassFrequency = (int)v,
                TextManager.Get("spw_voicelightlowpassfrequencytooltip"),
                bannedValue: SoundPlayer.MuffleFilterFrequency
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryscreammode"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_screammode"),
                localValue: unsavedConfig.ScreamMode,
                serverValue: ConfigManager.ServerConfig?.ScreamMode ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_screammodetooltip"),
                currentValue: unsavedConfig.ScreamMode,
                setter: v => unsavedConfig.ScreamMode = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_screammodemaxrange"));
            Slider(settingsFrame, (0, 10000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ScreamModeMaxRange ?? default,
                    formatter: Centimeters),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ScreamModeMaxRange,
                    vanillaValue: null),
                currentValue: unsavedConfig.ScreamModeMaxRange,
                setter: v => unsavedConfig.ScreamModeMaxRange = (int)v,
                TextManager.Get("spw_screammodemaxrangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_screammodeminrange"));
            Slider(settingsFrame, (0, 10000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ScreamModeMinRange ?? default,
                    formatter: Centimeters),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ScreamModeMinRange,
                    vanillaValue: null),
                currentValue: unsavedConfig.ScreamModeMinRange,
                setter: v => unsavedConfig.ScreamModeMinRange = (int)v,
                TextManager.Get("spw_screammodeminrangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_screammodereleaserate"));
            Slider(settingsFrame, (0, 10000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ScreamModeReleaseRate ?? default,
                    formatter: Centimeters),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ScreamModeReleaseRate,
                    vanillaValue: null),
                currentValue: unsavedConfig.ScreamModeReleaseRate,
                setter: v => unsavedConfig.ScreamModeReleaseRate = (int)v,
                TextManager.Get("spw_screammodereleaseratetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorycustomfilter"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_radiocustomfilter"),
                localValue: unsavedConfig.RadioCustomFilterEnabled, 
                serverValue: ConfigManager.ServerConfig?.RadioCustomFilterEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_radiocustomfiltertooltip"),
                currentValue: unsavedConfig.RadioCustomFilterEnabled,
                setter: v => unsavedConfig.RadioCustomFilterEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_customradiopresets"));
            DropdownWithServerInfo(
                parent: settingsFrame,
                values: new[] { Config.CUSTOM_RADIO_BROKEN, Config.CUSTOM_RADIO_DIRTY, Config.CUSTOM_RADIO_NORMAL, Config.CUSTOM_RADIO_CLEAN },
                localValue: unsavedConfig.RadioCustomPreset,
                serverValue: ConfigManager.ServerConfig?.RadioCustomPreset ?? default,
                setter: v =>
                {
                    unsavedConfig.RadioCustomPreset = v;
                    switch (v)
                    {
                        case Config.CUSTOM_RADIO_BROKEN:
                            unsavedConfig.RadioBandpassFrequency = 500;
                            unsavedConfig.RadioBandpassQualityFactor = 10;
                            unsavedConfig.RadioDistortionDrive = 10;
                            unsavedConfig.RadioDistortionThreshold = 0.3f;
                            unsavedConfig.RadioStatic = 0.11f;
                            unsavedConfig.RadioCompressionThreshold = 1;
                            unsavedConfig.RadioCompressionRatio = 1;
                            unsavedConfig.RadioPostFilterBoost = 1.1f;
                            break;

                        case Config.CUSTOM_RADIO_DIRTY:
                            unsavedConfig.RadioBandpassFrequency = 2250;
                            unsavedConfig.RadioBandpassQualityFactor = 7.5f;
                            unsavedConfig.RadioDistortionDrive = 8;
                            unsavedConfig.RadioDistortionThreshold = 0.55f;
                            unsavedConfig.RadioStatic = 0.05f;
                            unsavedConfig.RadioCompressionThreshold = 0.25f;
                            unsavedConfig.RadioCompressionRatio = 0.9f;
                            unsavedConfig.RadioPostFilterBoost = 0.9f;
                            break;

                        case Config.CUSTOM_RADIO_NORMAL:
                            unsavedConfig.RadioBandpassFrequency = defaultConfig.RadioBandpassFrequency;
                            unsavedConfig.RadioBandpassQualityFactor = defaultConfig.RadioBandpassQualityFactor;
                            unsavedConfig.RadioDistortionDrive = defaultConfig.RadioDistortionDrive;
                            unsavedConfig.RadioDistortionThreshold = defaultConfig.RadioDistortionThreshold;
                            unsavedConfig.RadioStatic = defaultConfig.RadioStatic;
                            unsavedConfig.RadioCompressionThreshold = defaultConfig.RadioCompressionThreshold;
                            unsavedConfig.RadioCompressionRatio = defaultConfig.RadioCompressionRatio;
                            unsavedConfig.RadioPostFilterBoost = defaultConfig.RadioPostFilterBoost;
                            break;

                        case Config.CUSTOM_RADIO_CLEAN:
                            unsavedConfig.RadioBandpassFrequency = 2010;
                            unsavedConfig.RadioBandpassQualityFactor = 0.1f;
                            unsavedConfig.RadioDistortionDrive = 1;
                            unsavedConfig.RadioDistortionThreshold = 1;
                            unsavedConfig.RadioStatic = 0;
                            unsavedConfig.RadioCompressionThreshold = 1;
                            unsavedConfig.RadioCompressionRatio = 1;
                            unsavedConfig.RadioPostFilterBoost = 1.4f;
                            break;
                    }

                    Create(startAtUnsavedValues: true, flashColor: new Color(0, 0, 0, 0));
                },
                formatter: value => value switch
                {
                    Config.CUSTOM_RADIO_BROKEN => TextManager.Get("spw_customradiopresetbroken").Value,
                    Config.CUSTOM_RADIO_DIRTY => TextManager.Get("spw_customradiopresetdirty").Value,
                    Config.CUSTOM_RADIO_NORMAL => TextManager.Get("spw_customradiopresetnormal").Value,
                    Config.CUSTOM_RADIO_CLEAN => TextManager.Get("spw_customradiopresetclean").Value,
                    _ => ""
                },
                tooltipFunc: value => value switch
                {
                    Config.CUSTOM_RADIO_BROKEN => TextManager.Get("spw_customradiopresetbrokentooltip").Value,
                    Config.CUSTOM_RADIO_DIRTY => TextManager.Get("spw_customradiopresetdirtytooltip").Value,
                    Config.CUSTOM_RADIO_NORMAL => TextManager.Get("spw_customradiopresetnormaltooltip").Value,
                    Config.CUSTOM_RADIO_CLEAN => TextManager.Get("spw_customradiopresetcleantooltip").Value,
                    _ => ""
                },
                mainTooltip: TextManager.Get("spw_customradiopresetstooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_radiobandpassfrequency"));
            PowerSlider(settingsFrame, (10, 24000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioBandpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioBandpassFrequency,
                    vanillaValue: ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY),
                currentValue: unsavedConfig.RadioBandpassFrequency,
                setter: v => unsavedConfig.RadioBandpassFrequency = (int)v,
                TextManager.Get("spw_radiobandpassfrequencytooltip"),
                bannedValue: ChannelInfoManager.VANILLA_VOIP_BANDPASS_FREQUENCY
            );

            Label(settingsFrame, TextManager.Get("spw_radiobandpassqualityfactor"));
            Slider(settingsFrame, (0.1f, 10), 0.05f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioBandpassQualityFactor ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioBandpassQualityFactor,
                    vanillaValue: Math.Round(1.0 / Math.Sqrt(2), 1)), // Source: https://github.com/FakeFishGames/Barotrauma/blob/567cae1b190e4aa80ebd7f17bda55e6752c36182/Barotrauma/BarotraumaClient/ClientSource/Sounds/SoundFilters.cs#L92C53-L92C71
                currentValue: unsavedConfig.RadioBandpassQualityFactor,
                setter: v => unsavedConfig.RadioBandpassQualityFactor = v,
                TextManager.Get("spw_radiobandpassqualityfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiodistortiondrive"));
            Slider(settingsFrame, (1, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioDistortionDrive ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioDistortionDrive,
                    vanillaValue: null),
                currentValue: unsavedConfig.RadioDistortionDrive,
                setter: v => unsavedConfig.RadioDistortionDrive = v,
                TextManager.Get("spw_radiodistortiondrivetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiodistortionthreshold"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioDistortionThreshold ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioDistortionThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.RadioDistortionThreshold,
                setter: v => unsavedConfig.RadioDistortionThreshold = v,
                TextManager.Get("spw_radiodistortionthresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiostatic"));
            Slider(settingsFrame, (0, 1.0f), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioStatic ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioStatic,
                    vanillaValue: null),
                currentValue: unsavedConfig.RadioStatic,
                setter: v => unsavedConfig.RadioStatic = v,
                TextManager.Get("spw_radiostatictooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiocompressionthreshold"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioCompressionThreshold ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioCompressionThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.RadioCompressionThreshold,
                setter: v => unsavedConfig.RadioCompressionThreshold = v,
                TextManager.Get("spw_radiocompressionthresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiocompressionratio"));
            Slider(settingsFrame, (0.1f, 10), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioCompressionRatio ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioCompressionRatio,
                    vanillaValue: null),
                currentValue: unsavedConfig.RadioCompressionRatio,
                setter: v => unsavedConfig.RadioCompressionRatio = v,
                TextManager.Get("spw_radiocompressionratiotooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiopostfilterboost"));
            Slider(settingsFrame, (0, 10), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RadioPostFilterBoost ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioPostFilterBoost,
                    vanillaValue: null),
                currentValue: unsavedConfig.RadioPostFilterBoost,
                setter: v => unsavedConfig.RadioPostFilterBoost = v,
                TextManager.Get("spw_radiopostfilterboosttooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorybubbles"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_drowningbubbles"),
                localValue: unsavedConfig.DrowningBubblesEnabled,
                serverValue: ConfigManager.ServerConfig?.DrowningBubblesEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_drowningbubblestooltip"),
                currentValue: unsavedConfig.DrowningBubblesEnabled,
                setter: v => unsavedConfig.DrowningBubblesEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_drowningbubbleslocalrange"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DrowningBubblesLocalRangeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DrowningBubblesLocalRangeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.DrowningBubblesLocalRangeMultiplier,
                setter: v => unsavedConfig.DrowningBubblesLocalRangeMultiplier = v,
                TextManager.Get("spw_drowningbubbleslocalrangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_drowningbubbleslocalvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DrowningBubblesLocalVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DrowningBubblesLocalVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DrowningBubblesLocalVolumeMultiplier,
                setter: v => unsavedConfig.DrowningBubblesLocalVolumeMultiplier = v,
                TextManager.Get("spw_drowningbubbleslocalvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_drowningbubblesradiovolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DrowningBubblesRadioVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DrowningBubblesRadioVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DrowningBubblesRadioVolumeMultiplier,
                setter: v => unsavedConfig.DrowningBubblesRadioVolumeMultiplier = v,
                TextManager.Get("spw_drowningbubblesradiovolumetooltip")
            );

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_bubbleignorednames"),
                tooltip: TextManager.Get("spw_bubbleignorednamestooltip"),
                getter: config => config.BubbleIgnoredNames,
                setter: newSet => unsavedConfig.BubbleIgnoredNames = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);
        }

        private void CreateMuffleTab()
        {
            var iconRect = new Rectangle(2 * TAB_ICON_SIZE, 0 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Muffle, iconRect);
            var content = GetTabContentFrame(Tab.Muffle);
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_muffledivingsuit"),
                localValue: unsavedConfig.MuffleDivingSuits, 
                serverValue: ConfigManager.ServerConfig?.MuffleDivingSuits ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_muffledivingsuittooltip"),
                currentValue: unsavedConfig.MuffleDivingSuits,
                setter: v => unsavedConfig.MuffleDivingSuits = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflesubmergedplayer"),
                localValue: unsavedConfig.MuffleSubmergedPlayer, 
                serverValue: ConfigManager.ServerConfig?.MuffleSubmergedPlayer ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflesubmergedplayertooltip"),
                currentValue: unsavedConfig.MuffleSubmergedPlayer,
                setter: v => unsavedConfig.MuffleSubmergedPlayer = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflesubmergedviewtarget"),
                localValue: unsavedConfig.MuffleSubmergedViewTarget, 
                serverValue: ConfigManager.ServerConfig?.MuffleSubmergedViewTarget ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflesubmergedviewtargettooltip"),
                currentValue: unsavedConfig.MuffleSubmergedViewTarget,
                setter: v => unsavedConfig.MuffleSubmergedViewTarget = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflewatersurface"),
                localValue: unsavedConfig.MuffleWaterSurface, 
                serverValue: ConfigManager.ServerConfig?.MuffleWaterSurface ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflewatersurfacetooltip"),
                currentValue: unsavedConfig.MuffleWaterSurface,
                setter: v => unsavedConfig.MuffleWaterSurface = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_muffleflowfirepath"),
                localValue: unsavedConfig.MuffleFlowFireSoundsWithEstimatedPath, 
                serverValue: ConfigManager.ServerConfig?.MuffleFlowFireSoundsWithEstimatedPath ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_muffleflowfirepathtooltip"),
                currentValue: unsavedConfig.MuffleFlowFireSoundsWithEstimatedPath,
                setter: v => unsavedConfig.MuffleFlowFireSoundsWithEstimatedPath = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_muffleflowsounds"),
                localValue: unsavedConfig.MuffleFlowSounds, 
                serverValue: ConfigManager.ServerConfig?.MuffleFlowSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_muffleflowsoundstooltip"),
                currentValue: unsavedConfig.MuffleFlowSounds,
                setter: v => unsavedConfig.MuffleFlowSounds = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflefiresounds"),
                localValue: unsavedConfig.MuffleFireSounds, 
                serverValue: ConfigManager.ServerConfig?.MuffleFireSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflefiresoundstooltip"),
                currentValue: unsavedConfig.MuffleFireSounds,
                setter: v => unsavedConfig.MuffleFireSounds = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_classiclowpassfrequency"));
            PowerSlider(settingsFrame, (10, 3200), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ClassicMuffleFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ClassicMuffleFrequency,
                    vanillaValue: SoundPlayer.MuffleFilterFrequency),
                currentValue: unsavedConfig.ClassicMuffleFrequency,
                setter: v => unsavedConfig.ClassicMuffleFrequency = (int)v,
                tooltip: TextManager.Get("spw_classiclowpassfrequencytooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryobstructions"));

            float obstructionCurve = 0.4f;
            Label(settingsFrame, TextManager.Get("spw_obstructionwatersurface"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWaterSurface ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWaterSurface,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionWaterSurface,
                setter: v => unsavedConfig.ObstructionWaterSurface = v,
                TextManager.Get("spw_obstructionwatersurfacetooltip"),
                curveFactor: obstructionCurve
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionwaterbody"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWaterBody ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWaterBody,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionWaterBody,
                setter: v => unsavedConfig.ObstructionWaterBody = v,
                TextManager.Get("spw_obstructionwaterbodytooltip"),
                curveFactor: obstructionCurve
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionwallthick"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWallThick ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWallThick,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionWallThick,
                setter: v => unsavedConfig.ObstructionWallThick = v,
                TextManager.Get("spw_obstructionwallthicktooltip"),
                curveFactor: obstructionCurve
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionwallthin"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWallThin ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWallThin,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionWallThin,
                setter: v => unsavedConfig.ObstructionWallThin = v,
                TextManager.Get("spw_obstructionwallthintooltip"),
                curveFactor: obstructionCurve
            );

            Label(settingsFrame, TextManager.Get("spw_obstructiondoorthick"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionDoorThick ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionDoorThick,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionDoorThick,
                setter: v => unsavedConfig.ObstructionDoorThick = v,
                TextManager.Get("spw_obstructiondoorthicktooltip"),
                curveFactor: obstructionCurve
            );

            Label(settingsFrame, TextManager.Get("spw_obstructiondoorthin"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionDoorThin ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionDoorThin,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionDoorThin,
                setter: v => unsavedConfig.ObstructionDoorThin = v,
                TextManager.Get("spw_obstructiondoorthintooltip"),
                curveFactor: obstructionCurve
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionsuit"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionSuit ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionSuit,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionSuit,
                setter: v => unsavedConfig.ObstructionSuit = v,
                TextManager.Get("spw_obstructionsuittooltip"),
                curveFactor: obstructionCurve
            );

            Label(settingsFrame, TextManager.Get("spw_obstructiondrowning"));
            PowerSlider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionDrowning ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionDrowning,
                    vanillaValue: null,
                    highPrecision: true),
                currentValue: unsavedConfig.ObstructionDrowning,
                setter: v => unsavedConfig.ObstructionDrowning = v,
                TextManager.Get("spw_obstructiondrowningtooltip"),
                curveFactor: obstructionCurve
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorythresholds"));

            Label(settingsFrame, TextManager.Get("spw_classicmufflethreshold"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ClassicMinMuffleThreshold ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ClassicMinMuffleThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.ClassicMinMuffleThreshold,
                setter: v => unsavedConfig.ClassicMinMuffleThreshold = v,
                TextManager.Get("spw_classicmufflethresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_staticlightmufflethreshold"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StaticMinLightMuffleThreshold ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StaticMinLightMuffleThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.StaticMinLightMuffleThreshold,
                setter: v => unsavedConfig.StaticMinLightMuffleThreshold = v,
                TextManager.Get("spw_staticlightmufflethresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_staticmediummufflethreshold"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StaticMinMediumMuffleThreshold ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StaticMinMediumMuffleThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.StaticMinMediumMuffleThreshold,
                setter: v => unsavedConfig.StaticMinMediumMuffleThreshold = v,
                TextManager.Get("spw_staticmediummufflethresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_staticheavymufflethreshold"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StaticMinHeavyMuffleThreshold ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StaticMinHeavyMuffleThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.StaticMinHeavyMuffleThreshold,
                setter: v => unsavedConfig.StaticMinHeavyMuffleThreshold = v,
                TextManager.Get("spw_staticheavymufflethresholdtooltip")
            );

        }

        private void CreateVolumeTab()
        {
            var iconRect = new Rectangle(3 * TAB_ICON_SIZE, 0 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Volume, iconRect);
            var content = GetTabContentFrame(Tab.Volume);
            GUIFrame settingsFrame = NewListContent(content);

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorysidechaining"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_sidechaining"),
                localValue: unsavedConfig.SidechainingEnabled, 
                serverValue: ConfigManager.ServerConfig?.SidechainingEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_sidechainingtooltip"),
                currentValue: unsavedConfig.SidechainingEnabled,
                setter: v => unsavedConfig.SidechainingEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_sidechainmusic"),
                localValue: unsavedConfig.SidechainMusic, 
                serverValue: ConfigManager.ServerConfig?.SidechainMusic ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_sidechainmusictooltip"),
                currentValue: unsavedConfig.SidechainMusic,
                setter: v => unsavedConfig.SidechainMusic = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_sidechainintensitymaster"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SidechainIntensityMaster ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SidechainIntensityMaster,
                    vanillaValue: null),
                currentValue: unsavedConfig.SidechainIntensityMaster,
                setter: v => unsavedConfig.SidechainIntensityMaster = v,
                TextManager.Get("spw_sidechainintensitymastertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_sidechainreleasemaster"));
            Slider(settingsFrame, (0, 10), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SidechainReleaseMaster ?? default,
                    formatter: PlusSeconds),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SidechainReleaseMaster,
                    vanillaValue: null),
                currentValue: unsavedConfig.SidechainReleaseMaster,
                setter: v => unsavedConfig.SidechainReleaseMaster = v,
                TextManager.Get("spw_sidechainreleasemastertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_sidechainreleasecurve"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SidechainReleaseCurve ?? default,
                    formatter: Curve),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SidechainReleaseCurve,
                    vanillaValue: null),
                currentValue: unsavedConfig.SidechainReleaseCurve,
                setter: v => unsavedConfig.SidechainReleaseCurve = v,
                TextManager.Get("spw_sidechainreleasecurvetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_sidechainmufflepower"));
            Slider(settingsFrame, (1, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SidechainMufflePower ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SidechainMufflePower,
                    vanillaValue: null),
                currentValue: unsavedConfig.SidechainMufflePower,
                setter: v => unsavedConfig.SidechainMufflePower = v,
                TextManager.Get("spw_sidechainmufflepowertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_sidechainmusicmultiplier"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SidechainMusicMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SidechainMusicMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.SidechainMusicMultiplier,
                setter: v => unsavedConfig.SidechainMusicMultiplier = v,
                TextManager.Get("spw_sidechainmusicmultipliertooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorygain"));

            Label(settingsFrame, TextManager.Get("spw_muffledsoundvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MuffledSoundVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MuffledSoundVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.MuffledSoundVolumeMultiplier,
                setter: v => unsavedConfig.MuffledSoundVolumeMultiplier = v,
                TextManager.Get("spw_muffledsoundvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_unmuffledsoundvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnmuffledSoundVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnmuffledSoundVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.UnmuffledSoundVolumeMultiplier,
                setter: v => unsavedConfig.UnmuffledSoundVolumeMultiplier = v,
                TextManager.Get("spw_unmuffledsoundvolumetooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_muffledvoicevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MuffledVoiceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MuffledVoiceVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.MuffledVoiceVolumeMultiplier,
                setter: v => unsavedConfig.MuffledVoiceVolumeMultiplier = v,
                TextManager.Get("spw_muffledvoicevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_unmuffledvoicevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnmuffledVoiceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnmuffledVoiceVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.UnmuffledVoiceVolumeMultiplier,
                setter: v => unsavedConfig.UnmuffledVoiceVolumeMultiplier = v,
                TextManager.Get("spw_unmuffledvoicevolumetooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_muffledloopingvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MuffledLoopingVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MuffledLoopingVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.MuffledLoopingVolumeMultiplier,
                setter: v => unsavedConfig.MuffledLoopingVolumeMultiplier = v,
                TextManager.Get("spw_muffledloopingvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_unmuffledloopingvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnmuffledLoopingVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnmuffledLoopingVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.UnmuffledLoopingVolumeMultiplier,
                setter: v => unsavedConfig.UnmuffledLoopingVolumeMultiplier = v,
                TextManager.Get("spw_unmuffledloopingvolumetooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_submergedvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SubmergedVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SubmergedVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.SubmergedVolumeMultiplier,
                setter: v => unsavedConfig.SubmergedVolumeMultiplier = v,
                TextManager.Get("spw_submergedvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_flowsoundvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.FlowSoundVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.FlowSoundVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.FlowSoundVolumeMultiplier,
                setter: v => unsavedConfig.FlowSoundVolumeMultiplier = v,
                TextManager.Get("spw_flowsoundvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_firesoundvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.FireSoundVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.FireSoundVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.FireSoundVolumeMultiplier,
                setter: v => unsavedConfig.FireSoundVolumeMultiplier = v,
                TextManager.Get("spw_firesoundvolumetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryadvanced"));

            Label(settingsFrame, TextManager.Get("spw_vanillaexosuitvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VanillaExosuitVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VanillaExosuitVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.VanillaExosuitVolumeMultiplier,
                setter: v => unsavedConfig.VanillaExosuitVolumeMultiplier = v,
                TextManager.Get("spw_vanillaexosuitvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_mindistancefalloffvolume"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MinDistanceFalloffVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MinDistanceFalloffVolumeMultiplier,
                    vanillaValue: 0.1f),
                currentValue: unsavedConfig.MinDistanceFalloffVolumeMultiplier,
                setter: v => unsavedConfig.MinDistanceFalloffVolumeMultiplier = v,
                TextManager.Get("spw_mindistancefalloffvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_voicenearmultiplier"));
            Slider(settingsFrame, (0, 0.99f), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceNearMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceNearMultiplier,
                    vanillaValue: 0.4f), // source const RangeNear in VoipClient: https://github.com/FakeFishGames/Barotrauma/blob/d13836ce878b3f319dc044b9ed8f5204c926262c/Barotrauma/BarotraumaClient/ClientSource/Networking/Voip/VoipClient.cs#L15
                currentValue: unsavedConfig.VoiceNearMultiplier,
                setter: v => unsavedConfig.VoiceNearMultiplier = v,
                TextManager.Get("spw_voicenearmultipliertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loopingcomponentsoundnearmultiplier"));
            Slider(settingsFrame, (0, 0.99f), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoopingComponentSoundNearMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoopingComponentSoundNearMultiplier,
                    vanillaValue: 0.3f),
                currentValue: unsavedConfig.LoopingComponentSoundNearMultiplier,
                setter: v => unsavedConfig.LoopingComponentSoundNearMultiplier = v,
                TextManager.Get("spw_loopingcomponentsoundnearmultipliertooltip")
            );
        }

        private void CreateEavesdroppingTab()
        {
            var iconRect = new Rectangle(2 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Eavesdropping, iconRect);
            var content = GetTabContentFrame(Tab.Eavesdropping);
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingenabled"),
                localValue: unsavedConfig.EavesdroppingEnabled, 
                serverValue: ConfigManager.ServerConfig?.EavesdroppingEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingenabledtooltip"),
                currentValue: unsavedConfig.EavesdroppingEnabled,
                setter: v => unsavedConfig.EavesdroppingEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingmuffleself"),
                localValue: unsavedConfig.EavesdroppingMuffleSelf,
                serverValue: ConfigManager.ServerConfig?.EavesdroppingMuffleSelf ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingmuffleselftooltip"),
                currentValue: unsavedConfig.EavesdroppingMuffleSelf,
                setter: v => unsavedConfig.EavesdroppingMuffleSelf = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingmuffle"),
                localValue: unsavedConfig.EavesdroppingMuffleOther, 
                serverValue: ConfigManager.ServerConfig?.EavesdroppingMuffleOther ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingmuffletooltip"),
                currentValue: unsavedConfig.EavesdroppingMuffleOther,
                setter: v => unsavedConfig.EavesdroppingMuffleOther = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingtransition"),
                localValue: unsavedConfig.EavesdroppingTransitionEnabled, 
                serverValue: ConfigManager.ServerConfig?.EavesdroppingTransitionEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingtransitiontooltip"),
                currentValue: unsavedConfig.EavesdroppingTransitionEnabled,
                setter: v => unsavedConfig.EavesdroppingTransitionEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingducksradio"),
                localValue: unsavedConfig.EavesdroppingDucksRadio, 
                serverValue: ConfigManager.ServerConfig?.EavesdroppingDucksRadio ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingducksradiotooltip"),
                currentValue: unsavedConfig.EavesdroppingDucksRadio,
                setter: v => unsavedConfig.EavesdroppingDucksRadio = v);

            Spacer(settingsFrame);

            CreateKeybindControl(
                parent: settingsFrame,
                TextManager.Get("spw_eavesdroppingbind"),
                valueNameGetter: () => unsavedConfig.EavesdroppingKeyOrMouse.Name.Value,
                valueSetter: v => { unsavedConfig.EavesdroppingKeyOrMouse = v; unsavedConfig.EavesdroppingBind = v.ToString(); }
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingsoundvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingOtherVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingOtherVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingOtherVolumeMultiplier,
                setter: v => unsavedConfig.EavesdroppingOtherVolumeMultiplier = v,
                TextManager.Get("spw_eavesdroppingsoundvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingvoicevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingVoiceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingVoiceVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingVoiceVolumeMultiplier,
                setter: v => unsavedConfig.EavesdroppingVoiceVolumeMultiplier = v,
                TextManager.Get("spw_eavesdroppingvoicevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingothervolume"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingSelfVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingSelfVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingSelfVolumeMultiplier,
                setter: v => unsavedConfig.EavesdroppingSelfVolumeMultiplier = v,
                TextManager.Get("spw_eavesdroppingothervolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingsoundpitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingPitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingPitchMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingPitchMultiplier,
                setter: v => unsavedConfig.EavesdroppingPitchMultiplier = v,
                TextManager.Get("spw_eavesdroppingsoundpitchtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingmaxdistance"));
            Slider(settingsFrame, (25, 100), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingMaxDistance ?? default,
                    formatter: Centimeters),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingMaxDistance,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingMaxDistance,
                setter: v => unsavedConfig.EavesdroppingMaxDistance = (int)v,
                TextManager.Get("spw_eavesdroppingmaxdistancetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingtransitionduration"));
            Slider(settingsFrame, (0.1f, 10), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingTransitionDuration ?? default,
                    formatter: Seconds),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingTransitionDuration,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingTransitionDuration,
                setter: v => unsavedConfig.EavesdroppingTransitionDuration = v,
                TextManager.Get("spw_eavesdroppingtransitiondurationtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingthreshold"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingThreshold ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingThreshold,
                setter: v => unsavedConfig.EavesdroppingThreshold = v,
                TextManager.Get("spw_eavesdroppingthresholdtooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryvisuals"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingvisualfeedbackenabled"),
                localValue: unsavedConfig.EavesdroppingVisualFeedbackEnabled,
                serverValue: ConfigManager.ServerConfig?.EavesdroppingVisualFeedbackEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingvisualfeedbackenabledtooltip"),
                currentValue: unsavedConfig.EavesdroppingVisualFeedbackEnabled,
                setter: v => unsavedConfig.EavesdroppingVisualFeedbackEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingrevealscharacteroutline"),
                localValue: unsavedConfig.EavesdroppingRevealsCharacterOutline,
                serverValue: ConfigManager.ServerConfig?.EavesdroppingRevealsCharacterOutline ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingrevealscharacteroutlinetooltip"),
                currentValue: unsavedConfig.EavesdroppingRevealsCharacterOutline,
                setter: v => unsavedConfig.EavesdroppingRevealsCharacterOutline = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingrevealsall"),
                localValue: unsavedConfig.EavesdroppingRevealsAll,
                serverValue: ConfigManager.ServerConfig?.EavesdroppingRevealsAll ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingrevealsalltooltip"),
                currentValue: unsavedConfig.EavesdroppingRevealsAll,
                setter: v => unsavedConfig.EavesdroppingRevealsAll = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingspritemaxsize"));
            Slider(settingsFrame, (0, 3000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingSpriteMaxSize ?? default,
                    formatter: CentimetersZeroInfinity),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingSpriteMaxSize,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingSpriteMaxSize,
                setter: v => unsavedConfig.EavesdroppingSpriteMaxSize = (int)v,
                TextManager.Get("spw_eavesdroppingspritemaxsizetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingspritesizemultiplier"));
            Slider(settingsFrame, (0.01f, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingSpriteSizeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingSpriteSizeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingSpriteSizeMultiplier,
                setter: v => unsavedConfig.EavesdroppingSpriteSizeMultiplier = v,
                TextManager.Get("spw_eavesdroppingspritesizemultipliertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingspriteopacity"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingSpriteOpacity ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingSpriteOpacity,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingSpriteOpacity,
                setter: v => unsavedConfig.EavesdroppingSpriteOpacity = v,
                TextManager.Get("spw_eavesdroppingspriteopacitytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingspritefadecurve"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingSpriteFadeCurve ?? default,
                    formatter: Curve),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingSpriteFadeCurve,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingSpriteFadeCurve,
                setter: v => unsavedConfig.EavesdroppingSpriteFadeCurve = v,
                TextManager.Get("spw_eavesdroppingspritefadecurvetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingzoom"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingZoomMultiplier ?? default,
                    formatter: PercentageOneDisabled),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingZoomMultiplier,
                    vanillaValue: null),    
                currentValue: unsavedConfig.EavesdroppingZoomMultiplier,
                setter: v => unsavedConfig.EavesdroppingZoomMultiplier = v,
                TextManager.Get("spw_eavesdroppingzoomtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingvignette"));
            PowerSlider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingVignetteOpacityMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingVignetteOpacityMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingVignetteOpacityMultiplier,
                setter: v => unsavedConfig.EavesdroppingVignetteOpacityMultiplier = v,
                TextManager.Get("spw_eavesdroppingvignettetooltip")
            );
        }

        private void CreateHydrophonesTab()

        {
            var iconRect = new Rectangle(3 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Hydrophones, iconRect);
            var content = GetTabContentFrame(Tab.Hydrophones);
            GUIListBox settingsList = NewList(content);
            GUIFrame settingsFrame = settingsList.Content;

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophoneswitchenabled"),
                localValue: unsavedConfig.HydrophoneSwitchEnabled, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneSwitchEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophoneswitchenabledtooltip"),
                currentValue: unsavedConfig.HydrophoneSwitchEnabled,
                setter: v => unsavedConfig.HydrophoneSwitchEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonemovementsounds"),
                localValue: unsavedConfig.HydrophoneMovementSounds, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneMovementSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonemovementsoundstooltip"),
                currentValue: unsavedConfig.HydrophoneMovementSounds,
                setter: v => unsavedConfig.HydrophoneMovementSounds = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonehearengine"),
                localValue: unsavedConfig.HydrophoneHearEngine, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneHearEngine ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonehearenginetooltip"),
                currentValue: unsavedConfig.HydrophoneHearEngine,
                setter: v => unsavedConfig.HydrophoneHearEngine = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonehearintostructures"),
                localValue: unsavedConfig.HydrophoneHearIntoStructures, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneHearIntoStructures ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonehearintostructurestooltip"),
                currentValue: unsavedConfig.HydrophoneHearIntoStructures,
                setter: v => unsavedConfig.HydrophoneHearIntoStructures = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonemuffleownsub"),
                localValue: unsavedConfig.HydrophoneMuffleSelf, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneMuffleSelf ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonemuffleownsubtooltip"),
                currentValue: unsavedConfig.HydrophoneMuffleSelf,
                setter: v => unsavedConfig.HydrophoneMuffleSelf = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_hydrophonerange"));
            Slider(settingsFrame, (0, 20000), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneSoundRange ?? default,
                    formatter: PlusMeters),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneSoundRange,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneSoundRange,
                setter: v => unsavedConfig.HydrophoneSoundRange = (int)v,
                TextManager.Get("spw_hydrophonerangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneVolumeMultiplier,
                setter: v => unsavedConfig.HydrophoneVolumeMultiplier = v,
                TextManager.Get("spw_hydrophonevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonepitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophonePitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophonePitchMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophonePitchMultiplier,
                setter: v => unsavedConfig.HydrophonePitchMultiplier = v,
                TextManager.Get("spw_hydrophonepitchtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophoneambiencevolume"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneAmbienceVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.HydrophoneAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_hydrophoneambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonemovementvolume"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneMovementVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneMovementVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneMovementVolumeMultiplier,
                setter: v => unsavedConfig.HydrophoneMovementVolumeMultiplier = v,
                TextManager.Get("spw_hydrophonemovementvolumetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryvisuals"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonevisualfeedbackenabled"),
                localValue: unsavedConfig.HydrophoneVisualFeedbackEnabled, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneVisualFeedbackEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonevisualfeedbackenabledtooltip"),
                currentValue: unsavedConfig.HydrophoneVisualFeedbackEnabled,
                setter: v => unsavedConfig.HydrophoneVisualFeedbackEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophoneusagedisablessonarblips"),
                localValue: unsavedConfig.HydrophoneUsageDisablesSonarBlips, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneUsageDisablesSonarBlips ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophoneusagedisablessonarblipstooltip"),
                currentValue: unsavedConfig.HydrophoneUsageDisablesSonarBlips,
                setter: v => unsavedConfig.HydrophoneUsageDisablesSonarBlips = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophoneusagedisablessuboutline"),
                localValue: unsavedConfig.HydrophoneUsageDisablesSubOutline, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneUsageDisablesSubOutline ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophoneusagedisablessuboutlinetooltip"),
                currentValue: unsavedConfig.HydrophoneUsageDisablesSubOutline,
                setter: v => unsavedConfig.HydrophoneUsageDisablesSubOutline = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_hydrophonevisualfeedbacksizemultiplier"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneVisualFeedbackSizeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneVisualFeedbackSizeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneVisualFeedbackSizeMultiplier,
                setter: v => unsavedConfig.HydrophoneVisualFeedbackSizeMultiplier = v,
                TextManager.Get("spw_hydrophonevisualfeedbacksizemultipliertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonevisualfeedbackopacitymultiplier"));
            Slider(settingsFrame, (0, 2), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneVisualFeedbackOpacityMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneVisualFeedbackOpacityMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneVisualFeedbackOpacityMultiplier,
                setter: v => unsavedConfig.HydrophoneVisualFeedbackOpacityMultiplier = v,
                TextManager.Get("spw_hydrophonevisualfeedbackopacitymultipliertooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorydynamicfx"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
            label: TextManager.Get("spw_hydrophonereverb"),
            localValue: unsavedConfig.HydrophoneReverbEnabled, 
            serverValue: ConfigManager.ServerConfig?.HydrophoneReverbEnabled ?? default,
            formatter: BoolFormatter),
            tooltip: TextManager.Get("spw_hydrophonereverbtooltip"),
            currentValue: unsavedConfig.HydrophoneReverbEnabled,
            setter: v => unsavedConfig.HydrophoneReverbEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_hydrophonereverbtargetgain"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneReverbTargetGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneReverbTargetGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneReverbTargetGain,
                setter: v => unsavedConfig.HydrophoneReverbTargetGain = v,
                TextManager.Get("spw_hydrophonereverbtargetgaintooltip")
            );

            Spacer(settingsFrame);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonedistortion"),
                localValue: unsavedConfig.HydrophoneDistortionEnabled, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneDistortionEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonedistortiontooltip"),
                currentValue: unsavedConfig.HydrophoneDistortionEnabled,
                setter: v => unsavedConfig.HydrophoneDistortionEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetgain"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneDistortionTargetGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneDistortionTargetGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneDistortionTargetGain,
                setter: v => unsavedConfig.HydrophoneDistortionTargetGain = v,
                TextManager.Get("spw_loudsounddistortiontargetgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetedge"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneDistortionTargetEdge ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneDistortionTargetEdge,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneDistortionTargetEdge,
                setter: v => unsavedConfig.HydrophoneDistortionTargetEdge = v,
                TextManager.Get("spw_loudsounddistortiontargetedgetooltip")
            );

            Spacer(settingsFrame);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonebandpassfilter"),
                localValue: unsavedConfig.HydrophoneBandpassFilterEnabled, 
                serverValue: ConfigManager.ServerConfig?.HydrophoneBandpassFilterEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonebandpassfiltertooltip"),
                currentValue: unsavedConfig.HydrophoneBandpassFilterEnabled,
                setter: v => unsavedConfig.HydrophoneBandpassFilterEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_hydrophonebandpassfilterhfgain"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneBandpassFilterHfGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneBandpassFilterHfGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneBandpassFilterHfGain,
                setter: v => unsavedConfig.HydrophoneBandpassFilterHfGain = v,
                TextManager.Get("spw_hydrophonebandpassfilterhfgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonebandpassfilterlfgain"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneBandpassFilterLfGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneBandpassFilterLfGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneBandpassFilterLfGain,
                setter: v => unsavedConfig.HydrophoneBandpassFilterLfGain = v,
                TextManager.Get("spw_hydrophonebandpassfilterlfgaintooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryadvanced"));

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_hydrophonemuffleignoredsounds"),
                tooltip: TextManager.Get("spw_hydrophonemuffleignoredsoundstooltip"),
                getter: config => config.HydrophoneMuffleIgnoredSounds,
                setter: newSet => unsavedConfig.HydrophoneMuffleIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_hydrophonevisualignoredsounds"),
                tooltip: TextManager.Get("spw_hydrophonevisualignoredsoundstooltip"),
                getter: config => config.HydrophoneVisualIgnoredSounds,
                setter: newSet => unsavedConfig.HydrophoneVisualIgnoredSounds = newSet,
                itemFormatter: s => s
            );
        }

        private void CreateAmbienceTab()
        {
            var iconRect = new Rectangle(0 * TAB_ICON_SIZE, 2 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Ambience, iconRect);
            var content = GetTabContentFrame(Tab.Ambience);
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_disablewhitenoise"),
                localValue: unsavedConfig.DisableWhiteNoise, 
                serverValue: ConfigManager.ServerConfig?.DisableWhiteNoise ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_disablewhitenoisetooltip"),
                currentValue: unsavedConfig.DisableWhiteNoise,
                setter: v => unsavedConfig.DisableWhiteNoise = v);

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorytrack"));

            Label(settingsFrame, TextManager.Get("spw_waterambienceinvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.WaterAmbienceInVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.WaterAmbienceInVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.WaterAmbienceInVolumeMultiplier,
                setter: v => unsavedConfig.WaterAmbienceInVolumeMultiplier = v,
                TextManager.Get("spw_waterambienceinvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_waterambienceoutvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.WaterAmbienceOutVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.WaterAmbienceOutVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.WaterAmbienceOutVolumeMultiplier,
                setter: v => unsavedConfig.WaterAmbienceOutVolumeMultiplier = v,
                TextManager.Get("spw_waterambienceoutvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_waterambiencemovingvolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.WaterAmbienceMovingVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.WaterAmbienceMovingVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.WaterAmbienceMovingVolumeMultiplier,
                setter: v => unsavedConfig.WaterAmbienceMovingVolumeMultiplier = v,
                TextManager.Get("spw_waterambiencemovingvolumetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryenvironment"));

            Label(settingsFrame, TextManager.Get("spw_unsubmergednosuitwaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnsubmergedNoSuitWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnsubmergedNoSuitWaterAmbienceVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.UnsubmergedNoSuitWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.UnsubmergedNoSuitWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_unsubmergednosuitwaterambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_unsubmergedsuitwaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnsubmergedSuitWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnsubmergedSuitWaterAmbienceVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.UnsubmergedSuitWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.UnsubmergedSuitWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_unsubmergedwaterambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_submergednosuitwaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SubmergedNoSuitWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SubmergedNoSuitWaterAmbienceVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.SubmergedNoSuitWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.SubmergedNoSuitWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_submergednosuitwaterambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_submergedsuitwaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SubmergedSuitWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SubmergedSuitWaterAmbienceVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.SubmergedSuitWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.SubmergedSuitWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_submergedsuitwaterambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_spectatingwaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SpectatingWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SpectatingWaterAmbienceVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.SpectatingWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.SpectatingWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_spectatingwaterambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_waterambiencetransitionspeed"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.WaterAmbienceTransitionSpeedMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.WaterAmbienceTransitionSpeedMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.WaterAmbienceTransitionSpeedMultiplier,
                setter: v => unsavedConfig.WaterAmbienceTransitionSpeedMultiplier = v,
                TextManager.Get("spw_waterambiencetransitionspeedtooltip")
            );
        }

        private void CreatePitchTab()
        {
            var iconRect = new Rectangle(1 * TAB_ICON_SIZE, 2 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Pitch, iconRect);
            var content = GetTabContentFrame(Tab.Pitch);
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_pitchenabled"),
                localValue: unsavedConfig.PitchEnabled, 
                serverValue: ConfigManager.ServerConfig?.PitchEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_pitchenabledtooltip"),
                currentValue: unsavedConfig.PitchEnabled,
                setter: v => unsavedConfig.PitchEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_pitchwithdistance"),
                localValue: unsavedConfig.PitchWithDistance, 
                serverValue: ConfigManager.ServerConfig?.PitchWithDistance ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_pitchwithdistancetooltip"),
                currentValue: unsavedConfig.PitchWithDistance,
                setter: v => unsavedConfig.PitchWithDistance = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_pitchstatuseffectsounds"),
                localValue: unsavedConfig.PitchStatusEffectSounds,
                serverValue: ConfigManager.ServerConfig?.PitchStatusEffectSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_pitchstatuseffectsoundstooltip"),
                currentValue: unsavedConfig.PitchStatusEffectSounds,
                setter: v => unsavedConfig.PitchStatusEffectSounds = v);


            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_divingsuitpitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DivingSuitPitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DivingSuitPitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.DivingSuitPitchMultiplier,
                setter: v => unsavedConfig.DivingSuitPitchMultiplier = v,
                TextManager.Get("spw_divingsuitpitchtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_submergedpitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SubmergedPitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SubmergedPitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.SubmergedPitchMultiplier,
                setter: v => unsavedConfig.SubmergedPitchMultiplier = v,
                TextManager.Get("spw_submergedpitchtooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_muffledsoundpitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MuffledSoundPitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MuffledSoundPitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.MuffledSoundPitchMultiplier,
                setter: v => unsavedConfig.MuffledSoundPitchMultiplier = v,
                TextManager.Get("spw_muffledsoundpitchtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_unmuffledsoundpitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnmuffledSoundPitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnmuffledSoundPitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.UnmuffledSoundPitchMultiplier,
                setter: v => unsavedConfig.UnmuffledSoundPitchMultiplier = v,
                TextManager.Get("spw_unmuffledsoundpitchtooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_muffledloopingpitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MuffledLoopingPitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MuffledLoopingPitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.MuffledLoopingPitchMultiplier,
                setter: v => unsavedConfig.MuffledLoopingPitchMultiplier = v,
                TextManager.Get("spw_muffledloopingpitchtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_unmuffledloopingpitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnmuffledLoopingPitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnmuffledLoopingPitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.UnmuffledLoopingPitchMultiplier,
                setter: v => unsavedConfig.UnmuffledLoopingPitchMultiplier = v,
                TextManager.Get("spw_unmuffledloopingpitchtooltip")
            );

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_muffledvoicepitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MuffledVoicePitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MuffledVoicePitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.MuffledVoicePitchMultiplier,
                setter: v => unsavedConfig.MuffledVoicePitchMultiplier = v,
                TextManager.Get("spw_muffledvoicepitchtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_unmuffledvoicepitch"));
            Slider(settingsFrame, (0.25f, 4), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnmuffledVoicePitchMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnmuffledVoicePitchMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.UnmuffledVoicePitchMultiplier,
                setter: v => unsavedConfig.UnmuffledVoicePitchMultiplier = v,
                TextManager.Get("spw_unmuffledvoicepitchtooltip")
            );
        }

        private void CreateAdvancedTab()
        {
            var iconRect = new Rectangle(2 * TAB_ICON_SIZE, 2 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Advanced, iconRect);
            var content = GetTabContentFrame(Tab.Advanced);
            GUIListBox settingsList = NewList(content);
            GUIFrame settingsFrame = settingsList.Content;

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_debugperformance"),
                localValue: unsavedConfig.ShowPerformance, 
                serverValue: ConfigManager.ServerConfig?.ShowPerformance ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_debugperformancetooltip"),
                currentValue: unsavedConfig.ShowPerformance,
                setter: v => unsavedConfig.ShowPerformance = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_debugplayingsounds"),
                localValue: unsavedConfig.ShowPlayingSounds, 
                serverValue: ConfigManager.ServerConfig?.ShowPlayingSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_debugplayingsoundstooltip"),
                currentValue: unsavedConfig.ShowPlayingSounds,
                setter: v => unsavedConfig.ShowPlayingSounds = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_debugchannelinfo"),
                localValue: unsavedConfig.ShowChannelInfo, 
                serverValue: ConfigManager.ServerConfig?.ShowChannelInfo ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_debugchannelinfotooltip"),
                currentValue: unsavedConfig.ShowChannelInfo,
                setter: v => unsavedConfig.ShowChannelInfo = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hidesettings"),
                localValue: unsavedConfig.HideSettingsButton, 
                serverValue: ConfigManager.ServerConfig?.HideSettingsButton ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hidesettingstooltip"),
                currentValue: unsavedConfig.HideSettingsButton,
                setter: v => unsavedConfig.HideSettingsButton = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_remembermenutabandscroll"),
                localValue: unsavedConfig.RememberMenuTabAndScroll, 
                serverValue: ConfigManager.ServerConfig?.RememberMenuTabAndScroll ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_remembermenutabandscrolltooltip"),
                currentValue: unsavedConfig.RememberMenuTabAndScroll,
                setter: v => unsavedConfig.RememberMenuTabAndScroll = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_maxsourcecount"));
            Slider(settingsFrame, (1, 240), 1,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MaxSourceCount ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MaxSourceCount,
                    vanillaValue: SoundManager.SourceCount),
                currentValue: unsavedConfig.MaxSourceCount,
                setter: v => unsavedConfig.MaxSourceCount = (int)v,
                TextManager.Get("spw_maxsourcecounttooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_maxsimultaneousinstances"));
            Slider(settingsFrame, (1, 120), 1,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MaxSimultaneousInstances ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MaxSimultaneousInstances,
                    vanillaValue: 5),
                currentValue: unsavedConfig.MaxSimultaneousInstances,
                setter: v => unsavedConfig.MaxSimultaneousInstances = (int)v,
                TextManager.Get("spw_maxsimultaneousinstancestooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryintervals"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_updatenonloopingsounds"),
                localValue: unsavedConfig.UpdateNonLoopingSounds, 
                serverValue: ConfigManager.ServerConfig?.UpdateNonLoopingSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_updatenonloopingsoundstooltip"),
                currentValue: unsavedConfig.UpdateNonLoopingSounds,
                setter: v => unsavedConfig.UpdateNonLoopingSounds = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_nonloopingsoundmuffleupdateinterval"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.NonLoopingSoundMuffleUpdateInterval ?? default,
                    formatter: SecondsOneTick),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.NonLoopingSoundMuffleUpdateInterval,
                    vanillaValue: null),
                currentValue: unsavedConfig.NonLoopingSoundMuffleUpdateInterval,
                setter: v => unsavedConfig.NonLoopingSoundMuffleUpdateInterval = v,
                TextManager.Get("spw_nonloopingsoundmuffleupdateintervaltooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_openaleffectsupdateinterval"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.OpenALEffectsUpdateInterval ?? default,
                    formatter: SecondsOneTick),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.OpenALEffectsUpdateInterval,
                    vanillaValue: null),
                currentValue: unsavedConfig.OpenALEffectsUpdateInterval,
                setter: v => unsavedConfig.OpenALEffectsUpdateInterval = v,
                TextManager.Get("spw_openaleffectsupdateintervaltooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_voicemuffleupdateinterval"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceMuffleUpdateInterval ?? default,
                    formatter: SecondsOneTick),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceMuffleUpdateInterval,
                    vanillaValue: null),
                currentValue: unsavedConfig.VoiceMuffleUpdateInterval,
                setter: v => unsavedConfig.VoiceMuffleUpdateInterval = v,
                TextManager.Get("spw_voicemuffleupdateintervaltooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_componentmuffleupdateinterval"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ComponentMuffleUpdateInterval ?? default,
                    formatter: SecondsOneTick),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ComponentMuffleUpdateInterval,
                    vanillaValue: 0.2f),
                currentValue: unsavedConfig.ComponentMuffleUpdateInterval,
                setter: v => unsavedConfig.ComponentMuffleUpdateInterval = v,
                TextManager.Get("spw_componentmuffleupdateintervaltooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_statuseffectmuffleupdateinterval"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.StatusEffectMuffleUpdateInterval ?? default,
                    formatter: SecondsOneTick),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StatusEffectMuffleUpdateInterval,
                    vanillaValue: 0.2f),
                currentValue: unsavedConfig.StatusEffectMuffleUpdateInterval,
                setter: v => unsavedConfig.StatusEffectMuffleUpdateInterval = v,
                TextManager.Get("spw_statuseffectmuffleupdateintervaltooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorytransitions"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_disablevanillafadeout"),
                localValue: unsavedConfig.DisableVanillaFadeOutAndDispose, 
                serverValue: ConfigManager.ServerConfig?.DisableVanillaFadeOutAndDispose ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_disablevanillafadeouttooltip"),
                currentValue: unsavedConfig.DisableVanillaFadeOutAndDispose,
                setter: v => unsavedConfig.DisableVanillaFadeOutAndDispose = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_gaintransitionfactor"));
            Slider(settingsFrame, (0, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.GainTransitionFactor ?? default,
                    formatter: RawValueZeroInstant),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.GainTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.GainTransitionFactor,
                setter: v => unsavedConfig.GainTransitionFactor = v,
                TextManager.Get("spw_gaintransitionfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_pitchtransitionfactor"));
            Slider(settingsFrame, (0, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.PitchTransitionFactor ?? default,
                    formatter: RawValueZeroInstant),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.PitchTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.PitchTransitionFactor,
                setter: v => unsavedConfig.PitchTransitionFactor = v,
                TextManager.Get("spw_pitchtransitionfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_airreverbgaintransitionfactor"));
            Slider(settingsFrame, (0, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.AirReverbGainTransitionFactor ?? default,
                    formatter: RawValueZeroInstant),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.AirReverbGainTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.AirReverbGainTransitionFactor,
                setter: v => unsavedConfig.AirReverbGainTransitionFactor = v,
                TextManager.Get("spw_airreverbgaintransitionfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonereverbgaintransitionfactor"));
            Slider(settingsFrame, (0, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneReverbGainTransitionFactor ?? default,
                    formatter: RawValueZeroInstant),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneReverbGainTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneReverbGainTransitionFactor,
                setter: v => unsavedConfig.HydrophoneReverbGainTransitionFactor = v,
                TextManager.Get("spw_hydrophonereverbgaintransitionfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicmuffletransitionfactor"));
            Slider(settingsFrame, (0, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicMuffleTransitionFactor ?? default,
                    formatter: RawValueZeroInstant),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicMuffleTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicMuffleTransitionFactor,
                setter: v => unsavedConfig.DynamicMuffleTransitionFactor = v,
                TextManager.Get("spw_dynamicmuffletransitionfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicmuffletransitionfactorflowfire"));
            Slider(settingsFrame, (0, 10), 0.1f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicMuffleFlowFireTransitionFactor ?? default,
                    formatter: RawValueZeroInstant),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicMuffleFlowFireTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicMuffleFlowFireTransitionFactor,
                setter: v => unsavedConfig.DynamicMuffleFlowFireTransitionFactor = v,
                TextManager.Get("spw_dynamicmuffletransitionfactorflowfiretooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categorypathfinding"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_traversewaterducts"),
                localValue: unsavedConfig.TraverseWaterDucts, 
                serverValue: ConfigManager.ServerConfig?.TraverseWaterDucts ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_traversewaterductstooltip"),
                currentValue: unsavedConfig.TraverseWaterDucts,
                setter: v => unsavedConfig.TraverseWaterDucts = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_flowsoundstraversewaterducts"),
                localValue: unsavedConfig.FlowSoundsTraverseWaterDucts, 
                serverValue: ConfigManager.ServerConfig?.FlowSoundsTraverseWaterDucts ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_flowsoundstraversewaterductstooltip"),
                currentValue: unsavedConfig.FlowSoundsTraverseWaterDucts,
                setter: v => unsavedConfig.FlowSoundsTraverseWaterDucts = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_opendoorthreshold"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.OpenDoorThreshold ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.OpenDoorThreshold,
                    vanillaValue: 0.1f), // Source: https://github.com/FakeFishGames/Barotrauma/blob/567cae1b190e4aa80ebd7f17bda55e6752c36182/Barotrauma/BarotraumaShared/SharedSource/Map/Hull.cs#L1160C25-L1160C62
                currentValue: unsavedConfig.OpenDoorThreshold,
                setter: v => unsavedConfig.OpenDoorThreshold = v,
                TextManager.Get("spw_opendoorthresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_openwallthreshold"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.OpenWallThreshold ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.OpenWallThreshold,
                    vanillaValue: 0.0f), // Source: https://github.com/FakeFishGames/Barotrauma/blob/567cae1b190e4aa80ebd7f17bda55e6752c36182/Barotrauma/BarotraumaShared/SharedSource/Map/Hull.cs#L1167C17-L1167C41
                currentValue: unsavedConfig.OpenWallThreshold,
                setter: v => unsavedConfig.OpenWallThreshold = v,
                TextManager.Get("spw_openwallthresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_soundpropagationrange"));
            Slider(settingsFrame, (0, 10000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SoundPropagationRange ?? default,
                    formatter: Centimeters),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SoundPropagationRange,
                    vanillaValue: null),
                currentValue: unsavedConfig.SoundPropagationRange,
                setter: v => unsavedConfig.SoundPropagationRange = (int)v,
                TextManager.Get("spw_soundpropagationrangetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_categoryrules"));

            foreach (var kvp in ConfigManager.ModdedCustomSounds)
            {
                ContentPackage mod = kvp.Key;
                HashSet<CustomSound> customSounds = kvp.Value;
                CreateModdedCustomSoundsJsonTextBox(
                    parentListBox: settingsList,
                    labelText: (LocalizedString)($"{mod.Name} " + TextManager.Get("spw_customsounds").Value),
                    tooltip: TextManager.GetWithVariable("spw_moddedcustomsoundstooltip", "[modname]", mod.Name),
                    getter: customSounds => customSounds[mod],
                    setter: newSet => unsavedModdedCustomSounds[mod] = newSet
                );

                Spacer(settingsFrame);
            }

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_customsounds"),
                tooltip: TextManager.Get("spw_customsoundstooltip"),
                getter: config => config.CustomSounds,
                setter: newSet => unsavedConfig.CustomSounds = newSet,
                itemFormatter: cs => cs.Keyword
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_ignoredsounds"),
                tooltip: TextManager.Get("spw_ignoredsoundstooltip"),
                getter: config => config.IgnoredSounds,
                setter: newSet => unsavedConfig.IgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_surfaceignoredsounds"),
                tooltip: TextManager.Get("spw_surfaceignoredsoundstooltip"),
                getter: config => config.SurfaceIgnoredSounds,
                setter: newSet => unsavedConfig.SurfaceIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_submersionignoredsounds"),
                tooltip: TextManager.Get("spw_submersionignoredsoundstooltip"),
                getter: config => config.SubmersionIgnoredSounds,
                setter: newSet => unsavedConfig.SubmersionIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_wallpropagatingsounds"),
                tooltip: TextManager.Get("spw_wallpropagatingsoundstooltip"),
                getter: config => config.PropagatingSounds,
                setter: newSet => unsavedConfig.PropagatingSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_barrierignoredsounds"),
                tooltip: TextManager.Get("spw_barrierignoredsoundstooltip"),
                getter: config => config.BarrierIgnoredSounds,
                setter: newSet => unsavedConfig.BarrierIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_pitchignoredsounds"),
                tooltip: TextManager.Get("spw_pitchignoredsoundstooltip"),
                getter: config => config.PitchIgnoredSounds,
                setter: newSet => unsavedConfig.PitchIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_reverbforcedsounds"),
                tooltip: TextManager.Get("spw_reverbforcedsoundstooltip"),
                getter: config => config.ReverbForcedSounds,
                setter: newSet => unsavedConfig.ReverbForcedSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_airreverbignoredsounds"),
                tooltip: TextManager.Get("spw_airreverbignoredsoundstooltip"),
                getter: config => config.AirReverbIgnoredSounds,
                setter: newSet => unsavedConfig.AirReverbIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_waterreverbignoredsounds"),
                tooltip: TextManager.Get("spw_waterreverbignoredsoundstooltip"),
                getter: config => config.WaterReverbIgnoredSounds,
                setter: newSet => unsavedConfig.WaterReverbIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_xmlignoredsounds"),
                tooltip: TextManager.Get("spw_xmlignoredsoundstooltip"),
                getter: config => config.XMLIgnoredSounds,
                setter: newSet => unsavedConfig.XMLIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_lowpassignoredsounds"),
                tooltip: TextManager.Get("spw_lowpassignoredsoundstooltip"),
                getter: config => config.LowpassIgnoredSounds,
                setter: newSet => unsavedConfig.LowpassIgnoredSounds = newSet,
                itemFormatter: s => s
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_containerignoredsounds"),
                tooltip: TextManager.Get("spw_containerignoredsoundstooltip"),
                getter: config => config.ContainerIgnoredSounds,
                setter: newSet => unsavedConfig.ContainerIgnoredSounds = newSet,
                itemFormatter: s => s
            );
        }

        private void CreateBottomButtons(GUILayoutGroup bottom)
        {
            new GUIButton(new RectTransform(new Vector2(0.33f, 1.0f), bottom.RectTransform), TextManager.Get("Cancel"))
            {
                OnClicked = (btn, obj) =>
                {
                    Close();
                    return false;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.33f, 1.0f), bottom.RectTransform), TextManager.Get("spw_resetall"))
            {
                OnClicked = (btn, obj) =>
                {
                    Create(true);
                    return false;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.33f, 1.0f), bottom.RectTransform), TextManager.Get("applysettingsbutton"))
            {
                OnClicked = (btn, obj) =>
                {
                    ApplyChanges();
                    return false;
                }
            };
        }
        #endregion

        private void ApplyChanges()
        {
            Config oldConfig = ConfigManager.Config;
            ConfigManager.LocalConfig = ConfigManager.CloneConfig(unsavedConfig);
            ConfigManager.SaveConfig(ConfigManager.LocalConfig);

            // Update and apply changes to other mod's custom sound lists (local only).
            if (SoundInfoManager.ShouldUpdateSoundInfo(unsavedModdedCustomSounds, ConfigManager.ModdedCustomSounds))
            {
                ConfigManager.ModdedCustomSounds = unsavedModdedCustomSounds.ToDictionary(
                entry => entry.Key,
                entry => new HashSet<CustomSound>(entry.Value.Select(sound => JsonSerializer.Deserialize<CustomSound>(JsonSerializer.Serialize(sound)))));
                
                SoundInfoManager.UpdateSoundInfoMap();
            }

            ConfigManager.LocalConfig.EavesdroppingKeyOrMouse = ConfigManager.LocalConfig.ParseEavesdroppingBind(); // todo maybe unnecessary

            // Compare json strings and only upload if there are changes.
            if (JsonSerializer.Serialize(ConfigManager.LocalConfig) != JsonSerializer.Serialize(oldConfig) && 
                GameMain.IsMultiplayer && (GameMain.Client.IsServerOwner || GameMain.Client.HasPermission(ClientPermissions.Ban)))
            {
                ConfigManager.UploadClientConfigToServer(manualUpdate: true);
            }

            if (!GameMain.IsMultiplayer || ConfigManager.ServerConfig == null)
            {
                ConfigManager.UpdateConfig(unsavedConfig, oldConfig);
            }
        }
    }
}