using Barotrauma;
using Barotrauma.Sounds;
using NVorbis;
using OpenAL;
using System;

namespace SoundproofWalls
{
    internal sealed class ExtendedOggSound : Sound
    {
        private readonly VorbisReader streamReader;

        public long MaxStreamSamplePos => streamReader == null ? 0 : streamReader.TotalSamples * streamReader.Channels * 2;

        private List<float> playbackAmplitude;
        private const int AMPLITUDE_SAMPLE_COUNT = 4410; //100ms in a 44100hz file

        private short[] sampleBuffer = Array.Empty<short>();

        public short[] HeavyMuffleBuffer = Array.Empty<short>();
        public short[] LightMuffleBuffer = Array.Empty<short>();
        public short[] MediumMuffleBuffer = Array.Empty<short>();


        private new ExtendedSoundBuffers buffers;
        public new ExtendedSoundBuffers Buffers
        {
            get { return !Stream ? buffers : null; }
        }

        public ExtendedOggSound(SoundManager owner, string filename, bool stream, ContentXElement xElement) : base(owner, filename,
            stream, true, xElement)
        {
            var reader = new VorbisReader(Filename);

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

                    LightMuffleBuffer = result.LightMuffleBuffer;
                    HeavyMuffleBuffer = result.HeavyMuffleBuffer;
                    MediumMuffleBuffer = result.MediumMuffleBuffer;

                    playbackAmplitude = result.PlaybackAmplitude;
                    Owner.KillChannels(this); // prevents INVALID_OPERATION error
                    buffers?.Dispose(); buffers = null;
                    Loading = false;
                });
        }

        private readonly record struct TaskResult(
            short[] SampleBuffer,
            short[] HeavyMuffleBuffer,
            short[] LightMuffleBuffer,
            short[] MediumMuffleBuffer,
            List<float> PlaybackAmplitude);

        private static async Task<TaskResult> LoadSamples(VorbisReader reader)
        {
            reader.DecodedPosition = 0;

            int bufferSize = (int)reader.TotalSamples * reader.Channels;

            float[] floatHeavyBuffer = new float[bufferSize];
            var sampleBuffer = new short[bufferSize];
            var heavyMuffleBuffer = new short[bufferSize];
            var lightMuffleBuffer = new short[bufferSize];
            var meidumMuffleBuffer = new short[bufferSize];

            int readSamples = await Task.Run(() => reader.ReadSamples(floatHeavyBuffer, 0, bufferSize));

            var playbackAmplitude = new List<float>();
            for (int i = 0; i < bufferSize; i += reader.Channels * AMPLITUDE_SAMPLE_COUNT)
            {
                float maxAmplitude = 0.0f;
                for (int j = i; j < i + reader.Channels * AMPLITUDE_SAMPLE_COUNT; j++)
                {
                    if (j >= bufferSize) { break; }
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(floatHeavyBuffer[j]));
                }
                playbackAmplitude.Add(maxAmplitude);
            }

            CastBuffer(floatHeavyBuffer, sampleBuffer, readSamples);

            // Create a copy of floatHeavyBuffer for the light version.
            float[] floatLightBuffer = new float[bufferSize];
            Array.Copy(floatHeavyBuffer, floatLightBuffer, bufferSize);

            // Create a copy of floatHeavyBuffer for the me iumversion.
            float[] floatMediumBuffer = new float[bufferSize];
            Array.Copy(floatHeavyBuffer, floatMediumBuffer, bufferSize);

            // Create muffled buffers.
            MuffleBufferHeavy(floatHeavyBuffer, reader.SampleRate);
            MuffleBufferLight(floatLightBuffer, reader.SampleRate);
            MuffleBufferMedium(floatMediumBuffer, reader.SampleRate);

            // Cast muffled buffers to short[]
            CastBuffer(floatHeavyBuffer, heavyMuffleBuffer, readSamples);
            CastBuffer(floatLightBuffer, lightMuffleBuffer, readSamples);
            CastBuffer(floatMediumBuffer, meidumMuffleBuffer, readSamples);

            return new TaskResult(sampleBuffer, heavyMuffleBuffer, lightMuffleBuffer, meidumMuffleBuffer, playbackAmplitude);
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
            //MuffleBufferHeavy(floatHeavyBuffer, reader.Channels);
            CastBuffer(streamFloatBuffer, buffer, readSamples);

            return readSamples;
        }

        static void MuffleBufferHeavy(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, SoundproofWalls.Config.HeavyLowpassFrequency);
            filter.Process(buffer);
        }

        static void MuffleBufferLight(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, SoundproofWalls.Config.LightLowpassFrequency);
            filter.Process(buffer);
        }

        static void MuffleBufferMedium(float[] buffer, int sampleRate)
        {
            var filter = new LowpassFilter(sampleRate, SoundproofWalls.Config.MediumLowpassFrequency);
            filter.Process(buffer);
        }

        public override void InitializeAlBuffers()
        {
            if (buffers != null && SoundBuffers.BuffersGenerated < SoundBuffers.MaxBuffers)
            {
                FillAlBuffers();
            }
        }

        public override void FillAlBuffers()
        {
            if (Stream) { return; }
            if (sampleBuffer.Length == 0 || HeavyMuffleBuffer.Length == 0 || LightMuffleBuffer.Length == 0 || MediumMuffleBuffer.Length == 0) { return; }
            buffers ??= new ExtendedSoundBuffers(this);
            if (!buffers.RequestAlBuffers()) { return; }

            // Clear error state.
            int alError = Al.GetError();

            Al.BufferData(buffers.AlBuffer, ALFormat, sampleBuffer,
                sampleBuffer.Length * sizeof(short), SampleRate);

            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to set regular buffer data for non-streamed audio! " + Al.GetErrorString(alError));
            }

            Al.BufferData(buffers.AlHeavyMuffledBuffer, ALFormat, HeavyMuffleBuffer,
                HeavyMuffleBuffer.Length * sizeof(short), SampleRate);

            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                
                throw new Exception("Failed to set heavy muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError));
            }

            Al.BufferData(buffers.AlMediumMuffledBuffer, ALFormat, MediumMuffleBuffer,
                MediumMuffleBuffer.Length * sizeof(short), SampleRate);

            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to set medium muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError));
            }

            Al.BufferData(buffers.AlLightMuffledBuffer, ALFormat, LightMuffleBuffer,
                LightMuffleBuffer.Length * sizeof(short), SampleRate);

            alError = Al.GetError();
            if (alError != Al.NoError)
            {
                throw new Exception("Failed to set light muffled buffer data for non-streamed audio! " + Al.GetErrorString(alError));
            }
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
