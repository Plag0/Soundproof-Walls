using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System.Reflection;
using System.Text.Json;

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
            bool shouldReloadReducedBuffers = oldReducedBuffersEnabled != newReducedBuffersEnabled;

            // For adding/removing extended buffers.
            bool changedReverbEffect = newConfig.StaticFx && newConfig.StaticReverbEnabled &&
                (oldConfig.StaticReverbEnabled != newConfig.StaticReverbEnabled ||
                oldConfig.StaticReverbDuration != newConfig.StaticReverbDuration ||
                oldConfig.StaticReverbWetDryMix != newConfig.StaticReverbWetDryMix);
            bool shouldReloadExtendedBuffers = oldConfig.StaticFx != newConfig.StaticFx || changedReverbEffect;

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

            return !oldConfig.CustomSounds.SetEquals(newConfig.CustomSounds) ||
                    !oldConfig.IgnoredSounds.SetEquals(newConfig.IgnoredSounds) ||
                    !oldConfig.SurfaceIgnoredSounds.SetEquals(newConfig.SurfaceIgnoredSounds) ||
                    !oldConfig.SubmersionIgnoredSounds.SetEquals(newConfig.SubmersionIgnoredSounds) ||
                    !oldConfig.PropagatingSounds.SetEquals(newConfig.PropagatingSounds) ||
                    !oldConfig.PathIgnoredSounds.SetEquals(newConfig.PathIgnoredSounds) ||
                    !oldConfig.PitchIgnoredSounds.SetEquals(newConfig.PitchIgnoredSounds) ||
                    !oldConfig.LowpassForcedSounds.SetEquals(newConfig.LowpassForcedSounds) ||
                    !oldConfig.LowpassIgnoredSounds.SetEquals(newConfig.LowpassIgnoredSounds) ||
                    !oldConfig.ReverbForcedSounds.SetEquals(newConfig.ReverbForcedSounds) ||
                    !oldConfig.WaterReverbIgnoredSounds.SetEquals(newConfig.WaterReverbIgnoredSounds) ||
                    !oldConfig.AirReverbIgnoredSounds.SetEquals(newConfig.AirReverbIgnoredSounds) ||
                    !oldConfig.ContainerIgnoredSounds.SetEquals(newConfig.ContainerIgnoredSounds) ||
                    !oldConfig.HydrophoneMuffleIgnoredSounds.SetEquals(newConfig.HydrophoneMuffleIgnoredSounds) ||
                    !oldConfig.HydrophoneVisualIgnoredSounds.SetEquals(newConfig.HydrophoneVisualIgnoredSounds);
        }

        public static bool ShouldUpdateSoundInfo(Dictionary<ContentPackage, HashSet<CustomSound>> dict1, Dictionary<ContentPackage, HashSet<CustomSound>> dict2)
        {
            if (dict1 == dict2) { return false; }
            if (dict1 == null || dict2 == null) { return true; }
            if (dict1.Count != dict2.Count) { return true; }

            // Check each key and its corresponding hash set.
            foreach (var kvp in dict1)
            {
                ContentPackage key = kvp.Key;
                HashSet<CustomSound> set1 = kvp.Value;

                // Check if the second dictionary has the same key.
                // If not, or if the hash sets don't match, they're not equal.
                if (!dict2.TryGetValue(key, out HashSet<CustomSound> set2) || 
                    JsonSerializer.Serialize(set1) != JsonSerializer.Serialize(set2))
                {
                    return true;
                }
            }

            return false;
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

            public static float SmoothStep(float current, float target, float maxStep)
            {
                float diff = target - current;
                return current + (MathF.Abs(diff) < maxStep ? diff : MathF.Sign(diff) * maxStep);
            }

        private static bool ShouldSkipSound(Sound? sound, bool starting, bool stopping)
        {
            if (sound == null || sound.Stream)
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
        
        private static Sound ReplaceAndMarkForDisposal(Sound oldSound, Dictionary<string, Sound> updatedSounds, List<Sound> soundsToDispose, ref int newBufferCounter)
        {
            if (!updatedSounds.TryGetValue(oldSound.Filename, out Sound? newSound))
            {
                newSound = GetNewSound(oldSound);
                updatedSounds.Add(oldSound.Filename, newSound);
                newBufferCounter++;
            }
            soundsToDispose.Add(oldSound);
            return newSound;
        }

        private static void ReloadRoundSounds(Dictionary<string, Sound> updatedSounds, List<Sound> soundsToDispose, bool starting = false, bool stopping = false)
        {
            int scannedSoundCount = 0;
            int newSoundCount = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (RoundSound roundSound in RoundSound.roundSounds)
            {
                scannedSoundCount++;
                Sound? oldSound = roundSound.Sound;

                if (ShouldSkipSound(oldSound, starting, stopping))
                    continue;

                roundSound.Sound = ReplaceAndMarkForDisposal(oldSound, updatedSounds, soundsToDispose, ref newSoundCount);
            }

            sw.Stop();
            LuaCsLogger.Log($"[SoundproofWalls] Scanned {scannedSoundCount} RoundSounds and created {newSoundCount} new buffers. ({sw.ElapsedMilliseconds} ms)");
        }
        private static void AllocateCharacterSounds(Dictionary<string, Sound> updatedSounds, List<Sound> soundsToDispose, bool starting = false, bool stopping = false)
        {
            int scannedSoundCount = 0;
            int newSoundCount = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead) { continue; }
                foreach (CharacterSound characterSound in character.sounds)
                {
                    scannedSoundCount++;
                    Sound? oldSound = characterSound.roundSound.Sound;

                    if (ShouldSkipSound(oldSound, starting, stopping))
                        continue;

                    characterSound.roundSound.Sound = ReplaceAndMarkForDisposal(oldSound, updatedSounds, soundsToDispose, ref newSoundCount);
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"[SoundproofWalls] Scanned {scannedSoundCount} CharacterSounds and created {newSoundCount} new buffers. ({sw.ElapsedMilliseconds} ms)");
        }

        private static void AllocateComponentSounds(Dictionary<string, Sound> updatedSounds, List<Sound> soundsToDispose, bool starting = false, bool stopping = false)
        {
            int scannedSoundCount = 0;
            int newSoundCount = 0;

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
                            scannedSoundCount++;
                            Sound? oldSound = itemSound.RoundSound.Sound;

                            if (ShouldSkipSound(oldSound, starting, stopping))
                                continue;

                            itemSound.RoundSound.Sound = ReplaceAndMarkForDisposal(oldSound, updatedSounds, soundsToDispose, ref newSoundCount);
                        }
                    }
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"[SoundproofWalls] Scanned {scannedSoundCount} ItemComponent sounds and created {newSoundCount} new buffers. ({sw.ElapsedMilliseconds} ms)");
        }

        private static void AllocateStatusEffectSounds(Dictionary<string, Sound> updatedSounds, List<Sound> soundsToDispose, bool starting = false, bool stopping = false)
        {
            int scannedSoundCount = 0;
            int newSoundCount = 0;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            foreach (StatusEffect statusEffect in StatusEffect.ActiveLoopingSounds)
            {
                foreach (RoundSound roundSound in statusEffect.Sounds)
                {
                    scannedSoundCount++;
                    Sound? oldSound = roundSound.Sound;

                    if (ShouldSkipSound(oldSound, starting, stopping))
                        continue;

                    roundSound.Sound = ReplaceAndMarkForDisposal(oldSound, updatedSounds, soundsToDispose, ref newSoundCount);
                }
            }
            sw.Stop();
            LuaCsLogger.Log($"[SoundproofWalls] Scanned {scannedSoundCount} StatusEffect sounds and created {newSoundCount} new buffers. ({sw.ElapsedMilliseconds} ms)");
        }

        private static void ReloadPrefabSounds(Dictionary<string, Sound> updatedSounds, List<Sound> soundsToDispose, bool starting = false, bool stopping = false)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            int scannedSoundCount = 0;
            int newSoundCount = 0;

            foreach (SoundPrefab soundPrefab in SoundPrefab.Prefabs)
            {
                scannedSoundCount++;
                Sound oldSound = soundPrefab.Sound;

                if (ShouldSkipSound(oldSound, starting, stopping))
                    continue;

                soundPrefab.Sound = ReplaceAndMarkForDisposal(oldSound, updatedSounds, soundsToDispose, ref newSoundCount);
            }

            sw.Stop();
            LuaCsLogger.Log($"[SoundproofWalls] Scanned {scannedSoundCount} SoundPrefab sounds and created {newSoundCount} new buffers. ({sw.ElapsedMilliseconds} ms)");
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
                        if (GameMain.SoundManager.playingChannels[i][j] != null)
                        {
                            GameMain.SoundManager.playingChannels[i][j].Dispose();
                            GameMain.SoundManager.playingChannels[i][j] = null;
                        }
                    }
                }
            }
        }

        public static void UpdateAITarget()
        {
            float soundMult = Config.AITargetSoundRangeMultiplierMaster;
            float sightMult = Config.AITargetSightRangeMultiplierMaster;

            foreach (Item item in Item.ItemList)
            {
                foreach (var subElement in item.Prefab.originalElement.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() == "aitarget")
                    {
                        float baseSoundRange = subElement.GetAttributeFloat("soundrange", 0f);
                        float baseMaxSoundRange = subElement.GetAttributeFloat("maxsoundrange", baseSoundRange);
                        float baseSightRange = subElement.GetAttributeFloat("sightrange", 0f);
                        float baseMaxSightRange = subElement.GetAttributeFloat("maxsoundrange", baseSoundRange);

                        AITarget newAITarget = new AITarget(item, subElement);
                        newAITarget.MaxSoundRange = item.AiTarget.MaxSoundRange > 0 ? baseMaxSoundRange * soundMult : 0;
                        newAITarget.SoundRange = item.AiTarget.SoundRange > 0 ? baseSoundRange * soundMult : 0;
                        newAITarget.MaxSightRange = item.AiTarget.MaxSightRange > 0 ? baseMaxSightRange * sightMult : 0;
                        newAITarget.SightRange = item.AiTarget.SightRange > 0 ? baseSightRange * sightMult : 0;
                        item.aiTarget = newAITarget;

                        break; // Go to next item.
                    }
                }
            }

            foreach (var character in Character.CharacterList)
            {
                foreach (var subElement in character.Prefab.originalElement.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() == "aitarget")
                    {
                        float baseSoundRange = subElement.GetAttributeFloat("soundrange", 0f);
                        float baseMaxSoundRange = subElement.GetAttributeFloat("maxsoundrange", baseSoundRange);
                        float baseSightRange = subElement.GetAttributeFloat("sightrange", 0f);
                        float baseMaxSightRange = subElement.GetAttributeFloat("maxsoundrange", baseSoundRange);

                        AITarget newAITarget = new AITarget(character, subElement);
                        newAITarget.MaxSoundRange = character.AiTarget.MaxSoundRange > 0 ? baseMaxSoundRange * soundMult : 0;
                        newAITarget.SoundRange = character.AiTarget.SoundRange > 0 ? baseSoundRange * soundMult : 0;
                        newAITarget.MaxSightRange = character.AiTarget.MaxSightRange > 0 ? baseMaxSightRange * sightMult : 0;
                        newAITarget.SightRange = character.AiTarget.SightRange > 0 ? baseSightRange * sightMult : 0;
                        character.aiTarget = newAITarget;

                        break; // Go to next character.
                    }
                }
            }
        }

        public static void ReloadSounds(bool starting = false, bool stopping = false)
        {
            LuaCsLogger.Log("[SoundproofWalls] Reloading sound buffers for non-streamed audio...");

            MoonSharp.Interpreter.DynValue Resound = GameMain.LuaCs.Lua.Globals.Get("Resound");
            StopResound(Resound);

            // Stop all channels to prevent them from using buffers while we swap them.
            StopPlayingChannels();

            List<Sound> soundsToDispose = new List<Sound>();
            Dictionary<string, Sound> updatedSounds = new Dictionary<string, Sound>();

            // Call the modified helpers.
            ReloadRoundSounds(updatedSounds, soundsToDispose, starting, stopping);
            ReloadPrefabSounds(updatedSounds, soundsToDispose, starting, stopping);
            AllocateStatusEffectSounds(updatedSounds, soundsToDispose, starting, stopping);
            AllocateCharacterSounds(updatedSounds, soundsToDispose, starting, stopping);
            AllocateComponentSounds(updatedSounds, soundsToDispose, starting, stopping);

            // Dispose of the old, now-unreferenced sounds.
            LuaCsLogger.Log($"[SoundproofWalls] Disposing {soundsToDispose.Count} old sound buffers...");
            foreach (Sound oldSound in soundsToDispose.Distinct())
            {
                oldSound.Dispose();
            }

            StartResound(Resound);

            LuaCsLogger.Log("[SoundproofWalls] Finished reloading sound buffers.");
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

        public static Gap? GetDoorSoundGap(Hull? soundHull, Vector2 soundPos)
        {
            if (soundHull == null) { return null; }

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
            float epsilon = 1.0f;
            if (soundHull?.WaterPercentage <= 0) { return false; }
            return soundHull == null || soundHull.WaterPercentage >= 100 || soundHull.WaterVolume > 0 && soundPos.Y < soundHull.Surface - epsilon;
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
