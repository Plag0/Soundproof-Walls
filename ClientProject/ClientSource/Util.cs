using Barotrauma.Items.Components;
using Barotrauma.Sounds;
using Barotrauma;
using System.Reflection;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace SoundproofWalls
{
    public static class Util
    {
        public static Config Config { get { return ConfigManager.Config; } }
        public static bool RoundStarted { get { return GameMain.gameSession?.IsRunning ?? false; } }

        // Returns true if there's a mismatch in any of the lowpass frequencies or the effect mode is changed.
        public static bool ShouldReloadSounds(Config newConfig, Config? oldConfig = null)
        {
            if (oldConfig == null) { oldConfig = Config; }

            // For adding/removing reduced buffers with the RemoveUnusedBuffers setting.
            bool oldReducedBuffersEnabled = oldConfig.DynamicFx && oldConfig.RemoveUnusedBuffers;
            bool newReducedBuffersEnabled = newConfig.DynamicFx && newConfig.RemoveUnusedBuffers;
            bool shouldReloadReducedBuffers = oldReducedBuffersEnabled && !newReducedBuffersEnabled || !oldReducedBuffersEnabled && newReducedBuffersEnabled;

            // For adding/removing extended buffers.
            bool shouldReloadExtendedBuffers = oldConfig.StaticFx && !newConfig.StaticFx || !oldConfig.StaticFx && newConfig.StaticFx;

            // For frequency changes in static/classic mode.
            double vanillaFreq = SoundPlayer.MuffleFilterFrequency;

            bool oldStaticFx = oldConfig.Enabled && oldConfig.StaticFx;
            bool newStaticFx = newConfig.Enabled && newConfig.StaticFx;

            bool oldUsingMuffleBuffer = oldConfig.Enabled && (!oldConfig.DynamicFx || oldConfig.DynamicFx && !oldConfig.RemoveUnusedBuffers);
            bool newUsingMuffleBuffer = newConfig.Enabled && (!newConfig.DynamicFx || newConfig.DynamicFx && !newConfig.RemoveUnusedBuffers);

            double oldHeavyFreq = oldUsingMuffleBuffer ? oldConfig.HeavyLowpassFrequency : vanillaFreq;
            double oldMediumFreq = oldStaticFx ? oldConfig.MediumLowpassFrequency : vanillaFreq;
            double oldLightFreq = oldStaticFx ? oldConfig.LightLowpassFrequency : vanillaFreq;

            double newHeavyFreq = newUsingMuffleBuffer ? newConfig.HeavyLowpassFrequency : vanillaFreq;
            double newMediumFreq = newStaticFx ? newConfig.MediumLowpassFrequency : vanillaFreq;
            double newLightFreq = newStaticFx ? newConfig.LightLowpassFrequency : vanillaFreq;

            return shouldReloadExtendedBuffers || shouldReloadReducedBuffers ||
                   oldHeavyFreq != newHeavyFreq ||
                   oldMediumFreq != newMediumFreq ||
                   oldLightFreq != newLightFreq;
        }

        public static bool ShouldUpdateSoundInfo(Config newConfig, Config? oldConfig = null)
        {
            if (oldConfig == null) { oldConfig = Config; }

            return  !oldConfig.CustomSounds.SetEquals(newConfig.CustomSounds) ||
                    !oldConfig.IgnoredSounds.SetEquals(newConfig.IgnoredSounds) ||
                    !oldConfig.PitchIgnoredSounds.SetEquals(newConfig.PitchIgnoredSounds) ||
                    !oldConfig.LowpassIgnoredSounds.SetEquals(newConfig.LowpassIgnoredSounds) ||
                    !oldConfig.LowpassForcedSounds.SetEquals(newConfig.LowpassForcedSounds) ||
                    !oldConfig.ContainerIgnoredSounds.SetEquals(newConfig.ContainerIgnoredSounds) ||
                    !oldConfig.PathIgnoredSounds.SetEquals(newConfig.PathIgnoredSounds) ||
                    !oldConfig.PropagatingSounds.SetEquals(newConfig.PropagatingSounds) ||
                    !oldConfig.ReverbIgnoredSounds.SetEquals(newConfig.ReverbIgnoredSounds) ||
                    !oldConfig.ReverbForcedSounds.SetEquals(newConfig.ReverbForcedSounds) ||
                    !oldConfig.SurfaceIgnoredSounds.SetEquals(newConfig.SurfaceIgnoredSounds) ||
                    !oldConfig.SubmersionIgnoredSounds.SetEquals(newConfig.SubmersionIgnoredSounds);
        }

        private static Sound GetNewSound(Sound oldSound)
        {
            if (oldSound.XElement != null)
            {
                return GameMain.SoundManager.LoadSound(oldSound.XElement, oldSound.Stream, oldSound.Filename);
            }
            else
            {
                return GameMain.SoundManager.LoadSound(oldSound.Filename, oldSound.Stream);
            }
        }

        public static void HardDispose(this SoundChannel channel)
        {
            channel.Far = channel.Sound.BaseFar;
            channel.Near = channel.Sound.BaseNear;
            channel.Looping = false;
            channel.FrequencyMultiplier = 1;
            channel.Gain = 0;
            channel.Dispose();
        }

        public static float SmoothStep(float current, float target, float maxStep)
        {
            float diff = target - current;
            return current + (MathF.Abs(diff) < maxStep ? diff : MathF.Sign(diff) * maxStep);
        }

        private static bool ShouldSkipSound(Sound? sound, bool starting, bool stopping)
        {
            if (sound == null)
                return true;

            bool isReduced = sound is ReducedOggSound;
            bool isExtended = sound is ExtendedOggSound;

            if ((starting && Config.DynamicFx && isReduced) ||
                (starting && Config.StaticFx && isExtended) ||
                (stopping && Config.DynamicFx && !isReduced) ||
                (stopping && Config.StaticFx && !isExtended))
            {
                return true;
            }

            return false;
        }

        private static void ReloadRoundSounds(Dictionary<string, Sound> updatedSounds, bool starting = false, bool stopping = false)
        {
            int t = 0;
            int i = 0;
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (RoundSound roundSound in RoundSound.roundSounds)
            {
                i++;
                Sound? oldSound = roundSound.Sound;

                if (ShouldSkipSound(oldSound, starting, stopping))
                    continue;

                if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                {
                    newSound = GetNewSound(oldSound);
                    updatedSounds.Add(oldSound.Filename, newSound);
                    t++;
                }

                roundSound.Sound = newSound;
                oldSound.Dispose();
            }

            sw.Stop();
            LuaCsLogger.Log($"Created {t} new round sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }
        private static void AllocateCharacterSounds(Dictionary<string, Sound> updatedSounds, bool starting = false, bool stopping = false)
        {
            int t = 0;
            int i = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead) { continue; }
                foreach (CharacterSound characterSound in character.sounds)
                {
                    i++;
                    Sound? oldSound = characterSound.roundSound.Sound;

                    if (ShouldSkipSound(oldSound, starting, stopping))
                        continue;

                    if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                    {
                        newSound = GetNewSound(oldSound);
                        updatedSounds.Add(oldSound.Filename, newSound);
                        t++;
                    }

                    characterSound.roundSound.Sound = newSound;
                    oldSound.Dispose();
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"Created {t} new character sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }

        private static void AllocateComponentSounds(Dictionary<string, Sound> updatedSounds, bool starting = false, bool stopping = false)
        {
            int t = 0;
            int i = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (Item item in Item.ItemList)
            {
                foreach (ItemComponent itemComponent in item.Components)
                {
                    foreach (var kvp in itemComponent.sounds)
                    {
                        itemComponent.StopSounds(kvp.Key);
                        foreach (ItemSound itemSound in kvp.Value)
                        {
                            i++;
                            Sound? oldSound = itemSound.RoundSound.Sound;

                            if (ShouldSkipSound(oldSound, starting, stopping))
                                continue;

                            if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                            {
                                newSound = GetNewSound(oldSound);
                                updatedSounds.Add(oldSound.Filename, newSound);
                                t++;
                            }

                            itemSound.RoundSound.Sound = newSound;
                            oldSound.Dispose();
                        }
                    }
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"Created {t} new comp sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }

        private static void AllocateStatusEffectSounds(Dictionary<string, Sound> updatedSounds, bool starting = false, bool stopping = false)
        {
            int t = 0;
            int i = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (StatusEffect statusEffect in StatusEffect.ActiveLoopingSounds)
            {
                foreach (RoundSound roundSound in statusEffect.Sounds)
                {
                    i++;
                    Sound? oldSound = roundSound.Sound;

                    if (ShouldSkipSound(oldSound, starting, stopping))
                        continue;

                    if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                    {
                        newSound = GetNewSound(oldSound);
                        updatedSounds.Add(oldSound.Filename, newSound);
                        t++;
                    }

                    roundSound.Sound = newSound;
                    oldSound.Dispose();
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"Created {t} new status effect sounds. Scanned {i} ({sw.ElapsedMilliseconds} ms)");
        }

        private static void ReloadPrefabSounds(Dictionary<string, Sound> updatedSounds, bool starting = false, bool stopping = false)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            int t = 0;
            foreach (SoundPrefab soundPrefab in SoundPrefab.Prefabs)
            {
                Sound oldSound = soundPrefab.Sound;

                if (ShouldSkipSound(oldSound, starting, stopping))
                    continue;

                if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
                {
                    newSound = GetNewSound(oldSound);
                    updatedSounds.Add(oldSound.Filename, newSound);
                    t++;
                }

                soundPrefab.Sound = newSound;
                oldSound.Dispose();
                t++;
            }

            sw.Stop();
            LuaCsLogger.Log($"Created {t} new Sound prefab sounds ({sw.ElapsedMilliseconds} ms)");
        }

        // Compatibility with ReSound.
        private static void StopResound(MoonSharp.Interpreter.DynValue Resound)
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

        private static void StartResound(MoonSharp.Interpreter.DynValue Resound)
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
                        GameMain.SoundManager.playingChannels[i][j]?.Dispose();
                    }
                }
            }
        }

        public static void ReloadSounds(bool starting = false, bool stopping = false)
        {
            LuaCsLogger.Log("Soundproof Walls: ReloadSounds() started running.");

            MoonSharp.Interpreter.DynValue Resound = GameMain.LuaCs.Lua.Globals.Get("Resound");
            // ReSound has its own code to stop at the end of the round but it needs to happen here and now before SPW.
            StopResound(Resound);

            ChannelInfoManager.ClearChannelInfo();
            StopPlayingChannels();

            // Cache sounds that have already been updated.
            Dictionary<string, Sound> updatedSounds = new Dictionary<string, Sound>();

            ReloadRoundSounds(updatedSounds, starting, stopping);
            ReloadPrefabSounds(updatedSounds, starting, stopping);
            AllocateStatusEffectSounds(updatedSounds, starting, stopping);
            AllocateCharacterSounds(updatedSounds, starting, stopping);
            AllocateComponentSounds(updatedSounds, starting, stopping);

            StartResound(Resound);

            LuaCsLogger.Log("Soundproof Walls: ReloadSounds() stopped running.");
        }

        public static Limb GetCharacterHead(Character character)
        {
            // It's weird defaulting to the body but who knows what people might mod into the game.
            return character.AnimController.GetLimb(LimbType.Head) ?? character.AnimController.MainLimb;
        }

        public static Vector2 LocalizePosition(Vector2 worldPos, Hull? posHull)
        {
            Vector2 localPos = worldPos;
            if (posHull?.Submarine != null)
            {
                localPos += -posHull.Submarine.WorldPosition + posHull.Submarine.HiddenSubPosition;
            }
            else
            {
                Submarine closestSub = Submarine.MainSub;
                if (closestSub == null) { return localPos; }

                float shortestDistToSub = Vector2.Distance(Listener.WorldPos, closestSub.WorldPosition);
                foreach (Submarine sub in Submarine.MainSubs)
                {
                    if (sub != null && 
                        sub != closestSub && 
                        Vector2.Distance(Listener.WorldPos, sub.WorldPosition) < shortestDistToSub)
                    { closestSub = sub; }
                }

                localPos += -closestSub.WorldPosition + closestSub.HiddenSubPosition;
            }

            return localPos;
        }

        public static Vector2 GetRelativeDirection(Vector2 listenerPosition, Vector2 sourcePosition)
        {
            Vector2 delta = sourcePosition - listenerPosition;

            if (delta == Vector2.Zero)
            {
                return Vector2.Zero;
            }

            return Vector2.Normalize(delta);
        }

        // Get a client's messageType (same implementation seen in VoipClient_Read method).
        public static ChatMessageType GetMessageType(Client client)
        {
            bool spectating = Character.Controlled == null;
            WifiComponent senderRadio = null;
            var messageType = ChatMessageType.Default;
            if (!spectating)
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

        // Copy of the vanilla GetConnectedHulls with minor adjustments to align with Soundproof Walls searching algorithms.
        public static HashSet<Hull> GetConnectedHulls(Hull? startHull, bool includingThis, int? searchDepth = null, bool respectClosedGaps = false)
        {
            if (startHull == null) { return new HashSet<Hull>(); }

            startHull.adjacentHulls.Clear();
            int step = 0;
            int valueOrDefault = searchDepth.GetValueOrDefault();
            if (!searchDepth.HasValue)
            {
                valueOrDefault = 100;
                searchDepth = valueOrDefault;
            }

            GetAdjacentHulls(startHull, startHull.adjacentHulls, ref step, searchDepth.Value, respectClosedGaps);
            if (!includingThis)
            {
                startHull.adjacentHulls.Remove(startHull);
            }

            return startHull.adjacentHulls;
        }

        public static bool IsDoorClosed(Door door)
        {
            if (door != null && !door.IsBroken) 
            {
                if (!ConfigManager.Config.TraverseWaterDucts && door.Item.HasTag("ductblock")) { return true; }

                bool isClosingOrClosed = (door.PredictedState.HasValue) ? !door.PredictedState.Value : door.IsClosed;
                return isClosingOrClosed && door.OpenState < ConfigManager.Config.OpenDoorThreshold;
            }
            return false;
        }

        public static Gap? GetDoorSoundGap(bool isDoorSound, Hull? soundHull, Vector2 soundPos)
        {
            if (!isDoorSound || soundHull == null) { return null; }

            Gap? closestGap = null; // Closest gap to the sound will be the gap with the door the sound came from.
            float bestDistance = float.MaxValue;
            int j = 0;

            // Get gap closest to sound.
            foreach (Gap g in soundHull.ConnectedGaps)
            {
                if (g.ConnectedDoor != null)
                {
                    float distToDoor = Vector2.Distance(soundPos, g.ConnectedDoor.Item.Position);
                    if (distToDoor < bestDistance) { closestGap = g; bestDistance = distToDoor; }
                }
                j++;
            }

            return closestGap;
        }

        public static bool IsPathThroughOwnDoor(bool isDoorSound, Hull soundHull, Vector2 soundPos)
        {
            Gap? closestGap = GetDoorSoundGap(isDoorSound, soundHull, soundPos);
            if (closestGap != null)
            {
                for (int i = 0; i < 2 && i < closestGap.linkedTo.Count; i++)
                {
                    if (closestGap.linkedTo[i] is Hull hull && Listener.ConnectedHulls.Contains(hull))
                    {
                        return true;
                    }
                }
            }

            return false;
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
                else if (respectClosedGaps && gap.Open < Config.OpenWallThreshold)
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

        // TODO Pretty bad. Redo this?
        public static bool IsPathToFlow()
        {
            Character character = Character.Controlled;
            if (character == null || character.CurrentHull == null) { return true; }

            return GetPathToFlow(character.CurrentHull, new HashSet<Hull>());
        }
        public static bool GetPathToFlow(Hull startHull, HashSet<Hull> connectedHulls)
        {
            Vector2 listenerPos = Character.Controlled.WorldPosition;

            foreach (Gap gap in startHull.ConnectedGaps)
            {
                Vector2 diff = gap.WorldPosition - listenerPos;

                if (Math.Abs(diff.X) >= SoundPlayer.FlowSoundRange && Math.Abs(diff.Y) >= SoundPlayer.FlowSoundRange) { continue; }
                if (gap.Open < 0.01f || gap.LerpedFlowForce.LengthSquared() < 100.0f) { continue; }
                float gapFlow = Math.Abs(gap.LerpedFlowForce.X) + Math.Abs(gap.LerpedFlowForce.Y) * 2.5f;
                if (!gap.IsRoomToRoom) { gapFlow *= 2.0f; }
                if (gapFlow >= 10.0f) { return true; }

                for (int i = 0; i < 2 && i < gap.linkedTo.Count; i++)
                {
                    if (gap.linkedTo[i] is Hull newStartHull && !connectedHulls.Contains(newStartHull))
                    {
                        bool path = GetPathToFlow(newStartHull, connectedHulls);
                        if (path)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
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
        public static bool SoundInWater(Vector2 soundPos, Hull? soundHull)
        {
            float epsilon = 30.0f;
            if (soundHull?.WaterPercentage <= 0) { return false; }
            return soundHull == null || soundHull.WaterVolume > 0 && soundPos.Y < soundHull.Surface - epsilon;
        }

        public static string GetModDirectory()
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

            while (!path.EndsWith("3153737715") && !path.EndsWith("Soundproof Walls"))
            {
                path = Directory.GetParent(path)?.FullName ?? "";
                if (path == "") break;
            }

            return path;
        }
    }
}
