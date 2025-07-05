#nullable enable
using Barotrauma;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Globalization;

namespace SoundproofWalls
{
    public class SoundproofWallsMenu
    {
        private const int TAB_ICON_SIZE = 64;
        private const string TAB_ICON_SHEET = "Content/UI/TabIcons.png";

        private static readonly Color DefaultColor = Color.ForestGreen;
        private static readonly Color VanillaColor = Color.LightGray;

        public static SoundproofWallsMenu? Instance { get; private set; }
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

        internal Config unsavedConfig;

        private readonly GUIFrame mainFrame;
        private readonly GUILayoutGroup tabber;
        private readonly GUIFrame contentFrame;
        private readonly Dictionary<Tab, (GUIButton Button, GUIFrame Content)> tabContents;

        // This will hold a reference to the function that updates the specific keybind we're currently editing.
        // If it's null, we are not waiting for any input.
        private Action<KeyOrMouse>? currentKeybindSetter;
        private GUIButton? selectedKeybindButton;
        // This is a flag to prevent the click that selects the box from also being registered as the new keybind.
        private bool keybindBoxSelectedThisFrame;

        public static void Create(bool startAtDefaultValues = false)
        {
            Instance?.Close();
            Instance = new SoundproofWallsMenu(startAtDefaultValues);
        }

        public static void ShowWelcomePopup()
        {
            new GUIMessageBox(
                TextManager.Get("spw_popuptitle"),
                TextManager.Get("spw_popupmessage"),
                new[] { TextManager.Get("close") });
        }

