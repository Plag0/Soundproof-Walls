namespace SoundproofWalls
{
    public class SidechainProcessor
    {
        private float sidechainRelease; // in seconds
        private float frameTime;
        private bool isReleasing = false;

        private float sidechainStartingValue = 0;
        private float progress = 0f;

        public float CompletionRatio { get; private set; } = 0;
        public CustomSound? ActiveSoundGroup { get; private set; } = null;
        // Current multiplier value
        public float SidechainRawStartValue { get; private set; } = 0;
        public float SidechainMultiplier { get; private set; } = 0;

        public SidechainProcessor(float frameRate = 60f)
        {
            // Calculate how much time passes per frame
            frameTime = 1f / frameRate;
        }

        // Call this to start the release phase
        public void StartRelease(float rawStartingValue, float releaseTimeInSeconds, CustomSound? activeSoundGroup)
        {
            // Only start a new release if it's more intense.
            if (rawStartingValue < SidechainRawStartValue * CompletionRatio) { return;}
            
            SidechainRawStartValue = rawStartingValue;
            // Normalise to between 0-1.
            sidechainStartingValue = Math.Clamp(SidechainRawStartValue, 0f, 1f);
            ActiveSoundGroup = activeSoundGroup;

            // Use the new release if it's longer than the current remaining release.
            if (releaseTimeInSeconds > sidechainRelease * (1 - progress))
            {
                // Prevent division by 0 error.
                sidechainRelease = Math.Max(releaseTimeInSeconds, 0.001f);
                progress = 0f;
            }

            SidechainMultiplier = sidechainStartingValue;
            isReleasing = true;
        }

        public float Update()
        {
            if (!isReleasing || sidechainRelease <= 0f)
            {
                return 0;
            }

            if (!ConfigManager.Config.SidechainingEnabled || !ConfigManager.Config.Enabled || !Util.RoundStarted)
            {
                SidechainMultiplier = 0;
                return 0;
            }

            progress += frameTime / sidechainRelease;

            if (progress >= 1f)
            {
                progress = 1f;
                isReleasing = false;
                SidechainMultiplier = 0f;
                return 0f;
            }

            float exponent = ConfigManager.Config.SidechainReleaseCurve;
            CompletionRatio = (float)Math.Pow(1 - progress, exponent);

            // Max out at 0.98 to prevent auto-disposing of SoundChannels unnecessarily.
            SidechainMultiplier = Math.Clamp(sidechainStartingValue * CompletionRatio, 0, 0.98f);

            return SidechainMultiplier;
        }
    }
}
