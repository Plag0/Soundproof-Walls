using Barotrauma.Networking;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    // Adapted from EvilFactory's EasySettings Lua code.
    public class Setting
    {
        public string Name { get; set; }
        public Action<GUIFrame> OnOpen { get; set; }
    }

    public static class EasySettings
    {
        public static SoundproofWalls? SPW;
        private static bool ShouldUpdateConfig = false;

        public static Config NewLocalConfig = ConfigManager.CloneConfig(SoundproofWalls.LocalConfig);

        private static Dictionary<string, Setting> settings = new Dictionary<string, Setting>();
        public static void AddMenu(string name, Action<GUIFrame> onOpen)
        {
            settings.Add(name, new Setting { Name = name, OnOpen = onOpen });
        }
        public static void SPW_TogglePauseMenu()
        {
            if (GUI.PauseMenuOpen)
            {
                GUIFrame frame = GUI.PauseMenu;
                GUIComponent list = GetChildren(GetChildren(frame)[1])[0];

                foreach (var kvp in settings)
                {
                    Setting value = kvp.Value;
                    GUIButton button = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), list.RectTransform), value.Name, Alignment.Center, "GUIButtonSmall");

                    button.OnClicked = (sender, args) =>
                    {
                        value.OnOpen(frame);
                        return true;
                    };
                }
            }
            else // On menu close. Updates configs in multiplayer/singleplayer.
            {
                if (!ShouldUpdateConfig) { return; }

                ShouldUpdateConfig = false;
                Config oldConfig = SoundproofWalls.Config;
                Config newConfig = NewLocalConfig;

                SoundproofWalls.LocalConfig = ConfigManager.CloneConfig(newConfig);

                // Multiplayer config update. Only admins can call this function.
                if (GameMain.IsMultiplayer && (GameMain.Client.IsServerOwner || GameMain.Client.HasPermission(ClientPermissions.Ban)))
                {
                    SoundproofWalls.UploadServerConfig(manualUpdate: true);
                }

                // Singleplayer/nosync config update.
                if (!GameMain.IsMultiplayer || SoundproofWalls.ServerConfig == null)
                {
                    if (SPW != null)
                    {
                        SPW.UpdateConfig(newConfig, oldConfig);
                    }
                    else
                    {
                        LuaCsLogger.LogError("Instance of Soundproof Walls not found in EasySettings");
                    }
                }
            }
        }
        public static GUIListBox BasicList(GUIFrame parent, Vector2? size = null)
        {
            GUIFrame menuContent = new GUIFrame(new RectTransform(size ?? new Vector2(0.25f, 0.65f), parent.RectTransform, Anchor.Center));
            GUIListBox menuList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.91f), menuContent.RectTransform, Anchor.Center));
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), menuContent.RectTransform), TextManager.Get("spw_settings").Value, textAlignment: Alignment.Center);
            CloseButton(menuContent);
            ResetAllButton(menuContent);
            return menuList;
        }

        public static GUITickBox TickBox(GUIFrame parent, string text, bool? state, Action<bool> onSelected)
        {
            GUITickBox tickBox = new GUITickBox(new RectTransform(new Vector2(1f, 0.2f), parent.RectTransform), text);
            tickBox.Selected = state ?? true;
            tickBox.OnSelected = (sender) =>
            {
                ShouldUpdateConfig = true;
                onSelected(tickBox.State == GUIComponent.ComponentState.Selected);
                return true;
            };
            tickBox.RectTransform.RelativeOffset = new Vector2(0.05f, 0);
            return tickBox;
        }

        public static GUIScrollBar Slider(GUIFrame parent, float min, float max, float? value, Action<float> onSelected, float multiple = 0.01f, bool reloadRoundSounds = false)
        {
            GUIScrollBar scrollBar = new GUIScrollBar(new RectTransform(new Vector2(1f, 0.1f), parent.RectTransform), 0.1f, style: "GUISlider");
            scrollBar.Range = new Vector2(min, max);
            scrollBar.BarScrollValue = value ?? max / 2;
            float startValue = Menu.RoundToNearestMultiple(scrollBar.BarScrollValue, multiple);
            scrollBar.OnMoved = (sender, args) =>
            {
                onSelected(scrollBar.BarScrollValue);
                ShouldUpdateConfig = startValue != Menu.RoundToNearestMultiple(scrollBar.BarScrollValue, multiple);
                return true;
            };
            scrollBar.RectTransform.RelativeOffset = new Vector2(0.01f, 0);
            return scrollBar;
        }

        public static GUITextBlock TextBlock(GUIListBox list, string text, float x = 1f, float y = 0.05f, float size = 1, Color? color = null)
        {
            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(x, y), list.Content.RectTransform), text, textAlignment: Alignment.Center, wrap: true);
            textBlock.Enabled = false;
            textBlock.OverrideTextColor(textBlock.TextColor);
            textBlock.TextScale = size;

            if (color.HasValue)
            {
                textBlock.OverrideTextColor((Color)color);
            }
            return textBlock;
        }

        public static GUITextBox MultiLineTextBox(RectTransform rectTransform, string text, float? height)
        {
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(1, height ?? 0.2f), rectTransform));
            GUITextBox textBox = new GUITextBox(new RectTransform(new Vector2(1, 1), listBox.Content.RectTransform), text, wrap: true, style: "GUITextBoxNoBorder");

            textBox.OnSelected += (sender, key) => { UpdateMessageScrollFromCaret(textBox, listBox); };

            string startValue = text;
            textBox.OnTextChangedDelegate = (sender, e) =>
            {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(listBox.Content.Rect.Height, (int)textSize.Y + 10));
                listBox.UpdateScrollBarSize();
                ShouldUpdateConfig = startValue != textBox.Text;
                return true;
            };

            textBox.OnEnterPressed = (sender, e) =>
            {
                string str = textBox.Text;
                int caretIndex = textBox.CaretIndex;

                textBox.Text = str.Substring(0, caretIndex) + "\n" + str.Substring(caretIndex);
                textBox.CaretIndex = caretIndex + 1; // Move the caret right after the inserted newline
                return true;
            };

            return textBox;
        }

        public static void UpdateMessageScrollFromCaret(GUITextBox textBox, GUIListBox listBox)
        {
            float caretY = textBox.CaretScreenPos.Y;
            float bottomCaretExtent = textBox.Font.LineHeight * 1.5f;
            float topCaretExtent = -textBox.Font.LineHeight * 0.5f;

            if (caretY + bottomCaretExtent > listBox.Rect.Bottom)
            {
                listBox.ScrollBar.BarScroll = (caretY - textBox.Rect.Top - listBox.Rect.Height + bottomCaretExtent) / (textBox.Rect.Height - listBox.Rect.Height);
            }
            else if (caretY + topCaretExtent < listBox.Rect.Top)
            {
                listBox.ScrollBar.BarScroll = (caretY - textBox.Rect.Top + topCaretExtent) / (textBox.Rect.Height - listBox.Rect.Height);
            }
        }

        public static GUIButton CloseButton(GUIFrame parent)
        {
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), parent.RectTransform, Anchor.BottomRight), TextManager.Get("close").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                GUI.TogglePauseMenu();
                return true;
            };

            return button;
        }

        public static GUIButton ResetAllButton(GUIFrame parent)
        {
            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), parent.RectTransform, Anchor.BottomLeft), TextManager.Get("spw_resetall").Value, Alignment.Center, "GUIButton");
            button.OnClicked = (sender, args) =>
            {
                NewLocalConfig = new Config();
                ConfigManager.SaveConfig(NewLocalConfig);
                ShouldUpdateConfig = true;
                GUI.TogglePauseMenu();
                return true;
            };

            return button;
        }

        public static List<GUIComponent> GetChildren(GUIComponent comp)
        {
            List<GUIComponent> children = new List<GUIComponent>();
            foreach (var child in comp.GetAllChildren())
            {
                children.Add(child);
            }
            return children;
        }
    }
}
