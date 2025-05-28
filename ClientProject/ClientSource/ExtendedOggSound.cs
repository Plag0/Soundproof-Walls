using Barotrauma;
using Barotrauma.Sounds;
using NVorbis;
using OpenAL;

namespace SoundproofWalls
{
    internal sealed class ExtendedOggSound : Sound
    {
        private readonly VorbisReader streamReader;

        public long TotalSamples;
        public long MaxStreamSamplePos => (streamReader == null || streamReader.TotalSamples == null || streamReader.Channels == null) ? 0 : streamReader.TotalSamples * streamReader.Channels * 2;

        private List<float> playbackAmplitude;
        private const int AMPLITUDE_SAMPLE_COUNT = 4410; //100ms in a 44100hz file

        private short[] sampleBuffer = Array.Empty<short>();
        private short[] reverbBuffer = Array.Empty<short>();
        private short[] muffleBufferHeavy = Array.Empty<short>();
        private short[] muffleBufferMedium = Array.Empty<short>();
        private short[] muffleBufferLight = Array.Empty<short>();


        private new ExtendedSoundBuffers buffers;
        public new ExtendedSoundBuffers Buffers
        {
            get { return !Stream ? buffers : null; }
        }

        public ExtendedOggSound(SoundManager owner, string filename, bool stream, ContentXElement xElement) : base(owner, filename,
            stream, true, xElement)
        {
            var reader = new VorbisReader(Filename);
            TotalSamples = reader.TotalSamples * reader.Channels;

            // Allow for more simulatenous instances so rapdily repeated sounds with long reverbs on them (e.g. gunshots) don't get skipped.
            MaxSimultaneousInstances = 10;

            ALFormat = reader.Channels == 1 ? Al.FormatMono16 : Al.FormatStereo16;
            SampleRate = reader.SampleRate;

            if (stream)
            {
                streamReader = reader;
                return;
            }

            Loading = true;
            TaskPool.Add(
                $"LoadSamples {filename}",
                LoadSamples(reader),
                t =>
                {
                    reader.Dispose();
                    if (!t.TryGetResult(out TaskResult result))
                    {
                        return;
                    }
                    sampleBuffer = result.SampleBuffer;
                    reverbBuffer = result.ReverbBuffer;
                    muffleBufferLight = result.MuffleBufferLight;
                    muffleBufferHeavy = result.MuffleBufferHeavy;
                    muffleBufferMedium = result.MuffleBufferMedium;

                    playbackAmplitude = result.PlaybackAmplitude;
                    Owner.KillChannels(this); // prevents INVALID_OPERATION error
                    buffers?.Dispose(); buffers = null;
                    base.buffers?.Dispose(); base.buffers = null;
                    Loading = false;
                });
        }

        private readonly record struct TaskResult(
            short[] SampleBuffer,
            short[] ReverbBuffer,
            short[] MuffleBufferHeavy,
            short[] MuffleBufferLight,
            short[] MuffleBufferMedium,
            List<float> PlaybackAmplitude);

        private static async Task<TaskResult> LoadSamples(VorbisReader reader)
        {
            reader.DecodedPosition = 0;

            int bufferSize = (int)reader.TotalSamples * reader.Channels;

            float[] floatBuffer = new float[bufferSize];
            var sampleBuffer = new short[bufferSize];
            var muffleBufferHeavy = new short[bufferSize];
            var muffleBufferLight = new short[bufferSize];
            var muffleBufferMedium = new short[bufferSize];

            int readSamples = await Task.Run(() => reader.ReadSamples(floatBuffer, 0, bufferSize));

            var playbackAmplitude = new List<float>();
            for (int i = 0; i < bufferSize; i += reader.Channels * AMPLITUDE_SAMPLE_COUNT)
            {
                float maxAmplitude = 0.0f;
                for (int j = i; j < i + reader.Channels * AMPLITUDE_SAMPLE_COUNT; j++)
                {
                    if (j >= bufferSize) { break; }
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(floatBuffer[j]));
                }
                playbackAmplitude.Add(maxAmplitude);
            }

            CastBuffer(floatBuffer, sampleBuffer, readSamples);

            // Create a copy of floatBuffer for the muffled light version.
            float[] floatBufferLight = new float[bufferSize];
            Array.Copy(floatBuffer, floatBufferLight, bufferSize);

            // Create a copy of floatBuffer for the muffled medium version.
            float[] floatBufferMedium = new float[bufferSize];
            Array.Copy(floatBuffer, floatBufferMedium, bufferSize);

            // Create reverb buffer.
            float[] floatReverbBuffer = GetReverbBuffer(floatBuffer, reader.SampleRate);
            var reverbBuffer = new short[floatReverbBuffer.Length];

            // Create muffle buffers.
            MuffleBufferHeavy(floatBuffer, reader.SampleRate);
            MuffleBufferLight(floatBufferLight, reader.SampleRate);
            MuffleBufferMedium(floatBufferMedium, reader.SampleRate);

            // Cast muffled buffers to short[]
            CastBuffer(floatReverbBuffer, reverbBuffer, floatReverbBuffer.Length);
            CastBuffer(floatBuffer, muffleBufferHeavy, readSamples);
            CastBuffer(floatBufferLight, muffleBufferLight, readSamples);
            CastBuffer(floatBufferMedium, muffleBufferMedium, readSamples);

