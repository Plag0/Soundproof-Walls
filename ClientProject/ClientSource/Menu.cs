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
        private static float lastScroll = 0;

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

        private Config unsavedConfig;
        private Dictionary<ContentPackage, HashSet<CustomSound>> unsavedModdedCustomSounds;

        private readonly GUIFrame mainFrame;
        private readonly GUILayoutGroup tabber;
        private readonly GUIFrame contentFrame;
        private readonly Dictionary<Tab, (GUIButton Button, GUIFrame Content)> tabContents;

        private Action<KeyOrMouse>? currentKeybindSetter;
        private GUIButton? selectedKeybindButton;
        private bool keybindBoxSelectedThisFrame;

        public static void Create(bool startAtDefaultValues = false)
        {
            Instance?.Close();
            Instance = new Menu(startAtDefaultValues);
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

            var popupFrame = new GUIFrame(new RectTransform(new Vector2(0.55f, 0.65f), GUI.PauseMenu.RectTransform, Anchor.Center));
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
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerSection.RectTransform), TextManager.Get("spw_popupheader"), textAlignment: Alignment.Center, font: GUIStyle.LargeFont, textColor: PopupPrimaryColor);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerSection.RectTransform), TextManager.Get("spw_popupheadersubtext"), textAlignment: Alignment.Center, font: GUIStyle.Font, wrap: true, textColor: PopupPrimaryColor * 0.8f);

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
                    string buttonText = TextManager.Get("spw_settings").Value;
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

        private Menu(bool startAtDefaultValues = false)
        {
            if (GUI.PauseMenu == null) { return; }

            unsavedConfig = ConfigManager.CloneConfig(startAtDefaultValues ? Menu.defaultConfig : ConfigManager.LocalConfig);
            unsavedConfig.EavesdroppingKeyOrMouse = unsavedConfig.ParseEavesdroppingBind();

            // Clone the modded custom sounds list.
            var targetDict = startAtDefaultValues ? ConfigManager.DefaultModdedCustomSounds : ConfigManager.ModdedCustomSounds;
            unsavedModdedCustomSounds = targetDict.ToDictionary(
                entry => entry.Key, 
                entry => new HashSet<CustomSound>(entry.Value.Select(sound => JsonSerializer.Deserialize<CustomSound>(JsonSerializer.Serialize(sound)))));

            mainFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.6f), GUI.PauseMenu.RectTransform, Anchor.Center));

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
            
            var iconContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), leftColumn.RectTransform, scaleBasis: ScaleBasis.BothWidth), style: "InnerFrame");
            var iconSprite = new Sprite(Path.Combine(Plugin.ModPath, "Content/UI/SoundproofWallsIcon.jpg"), sourceRectangle: null);
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

            if (!ConfigManager.LocalConfig.RememberMenuTabAndScroll)
            { lastTab = Tab.General; lastScroll = 0; }

            SelectTab(lastTab, lastScroll);
            mainLayout.Recalculate();

            mainFrame.Flash(MenuFlashColor, flashDuration: 0.45f);
        }

        public void Close()
        {
            // Save scroll position.
            foreach (var child in tabContents[CurrentTab].Content.Children)
            {
                var list = child as GUIListBox;
                if (list != null)
                {
                    lastScroll = list.BarScroll;
                    break;
                }
            }

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
                    SelectTab(tab);
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
            string text = $"Soundproof Walls v{ModStateManager.State.Version} by Plag\n\n";
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
        private string RawValuePrecise(float v) => $"{MathF.Round(v, 3)}";
        private string Percentage(float v)
        {
            string str = $"{MathF.Round(v, 2).ToString("P0", CultureInfo.CurrentUICulture)}";
            if (CultureInfo.CurrentUICulture == CultureInfo.InvariantCulture) { str = str.Replace(" %", "%"); }
            return str;
        }
        private string Hertz(float v) => $"{MathF.Round(v).ToString("N0", CultureInfo.CurrentUICulture)} Hz";
        private string Seconds(float v) => $"{MathF.Round(v, 2)} {TextManager.Get("spw_seconds")}"; // Custom localization for seconds, e.g., English is 15s but Russian is 15с.
        private string PlusSeconds(float v) => $"+{MathF.Round(v, 2)} {TextManager.Get("spw_seconds")}";
        private string Centimeters(float v) => $"{MathF.Round(v).ToString("N0", CultureInfo.CurrentUICulture)} cm";
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
            if (GameMain.IsMultiplayer && ConfigManager.ServerConfig != null && ConfigManager.ServerConfigUploader != GameMain.Client?.MyClient)
            {
                localFormatted += $" ({formatter(serverValue)})";
            }

            return localFormatted;
        }

        private Color GetSettingColor<T>(T localValue, GUIComponentStyle componentStyle, object? defaultValue = null, object? vanillaValue = null)
        {
            Color color = componentStyle.TextColor;
            if (defaultValue != null && ValuesEqual(localValue, defaultValue))
            {
                color = MenuDefaultValueColor;
            }
            else if (vanillaValue != null && ValuesEqual(localValue, vanillaValue))
            {
                color = MenuVanillaValueColor;
            }
            return color;
        }

        private bool ValuesEqual<T>(T localValue, object otherValue)
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
                    double localDouble = Convert.ToDouble(localValue);
                    double otherDouble = Convert.ToDouble(otherValue);
                    return Math.Abs(localDouble - otherDouble) < double.Epsilon;
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

        private string FormatTextBoxLabel(LocalizedString label, bool serverValue, Func<bool, string> formatter)
        {
            string text = label.Value;
            if (GameMain.IsMultiplayer && ConfigManager.ServerConfig != null)
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
                    $"{formatter(currentLocal)}{(ConfigManager.ServerConfig != null ? $" ({formatter(serverValue)})" : "")}";
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

            // Delayed update to recalculate size. Without this, the text box is cut off at the top and bottom.
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

            var differences = localSet.Except(serverSet).Union(serverSet.Except(localSet)).ToList();

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
                // Use the itemFormatter to display the difference
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
                serverValue: ConfigManager.ServerConfig?.Enabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_enablemodtooltip"), 
                currentValue: unsavedConfig.Enabled,
                setter: v => unsavedConfig.Enabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_syncsettings"),
                serverValue: ConfigManager.ServerConfig?.SyncSettings ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_syncsettingstooltip"),
                currentValue: unsavedConfig.SyncSettings,
                setter: v => unsavedConfig.SyncSettings = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_focustargetaudio"),
                serverValue: ConfigManager.ServerConfig?.FocusTargetAudio ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_focustargetaudiotooltip"),
                currentValue: unsavedConfig.FocusTargetAudio,
                setter: v => unsavedConfig.FocusTargetAudio = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_attenuatewithapproximatedistance"),
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

            Label(settingsFrame, TextManager.Get("spw_soundrange"));
            Slider(settingsFrame, (0, 5), 0.01f,
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
            Slider(settingsFrame, (0, 5), 0.01f,
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
        }

        private void CreateDynamicFxTab()
        {
            var iconRect = new Rectangle(0 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.DynamicFx, iconRect);
            var content = GetTabContentFrame(Tab.DynamicFx);
            GUIFrame settingsFrame = NewListContent(content);

            SpacerLabel(settingsFrame, TextManager.Get("spw_dynamiccategorymuffling"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_occludesounds"),
                serverValue: ConfigManager.ServerConfig?.OccludeSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_occludesoundstooltip"),
                currentValue: unsavedConfig.OccludeSounds,
                setter: v => unsavedConfig.OccludeSounds = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_autoattenuatemuffledsounds"),
                serverValue: ConfigManager.ServerConfig?.AutoAttenuateMuffledSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_autoattenuatemuffledsoundstooltip"),
                currentValue: unsavedConfig.AutoAttenuateMuffledSounds,
                setter: v => unsavedConfig.AutoAttenuateMuffledSounds = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_dynamicmufflestrengthmaster"));
            PowerSlider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicMuffleStrengthMultiplier ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicMuffleStrengthMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicMuffleStrengthMultiplier,
                setter: v => unsavedConfig.DynamicMuffleStrengthMultiplier = v,
                tooltip: TextManager.Get("spw_dynamicmufflestrengthmastertooltip"),
                curveFactor: 0.4f
            );

            Label(settingsFrame, TextManager.Get("spw_maxocclusions"));
            Slider(settingsFrame, (0, 5), 1,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MaxOcclusions ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MaxOcclusions,
                    vanillaValue: null),
                currentValue: unsavedConfig.MaxOcclusions,
                setter: v => unsavedConfig.MaxOcclusions = (int)v,
                TextManager.Get("spw_maxocclusionstooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicmuffletransitionfactor"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicMuffleTransitionFactor ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicMuffleTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicMuffleTransitionFactor,
                setter: v => unsavedConfig.DynamicMuffleTransitionFactor = v,
                TextManager.Get("spw_dynamicmuffletransitionfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_dynamicmuffletransitionfactorflowfire"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.DynamicMuffleFlowFireTransitionFactor ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.DynamicMuffleFlowFireTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.DynamicMuffleFlowFireTransitionFactor,
                setter: v => unsavedConfig.DynamicMuffleFlowFireTransitionFactor = v,
                TextManager.Get("spw_dynamicmuffletransitionfactorflowfiretooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_dynamiccategoryreverb"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverb"),
                serverValue: ConfigManager.ServerConfig?.DynamicReverbEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbtooltip"),
                currentValue: unsavedConfig.DynamicReverbEnabled,
                setter: v => unsavedConfig.DynamicReverbEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverbwatersubtractsarea"),
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
            Slider(settingsFrame, (0, 5), 0.01f,
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
            Slider(settingsFrame, (0, 5), 0.01f,
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_dynamiccategoryloudsounddistortion"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_loudsounddistortion"),
                serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_loudsounddistortiontooltip"),
                currentValue: unsavedConfig.LoudSoundDistortionEnabled,
                setter: v => unsavedConfig.LoudSoundDistortionEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetgain"));
            Slider(settingsFrame, (0.01f, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionTargetGain ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionTargetGain,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionTargetGain,
                setter: v => unsavedConfig.LoudSoundDistortionTargetGain = v,
                TextManager.Get("spw_loudsounddistortiontargetgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetedge"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionTargetEdge ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionTargetEdge,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionTargetEdge,
                setter: v => unsavedConfig.LoudSoundDistortionTargetEdge = v,
                TextManager.Get("spw_loudsounddistortiontargetedgetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortiontargetfrequency"));
            PowerSlider(settingsFrame, (80, 24000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionTargetFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionTargetFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionTargetFrequency,
                setter: v => unsavedConfig.LoudSoundDistortionTargetFrequency = (int)v,
                tooltip: TextManager.Get("spw_loudsounddistortiontargetfrequencytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_loudsounddistortionlowpassfrequency"));
            PowerSlider(settingsFrame, (80, 24000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.LoudSoundDistortionLowpassFrequency ?? default,
                    formatter: Hertz),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.LoudSoundDistortionLowpassFrequency,
                    vanillaValue: null),
                currentValue: unsavedConfig.LoudSoundDistortionLowpassFrequency,
                setter: v => unsavedConfig.LoudSoundDistortionLowpassFrequency = (int)v,
                tooltip: TextManager.Get("spw_loudsounddistortionlowpassfrequencytooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_dynamiccategoryexperimental"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_removeunusedbuffers"),
                serverValue: ConfigManager.ServerConfig?.RemoveUnusedBuffers ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_removeunusedbufferstooltip"),
                currentValue: unsavedConfig.RemoveUnusedBuffers,
                setter: v => unsavedConfig.RemoveUnusedBuffers = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_simulatesounddirection"),
                serverValue: ConfigManager.ServerConfig?.RealSoundDirectionsEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_simulatesounddirectiontooltip"),
                currentValue: unsavedConfig.RealSoundDirectionsEnabled,
                setter: v => unsavedConfig.RealSoundDirectionsEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_maxsounddirections"));
            Slider(settingsFrame, (1, 3), 1,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.RealSoundDirectionsMax ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RealSoundDirectionsMax,
                    vanillaValue: null),
                currentValue: unsavedConfig.RealSoundDirectionsMax,
                setter: v => unsavedConfig.RealSoundDirectionsMax = (int)v,
                TextManager.Get("spw_maxsounddirectionstooltip")
            );
        }

        private void CreateStaticFxTab()
        {
            var iconRect = new Rectangle(1 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.StaticFx, iconRect);
            var content = GetTabContentFrame(Tab.StaticFx);
            GUIFrame settingsFrame = NewListContent(content);

            SpacerLabel(settingsFrame, TextManager.Get("spw_staticcategorymuffle"));

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

            SpacerLabel(settingsFrame, TextManager.Get("spw_staticcategoryreverb"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_staticreverb"),
                serverValue: ConfigManager.ServerConfig?.StaticReverbEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_staticreverbtooltip"),
                currentValue: unsavedConfig.StaticReverbEnabled,
                setter: v => unsavedConfig.StaticReverbEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_staticreverbalwaysloudsounds"),
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
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_talkingragdolls"),
                serverValue: ConfigManager.ServerConfig?.TalkingRagdolls ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_talkingragdollstooltip"),
                currentValue: unsavedConfig.TalkingRagdolls,
                setter: v => unsavedConfig.TalkingRagdolls = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverblocal"),
                serverValue: ConfigManager.ServerConfig?.VoiceLocalReverb ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverblocaltooltip"),
                currentValue: unsavedConfig.VoiceLocalReverb,
                setter: v => unsavedConfig.VoiceLocalReverb = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverbradio"),
                serverValue: ConfigManager.ServerConfig?.VoiceRadioReverb ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbradiotooltip"),
                currentValue: unsavedConfig.VoiceRadioReverb,
                setter: v => unsavedConfig.VoiceRadioReverb = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_whispermode"),
                serverValue: ConfigManager.ServerConfig?.WhisperMode ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_whispermodetooltip"),
                currentValue: unsavedConfig.WhisperMode,
                setter: v => unsavedConfig.WhisperMode = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_voicedynamicmufflemultiplier"));
            Slider(settingsFrame, (0, 3), 0.01f,
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
                TextManager.Get("spw_voicedynamicmufflemultipliertooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_voicelowpassfrequency"));
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
                TextManager.Get("spw_voicelowpassfrequencytooltip"),
                bannedValue: SoundPlayer.MuffleFilterFrequency
            );

            Label(settingsFrame, TextManager.Get("spw_voicerange"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceLocalRangeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceLocalRangeMultiplier,
                    vanillaValue: 1.0f),
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
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.VoiceRadioRangeMultiplier,
                setter: v => unsavedConfig.VoiceRadioRangeMultiplier = v,
                TextManager.Get("spw_radiorangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_voicevolume"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceLocalVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceLocalVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.VoiceLocalVolumeMultiplier,
                setter: v => unsavedConfig.VoiceLocalVolumeMultiplier = v,
                TextManager.Get("spw_voicevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiovolume"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.VoiceRadioVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.VoiceRadioVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.VoiceRadioVolumeMultiplier,
                setter: v => unsavedConfig.VoiceRadioVolumeMultiplier = v,
                TextManager.Get("spw_radiovolumetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_voicecategorybubbles"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_drowningbubbles"),
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
                    vanillaValue: 1.0f),
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
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.DrowningBubblesRadioVolumeMultiplier,
                setter: v => unsavedConfig.DrowningBubblesRadioVolumeMultiplier = v,
                TextManager.Get("spw_drowningbubblesradiovolumetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_voicecategorycustomfilter"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_radiocustomfilter"),
                serverValue: ConfigManager.ServerConfig?.RadioCustomFilterEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_radiocustomfiltertooltip"),
                currentValue: unsavedConfig.RadioCustomFilterEnabled,
                setter: v => unsavedConfig.RadioCustomFilterEnabled = v);

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
            Slider(settingsFrame, (0.1f, 10), 0.01f,
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
            Slider(settingsFrame, (0, 10), 0.01f,
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
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.RadioCompressionThreshold,
                    vanillaValue: null),
                currentValue: unsavedConfig.RadioCompressionThreshold,
                setter: v => unsavedConfig.RadioCompressionThreshold = v,
                TextManager.Get("spw_radiocompressionthresholdtooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_radiocompressionratio"));
            Slider(settingsFrame, (0, 10), 0.01f,
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
        }

        private void CreateMuffleTab()
        {
            var iconRect = new Rectangle(2 * TAB_ICON_SIZE, 0 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Muffle, iconRect);
            var content = GetTabContentFrame(Tab.Muffle);
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_muffledivingsuit"),
                serverValue: ConfigManager.ServerConfig?.MuffleDivingSuits ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_muffledivingsuittooltip"),
                currentValue: unsavedConfig.MuffleDivingSuits,
                setter: v => unsavedConfig.MuffleDivingSuits = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflesubmergedplayer"),
                serverValue: ConfigManager.ServerConfig?.MuffleSubmergedPlayer ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflesubmergedplayertooltip"),
                currentValue: unsavedConfig.MuffleSubmergedPlayer,
                setter: v => unsavedConfig.MuffleSubmergedPlayer = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflesubmergedviewtarget"),
                serverValue: ConfigManager.ServerConfig?.MuffleSubmergedViewTarget ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflesubmergedviewtargettooltip"),
                currentValue: unsavedConfig.MuffleSubmergedViewTarget,
                setter: v => unsavedConfig.MuffleSubmergedViewTarget = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflewatersurface"),
                serverValue: ConfigManager.ServerConfig?.MuffleWaterSurface ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflewatersurfacetooltip"),
                currentValue: unsavedConfig.MuffleWaterSurface,
                setter: v => unsavedConfig.MuffleWaterSurface = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_muffleflowfirepath"),
                serverValue: ConfigManager.ServerConfig?.MuffleFlowFireSoundsWithEstimatedPath ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_muffleflowfirepathtooltip"),
                currentValue: unsavedConfig.MuffleFlowFireSoundsWithEstimatedPath,
                setter: v => unsavedConfig.MuffleFlowFireSoundsWithEstimatedPath = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_muffleflowsounds"),
                serverValue: ConfigManager.ServerConfig?.MuffleFlowSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_muffleflowsoundstooltip"),
                currentValue: unsavedConfig.MuffleFlowSounds,
                setter: v => unsavedConfig.MuffleFlowSounds = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_mufflefiresounds"),
                serverValue: ConfigManager.ServerConfig?.MuffleFireSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_mufflefiresoundstooltip"),
                currentValue: unsavedConfig.MuffleFireSounds,
                setter: v => unsavedConfig.MuffleFireSounds = v);

            SpacerLabel(settingsFrame, TextManager.Get("spw_mufflecategoryobstructions"));

            Label(settingsFrame, TextManager.Get("spw_obstructionwatersurface"));
            Slider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWaterSurface ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWaterSurface,
                    vanillaValue: null),
                currentValue: unsavedConfig.ObstructionWaterSurface,
                setter: v => unsavedConfig.ObstructionWaterSurface = v,
                TextManager.Get("spw_obstructionwatersurfacetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionwaterbody"));
            Slider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWaterBody ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWaterBody,
                    vanillaValue: null),
                currentValue: unsavedConfig.ObstructionWaterBody,
                setter: v => unsavedConfig.ObstructionWaterBody = v,
                TextManager.Get("spw_obstructionwaterbodytooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionwallthick"));
            Slider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWallThick ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWallThick,
                    vanillaValue: null),
                currentValue: unsavedConfig.ObstructionWallThick,
                setter: v => unsavedConfig.ObstructionWallThick = v,
                TextManager.Get("spw_obstructionwallthicktooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionwallthin"));
            Slider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionWallThin ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionWallThin,
                    vanillaValue: null),
                currentValue: unsavedConfig.ObstructionWallThin,
                setter: v => unsavedConfig.ObstructionWallThin = v,
                TextManager.Get("spw_obstructionwallthintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_obstructiondoorthick"));
            Slider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionDoorThick ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionDoorThick,
                    vanillaValue: null),
                currentValue: unsavedConfig.ObstructionDoorThick,
                setter: v => unsavedConfig.ObstructionDoorThick = v,
                TextManager.Get("spw_obstructiondoorthicktooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_obstructiondoorthin"));
            Slider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionDoorThin ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionDoorThin,
                    vanillaValue: null),
                currentValue: unsavedConfig.ObstructionDoorThin,
                setter: v => unsavedConfig.ObstructionDoorThin = v,
                TextManager.Get("spw_obstructiondoorthintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_obstructionsuit"));
            Slider(settingsFrame, (0, 1), 0.001f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.ObstructionSuit ?? default,
                    formatter: RawValuePrecise),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.ObstructionSuit,
                    vanillaValue: null),
                currentValue: unsavedConfig.ObstructionSuit,
                setter: v => unsavedConfig.ObstructionSuit = v,
                TextManager.Get("spw_obstructionsuittooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_mufflecategorythresholds"));

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

            SpacerLabel(settingsFrame, TextManager.Get("spw_volumecategorysidechaining"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_sidechaining"),
                serverValue: ConfigManager.ServerConfig?.SidechainingEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_sidechainingtooltip"),
                currentValue: unsavedConfig.SidechainingEnabled,
                setter: v => unsavedConfig.SidechainingEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_sidechainmusic"),
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_volumecategorygain"));

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
        }

        private void CreateEavesdroppingTab()
        {
            var iconRect = new Rectangle(2 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Eavesdropping, iconRect);
            var content = GetTabContentFrame(Tab.Eavesdropping);
            GUIFrame settingsFrame = NewListContent(content);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingenabled"),
                serverValue: ConfigManager.ServerConfig?.EavesdroppingEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingenabledtooltip"),
                currentValue: unsavedConfig.EavesdroppingEnabled,
                setter: v => unsavedConfig.EavesdroppingEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingmuffle"),
                serverValue: ConfigManager.ServerConfig?.EavesdroppingMuffle ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingmuffletooltip"),
                currentValue: unsavedConfig.EavesdroppingMuffle,
                setter: v => unsavedConfig.EavesdroppingMuffle = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingtransition"),
                serverValue: ConfigManager.ServerConfig?.EavesdroppingTransitionEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingtransitiontooltip"),
                currentValue: unsavedConfig.EavesdroppingTransitionEnabled,
                setter: v => unsavedConfig.EavesdroppingTransitionEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingducksradio"),
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
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingSoundVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.EavesdroppingSoundVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.EavesdroppingSoundVolumeMultiplier,
                setter: v => unsavedConfig.EavesdroppingSoundVolumeMultiplier = v,
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_eavesdroppingcategoryvisuals"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_eavesdroppingrevealsall"),
                serverValue: ConfigManager.ServerConfig?.EavesdroppingRevealsAll ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_eavesdroppingrevealsalltooltip"),
                currentValue: unsavedConfig.EavesdroppingRevealsAll,
                setter: v => unsavedConfig.EavesdroppingRevealsAll = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_eavesdroppingspritemaxsize"));
            Slider(settingsFrame, (10, 10000), 10,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.EavesdroppingSpriteMaxSize ?? default,
                    formatter: Centimeters),
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
                    formatter: Percentage),
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
                serverValue: ConfigManager.ServerConfig?.HydrophoneSwitchEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophoneswitchenabledtooltip"),
                currentValue: unsavedConfig.HydrophoneSwitchEnabled,
                setter: v => unsavedConfig.HydrophoneSwitchEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonemovementsounds"),
                serverValue: ConfigManager.ServerConfig?.HydrophoneMovementSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonemovementsoundstooltip"),
                currentValue: unsavedConfig.HydrophoneMovementSounds,
                setter: v => unsavedConfig.HydrophoneMovementSounds = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonehearengine"),
                serverValue: ConfigManager.ServerConfig?.HydrophoneHearEngine ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonehearenginetooltip"),
                currentValue: unsavedConfig.HydrophoneHearEngine,
                setter: v => unsavedConfig.HydrophoneHearEngine = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonehearintostructures"),
                serverValue: ConfigManager.ServerConfig?.HydrophoneHearIntoStructures ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonehearintostructurestooltip"),
                currentValue: unsavedConfig.HydrophoneHearIntoStructures,
                setter: v => unsavedConfig.HydrophoneHearIntoStructures = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonemuffleownsub"),
                serverValue: ConfigManager.ServerConfig?.HydrophoneMuffleOwnSub ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonemuffleownsubtooltip"),
                currentValue: unsavedConfig.HydrophoneMuffleOwnSub,
                setter: v => unsavedConfig.HydrophoneMuffleOwnSub = v);

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

            SpacerLabel(settingsFrame, TextManager.Get("spw_hydrophonecategoryvisuals"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonevisualfeedbackenabled"),
                serverValue: ConfigManager.ServerConfig?.HydrophoneVisualFeedbackEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonevisualfeedbackenabledtooltip"),
                currentValue: unsavedConfig.HydrophoneVisualFeedbackEnabled,
                setter: v => unsavedConfig.HydrophoneVisualFeedbackEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophoneusagedisablessonarblips"),
                serverValue: ConfigManager.ServerConfig?.HydrophoneUsageDisablesSonarBlips ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophoneusagedisablessonarblipstooltip"),
                currentValue: unsavedConfig.HydrophoneUsageDisablesSonarBlips,
                setter: v => unsavedConfig.HydrophoneUsageDisablesSonarBlips = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophoneusagedisablessuboutline"),
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_hydrophonecategorydynamicfx"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
            label: TextManager.Get("spw_hydrophonereverb"),
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
                serverValue: ConfigManager.ServerConfig?.HydrophoneDistortionEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hydrophonedistortiontooltip"),
                currentValue: unsavedConfig.HydrophoneDistortionEnabled,
                setter: v => unsavedConfig.HydrophoneDistortionEnabled = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_hydrophonedistortiontargetgain"));
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
                TextManager.Get("spw_hydrophonedistortiontargetgaintooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonedistortiontargetedge"));
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
                TextManager.Get("spw_hydrophonedistortiontargetedgetooltip")
            );

            Spacer(settingsFrame);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_hydrophonebandpassfilter"),
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_hydrophonecategoryadvanced"));

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
                serverValue: ConfigManager.ServerConfig?.DisableWhiteNoise ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_disablewhitenoisetooltip"),
                currentValue: unsavedConfig.DisableWhiteNoise,
                setter: v => unsavedConfig.DisableWhiteNoise = v);

            SpacerLabel(settingsFrame, TextManager.Get("spw_ambiencecategorytrack"));

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

            SpacerLabel(settingsFrame, TextManager.Get("spw_ambiencecategoryenvironment"));

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
                serverValue: ConfigManager.ServerConfig?.PitchEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_pitchenabledtooltip"),
                currentValue: unsavedConfig.PitchEnabled,
                setter: v => unsavedConfig.PitchEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_pitchwithdistance"),
                serverValue: ConfigManager.ServerConfig?.PitchWithDistance ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_pitchwithdistancetooltip"),
                currentValue: unsavedConfig.PitchWithDistance,
                setter: v => unsavedConfig.PitchWithDistance = v);

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
                label: TextManager.Get("spw_hidesettings"),
                serverValue: ConfigManager.ServerConfig?.HideSettingsButton ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_hidesettingstooltip"),
                currentValue: unsavedConfig.HideSettingsButton,
                setter: v => unsavedConfig.HideSettingsButton = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_remembermenutabandscroll"),
                serverValue: ConfigManager.ServerConfig?.RememberMenuTabAndScroll ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_remembermenutabandscrolltooltip"),
                currentValue: unsavedConfig.RememberMenuTabAndScroll,
                setter: v => unsavedConfig.RememberMenuTabAndScroll = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_debugobstructions"),
                serverValue: ConfigManager.ServerConfig?.DebugObstructions ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_debugobstructionstooltip"),
                currentValue: unsavedConfig.DebugObstructions,
                setter: v => unsavedConfig.DebugObstructions = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_debugplayingsounds"),
                serverValue: ConfigManager.ServerConfig?.DebugPlayingSounds ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_debugplayingsoundstooltip"),
                currentValue: unsavedConfig.DebugPlayingSounds,
                setter: v => unsavedConfig.DebugPlayingSounds = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_maxsourcecount"));
            Slider(settingsFrame, (1, 256), 1,
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
            Slider(settingsFrame, (1, 128), 1,
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_advancedcategoryintervals"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_updatenonloopingsounds"),
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
                    formatter: Seconds),
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
                    formatter: Seconds),
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
                    formatter: Seconds),
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
                    formatter: Seconds),
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
                    formatter: Seconds),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.StatusEffectMuffleUpdateInterval,
                    vanillaValue: 0.2f),
                currentValue: unsavedConfig.StatusEffectMuffleUpdateInterval,
                setter: v => unsavedConfig.StatusEffectMuffleUpdateInterval = v,
                TextManager.Get("spw_statuseffectmuffleupdateintervaltooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_advancedcategorytransitions"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_disablevanillafadeout"),
                serverValue: ConfigManager.ServerConfig?.DisableVanillaFadeOutAndDispose ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_disablevanillafadeouttooltip"),
                currentValue: unsavedConfig.DisableVanillaFadeOutAndDispose,
                setter: v => unsavedConfig.DisableVanillaFadeOutAndDispose = v);

            Spacer(settingsFrame);

            Label(settingsFrame, TextManager.Get("spw_gaintransitionfactor"));
            Slider(settingsFrame, (0, 10), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.GainTransitionFactor ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.GainTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.GainTransitionFactor,
                setter: v => unsavedConfig.GainTransitionFactor = v,
                TextManager.Get("spw_gaintransitionfactortooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_pitchtransitionfactor"));
            Slider(settingsFrame, (0, 10), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.PitchTransitionFactor ?? default,
                    formatter: RawValue),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.PitchTransitionFactor,
                    vanillaValue: null),
                currentValue: unsavedConfig.PitchTransitionFactor,
                setter: v => unsavedConfig.PitchTransitionFactor = v,
                TextManager.Get("spw_pitchtransitionfactortooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_advancedcategoryattenuation"));

            Label(settingsFrame, TextManager.Get("spw_loopingcomponentsoundnearmultiplier"));
            Slider(settingsFrame, (0, 1), 0.01f,
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

            Label(settingsFrame, TextManager.Get("spw_mindistancefalloffvolume"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.MinDistanceFalloffVolume ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.MinDistanceFalloffVolume,
                    vanillaValue: 0.1f),
                currentValue: unsavedConfig.MinDistanceFalloffVolume,
                setter: v => unsavedConfig.MinDistanceFalloffVolume = v,
                TextManager.Get("spw_mindistancefalloffvolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_sidechainmuffleinfluence"));
            Slider(settingsFrame, (0, 1), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SidechainMuffleInfluence ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SidechainMuffleInfluence,
                    vanillaValue: null),
                currentValue: unsavedConfig.SidechainMuffleInfluence,
                setter: v => unsavedConfig.SidechainMuffleInfluence = v,
                TextManager.Get("spw_sidechainmuffleinfluencetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_advancedcategorypathfinding"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_traversewaterducts"),
                serverValue: ConfigManager.ServerConfig?.TraverseWaterDucts ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_traversewaterductstooltip"),
                currentValue: unsavedConfig.TraverseWaterDucts,
                setter: v => unsavedConfig.TraverseWaterDucts = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_flowsoundstraversewaterducts"),
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
            Slider(settingsFrame, (0, 10000), 1,
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_advancedcategoryai"));

            Label(settingsFrame, TextManager.Get("spw_aitargetsoundrange"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.AITargetSoundRangeMultiplierMaster ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.AITargetSoundRangeMultiplierMaster,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.AITargetSoundRangeMultiplierMaster,
                setter: v => unsavedConfig.AITargetSoundRangeMultiplierMaster = v,
                TextManager.Get("spw_aitargetsoundrangetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_aitargetsightrange"));
            Slider(settingsFrame, (0, 5), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.AITargetSightRangeMultiplierMaster ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.AITargetSightRangeMultiplierMaster,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.AITargetSightRangeMultiplierMaster,
                setter: v => unsavedConfig.AITargetSightRangeMultiplierMaster = v,
                TextManager.Get("spw_aitargetsightrangetooltip")
            );

            SpacerLabel(settingsFrame, TextManager.Get("spw_advancedcategoryrules"));

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
                labelText: TextManager.Get("spw_pathignoredsounds"),
                tooltip: TextManager.Get("spw_pathignoredsoundstooltip"),
                getter: config => config.PathIgnoredSounds,
                setter: newSet => unsavedConfig.PathIgnoredSounds = newSet,
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
                labelText: TextManager.Get("spw_lowpassforcedsounds"),
                tooltip: TextManager.Get("spw_lowpassforcedsoundstooltip"),
                getter: config => config.LowpassForcedSounds,
                setter: newSet => unsavedConfig.LowpassForcedSounds = newSet,
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

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_bubbleignorednames"),
                tooltip: TextManager.Get("spw_bubbleignorednamestooltip"),
                getter: config => config.BubbleIgnoredNames,
                setter: newSet => unsavedConfig.BubbleIgnoredNames = newSet,
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
            if (Util.ShouldUpdateSoundInfo(unsavedModdedCustomSounds, ConfigManager.ModdedCustomSounds))
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