        private SoundproofWallsMenu(bool startAtDefaultValues = false)
        {
            if (GUI.PauseMenu == null) { return; }

            unsavedConfig = ConfigManager.CloneConfig(startAtDefaultValues ? Menu.defaultConfig : ConfigManager.LocalConfig);
            unsavedConfig.EavesdroppingKeyOrMouse = unsavedConfig.ParseEavesdroppingBind();

            mainFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.6f), GUI.PauseMenu.RectTransform, Anchor.Center));

            Menu.currentMenuFrame = this.mainFrame;

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
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.95f), infoTextContainer.RectTransform, Anchor.Center), GetInfoPanelText(), wrap: true, textAlignment: Alignment.TopLeft) { CanBeFocused = false };
            
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
            Menu.currentMenuFrame = null;

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
                        TextManager.Get("spw_syncingenabled").Value.Replace("[]", $"\"{ConfigManager.ServerConfigUploader?.Name ?? "unknown"}\"");
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
                text += TextManager.Get("spw_offlinemode").Value.Replace("[]", $"\"{configPath}\"");
            }

            return text;
        }

        private void CreateLink(GUILayoutGroup parent, LocalizedString text, string url)
        {
            var linkText = new GUITextBlock(new RectTransform(Vector2.Zero, parent.RectTransform) { IsFixedSize = true }, text, font: GUIStyle.SmallFont);
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
                            DebugConsole.ThrowError("Failed to open the url " + url, e);
                        }
                    }
                },
                onDraw: (sb, component) =>
                {
                    var (rect, mouseOn) = getHoverRect();
                    Color color = mouseOn ? GUIStyle.Green : Color.LightBlue * 0.8f;
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
            return new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.8f), frame.RectTransform), str, font: GUIStyle.HotkeyFont, textColor: new Color(Color.White * 0.8f, 100), textAlignment: Alignment.BottomCenter) { CanBeFocused = false };
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
        private string PlusMeters(float v) => $"{MathF.Round(v).ToString("N0", CultureInfo.CurrentUICulture)} m";
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
                color = DefaultColor;
            }
            else if (vanillaValue != null && ValuesEqual(localValue, vanillaValue))
            {
                color = VanillaColor;
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
            float curveFactor = MathF.E)
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
            // 1. Create a horizontal layout group to hold the title and the button.
            var layout = new GUILayoutGroup(NewItemRectT(parent), isHorizontal: true);

            // 2. Create the title on the left side (taking up 60% of the width).
            //    Aligning the text to the left makes it look neat with other labels.
            new GUITextBlock(
                new RectTransform((0.6f, 1.0f), layout.RectTransform),
                title,
                textAlignment: Alignment.CenterLeft,
                wrap: true
            );

            // 3. Create the keybind button on the right side (taking up the remaining 40%).
            var keybindBox = new GUIButton(
                new RectTransform((0.4f, 1.0f), layout.RectTransform),
                valueNameGetter(),
                style: "GUITextBoxNoIcon"
            )
            {
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

        private void CreateJsonTextBox(
            GUIListBox parentListBox,
            LocalizedString labelText,
            LocalizedString tooltip,
            Func<Config, HashSet<string>> getter,
            Action<HashSet<string>> setter)
        {
            GUIFrame parent = parentListBox.Content;
            var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), parent.RectTransform), isHorizontal: true);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1.0f), topRow.RectTransform), labelText, font: GUIStyle.SubHeadingFont);
            var resetButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), topRow.RectTransform), TextManager.Get("spw_reset"), style: "GUIButtonSmall");

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.25f), parent.RectTransform));
            var textBox = new GUITextBox(new RectTransform(Vector2.One, listBox.Content.RectTransform), "", wrap: true, style: "GUITextBoxNoBorder");
            listBox.ScrollBarEnabled = false;

            // Custom component to fix bug where scrolling the list could block the tab buttons.
            new GUICustomComponent(new RectTransform(Vector2.Zero, parent.RectTransform), onUpdate: (deltaTime, component) =>
            {
                bool mouseInMenu = parentListBox.Rect.Contains(PlayerInput.MousePosition);
                bool mouseInListBox = listBox.Rect.Contains(PlayerInput.MousePosition);
                
                textBox.CanBeFocused = mouseInMenu;
                textBox.Visible = !mouseInListBox || mouseInMenu;
                //listBox.CanBeFocused = mouseInMenu;
                //listBox.Visible = mouseInMenu;
            });

            var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

            Action updateSize = () =>
            {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                listBox.RectTransform.NonScaledSize = new Point(listBox.Rect.Width, (int)textSize.Y + GUI.IntScale(20));
                float contentHeight = Math.Max(listBox.Rect.Height, textSize.Y + GUI.IntScale(15));
                textBox.RectTransform.NonScaledSize = new Point(textBox.Rect.Width, (int)contentHeight);
                listBox.Content.RectTransform.NonScaledSize = textBox.RectTransform.NonScaledSize;

                parentListBox.scrollBarNeedsRecalculation = true;
            };

            resetButton.OnClicked = (btn, data) =>
            {
                var defaultSet = getter(Menu.defaultConfig);
                setter(defaultSet);
                textBox.Text = System.Text.Json.JsonSerializer.Serialize(defaultSet, jsonOptions);
                // OnTextChanged will fire, handling the rest.
                return true;
            };

            //System.Text.Json.JsonSerializer.Serialize(defaultSet, jsonOptions)
            textBox.OnTextChangedDelegate = (sender, e) =>
            {
                textBox.SetText(textBox.Text, store: true);
                updateSize();

                try
                {
                    var newSet = System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(textBox.Text) ?? new HashSet<string>();
                    setter(newSet);
                    label.TextColor = GUIStyle.TextColorNormal;
                }
                catch (System.Text.Json.JsonException)
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
            var (fullTooltip, diffCount) = GenerateServerDiffTooltip(localSet, serverSet, tooltip);

            textBox.ToolTip = resetButton.ToolTip = label.ToolTip = fullTooltip;
            label.Text = diffCount > 0 ? $"{labelText} ({diffCount})" : labelText;

            textBox.Text = System.Text.Json.JsonSerializer.Serialize(localSet, jsonOptions);
            updateSize();
        }

        /// <summary>
        /// Compares a local and server HashSet, generates a formatted tooltip, and returns the number of differences.
        /// </summary>
        private (string fullTooltip, int diffCount) GenerateServerDiffTooltip(
            HashSet<string> localSet,
            HashSet<string> serverSet,
            LocalizedString baseTooltip)
        {
            if (!GameMain.IsMultiplayer || ConfigManager.ServerConfig == null)
            {
                return (baseTooltip.Value, 0);
            }

            // Find items that are in one set but not the other
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
                sb.Append($"{i + 1}. {differences[i]}");
            }

            return (sb.ToString(), differences.Count);
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_dynamiccategoryreverb"));

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverb"),
                serverValue: ConfigManager.ServerConfig?.DynamicReverbEnabled ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbtooltip"),
                currentValue: unsavedConfig.DynamicReverbEnabled,
                setter: v => unsavedConfig.DynamicReverbEnabled = v);

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_dynamicreverbradio"),
                serverValue: ConfigManager.ServerConfig?.DynamicReverbRadio ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_dynamicreverbradiotooltip"),
                currentValue: unsavedConfig.DynamicReverbRadio,
                setter: v => unsavedConfig.DynamicReverbRadio = v);

            Spacer(settingsFrame);

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

            Label(settingsFrame, TextManager.Get("spw_dynamicreverbwateramplitudethreshold"));
            Slider(settingsFrame, (0, 3), 0.01f,
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

            SpacerLabel(settingsFrame, TextManager.Get("spw_dynamiccategoryhydrophoneeffects"));

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
                tooltip: TextManager.Get("spw_mufflesubmergedviewtargettooltip"),
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

            CreateKeybindControl(
                parent: settingsFrame,
                TextManager.Get("spw_eavesdroppingbind"),
                valueNameGetter: () => unsavedConfig.EavesdroppingKeyOrMouse.Name.Value,
                valueSetter: v => { unsavedConfig.EavesdroppingKeyOrMouse = v; unsavedConfig.EavesdroppingBind = v.ToString(); }
            );

            Tickbox(settingsFrame, FormatTextBoxLabel(
                label: TextManager.Get("spw_muffleeavesdropping"),
                serverValue: ConfigManager.ServerConfig?.EavesdroppingMuffle ?? default,
                formatter: BoolFormatter),
                tooltip: TextManager.Get("spw_muffleeavesdroppingtooltip"),
                currentValue: unsavedConfig.EavesdroppingMuffle,
                setter: v => unsavedConfig.EavesdroppingMuffle = v);
        }

        private void CreateHydrophonesTab()
        {
            var iconRect = new Rectangle(3 * TAB_ICON_SIZE, 1 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Hydrophones, iconRect);
            var content = GetTabContentFrame(Tab.Hydrophones);
            GUIFrame settingsFrame = NewListContent(content);
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

            Label(settingsFrame, TextManager.Get("spw_unsubmergedwaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.UnsubmergedWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.UnsubmergedWaterAmbienceVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.UnsubmergedWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.UnsubmergedWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_unsubmergedwaterambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_submergedwaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.SubmergedWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.SubmergedWaterAmbienceVolumeMultiplier,
                    vanillaValue: 1.0f),
                currentValue: unsavedConfig.SubmergedWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.SubmergedWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_submergedwaterambiencevolumetooltip")
            );

            Label(settingsFrame, TextManager.Get("spw_hydrophonewaterambiencevolume"));
            Slider(settingsFrame, (0, 3), 0.01f,
                labelFunc: localSliderValue =>
                FormatSettingText(localSliderValue,
                    serverValue: ConfigManager.ServerConfig?.HydrophoneWaterAmbienceVolumeMultiplier ?? default,
                    formatter: Percentage),
                colorFunc: (localSliderValue, componentStyle) =>
                GetSettingColor(localSliderValue, componentStyle,
                    defaultValue: Menu.defaultConfig.HydrophoneWaterAmbienceVolumeMultiplier,
                    vanillaValue: null),
                currentValue: unsavedConfig.HydrophoneWaterAmbienceVolumeMultiplier,
                setter: v => unsavedConfig.HydrophoneWaterAmbienceVolumeMultiplier = v,
                TextManager.Get("spw_hydrophonewaterambiencevolumetooltip")
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
        }

        private void CreateAdvancedTab()
        {
            var iconRect = new Rectangle(2 * TAB_ICON_SIZE, 2 * TAB_ICON_SIZE, TAB_ICON_SIZE, TAB_ICON_SIZE);
            AddButtonToTabber(Tab.Advanced, iconRect);
            var content = GetTabContentFrame(Tab.Advanced);
            GUIListBox settingsList = NewList(content);
            GUIFrame settingsFrame = settingsList.Content;

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

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_ignoredsounds"),
                tooltip: TextManager.Get("spw_ignoredsoundstooltip"),
                getter: config => config.IgnoredSounds,
                setter: newSet => unsavedConfig.IgnoredSounds = newSet
            );

            Spacer(settingsFrame);

            CreateJsonTextBox(
                parentListBox: settingsList,
                labelText: TextManager.Get("spw_lowpassignoredsounds"),
                tooltip: TextManager.Get("spw_lowpassignoredsoundstooltip"),
                getter: config => config.LowpassIgnoredSounds,
                setter: newSet => unsavedConfig.LowpassIgnoredSounds = newSet
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
                    mainFrame.Flash(GUIStyle.Green);
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
            
            ConfigManager.LocalConfig.EavesdroppingKeyOrMouse = ConfigManager.LocalConfig.ParseEavesdroppingBind(); // todo maybe unnecessary

            if (GameMain.IsMultiplayer && (GameMain.Client.IsServerOwner || GameMain.Client.HasPermission(ClientPermissions.Ban)))
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