            return new TaskResult(sampleBuffer, reverbBuffer, muffleBufferHeavy, muffleBufferLight, muffleBufferMedium, playbackAmplitude);
        }

        public override float GetAmplitudeAtPlaybackPos(int playbackPos)
        {
            if (playbackAmplitude == null || playbackAmplitude.Count == 0) { return 0.0f; }
            int index = playbackPos / AMPLITUDE_SAMPLE_COUNT;
            if (index < 0) { return 0.0f; }
            if (index >= playbackAmplitude.Count) { index = playbackAmplitude.Count - 1; }
            return playbackAmplitude[index];
        }

        private float[] streamFloatBuffer = null;
        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!Stream) { throw new Exception("Called FillStreamBuffer on a non-streamed sound!"); }
            if (streamReader == null) { throw new Exception("Called FillStreamBuffer when the reader is null!"); }

            if (samplePos >= MaxStreamSamplePos) { return 0; }

            samplePos /= streamReader.Channels * 2;
            streamReader.DecodedPosition = samplePos;

            if (streamFloatBuffer is null || streamFloatBuffer.Length < buffer.Length)
            {
                streamFloatBuffer = new float[buffer.Length];
            }
            int readSamples = streamReader.ReadSamples(streamFloatBuffer, 0, buffer.Length);
            //MuffleBufferHeavy(floatBuffer, reader.Channels);
            CastBuffer(streamFloatBuffer, buffer, readSamples);

            return readSamples;
        }

        static float[] GetReverbBuffer(float[] buffer, int sampleRate)
        {
            var filter = new ReverbFilter(sampleRate, ConfigManager.Config.StaticReverbDuration, ConfigManager.Config.StaticReverbWetDryMix);
            return filter.ProcessBufferWithTail(buffer, sampleRate);
        }

        static void MuffleBufferHeavy(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, ConfigManager.Config.HeavyLowpassFrequency);
            filter.Process(buffer);
        }

        static void MuffleBufferLight(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, ConfigManager.Config.LightLowpassFrequency);
            filter.Process(buffer);
        }

        static void MuffleBufferMedium(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, ConfigManager.Config.MediumLowpassFrequency);
            filter.Process(buffer);
        }

        public override void InitializeAlBuffers()
        {
            if (buffers != null && ExtendedSoundBuffers.BuffersGenerated < ExtendedSoundBuffers.MaxBuffers)
            {
                FillAlBuffers();
            }
        }

        public override void FillAlBuffers()
        {
            if (Stream) { return; }
            if (sampleBuffer.Length == 0 || reverbBuffer.Length == 0 || muffleBufferHeavy.Length == 0 || muffleBufferLight.Length == 0 || muffleBufferMedium.Length == 0) { return; }
            buffers ??= new ExtendedSoundBuffers(this);
            if (!buffers.RequestAlBuffers()) { return; }

            // Clear error state.
            // Doing this here seems to have fixed an occasional INVALID_OPERATION error from OpenAl.
            // The following BufferData function doesn't produce an invalid operation error state, so we're actually clearing a residual state from elsewhere.
            int alError = Al.GetError();

            Al.BufferData(buffers.AlBuffer, ALFormat, sampleBuffer, sampleBuffer.Length * sizeof(short), SampleRate);
            alError = Al.GetError();
            if (alError != Al.NoError) { throw new Exception("Failed to set regular buffer data for non-streamed audio! " + Al.GetErrorString(alError)); }

            Al.BufferData(buffers.AlHeavyMuffledBuffer, ALFormat, muffleBufferHeavy, muffleBufferHeavy.Length * sizeof(short), SampleRate);
            alError = Al.GetError();
            if (alError != Al.NoError) { throw new Exception("Failed to set heavy muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError)); }

            Al.BufferData(buffers.AlMediumMuffledBuffer, ALFormat, muffleBufferMedium, muffleBufferMedium.Length * sizeof(short), SampleRate);
            alError = Al.GetError();
            if (alError != Al.NoError) { throw new Exception("Failed to set medium muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError)); }

            Al.BufferData(buffers.AlLightMuffledBuffer, ALFormat, muffleBufferLight, muffleBufferLight.Length * sizeof(short), SampleRate);
            alError = Al.GetError();
            if (alError != Al.NoError) { throw new Exception("Failed to set light muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError)); }

            Al.BufferData(buffers.AlReverbBuffer, ALFormat, reverbBuffer, reverbBuffer.Length * sizeof(short), SampleRate);
            alError = Al.GetError();
            if (alError != Al.NoError) { throw new Exception("Failed to set reverb buffer data for non-streamed audio! " + Al.GetErrorString(alError)); }
        }

        // This override is not included in the vanilla OggSound but in our case it's required to prevent memory leaks.
        public override void DeleteAlBuffers()
        {
            Owner.KillChannels(this);
            buffers?.Dispose();
            base.buffers?.Dispose();
        }

        public override void Dispose()
        {
            if (Stream)
            {
                streamReader?.Dispose();
            }

            if (disposed) { return; }

            DeleteAlBuffers();

            Owner.RemoveSound(this);
            disposed = true;
        }
    }
}
