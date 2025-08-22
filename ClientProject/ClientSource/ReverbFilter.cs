namespace SoundproofWalls
{
    // Basic Delay Line
    public class DelayLine
    {
        private float[] buffer;
        private int writeIndex;
        private int bufferSize;

        public DelayLine(int size)
        {
            bufferSize = size;
            buffer = new float[bufferSize];
            writeIndex = 0;
        }

        public void Write(float value)
        {
            buffer[writeIndex] = value;
            writeIndex = (writeIndex + 1) % bufferSize;
        }

        public float Read(int delaySamples)
        {
            int readIndex = (writeIndex - delaySamples + bufferSize) % bufferSize;
            return buffer[readIndex];
        }

        public int Size => bufferSize;
    }

    // Basic Comb Filter (builds echoes)
    public class CombFilter
    {
        private DelayLine delayLine;
        private float feedbackGain; // How much signal feeds back (controls decay)
        private float filterStore;  // Stores previous filtered value for smoothing
        private float damping;      // Controls high-frequency damping

        public CombFilter(int delaySamples, float feedback, float damping)
        {
            delayLine = new DelayLine(delaySamples);
            feedbackGain = feedback;
            this.damping = Math.Clamp(damping, 0, 1);
            filterStore = 0;
        }

        public float Process(float input)
        {
            float delayedSample = delayLine.Read(delayLine.Size);

            // Damping (low-pass filter on feedback)
            filterStore = (delayedSample * (1.0f - damping)) + (filterStore * damping);

            float output = input + filterStore * feedbackGain;
            delayLine.Write(output); // Feed processed signal back into delay
            return output;
        }
    }

    // Basic All-Pass Filter (smears phase, adds density)
    public class AllPassFilter
    {
        private DelayLine delayLine;
        private float feedbackGain; // Coefficient for feedback/feedforward

        public AllPassFilter(int delaySamples, float gain)
        {
            delayLine = new DelayLine(delaySamples);
            feedbackGain = gain;
        }

        public float Process(float input)
        {
            float delayedSample = delayLine.Read(delayLine.Size);
            float output = -input * feedbackGain + delayedSample;
            delayLine.Write(input + output * feedbackGain); // Feedforward part
            return output;
        }
    }

    // Simple Schroeder-style Reverb Filter
    public class ReverbFilter
    {
        private List<CombFilter> combFilters;
        private List<AllPassFilter> allPassFilters;

        private float wetMix;
        private float dryMix;

        private float reverbTimeSeconds;
        private static readonly int[] CombDelays = { 1557, 1617, 1491, 1422 };
        private static readonly int[] AllPassDelays = { 556, 441, 341, 225 };
        private const float AllPassGain = 0.5f;

        public ReverbFilter(int sampleRate, float reverbTimeSeconds, float damping, float mix)
        {
            reverbTimeSeconds = MathF.Max(reverbTimeSeconds, 0.01f);
            this.reverbTimeSeconds = reverbTimeSeconds;

            combFilters = new List<CombFilter>();

            foreach (int delay in CombDelays)
            {
                float feedback = (float)Math.Pow(0.001, (double)delay / (reverbTimeSeconds * sampleRate));
                combFilters.Add(new CombFilter(delay, feedback, damping));
            }

            allPassFilters = new List<AllPassFilter>();
            foreach (int delay in AllPassDelays)
            {
                allPassFilters.Add(new AllPassFilter(delay, AllPassGain));
            }

            SetMix(mix);
        }

        public void SetMix(float mix)
        {
            wetMix = mix;
            dryMix = 1.0f - mix;
        }

        public float Process(float input)
        {
            float combOutput = 0.0f;

            foreach (var comb in combFilters)
            {
                combOutput += comb.Process(input);
            }
            combOutput /= combFilters.Count;

            float allPassOutput = combOutput;
            foreach (var allPass in allPassFilters)
            {
                allPassOutput = allPass.Process(allPassOutput);
            }

            return (input * dryMix) + (allPassOutput * wetMix);
        }

        public int GetTailSampleLength(int sampleRate)
        {
            return (int)(reverbTimeSeconds * sampleRate);
        }

        public float[] ProcessBufferWithTail(float[] inputBuffer, int sampleRate)
        {
            int inputLength = inputBuffer.Length;
            int tailLength = GetTailSampleLength(sampleRate);
            int outputLength = inputLength + tailLength;
            float[] outputBuffer = new float[outputLength];

            // Process the original sound
            for (int i = 0; i < inputLength; i++)
            {
                outputBuffer[i] = Process(inputBuffer[i]);
            }

            // Process the tail by feeding silence
            for (int i = 0; i < tailLength; i++)
            {
                outputBuffer[inputLength + i] = Process(0.0f);
            }

            return outputBuffer;
        }
    }
}
