
using Barotrauma.Sounds;

namespace SoundproofWalls
{
    internal sealed class RadioFilter
    {
        public double frequency;
        public double q;
        public float distortionAmount;
        public float staticAmount;
        public float compressionThreshold;
        public float compressionRatio;

        private float qCompensation;
        private BandpassFilter bandpassFilter;
        private SimpleCompressor compressor;
        private Random random = new Random();

        public RadioFilter(int sampleRate, double frequency, double q,
                                   float distortionAmount, float staticAmount,
                                   float compressionThreshold, float compressionRatio)
        {
            this.frequency = frequency;
            this.q = q;
            this.distortionAmount = distortionAmount;
            this.staticAmount = staticAmount;
            this.compressionThreshold = compressionThreshold;
            this.compressionRatio = compressionRatio;
            this.qCompensation = (float)Math.Sqrt(q);

            bandpassFilter = new BandpassFilter(sampleRate, frequency);
            compressor = new SimpleCompressor(compressionThreshold, compressionRatio, 5.0f, 50.0f, sampleRate);
        }

        public float Process(float sample)
        {
            // Apply distortion first (input stage)
            sample = ApplyDistortion(sample, distortionAmount);

            // Apply bandpass filter (frequency response)
            sample = bandpassFilter.Process(sample);

            // Apply Q compensation
            sample *= qCompensation;

            // Apply compression (automatic gain control)
            sample = compressor.Process(sample);

            // Add static noise (transmission artifacts)
            sample = AddStatic(sample, staticAmount);

            return sample;
        }

        public float ApplyDistortion(float sample, float amount)
        {
            // Simple soft clipping distortion
            // amount should be between 0.0 (no distortion) and 1.0 (heavy distortion)
            float threshold = 1.0f - amount * 0.9f;

            if (sample > threshold)
                sample = threshold + (1.0f - threshold) * (float)Math.Tanh((sample - threshold) / (1.0f - threshold));
            else if (sample < -threshold)
                sample = -threshold + (1.0f - threshold) * (float)Math.Tanh((sample + threshold) / (1.0f - threshold));

            return sample;
        }

        public float AddStatic(float sample, float amount)
        {
            // amount should be between 0.0 (no static) and 1.0 (lots of static)
            float noise = (float)(random.NextDouble() * 2.0 - 1.0) * amount;
            return sample * (1.0f - amount * 0.5f) + noise;
        }
    }

    public class SimpleCompressor
    {
        private float threshold;
        private float ratio;
        private float attackTime;
        private float releaseTime;
        private float envelope = 0.0f;
        private float sampleRate;

        public SimpleCompressor(float threshold, float ratio, float attackTimeMs, float releaseTimeMs, int sampleRate)
        {
            this.threshold = threshold;
            this.ratio = ratio;
            this.sampleRate = sampleRate;

            // Convert times to coefficients
            this.attackTime = (float)Math.Exp(-1.0 / (sampleRate * attackTimeMs / 1000.0));
            this.releaseTime = (float)Math.Exp(-1.0 / (sampleRate * releaseTimeMs / 1000.0));
        }

        public float Process(float sample)
        {
            // Get sample absolute value
            float inputLevel = Math.Abs(sample);

            // Envelope follower
            if (inputLevel > envelope)
                envelope = envelope * attackTime + inputLevel * (1.0f - attackTime);
            else
                envelope = envelope * releaseTime + inputLevel * (1.0f - releaseTime);

            // Apply compression if envelope exceeds threshold
            if (envelope > threshold)
            {
                float gain = threshold + (envelope - threshold) / ratio;
                gain /= envelope;
                return sample * gain;
            }

            return sample;
        }
    }
}
