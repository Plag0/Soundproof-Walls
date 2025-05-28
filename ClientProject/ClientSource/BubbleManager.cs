using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    public static class BubbleManager
    {
        public enum PlayerBubbleSoundState
        {
            DoNotPlayBubbles,
            PlayRadioBubbles,
            PlayLocalBubbles,
        }

        static double LastBubbleUpdateTime = 0.2f;

        // Custom sounds.
        static Sound? BubbleSound;
        static Sound? RadioBubbleSound;

        private static ConcurrentDictionary<Client, SoundChannel> clientBubbleChannels = new ConcurrentDictionary<Client, SoundChannel>();

        public static void Update()
        {
            // Bubble sound stuff.
            if (GameMain.IsMultiplayer && Timing.TotalTime > LastBubbleUpdateTime + 0.2f)
            {
                LastBubbleUpdateTime = (float)Timing.TotalTime;

                // In case a client disconnects while their bubble Channel is playing.
                foreach (var kvp in clientBubbleChannels)
                {
                    Client client = kvp.Key;

                    if (!GameMain.Client.ConnectedClients.Contains(client))
                    {
                        StopBubbleSound(client);
                    }
                }

                // Start, stop, or continue bubble sounds.
                foreach (Client client in GameMain.Client.ConnectedClients)
                {
                    UpdateClientBubbleSounds(client);
                }
            }
        }
        public static void Dispose()
        {
            foreach (var kvp in clientBubbleChannels)
            {
                StopBubbleSound(kvp.Key);
            }
            clientBubbleChannels.Clear();

            BubbleSound?.Dispose();
            RadioBubbleSound?.Dispose();
        }

        public static void Setup()
        {
            try
            {
                BubbleSound = GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.BubbleLocal]);
                RadioBubbleSound = GameMain.SoundManager.LoadSound(Plugin.CustomSoundPaths[Plugin.SoundPath.BubbleRadio]);
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"[SoundproofWalls] Failed to load bubble sounds: {ex.Message}");
            }
        }

        private static void StopBubbleSound(Client client)
        {
            if (clientBubbleChannels.TryGetValue(client, out SoundChannel? bubbleChannel) && bubbleChannel != null)
            {
                // The redundancy of these operations is an echo of the infinite bubble bug of old.
                ChannelInfoManager.RemovePitchedChannel(bubbleChannel);
                bubbleChannel.Looping = false;
                bubbleChannel.Gain = 0;
                bubbleChannel.Dispose();
                clientBubbleChannels.TryRemove(client, out _);
            }
        }

        public static bool RemoveBubbleSound(SoundChannel channel)
        {
            foreach(var kvp in clientBubbleChannels)
            {
                if (kvp.Value == channel)
                { 
                    return clientBubbleChannels.TryRemove(kvp.Key, out _); 
                }
            }
            return false;
        }

        public static bool ShouldPlayBubbles(Character character)
        {
            if (character == null) return false;

            Limb playerHead = Util.GetCharacterHead(character);
            Hull limbHull = playerHead.Hull;
            bool wearingDivingGear = Util.IsCharacterWearingDivingGear(character);
            bool oxygenReqMet = wearingDivingGear && character.Oxygen < 11 || !wearingDivingGear && character.OxygenAvailable < 96;
            bool ignoreBubbles = !ConfigManager.Config.DrowningBubblesEnabled || Util.StringHasKeyword(character.Name, ConfigManager.Config.BubbleIgnoredNames);
            bool headInWater = Util.SoundInWater(playerHead.Position, limbHull);
            bool canSpeak = character.SpeechImpediment < 100;

            if (!ignoreBubbles && oxygenReqMet && headInWater && canSpeak)
            {
                return true;
            }

            return false;
        }

        private static void UpdateClientBubbleSounds(Client client)
        {
            PlayerBubbleSoundState state = PlayerBubbleSoundState.DoNotPlayBubbles; // Default to not playing.

            Character? player = client.Character;
            Limb? playerHead = player?.AnimController?.GetLimb(LimbType.Head);
            SoundChannel? voiceChannel = client.VoipSound?.soundChannel;

            bool shouldStop = !ConfigManager.Config.Enabled || !ConfigManager.Config.DrowningBubblesEnabled || !Util.RoundStarted;

            if (shouldStop || voiceChannel == null || player == null || playerHead == null)
            {
                StopBubbleSound(client);
                return;
            }

            var messageType = Util.GetMessageType(client);
            bool isPlaying = clientBubbleChannels.TryGetValue(client, out SoundChannel? currentBubbleChannel) && currentBubbleChannel != null;
            bool soundMatches = true;
            if (isPlaying)
            {
                soundMatches = currentBubbleChannel.Sound.Filename == RadioBubbleSound?.Filename && messageType == ChatMessageType.Radio ||
                               currentBubbleChannel.Sound.Filename == BubbleSound?.Filename && messageType != ChatMessageType.Radio;
            }

            // Check if bubbles should be playing.
            if (soundMatches && ShouldPlayBubbles(player))
            {
                state = messageType == ChatMessageType.Radio ? PlayerBubbleSoundState.PlayRadioBubbles : PlayerBubbleSoundState.PlayLocalBubbles;
            }

            if (state == PlayerBubbleSoundState.DoNotPlayBubbles)
            {
                StopBubbleSound(client);
                return;
            }

            if (isPlaying) // Continue playing.
            {
                if (state == PlayerBubbleSoundState.PlayRadioBubbles) // Continue radio
                {
                    currentBubbleChannel.Position = GameMain.SoundManager.ListenerPosition;
                    currentBubbleChannel.FrequencyMultiplier = MathHelper.Lerp(0.85f, 1.15f, MathUtils.InverseLerp(0, 2, player.CurrentSpeed));
                }
                else if (state == PlayerBubbleSoundState.PlayLocalBubbles) // Continue local
                {
                    currentBubbleChannel.Position = new Vector3(playerHead.WorldPosition, 0);
                    currentBubbleChannel.FrequencyMultiplier = MathHelper.Lerp(1, 4, MathUtils.InverseLerp(0, 2, player.CurrentSpeed));
                }

                GameMain.ParticleManager.CreateParticle(
                    "bubbles",
                    playerHead.WorldPosition,
                    velocity: playerHead.LinearVelocity * 10,
                    rotation: 0,
                    playerHead.Hull);
            }
            else // New sound
            {
                SoundChannel? newBubbleChannel = null;
                if (state == PlayerBubbleSoundState.PlayRadioBubbles) // Start radio
                {
                    newBubbleChannel = SoundPlayer.PlaySound(RadioBubbleSound, new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), volume: 0.7f, freqMult: MathHelper.Lerp(0.85f, 1.15f, MathUtils.InverseLerp(0, 2, player.CurrentSpeed)), ignoreMuffling: true);
                }
                else if (state == PlayerBubbleSoundState.PlayLocalBubbles) // Start local
                {
                    newBubbleChannel = SoundPlayer.PlaySound(BubbleSound, playerHead.WorldPosition, volume: 1, range: 350, freqMult: MathHelper.Lerp(1, 4, MathUtils.InverseLerp(0, 2, player.CurrentSpeed)), ignoreMuffling: true);
                }

                if (newBubbleChannel != null)
                {
                    newBubbleChannel.Looping = true;
                    clientBubbleChannels[client] = newBubbleChannel;
                }
            }
        }
    }
}
