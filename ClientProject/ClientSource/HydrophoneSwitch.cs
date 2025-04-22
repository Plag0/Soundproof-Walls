using Barotrauma;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    public class HydrophoneSwitch
    {
        public static Color textDefaultColor = new Color(228, 217, 167);
        public static Color textDisabledColor = new Color(114, 108, 83);
        public static Color buttonDefaultColor = new Color(255, 255, 255);
        public static Color buttonDisabledColor = new Color(127, 127, 127);

        public bool State { get; set; }
        public GUIButton? Switch { get; set; }
        public GUITextBlock? TextBlock { get; set; }

        public HydrophoneSwitch(bool state, GUIButton? switchButton, GUITextBlock? textBlock)
        {
            State = state;
            Switch = switchButton;
            TextBlock = textBlock;
        }
    }
}
