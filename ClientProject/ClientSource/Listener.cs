using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    public static class Listener
    {
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
        public static float ConnectedArea;
        public static Vector2 WorldPos;
        public static Vector2 LocalPos;
        public static Vector2 SimPos;

        public static HashSet<Hull> HullsWithSmallFlow = new HashSet<Hull>();
        public static HashSet<Hull> HullsWithMediumFlow = new HashSet<Hull>();
        public static HashSet<Hull> HullsWithLargeFlow = new HashSet<Hull>();

        public static void Update()
        {
            Character character = Character.Controlled;

            IsSpectating = character == null || character.IsDead || LightManager.ViewTarget == null;
            
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

                return;
            }

            IsEavesdropping = EavesdroppedHull != null && EavesdropManager.Efficiency >= ConfigManager.Config.EavesdroppingThreshold;

            EavesdroppedHull = GetListenerEavesdroppedHull();
            CurrentHull = GetListenerHull();
            FocusedHull = IsEavesdropping ? EavesdroppedHull : CurrentHull;
            ConnectedHulls = GetConnectedHulls(FocusedHull, includingThis: true, respectClosedGaps: true);

            ConnectedArea = 0;
            foreach (Hull hull in ConnectedHulls)
            {
                ConnectedArea += hull.RectHeight * hull.RectWidth;
            }

            IsCharacter = !ConfigManager.Config.FocusTargetAudio || LightManager.ViewTarget as Character == Character.Controlled;
            IsSubmerged = IsCharacter ? Character.Controlled?.AnimController?.HeadInWater == true : LightManager.ViewTarget != null && Util.SoundInWater(LightManager.ViewTarget.Position, CurrentHull);
            IsUsingHydrophones = HydrophoneManager.HydrophoneEfficiency > 0.01f && Character.Controlled?.SelectedItem?.GetComponent<Sonar>() is Sonar sonar && HydrophoneManager.HydrophoneSwitches.ContainsKey(sonar) && HydrophoneManager.HydrophoneSwitches[sonar].State;
            IsWearingDivingSuit = Character.Controlled?.LowPassMultiplier < 0.5f;
            IsWearingExoSuit = IsCharacterWearingExoSuit(character!);
            
            Limb head = Util.GetCharacterHead(character!);
            LocalPos = IsCharacter ? head.Position : LightManager.ViewTarget?.Position ?? Vector2.Zero;
            WorldPos = IsCharacter ? head.WorldPosition : LightManager.ViewTarget?.WorldPosition ?? new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y);
            SimPos = Util.LocalizePosition(WorldPos, CurrentHull) / 100;
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
                !ConfigManager.Config.EavesdroppingKeyOrMouse.IsDown() ||
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
