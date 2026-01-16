using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma;
using Microsoft.Xna.Framework;
using FarseerPhysics;
using Microsoft.Xna.Framework.Graphics;
using OpenAL;
using System.Security.Cryptography.X509Certificates;

namespace SoundproofWalls
{
    public static class Listener
    {
        private const float SpeedOfSoundWater = 1480;
        private const float SpeedOfSoundAir = 343;

        public static bool IsSpectating;
        public static bool IsCharacter; // True if the listener is listening from the perspective of their own character.
        public static bool IsSubmerged;
        public static bool IsEavesdropping;
        public static bool IsUsingHydrophones;
        // Check if the hydrophones are being used on the current tick without waiting for the next update. Used for the fist sonar ping sound when hydrophones are enabled.
        public static bool IsUsingHydrophonesNow { get { return HydrophoneManager.HydrophoneEfficiency > 0.01f && Character.Controlled?.SelectedItem?.GetComponent<Sonar>() is Sonar sonar && HydrophoneManager.HydrophoneSwitches.ContainsKey(sonar) && HydrophoneManager.HydrophoneSwitches[sonar].State; } }
        public static bool IsWearingDivingSuit;
        public static bool IsWearingExoSuit;
        public static Hull? CurrentHull;
        public static Hull? FocusedHull;
        public static Hull? EavesdroppedHull;
        public static HashSet<Hull> ConnectedHulls = new HashSet<Hull>();
        public static Vector2 WorldPos;
        public static Vector2 LocalPos;
        public static Vector2 SimPos;

        // Flow sound muffling.
        public static HashSet<Hull> HullsWithSmallFlow = new HashSet<Hull>();
        public static HashSet<Hull> HullsWithMediumFlow = new HashSet<Hull>();
        public static HashSet<Hull> HullsWithLargeFlow = new HashSet<Hull>();

        // Rerverb area calculation.
        public static float TrailingConnectedReverbArea;
        private static float _connectedReverbArea;
        private static double _lastReverbAreaUpdate = 0.0f;
        private static Vector2[]? _rayHitBuffer;

        private static bool _velocityEnabled = false;
        public static Vector2 Velocity
        {
            get
            {
                Al.GetError();
                Al.GetListener3f(Al.Velocity, out float x, out float y, out float _);
                int alError;
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to get velocity for listener, {Al.GetErrorString(alError)}");
                return new Vector2(x, y);
            }
            set
            {
                Al.GetError();
                Al.Listener3f(Al.Velocity, value.X, value.Y, 0);
                int alError;
                if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to set velocity for listener, {Al.GetErrorString(alError)}");
            }
        }

        private static float _speedOfSound;
        public static float SpeedOfSound
        {
            get
            {
                return _speedOfSound;
            }
            set
            {
                float currentSpeedOfSound = _speedOfSound;
                float targetSpeedOfSound = value;
                if (currentSpeedOfSound != targetSpeedOfSound)
                {
                    _speedOfSound = targetSpeedOfSound;
                    Al.GetError();
                    Al.SpeedOfSound(targetSpeedOfSound);
                    int alError;
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to set speed of sound to {_speedOfSound}, {Al.GetErrorString(alError)}");
                }
            }
        }

        private static float _dopplerFactor = 1;
        public static float DopplerFactor
        {
            get
            {
                return _dopplerFactor;
            }
            set
            {
                float currentDopplerFactor = _dopplerFactor;
                float targetDopplerFactor = value;
                if (currentDopplerFactor != targetDopplerFactor)
                {
                    _dopplerFactor = targetDopplerFactor;
                    Al.GetError();
                    Al.DopplerFactor(targetDopplerFactor);
                    int alError;
                    if ((alError = Al.GetError()) != Al.NoError) DebugConsole.NewMessage($"[SoundproofWalls] Failed to set doppler factor to {_dopplerFactor}, {Al.GetErrorString(alError)}");
                }
            }
        }

