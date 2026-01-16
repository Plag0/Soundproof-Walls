using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace SoundproofWalls
{
    public static class Util
    {
        public static bool RoundStarted { get { return GameMain.gameSession?.IsRunning ?? false; } }

        public static float SmoothStep(float current, float target, float maxStep)
        {
            float diff = target - current;
            float min = Math.Min(current, target);
            float max = Math.Max(current, target);
            return Math.Clamp(current + (MathF.Abs(diff) < maxStep ? diff : MathF.Sign(diff) * maxStep), min, max);
        }

        public static Limb GetCharacterHead(Character character)
        {
            // It's weird defaulting to the body but who knows what people might mod into the game.
            return character.AnimController.GetLimb(LimbType.Head) ?? character.AnimController.MainLimb;
        }

        public static Vector2 LocalizePosition(Vector2 worldPos, Submarine? sub)
        {
            Vector2 localPos = worldPos;
            if (sub != null)
            {
                localPos += -sub.WorldPosition + sub.HiddenSubPosition;
            }
            else
            {
                Submarine closestSub = Submarine.MainSub;
                if (closestSub == null) { return localPos; }

                float shortestDistToSub = Vector2.Distance(Listener.WorldPos, closestSub.WorldPosition);
                foreach (Submarine s in Submarine.MainSubs)
                {
                    if (s != null && 
                        s != closestSub && 
                        Vector2.Distance(Listener.WorldPos, s.WorldPosition) < shortestDistToSub)
                    { closestSub = s; }
                }

                localPos += -closestSub.WorldPosition + closestSub.HiddenSubPosition;
            }

            return localPos;
        }

        public static Vector2 WorldizePosition(Vector2 localPos, Submarine? sub)
        {
            Vector2 worldPos = localPos;
            if (sub != null)
            {
                worldPos += sub.WorldPosition - sub.HiddenSubPosition;
            }
            else
            {
                Submarine closestSub = Submarine.MainSub;
                if (closestSub == null) { return worldPos; }

                float shortestDistToSub = Vector2.Distance(Listener.WorldPos, closestSub.WorldPosition);
                foreach (Submarine s in Submarine.MainSubs)
                {
                    if (s != null &&
                        s != closestSub &&
                        Vector2.Distance(Listener.WorldPos, s.WorldPosition) < shortestDistToSub)
                    {
                        closestSub = s;
                    }
                }

                worldPos += closestSub.WorldPosition - closestSub.HiddenSubPosition;
            }

            return worldPos;
        }

        /// <summary>
        /// Warning: returns 0 if collection is empty.
        /// </summary>
        public static float GetMedian<T>(IEnumerable<T> values) where T : IComparable<T>
        {
            var copy = values.ToList();

            int count = copy.Count;
            if (count == 0) return 0;

            copy.Sort();

            int mid = count / 2;
            if (count % 2 != 0)
            {
                return Convert.ToSingle(copy[mid]);
            }

            return (Convert.ToSingle(copy[mid - 1]) + Convert.ToSingle(copy[mid])) * 0.5f;
        }

        public static double GetCompensatedBiquadFrequency(double muffleStrength, double minFrequency, double sampleRate = 48000.0)
        {
            double onePoleFrequency = GetEffectiveLowpassFrequency(muffleStrength, sampleRate);

            double nyquist = sampleRate / 2.0;

            // No muffling.
            if (onePoleFrequency >= nyquist)
            {
                return nyquist;
            }

            double originalRange = nyquist - 1.0;
            double newRange = nyquist - minFrequency;

            // Avoid division by zero if range is invalid.
            if (originalRange <= 0)
            {
                return nyquist;
            }

            // Calculate how far into the original range our value is as a percentage.
            double normalizedValue = (onePoleFrequency - 1.0) / originalRange;

            // Apply that percentage to the new range to get the remapped frequency.
            double remappedFrequency = minFrequency + (normalizedValue * newRange);

            double compensatedFrequency = remappedFrequency * Math.Sqrt(2);

            // Ensure the final frequency does not exceed the Nyquist frequency.
            return Math.Min(compensatedFrequency, nyquist);
        }

        public static double GetEffectiveLowpassFrequency(double muffleStrength, double sampleRate = 48000.0)
        {
            muffleStrength = Math.Clamp(muffleStrength, 0, 1);

            // Convert MuffleStrength to the equivalent GAINHF value.
            double gainHf = 1.0 - muffleStrength;

            // No muffle.
            if (gainHf >= 1.0)
            {
                return sampleRate / 2.0;
            }

            if (gainHf <= 0.0)
            {
                return 1.0;
            }

            // Calculate filter coefficient.
            double alpha = (1.0 - gainHf) / (1.0 + gainHf);

            // Calculate cutoff frequency from coefficient.
            double arccosArg = (2.0 * alpha) / (1.0 + alpha * alpha);
            arccosArg = Math.Max(-1.0, Math.Min(1.0, arccosArg));
            double cutoffFrequency = (sampleRate / (2.0 * Math.PI)) * Math.Acos(arccosArg);

            return cutoffFrequency;
        }

        // Get a client's messageType (same implementation seen in VoipClient_Read method).
        public static ChatMessageType GetMessageType(Client client, out WifiComponent? senderRadio)
        {
            senderRadio = null;
            var messageType = ChatMessageType.Default;
            if (Character.Controlled != null)
            {
                messageType =
                    !client.VoipQueue.ForceLocal &&
                    ChatMessage.CanUseRadio(client.Character, out senderRadio) &&
                    ChatMessage.CanUseRadio(Character.Controlled, out var recipientRadio) &&
                    senderRadio.CanReceive(recipientRadio) ?
                        ChatMessageType.Radio : ChatMessageType.Default;
            }
            else
            {
                messageType =
                    !client.VoipQueue.ForceLocal &&
                    ChatMessage.CanUseRadio(client.Character, out senderRadio) ?
                        ChatMessageType.Radio : ChatMessageType.Default;
            }

            return messageType;
        }

        public static bool IsCharacterWearingDivingGear(Character character)
        {
            Identifier id = new Identifier("diving");
            Item outerItem = character.Inventory.GetItemInLimbSlot(InvSlotType.OuterClothes);
            Item headItem = character.Inventory.GetItemInLimbSlot(InvSlotType.Head);

            return (outerItem != null && outerItem.HasTag(id)) || (headItem != null && headItem.HasTag(id));
        }

        public static bool IsDoorClosed(Door door)
        {
            if (door != null && !door.IsBroken && !door.IsFullyOpen && door.Item.Condition > 0) 
            {
                if (!ConfigManager.Config.TraverseWaterDucts && door.Item.HasTag("ductblock")) { return true; }

                bool isClosingOrClosed = (door.PredictedState.HasValue) ? !door.PredictedState.Value : door.IsClosed;
                return isClosingOrClosed && door.OpenState < ConfigManager.Config.OpenDoorThreshold;
            }
            return false;
        }

        public static Gap? GetClosestGapToPos(Hull? soundHull, Vector2 localPos)
        {
            if (soundHull == null) { return null; }

            Gap? closestGap = null;
            float bestDistance = float.MaxValue;
            int j = 0;

            foreach (Gap g in soundHull.ConnectedGaps)
            {
                if (g.ConnectedDoor != null)
                {
                    float distToDoor = Vector2.Distance(localPos, g.ConnectedDoor.Item.Position);
                    if (distToDoor < bestDistance) { closestGap = g; bestDistance = distToDoor; }
                }
                j++;
            }

            return closestGap;
        }

        public static void GetAdjacentHulls(Hull currentHull, HashSet<Hull> connectedHulls, ref int step, int searchDepth, bool respectClosedGaps = false)
        {
            connectedHulls.Add(currentHull);
            if (step > searchDepth)
            {
                return;
            }

            foreach (Gap gap in currentHull.ConnectedGaps)
            {
                if (gap == null) { continue; }

                // For doors.
                if (gap.ConnectedDoor != null)
                {
                    if (respectClosedGaps && IsDoorClosed(gap.ConnectedDoor)) { continue; }
                }
                // For holes in hulls.
                else if (respectClosedGaps && gap.Open < ConfigManager.Config.OpenWallThreshold)
                {
                    continue;
                }

                for (int i = 0; i < 2 && i < gap.linkedTo.Count; i++)
                {
                    if (gap.linkedTo[i] is Hull hull && !connectedHulls.Contains(hull))
                    {
                        step++;
                        GetAdjacentHulls(hull, connectedHulls, ref step, searchDepth, respectClosedGaps);
                    }
                }
            }
        }

        public static bool StringHasKeyword(string inputString, HashSet<string> set, string? exclude = null, string? include = null)
        {
            string s = inputString.ToLower();

            if (exclude != null && s.Contains(exclude.ToLower()))
                return false;

            if (include != null && s.Contains(include.ToLower()))
                return true;

            foreach (string keyword in set)
            {
                if (s.Contains(keyword.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        public static Vector2 GetSoundChannelWorldPos(SoundChannel channel)
        {
            if (channel == null) { return Vector2.Zero; }
            return channel.Position.HasValue ? new Vector2(channel.Position.Value.X, channel.Position.Value.Y) : Listener.WorldPos;
        }

        // Returns true if the given localised position is in water (not accurate when using WorldPositions).
        public static bool SoundInWater(Vector2 soundPos, Hull soundHull)
        {
            float epsilon = 1.0f;
            if (soundHull?.WaterPercentage <= 0) { return false; }
            return soundHull == null || soundHull.WaterPercentage >= 100 || soundHull.WaterVolume > 0 && soundPos.Y < soundHull.Surface - epsilon;
        }

        // Should be used if Listener.WorldPos is a position other than the listener's character and HearLocalVoiceOnFocusedTarget is enabled.
        // Assumes the character passed in and the player character are not null.
        public static Vector2 GetTargetOffsetVoicePosition(Character speakingCharacter)
        {
            Vector2 voiceWorldPos = GetCharacterHead(speakingCharacter).WorldPosition;
            Vector2 listenerCharacterWorldPos = GetCharacterHead(Character.Controlled).WorldPosition;
            Vector2 offset = voiceWorldPos - listenerCharacterWorldPos;
            return Listener.WorldPos + offset;
        }

        public static string GetModDirectory()
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

            while (!path.EndsWith("3153737715") && !path.EndsWith("Soundproof Walls 2.0"))
            {
                path = Directory.GetParent(path)?.FullName ?? "";
                if (path == "") break;
            }

            return path;
        }

        public static void ResizeSoundManagerPools(int newSize)
        {
            SoundManager soundManager = GameMain.SoundManager;
            var playingChannelsField = AccessTools.Field(typeof(SoundManager), "playingChannels");
            var sourcePoolsField = AccessTools.Field(typeof(SoundManager), "sourcePools");
            var playingChannels = (SoundChannel[][])playingChannelsField.GetValue(soundManager);
            var sourcePools = (SoundSourcePool[])sourcePoolsField.GetValue(soundManager);

            SoundChannel[] defaultChannels = playingChannels[(int)SoundManager.SourcePoolIndex.Default];
            lock (defaultChannels)
            {
                for (int i = 0; i < defaultChannels.Length; i++)
                {
                    defaultChannels[i]?.Dispose();
                    defaultChannels[i] = null;
                }
            }
            sourcePools[(int)SoundManager.SourcePoolIndex.Default]?.Dispose();

            ChannelInfoManager.SourceCount = newSize;

            // Re-initialize the arrays with the new size
            playingChannels[(int)SoundManager.SourcePoolIndex.Default] = new SoundChannel[newSize];
            sourcePools[(int)SoundManager.SourcePoolIndex.Default] = new SoundSourcePool(newSize);
        }

        // Compatibility with ReSound.
        public static void StopResound(MoonSharp.Interpreter.DynValue Resound)
        {
            if (Resound.Type == MoonSharp.Interpreter.DataType.Table)
            {
                MoonSharp.Interpreter.Table resoundTable = Resound.Table;
                MoonSharp.Interpreter.DynValue stopFunction = resoundTable.Get("StopMod");
                if (stopFunction.Type == MoonSharp.Interpreter.DataType.Function)
                {
                    GameMain.LuaCs.Lua.Call(stopFunction);
                }
            }
        }

        public static void StartResound(MoonSharp.Interpreter.DynValue Resound)
        {
            if (Resound.Type == MoonSharp.Interpreter.DataType.Table)
            {
                MoonSharp.Interpreter.Table resoundTable = Resound.Table;
                MoonSharp.Interpreter.DynValue startFunction = resoundTable.Get("StartMod");
                if (startFunction.Type == MoonSharp.Interpreter.DataType.Function)
                {
                    GameMain.LuaCs.Lua.Call(startFunction);
                }
            }
        }

        // Dispose all soundchannels.
        public static void StopPlayingChannels()
        {
            for (int i = 0; i < GameMain.SoundManager.playingChannels.Length; i++)
            {
                lock (GameMain.SoundManager.playingChannels[i])
                {
                    for (int j = 0; j < GameMain.SoundManager.playingChannels[i].Length; j++)
                    {
                        if (GameMain.SoundManager.playingChannels[i][j] != null)
                        {
                            GameMain.SoundManager.playingChannels[i][j].Dispose();
                            GameMain.SoundManager.playingChannels[i][j] = null;
                        }
                    }
                }
            }
        }
    }
}
