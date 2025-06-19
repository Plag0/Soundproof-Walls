using Barotrauma;
using Barotrauma.Sounds;
using NVorbis;
using OpenAL;

namespace SoundproofWalls
{
    public sealed class ReducedOggSound : Sound
    {
        private readonly VorbisReader streamReader;

        public long MaxStreamSamplePos => (streamReader == null || streamReader.TotalSamples == null || streamReader.Channels == null) ? 0 : streamReader.TotalSamples * streamReader.Channels * 2;

        private List<float> playbackAmplitude;
        private const int AMPLITUDE_SAMPLE_COUNT = 4410; //100ms in a 44100hz file

        private short[] sampleBuffer = Array.Empty<short>();

        private readonly double durationSeconds;
        public override double? DurationSeconds => durationSeconds;

        private new ReducedSoundBuffers buffers;
        public new ReducedSoundBuffers Buffers
        {
            get { return !Stream ? buffers : null; }
        }

        public ReducedOggSound(SoundManager owner, string filename, bool stream, ContentXElement xElement) : base(owner, filename,
            stream, true, xElement)
        {
            var reader = new VorbisReader(Filename);
            durationSeconds = reader.TotalTime.TotalSeconds;

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

                    playbackAmplitude = result.PlaybackAmplitude;
                    Owner.KillChannels(this); // prevents INVALID_OPERATION error
                    buffers?.Dispose(); buffers = null;
                    base.buffers?.Dispose(); base.buffers = null;
                    Loading = false;
                });
        }

        private readonly record struct TaskResult(
            short[] SampleBuffer,
            List<float> PlaybackAmplitude);

        private static async Task<TaskResult> LoadSamples(VorbisReader reader)
        {
            reader.DecodedPosition = 0;

            int bufferSize = (int)reader.TotalSamples * reader.Channels;

            float[] floatBuffer = new float[bufferSize];
            var sampleBuffer = new short[bufferSize];

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

            return new TaskResult(sampleBuffer, playbackAmplitude);
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
            CastBuffer(streamFloatBuffer, buffer, readSamples);

            return readSamples;
        }

        public override void InitializeAlBuffers()
        {
            if (buffers != null && ReducedSoundBuffers.BuffersGenerated < ReducedSoundBuffers.MaxBuffers)
            {
                FillAlBuffers();
            }
        }

        public override void FillAlBuffers()
        {
            if (Stream) { return; }
            if (sampleBuffer.Length == 0) { return; }
            buffers ??= new ReducedSoundBuffers(this);
            if (!buffers.RequestAlBuffers()) { return; }

            // Clear error state.
            // Doing this here seems to have fixed an occasional INVALID_OPERATION error from OpenAl.
            // The following BufferData function doesn't produce an invalid operation error state, so we're actually clearing a residual state from elsewhere.
            int alError = Al.GetError();

            Al.BufferData(buffers.AlBuffer, ALFormat, sampleBuffer, sampleBuffer.Length * sizeof(short), SampleRate);
            alError = Al.GetError();
            if (alError != Al.NoError) { throw new Exception("Failed to set regular buffer data for non-streamed audio! " + Al.GetErrorString(alError)); }
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