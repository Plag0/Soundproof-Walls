using Barotrauma.Items.Components;
using Barotrauma.Sounds;
using Barotrauma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoundproofWalls
{
    public static class BufferManager
    {
        public static ReloadRequest? ActiveReloadRequest = null;
        public static bool ReadyToReload = false;

        public struct ReloadRequest
        {
            public bool Starting;
            public bool Stopping;
        }

        public static void Update()
        {
            if (ActiveReloadRequest.HasValue && ReadyToReload)
            {
                ReloadRequest request = ActiveReloadRequest.Value;
                ReloadBuffers(request);
                ActiveReloadRequest = null;
                ReadyToReload = false;
            }
        }

        public static void DrawBufferReloadText(SpriteBatch spriteBatch)
        {
            if (!ActiveReloadRequest.HasValue) { return; }

            LocalizedString modName = TextManager.GetWithVariable("spw_modname", "[version]", ModStateManager.State.Version);
            Vector2 modNameSize = GUIStyle.LargeFont.MeasureString(modName);
            Vector2 modNamePos = new Vector2(GameMain.GraphicsWidth / 2 - modNameSize.X / 2, GameMain.GraphicsHeight / 2 - modNameSize.Y / 2);

            float linePadding = 22;

            LocalizedString loadingText = TextManager.Get("spw_loading");
            Vector2 loadingTextSize = GUIStyle.SmallFont.MeasureString(loadingText);
            Vector2 loadingTextPos = new Vector2(modNamePos.X, GameMain.GraphicsHeight / 2 - loadingTextSize.Y / 2 + linePadding);

            GUIButton backgroundHolder = new GUIButton(new RectTransform((0.35f, 0.35f), GUI.Canvas, Anchor.Center), style: null);
            backgroundHolder.RectTransform.RelativeOffset = (-0.008f, 0);
            GUIFrame background = new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, backgroundHolder.RectTransform, Anchor.Center), style: "CommandBackground");

            EavesdropManager.Vignette.Draw(spriteBatch);
            background.Draw(spriteBatch);

            GUI.DrawString(spriteBatch, modNamePos, loadingText, GUIStyle.TextColorBright, Color.Black * 0.3f, backgroundPadding: 18, font: GUIStyle.LargeFont);
            GUI.DrawString(spriteBatch, loadingTextPos, modName, GUIStyle.TextColorNormal, font: GUIStyle.SmallFont);

            // Once the text is displayed, start the reload.
            ReadyToReload = true;
        }

        public static void TriggerBufferReload(bool starting = false, bool stopping = false)
        {
            if (ActiveReloadRequest.HasValue) return;

            ActiveReloadRequest = new ReloadRequest
            {
                Starting = starting,
                Stopping = stopping
            };
        }

        public static void ReloadBuffers(ReloadRequest request)
        {
            LuaCsLogger.Log("[SoundproofWalls] Reloading sound buffers for non-streamed audio...");

            MoonSharp.Interpreter.DynValue Resound = GameMain.LuaCs.Lua.Globals.Get("Resound");
            Util.StopResound(Resound);

            // Stop all channels to prevent them from using buffers while we swap them.
            Util.StopPlayingChannels();

            List<Sound> soundsToDispose = new List<Sound>();
            Dictionary<string, Sound> updatedSounds = new Dictionary<string, Sound>();

            bool starting = request.Starting;
            bool stopping = request.Stopping;
            ReloadRoundSounds(updatedSounds, soundsToDispose, starting, stopping);
            ReloadPrefabSounds(updatedSounds, soundsToDispose, starting, stopping);
            AllocateStatusEffectSounds(updatedSounds, soundsToDispose, starting, stopping);
            AllocateCharacterSounds(updatedSounds, soundsToDispose, starting, stopping);
            AllocateComponentSounds(updatedSounds, soundsToDispose, starting, stopping);

            // Dispose of the old unreferenced sounds.
            LuaCsLogger.Log($"[SoundproofWalls] Disposing {soundsToDispose.Count} old sound buffers...");
            foreach (Sound oldSound in soundsToDispose.Distinct())
            {
                oldSound.Dispose();
            }

            Util.StartResound(Resound);

            LuaCsLogger.Log("[SoundproofWalls] Finished reloading sound buffers.");
        }

        public static bool ShouldReloadBuffers(Config newConfig, Config oldConfig)
        {
            // Reduced buffers.
            bool oldReducedBuffersEnabled = oldConfig.Enabled && oldConfig.DynamicFx && oldConfig.RemoveUnusedBuffers;
            bool newReducedBuffersEnabled = newConfig.Enabled && newConfig.DynamicFx && newConfig.RemoveUnusedBuffers;
            bool shouldReloadReducedBuffers = oldReducedBuffersEnabled != newReducedBuffersEnabled;

            // Extended buffers.
            bool toggledReverbEffect = newConfig.Enabled && newConfig.StaticFx && (oldConfig.StaticReverbEnabled != newConfig.StaticReverbEnabled);
            bool changedReverbEffect = newConfig.Enabled && newConfig.StaticFx && newConfig.StaticReverbEnabled &&
                (oldConfig.StaticReverbDuration != newConfig.StaticReverbDuration ||
                oldConfig.StaticReverbWetDryMix != newConfig.StaticReverbWetDryMix);
            bool shouldReloadExtendedBuffers = newConfig.Enabled && (oldConfig.StaticFx != newConfig.StaticFx || changedReverbEffect || toggledReverbEffect);

            // Frequency changes.
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

            return shouldReloadExtendedBuffers || 
                   shouldReloadReducedBuffers ||
                   oldHeavyFreq != newHeavyFreq ||
                   oldMediumFreq != newMediumFreq ||
                   oldLightFreq != newLightFreq;
        }

        private static bool ShouldSkipSound(Sound? sound, bool starting, bool stopping)
        {
            if (sound == null || sound.Stream)
                return true;

            bool isReduced = sound is ReducedOggSound;
            bool isExtended = sound is ExtendedOggSound;

            Config config = ConfigManager.Config;

            if ((starting && config.DynamicFx && isReduced) ||
                (starting && config.StaticFx && isExtended) ||
                (stopping && config.DynamicFx && !isReduced) ||
                (stopping && config.StaticFx && !isExtended))
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
    }
}
