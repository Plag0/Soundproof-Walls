﻿using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Sounds;
using OpenAL;

namespace SoundproofWalls
{
    public sealed class ReducedSoundBuffers : IDisposable
    {
        private static readonly HashSet<uint> bufferPool = new HashSet<uint>();
#if OSX
        public const int MaxBuffers = 400;
#else
        public const int MaxBuffers = 32000;
#endif
        public static int BuffersGenerated { get; private set; } = 0;
        private readonly Sound sound;

        public uint AlBuffer { get; private set; } = 0;

        public ReducedSoundBuffers(Sound sound) { this.sound = sound; }
        public void Dispose()
        {
            if (AlBuffer != 0)
            {
                lock (bufferPool)
                {
                    bufferPool.Add(AlBuffer);
                }
            }
            AlBuffer = 0;
        }

        public static void ClearPool()
        {
            lock (bufferPool)
            {
                bufferPool.ForEach(b => Al.DeleteBuffer(b));
                bufferPool.Clear();
            }
            BuffersGenerated = 0;
        }

        public bool RequestAlBuffers()
        {
            if (AlBuffer != 0) { return false; }
            int alError;
            lock (bufferPool)
            {
                while (bufferPool.Count < 1 && BuffersGenerated < MaxBuffers)
                {
                    Al.GenBuffer(out uint newBuffer);
                    alError = Al.GetError();
                    if (alError != Al.NoError)
                    {
                        DebugConsole.AddWarning($"Error when generating sound buffer: {Al.GetErrorString(alError)}. {BuffersGenerated} buffer(s) were generated. No more sound buffers will be generated.");
                        BuffersGenerated = MaxBuffers;
                    }
                    else if (!Al.IsBuffer(newBuffer))
                    {
                        DebugConsole.AddWarning($"Error when generating sound buffer: result is not a valid buffer. {BuffersGenerated} buffer(s) were generated. No more sound buffers will be generated.");
                        BuffersGenerated = MaxBuffers;
                    }
                    else
                    {
                        bufferPool.Add(newBuffer);
                        BuffersGenerated++;
                        if (BuffersGenerated >= MaxBuffers)
                        {
                            DebugConsole.AddWarning($"{BuffersGenerated} buffer(s) were generated. No more sound buffers will be generated.");
                        }
                    }
                }

                if (bufferPool.Count >= 1)
                {
                    AlBuffer = bufferPool.First();
                    bufferPool.Remove(AlBuffer);
                    return true;
                }
            }

            //can't generate any more OpenAL buffers! we'll have to steal a buffer from someone...
            foreach (var s in sound.Owner.LoadedSounds)
            {
                if (s is not ReducedOggSound otherSound) { continue; }
                if (otherSound == sound) { continue; }
                if (otherSound.IsPlaying()) { continue; }
                if (otherSound.Buffers == null) { continue; }
                if (otherSound.Buffers.AlBuffer == 0) { continue; }

                // Dispose all channels that are holding
                // a reference to these buffers, otherwise
                // an INVALID_OPERATION error will be thrown
                // when attempting to set the buffer data later.
                // Having the sources not play is not enough,
                // as OpenAL assumes that you may want to call
                // alSourcePlay without reassigning the buffer.
                otherSound.Owner.KillChannels(otherSound);

                AlBuffer = otherSound.Buffers.AlBuffer;
                otherSound.Buffers.AlBuffer = 0;

                // For performance reasons, sift the current sound to
                // the end of the loadedSounds list, that way it'll
                // be less likely to have its buffers stolen, which
                // means less reuploads for frequently played sounds.
                sound.Owner.MoveSoundToPosition(sound, sound.Owner.LoadedSoundCount - 1);

                if (!Al.IsBuffer(AlBuffer))
                {
                    throw new Exception(sound.Filename + " has an invalid buffer!");
                }

                return true;
            }

            return false;
        }
    }
}
