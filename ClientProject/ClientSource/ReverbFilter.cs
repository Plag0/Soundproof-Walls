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
        private float filterStore; // Stores previous filtered value for smoothing (optional)
        private float damping;     // Controls high-frequency damping (optional)

        public CombFilter(int delaySamples, float feedback, float damping = 0.3f)
        {
            delayLine = new DelayLine(delaySamples);
            feedbackGain = feedback;
            this.damping = damping;
            filterStore = 0;
        }

        public float Process(float input)
        {
            float delayedSample = delayLine.Read(delayLine.Size);

            // Optional damping (low-pass filter on feedback)
            filterStore = (delayedSample * (1.0f - damping)) + (filterStore * damping);

            float output = input + filterStore * feedbackGain;
            delayLine.Write(output); // Feed processed signal back into delay
            return output; // Return the direct output for parallel comb filters
                           // Or return delayedSample for Schroeder-style serial output
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

        private float wetMix; // 0.0 (dry) to 1.0 (wet)
        private float dryMix;

        private float reverbTimeSeconds;
        private static readonly int[] CombDelays = { 1116, 1188, 1277, 1356 }; // Sample delays
        private const float CombFeedback = 0.84f; // Example feedback
        private static readonly int[] AllPassDelays = { 225, 556 }; // Sample delays
        private const float AllPassGain = 0.5f; // Example gain

        public ReverbFilter(int sampleRate, float reverbTimeSeconds, float mix = 0.5f)
        {
            reverbTimeSeconds = MathF.Max(reverbTimeSeconds, 0.01f);
            this.reverbTimeSeconds = reverbTimeSeconds;

            combFilters = new List<CombFilter>();
            // Adjust feedback based on desired reverb time (simplistic calculation)
            // More complex models relate feedback directly to RT60
            float adjustedFeedback = (float)Math.Pow(0.001, (double)CombDelays[0] / (reverbTimeSeconds * sampleRate)); // Example adjustment


            // Initialize comb filters (typically parallel)
            foreach (int delay in CombDelays)
            {
                // Adjust delay based on sample rate if needed, or use fixed sample counts
                combFilters.Add(new CombFilter(delay, adjustedFeedback)); // Use adjusted or fixed feedback
            }

            // Initialize all-pass filters (typically serial)
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

            // Process through parallel comb filters
            foreach (var comb in combFilters)
            {
                combOutput += comb.Process(input);
            }
            // Average or sum comb outputs (divide by count if summing)
            combOutput /= combFilters.Count; // Simple averaging


            // Process through serial all-pass filters
            float allPassOutput = combOutput;
            foreach (var allPass in allPassFilters)
            {
                allPassOutput = allPass.Process(allPassOutput);
            }

            // Combine dry signal and wet (reverberated) signal
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
                // Use the single-sample Process method internally
                // We need the DRY input for the final mix adjustment here
                float dryInput = inputBuffer[i];
                float wetOutput = ProcessSingleSampleWithoutMix(dryInput); // Need a helper for this
                outputBuffer[i] = (dryInput * dryMix) + (wetOutput * wetMix);
            }

            // Process the tail by feeding silence
            for (int i = 0; i < tailLength; i++)
            {
                // Use the single-sample Process method internally
                float dryInput = 0.0f; // Dry input is zero for the tail
                float wetOutput = ProcessSingleSampleWithoutMix(dryInput); // Need a helper for this
                outputBuffer[inputLength + i] = (dryInput * dryMix) + (wetOutput * wetMix); // Mix is applied
            }

            return outputBuffer;
        }

        private float ProcessSingleSampleWithoutMix(float input)
        {
            float combOutput = 0.0f;
            foreach (var comb in combFilters) { combOutput += comb.Process(input); }
            combOutput /= combFilters.Count; // Average comb outputs

            float allPassOutput = combOutput;
            foreach (var allPass in allPassFilters) { allPassOutput = allPass.Process(allPassOutput); }

            return allPassOutput; // Return only the wet signal path output
        }
    }
}