        public static void Update()
        {
            PerformanceProfiler.Instance.StartTimingEvent(ProfileEvents.ListenerUpdate);

            Character character = Character.Controlled;

            IsSpectating = character == null || character.IsDead || character.Removed || LightManager.ViewTarget == null;
            
            if (IsSpectating)
            {
                CurrentHull = null;
                EavesdroppedHull = null;
                FocusedHull = null;

                IsCharacter = false;
                IsSubmerged = false;
                IsEavesdropping = false;
                IsUsingHydrophones = false;
                IsWearingDivingSuit = false;
                IsWearingExoSuit = false;

                LocalPos = Vector2.Zero;
                WorldPos = new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);
                SimPos = WorldPos;
                
                _rayHitBuffer = null;

                if (_velocityEnabled) Velocity = Vector2.Zero;

                return;
            }

            IsEavesdropping = EavesdroppedHull != null && EavesdropManager.Efficiency >= ConfigManager.Config.EavesdroppingThreshold;

            EavesdroppedHull = GetListenerEavesdroppedHull();
            CurrentHull = GetListenerHull();
            FocusedHull = IsEavesdropping ? EavesdroppedHull : CurrentHull;
            ConnectedHulls = GetConnectedHulls(FocusedHull, includingThis: true, respectClosedGaps: true);

            IsCharacter = !ConfigManager.Config.FocusTargetAudio || LightManager.ViewTarget as Character == Character.Controlled;
            IsSubmerged = IsCharacter ? Character.Controlled?.AnimController?.HeadInWater == true : LightManager.ViewTarget != null && Util.SoundInWater(LightManager.ViewTarget.Position, CurrentHull);
            IsUsingHydrophones = HydrophoneManager.HydrophoneEfficiency > 0.01f && Character.Controlled?.SelectedItem?.GetComponent<Sonar>() is Sonar sonar && HydrophoneManager.HydrophoneSwitches.ContainsKey(sonar) && HydrophoneManager.HydrophoneSwitches[sonar].State;
            IsWearingDivingSuit = Character.Controlled?.LowPassMultiplier < 0.5f;
            IsWearingExoSuit = IsCharacterWearingExoSuit(character!);

            Limb head = Util.GetCharacterHead(character!);
            LocalPos = IsCharacter ? head.Position : LightManager.ViewTarget?.Position ?? Vector2.Zero;
            WorldPos = IsCharacter ? head.WorldPosition : LightManager.ViewTarget?.WorldPosition ?? new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);
            SimPos = Util.LocalizePosition(WorldPos, CurrentHull?.Submarine) / 100;

            // Area
            _connectedReverbArea = GetListenerConnectedReverbArea();
            float transitionFactor = ConfigManager.Config.ReverbAreaTransitionFactor;
            if (transitionFactor > 0)
            {
                float maxStep = (float)(transitionFactor * Timing.Step);
                TrailingConnectedReverbArea = Util.SmoothStep(TrailingConnectedReverbArea, _connectedReverbArea, maxStep);
            }
            else
            {
                TrailingConnectedReverbArea = _connectedReverbArea;
            }

            // Velocity
            Vector2 listenerVelocity = Vector2.Zero;
            if (ConfigManager.Config.DopplerEffect)
            {
                _velocityEnabled = true;
                listenerVelocity = head.body?.LinearVelocity ?? Vector2.Zero;
            }
            if (_velocityEnabled)
            {
                Velocity = listenerVelocity;
                if (!ConfigManager.Config.DopplerEffect) _velocityEnabled = false;
            };

            // Speed of sound & doppler factor
            SpeedOfSound = IsSubmerged ? SpeedOfSoundWater : SpeedOfSoundAir;
            DopplerFactor = ConfigManager.Config.DopplerEffectStrengthMultiplier;

