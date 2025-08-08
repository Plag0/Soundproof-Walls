using Barotrauma.Sounds;

namespace SoundproofWalls
{
    public class RadioFilter
    {
        public double frequency;
        public double q;
        public float distortionDrive;
        public float distortionThreshold;
        public float staticAmount;
        public float compressionThreshold;
        public float compressionRatio;

        private static float compressionAttackTimeMs = 5.0f;
        private static float compressionReleaseTimeMs = 50.0f;

        private float qCompensation;
        private BandpassFilter bandpassFilter;
        private SimpleCompressor compressor;
        private Random random = new Random();

        private float lastNoise = 0.0f;

        public RadioFilter(int sampleRate, double frequency, double q,
            float distortionDrive, float distortionThreshold, float staticAmount,
            float compressionThreshold, float compressionRatio)
        {
            this.frequency = frequency;
            this.q = q;
            this.distortionDrive = distortionDrive;
            this.distortionThreshold = distortionThreshold;
            this.staticAmount = staticAmount;
            this.compressionThreshold = compressionThreshold;
            this.compressionRatio = compressionRatio;
            this.qCompensation = (float)Math.Sqrt(q);

            bandpassFilter = new BandpassFilter(sampleRate, frequency);
            compressor = new SimpleCompressor(compressionThreshold, compressionRatio, compressionAttackTimeMs, compressionReleaseTimeMs, sampleRate);
        }

        public float Process(float sample)
        {
            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.RadioFilterUpdate);
            sample = ApplyHardClipDistortion(sample, distortionDrive, distortionThreshold);
            PerformanceProfiler.Instance.StopTimingEvent();

            // Vanilla bandpass filter.
            sample = bandpassFilter.Process(sample);

            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.RadioFilterUpdate);
            sample *= qCompensation;
            sample = AddFilteredStatic(sample, staticAmount);
            sample = compressor.Process(sample);
            PerformanceProfiler.Instance.StopTimingEvent();

            return sample;
        }

        public float ApplyHardClipDistortion(float sample, float drive, float threshold)
        {
            // Amplify the signal
            float drivenSample = sample * drive;

            // Hard clip the signal
            return Math.Max(-threshold, Math.Min(drivenSample, threshold));
        }

        public float AddFilteredStatic(float sample, float amount)
        {
            // Generate white noise
            float whiteNoise = (float)(random.NextDouble() * 2.0 - 1.0);

            // Apply a simple low-pass filter to the noise to make it "brownish"
            // This makes the static sound more like a rumble than a screech.
            float filteredNoise = (lastNoise + 0.08f * whiteNoise) / 1.08f;
            lastNoise = filteredNoise;

            return sample + filteredNoise * amount;
        }
    }

    public class SimpleCompressor
    {
        private float threshold;
        private float ratio;
        private float attackCoeff;
        private float releaseCoeff;
        private float envelope = 0.0f;

        public SimpleCompressor(float threshold, float ratio, float attackTimeMs, float releaseTimeMs, int sampleRate)
        {
            this.threshold = threshold;
            this.ratio = ratio;

            // Convert times to coefficients
            this.attackCoeff = (float)Math.Exp(-1.0 / (sampleRate * attackTimeMs / 1000.0));
            this.releaseCoeff = (float)Math.Exp(-1.0 / (sampleRate * releaseTimeMs / 1000.0));
        }

        public float Process(float sample)
        {
            // Get sample absolute value
            float inputLevel = Math.Abs(sample);

            // Envelope follower
            if (inputLevel > envelope)
                envelope = attackCoeff * envelope + (1.0f - attackCoeff) * inputLevel;
            else
                envelope = releaseCoeff * envelope + (1.0f - releaseCoeff) * inputLevel;

            // Apply compression if envelope exceeds threshold
            if (envelope > threshold)
            {
                // Calculate gain reduction
                float gain = threshold + (envelope - threshold) / ratio;
                // Avoid division by zero
                if (envelope > 0)
                {
                    gain /= envelope;
                    return sample * gain;
                }
            }

            return sample;
        }
    }
}