            PerformanceProfiler.Instance.StopTimingEvent();
        }

        public static void DebugDraw(SpriteBatch spriteBatch, Camera cam)
        {
            if (!ConfigManager.LocalConfig.ShowReverbArea) return;
            if (!DebugConsole.CheatsEnabled || !AchievementManager.CheatsEnabled) return;
            if (_rayHitBuffer == null || _rayHitBuffer.Length == 0) return;
            if (cam == null) return;

            Vector2 worldOffset = Vector2.Zero;
            if (FocusedHull?.Submarine != null)
            {
                worldOffset = FocusedHull.Submarine.DrawPosition;
            }

            // Helper to convert meters to pixels
            Vector2 SimToScreen(Vector2 simPos)
            {
                Vector2 localPos = ConvertUnits.ToDisplayUnits(simPos);
                Vector2 worldPos = localPos + worldOffset;
                return cam.WorldToScreen(worldPos);
            }

            // Use different coordinate systems when inside/outside
            bool inside = FocusedHull != null;
            Vector2 screenOrigin = inside ? SimToScreen(SimPos) : SimToScreen(WorldPos / 100);

            // Drawing
            for (int i = 0; i < _rayHitBuffer.Length; i++)
            {
                Vector2 p1 = SimToScreen(_rayHitBuffer[i]);
                Vector2 p2 = SimToScreen(_rayHitBuffer[(i + 1) % _rayHitBuffer.Length]);

                // From center to wall
                GUI.DrawLine(spriteBatch, screenOrigin, p1, Color.Orange * 0.2f, 0, 1);

                // Perimeter
                GUI.DrawLine(spriteBatch, p1, p2, Color.Lime * 0.9f, 0, 2);

                // Contact points
                GUI.DrawRectangle(spriteBatch, p1 - new Vector2(2, 2), new Vector2(4, 4), Color.Yellow, true);
            }

            // Center point
            GUI.DrawRectangle(spriteBatch, screenOrigin - new Vector2(3, 3), new Vector2(6, 6), Color.Cyan, true);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight / 2), $"Raw area: {_connectedReverbArea:N0}\n" +
                $"Trailing area: {TrailingConnectedReverbArea:N0}\n" +
                $"Ray count: {_rayHitBuffer.Length}", Color.Cyan);
        }

        private static float GetListenerConnectedReverbArea()
        {
            float area = 0;

            if (Timing.TotalTime > _lastReverbAreaUpdate + ConfigManager.Config.ReverbAreaUpdateInterval)
            {
                _lastReverbAreaUpdate = (float)Timing.TotalTime;
            }
            else
            {
                return _connectedReverbArea;
            }

            if (ConfigManager.Config.DynamicFx && 
                ConfigManager.Config.DynamicReverbEnabled && 
                ConfigManager.Config.DynamicReverbRaycastArea)
            {
                int rayCount = 16;
                float maxRadius = (float)Math.Sqrt(ConfigManager.Config.DynamicReverbMaxArea / Math.PI);

                if (_rayHitBuffer == null || _rayHitBuffer.Length != rayCount)
                {
                    _rayHitBuffer = new Vector2[rayCount];
                }

                float stepAngle = MathHelper.TwoPi / rayCount;

                // Over time, rotate the start angle to create a more accurate smoothed area.
                float angleOffset = (float)Timing.TotalTime * 0.5f;

                bool inside = FocusedHull != null;
                Vector2 startPoint;
                if (inside) { startPoint = FocusedHull == CurrentHull ? SimPos : (FocusedHull!.Position / 100); }
                else { startPoint = WorldPos / 100; }

                // Situationally collide wih platforms when near docking hatches to prevent the rays going infinitely through the hatch when open.
                bool nearDockingHatch = false;
                if (FocusedHull?.Submarine.DockedTo != null)
                {
                    foreach (var kvp in FocusedHull.Submarine.ConnectedDockingPorts)
                    {
                        DockingPort dockingPort = kvp.Value;
                        if (dockingPort.Docked && Vector2.Distance(dockingPort.Item.WorldPosition, WorldPos) < 700)
                        {
                            nearDockingHatch = true;
                            break;
                        }
                    }
                }

                FarseerPhysics.Dynamics.Category situationalCollision = nearDockingHatch ? Physics.CollisionPlatform : Physics.CollisionNone;

                for (int i = 0; i < rayCount; i++)
                {
                    float angle = (i * stepAngle) + angleOffset;
                    Vector2 direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                    Vector2 endPoint = startPoint + (direction * maxRadius);

                    // Default hit is max range
                    _rayHitBuffer[i] = endPoint;
                    int index = i; // Local copy for lambda closure

                    GameMain.World.RayCast((fixture, point, normal, fraction) =>
                    {
                        if (fixture.IsSensor) return -1;

                        _rayHitBuffer[index] = point;
                        return fraction; // Clip ray to this hit
                    }, startPoint, endPoint, collisionCategory: Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking | situationalCollision);
                }

                // Calculate area
                for (int i = 0; i < rayCount; i++)
                {
                    Vector2 p1 = _rayHitBuffer[i];
                    Vector2 p2 = _rayHitBuffer[(i + 1) % rayCount];

                    // Cross product calculation for triangle area slices
                    float cross = (p1.X - SimPos.X) * (p2.Y - SimPos.Y) -
                                  (p1.Y - SimPos.Y) * (p2.X - SimPos.X);

                    area += 0.5f * Math.Abs(cross);
                }

                float insideMult = 1.35f; // Compensate for some missed area.
                area *= inside ? (100 * 100) * insideMult : 100; // Convert to cm

                float wetRoomMult = FocusedHull != null && FocusedHull.IsWetRoom ? ConfigManager.Config.DynamicReverbWetRoomAreaSizeMultiplier : 1;
                area *= wetRoomMult;
            }
            else
            {
                _rayHitBuffer = null;
                foreach (Hull hull in ConnectedHulls)
                {
                    float roomArea = hull.RectHeight * hull.RectWidth;
                    if (ConfigManager.Config.DynamicReverbWaterSubtractsArea)
                    {
                        float waterRatio = Math.Clamp(hull.WaterPercentage, 0.0f, 100.0f) / 100;
                        // If listener is submerged use the area of the water, otherwise use the area of the air.
                        roomArea *= IsSubmerged ? waterRatio : 1 - waterRatio;
                    }

                    float wetRoomMult = hull.IsWetRoom && FocusedHull == hull ? ConfigManager.Config.DynamicReverbWetRoomAreaSizeMultiplier : 1;
                    area += roomArea * wetRoomMult;
                }
            }

            return Math.Min(area, ConfigManager.Config.DynamicReverbMaxArea);
        }

        private static Hull? GetListenerHull()
        {
            if (IsCharacter)
            {
                Limb? limb = Character.Controlled?.AnimController.GetLimb(LimbType.Head) ?? Character.Controlled?.AnimController.GetLimb(LimbType.Torso);
                return limb?.Hull;
            }
            else
            {
                Character? viewedCharacter = LightManager.ViewTarget as Character; // Viewing characters other than the player.
                Item? viewedItem = LightManager.ViewTarget as Item; // Viewing an item like a turret.

                if (viewedCharacter != null)
                {
                    Limb? limb = viewedCharacter.AnimController.GetLimb(LimbType.Head) ?? viewedCharacter.AnimController.GetLimb(LimbType.Torso);
                    return limb?.Hull;
                }
                else if (viewedItem != null)
                {
                    return viewedItem.CurrentHull;
                }
                else
                {
                    return Hull.FindHull(LightManager.ViewTarget.WorldPosition, Character.Controlled?.CurrentHull);
                }
            }
        }

        private static Hull? GetListenerEavesdroppedHull()
        {
            Character character = Character.Controlled;

            if (!ConfigManager.Config.EavesdroppingEnabled ||
                !ConfigManager.LocalConfig.EavesdroppingKeyOrMouse.IsDown() ||
                character == null ||
                character.CurrentHull == null ||
                character.CurrentSpeed > 0.05 ||
                character.IsUnconscious ||
                character.IsDead)
            {
                return null;
            }

            int expansionAmount = ConfigManager.Config.EavesdroppingMaxDistance;
            Limb limb = Util.GetCharacterHead(character);
            Vector2 headPos = limb.WorldPosition;
            headPos.Y = -headPos.Y;

            foreach (Gap gap in character.CurrentHull.ConnectedGaps)
            {
                if (gap.ConnectedDoor == null || gap.ConnectedDoor.OpenState > 0 || gap.ConnectedDoor.IsBroken) { continue; }

                Rectangle gWorldRect = gap.WorldRect;
                Rectangle gapBoundingBox = new Rectangle(
                    gWorldRect.X - expansionAmount / 2,
                    -gWorldRect.Y - expansionAmount / 2,
                    gWorldRect.Width + expansionAmount,
                    gWorldRect.Height + expansionAmount);

                if (gapBoundingBox.Contains(headPos))
                {
                    foreach (Hull linkedHull in gap.linkedTo)
                    {
                        if (linkedHull != character.CurrentHull)
                        {
                            return linkedHull;
                        }
                    }
                }
            }

            return null;
        }

        // Copy of the vanilla GetConnectedHulls with minor adjustments to align with Soundproof Walls searching algorithms.
        public static HashSet<Hull> GetConnectedHulls(Hull? startHull, bool includingThis, int? searchDepth = null, bool respectClosedGaps = false)
        {
            HashSet<Hull> connectedHulls = new HashSet<Hull>();

            if (startHull == null) { return connectedHulls; }

            int step = 0;
            int valueOrDefault = searchDepth.GetValueOrDefault();
            if (!searchDepth.HasValue)
            {
                valueOrDefault = 100;
                searchDepth = valueOrDefault;
            }

            Util.GetAdjacentHulls(startHull, connectedHulls, ref step, searchDepth.Value, respectClosedGaps);
            if (!includingThis)
            {
                connectedHulls.Remove(startHull);
            }

            return connectedHulls;
        }

        public static void UpdateHullsWithLeaks()
        {
            HullsWithSmallFlow.Clear();
            HullsWithMediumFlow.Clear();
            HullsWithLargeFlow.Clear();

            // This code uses the same approach found in SoundPlayer.UpdateWaterFlowSounds.
            Vector2 listenerPos = new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);
            foreach (Gap gap in Gap.GapList)
            {
                Vector2 diff = gap.WorldPosition - listenerPos;
                if (Math.Abs(diff.X) < SoundPlayer.FlowSoundRange && Math.Abs(diff.Y) < SoundPlayer.FlowSoundRange)
                {
                    if (gap.Open < 0.01f || gap.LerpedFlowForce.LengthSquared() < 100.0f || !ConfigManager.Config.FlowSoundsTraverseWaterDucts && gap.ConnectedDoor?.Item != null && gap.ConnectedDoor.Item.HasTag("ductblock")) { continue; }

                    float gapFlow = Math.Abs(gap.LerpedFlowForce.X) + Math.Abs(gap.LerpedFlowForce.Y) * 2.5f;
                    if (!gap.IsRoomToRoom) { gapFlow *= 2.0f; }
                    if (gapFlow < 10.0f) { continue; }

                    if (gap.linkedTo.Count == 2 && gap.linkedTo[0] is Hull hull1 && gap.linkedTo[1] is Hull hull2)
                    {
                        //no flow sounds between linked hulls (= rooms consisting of multiple hulls)
                        if (hull1.linkedTo.Contains(hull2)) { continue; }
                        if (hull1.linkedTo.Any(h => h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2))) { continue; }
                        if (hull2.linkedTo.Any(h => h.linkedTo.Contains(hull1) && h.linkedTo.Contains(hull2))) { continue; }
                    }

                    int flowSoundIndex = (int)Math.Floor(MathHelper.Clamp(gapFlow / SoundPlayer.MaxFlowStrength, 0, SoundPlayer.FlowSounds.Count));
                    flowSoundIndex = Math.Min(flowSoundIndex, SoundPlayer.FlowSounds.Count - 1);

                    float dist = diff.Length();
                    float distFallOff = dist / SoundPlayer.FlowSoundRange;
                    if (distFallOff >= 0.99f) { continue; }

                    HashSet<Hull> targetSet = HullsWithSmallFlow;
                    if (flowSoundIndex == 2) { targetSet = HullsWithLargeFlow; }
                    else if (flowSoundIndex == 1) { targetSet = HullsWithMediumFlow; }

                    if (gap.FlowTargetHull != null) { targetSet.Add(gap.FlowTargetHull); }
                }
            }
        }

        private static bool IsCharacterWearingExoSuit(Character character)
        {
            Identifier id = new Identifier("deepdivinglarge");
            Item outerItem = character.Inventory.GetItemInLimbSlot(InvSlotType.OuterClothes);

            return outerItem != null && outerItem.HasTag(id);
        }
    }
}
