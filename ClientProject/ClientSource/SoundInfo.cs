using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    public enum MuffleReason
    {
        None,
        LightObstruction,
        MediumObstruction,
        HeavyObstruction,
        SoundInWater,
        EarsInWater,
        BothInWater
    }

    // This class is responsible for calculating if a SoundChannel should be muffled and why.
    public class SoundInfo
    {
        private enum SoundType
        {
            Default,
            LocalVoice,
            RadioVoice
        }

        public MuffleReason Reason = MuffleReason.None;
        public MuffleReason PreviousReason = MuffleReason.None;
        private float worldDistance;
        private float? localDistance;
        public float Distance => localDistance ?? worldDistance;
        private Vector2 worldPos;
        private Vector2 localPos;
        private bool inWater;
        private bool inContainer;
        private bool noPath;

        private SoundType soundType;
        public bool IsVoice => soundType != SoundType.Default;
        public bool IsRadio => soundType == SoundType.RadioVoice;

        public float GainMult;

        public bool IgnorePath = false;
        public bool IgnoreSurface = false;
        public bool IgnoreSubmersion = false;
        public bool IgnorePitch = false;
        public bool IgnoreLowpass = false;
        public bool IgnoreContainer = false;
        public bool IgnoreAll = false;
        public bool PropagateWalls = false;

        private bool muffle = false;
        public bool Muffle { get { return muffle; } set { muffle = value && !IgnoreLowpass; } }
        public bool Reverb = false;
        public bool Eavesdropped = false;

        // For attenuating other sounds.
        public bool IsLoud = false;
        private float SidechainMult = 0;
        private float Release = 60;

        public Hull? SoundHull;
        public ItemComponent? ItemComp = null;
        public Client? VoiceOwner = null;
        private SoundChannel Channel;

        public SoundInfo(SoundChannel channel, Hull? soundHull = null, ItemComponent? itemComp = null, Client? voiceOwner = null, ChatMessageType? messageType = null, Item? emitter = null, bool dontMuffle = false, bool dontPitch = false)
        {
            Channel = channel;
            ItemComp = itemComp;
            VoiceOwner = voiceOwner;
            string filename = Channel.Sound.Filename;

            if (SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.IgnoredSounds))
            {
                IgnoreAll = true;
            }
            else
            {
                IgnoreLowpass = (dontMuffle && !SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.LowpassForcedSounds)) || SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.LowpassIgnoredSounds);
                IgnorePitch = dontPitch || SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.PitchIgnoredSounds);
                IgnorePath = IgnoreLowpass || SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.PathIgnoredSounds, include: "Barotrauma/Content/Sounds/Water/Flow");
                IgnoreSurface = IgnoreLowpass || IgnorePath || !SoundproofWalls.Config.MuffleSubmergedSounds || SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.SurfaceIgnoredSounds);
                IgnoreSubmersion = IgnoreLowpass || SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.SubmersionIgnoredSounds, exclude: "Barotrauma/Content/Characters/Human/");
                IgnoreContainer = IgnoreLowpass || SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.ContainerIgnoredSounds);
                IgnoreAll = IgnoreLowpass && IgnorePitch;
                PropagateWalls = !IgnoreAll && !IgnoreLowpass && SoundproofWalls.StringHasKeyword(filename, SoundproofWalls.Config.WallPropagatingSounds);

                GetCustomSoundData(filename, out GainMult, out SidechainMult, out Release);
                IsLoud = SidechainMult > 0;

                if (SoundproofWalls.Config.DynamicFx && SoundproofWalls.EffectsManager != null)
                {
                    uint sourceId = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
                    SoundproofWalls.EffectsManager.RegisterSource(sourceId);
                }
            }
            Update(soundHull, emitter, messageType);
        }
        public void Update(Hull? soundHull = null, Item? emitter = null, ChatMessageType? messageType = null)
        {
            UpdateProperties(soundHull, emitter, messageType);
            UpdateMuffle();
        }

        private void UpdateProperties(Hull? soundHull, Item? emitter, ChatMessageType? messageType)
        {
            Muffle = false;
            Eavesdropped = false;
            Reason = MuffleReason.None;
            soundType = SoundType.Default;
            noPath = false;

            Character character = Character.Controlled;
            Character? player = VoiceOwner?.Character;
            Limb? playerHead = player?.AnimController?.GetLimb(LimbType.Head);

            worldPos = playerHead?.WorldPosition ?? SoundproofWalls.GetSoundChannelPos(Channel);
            SoundHull = soundHull ?? Hull.FindHull(worldPos, player?.CurrentHull ?? character?.CurrentHull);
            localPos = SoundproofWalls.LocalizePosition(worldPos, SoundHull);
            inWater = SoundproofWalls.SoundInWater(localPos, SoundHull);
            inContainer = emitter != null && !IgnoreContainer && IsContainedWithinContainer(emitter);
            localDistance = null;
            worldDistance = Vector2.Distance(Listener.WorldPos, worldPos); // euclidean distance

            if (VoiceOwner != null && messageType != null)
            {
                soundType = (messageType == ChatMessageType.Radio) ? SoundType.RadioVoice : SoundType.LocalVoice;
            }
        }

        private void UpdateMuffle()
        {
            if (IgnoreAll) return;

            if (SoundproofWalls.Config.DynamicFx && 
                SoundproofWalls.EffectsManager != null)
            {
                UpdateDynamicFx();
            }
            else
            {
                UpdateBaseMuffleReason();
                UpdateExtendedMuffleReason();
                if (SoundproofWalls.Config.StaticFx)
                {
                    Muffle = Reason != MuffleReason.None;
                }
                else
                {
                    Muffle = Reason != MuffleReason.None && Reason != MuffleReason.LightObstruction && Reason != MuffleReason.MediumObstruction;
                }
            }

            if (IsLoud && !Muffle)
            {
                SoundproofWalls.Sidechain.StartRelease(SidechainMult, Release);
            }
            // Still works (to a lesser degree) if not heavily muffled.
            else if (IsLoud && Muffle && (Reason == MuffleReason.LightObstruction || Reason == MuffleReason.MediumObstruction))
            {
                SoundproofWalls.Sidechain.StartRelease(SidechainMult / 1.2f, Release / 1.2f);
            }
        }

        private void UpdateDynamicFx()
        {
            if (SoundproofWalls.EffectsManager == null) return;

            // TODO Calculate these values here.
            float gainHf = 1;
            bool isInListenerEnvironment = true;

            uint sourceId = Channel.Sound.Owner.GetSourceFromIndex(Channel.Sound.SourcePoolIndex, Channel.ALSourceIndex);
            SoundproofWalls.EffectsManager.UpdateSourceLowpass(sourceId, gainHf);
            SoundproofWalls.EffectsManager.RouteSourceToEnvironment(sourceId, isInListenerEnvironment);
        }

        private void UpdateExtendedMuffleReason()
        {
            if (!SoundproofWalls.Config.StaticFx) { return; }

            double currentMuffleFreq = double.PositiveInfinity;
            if (Reason != MuffleReason.None) { currentMuffleFreq = IsVoice ? SoundproofWalls.Config.VoiceHeavyLowpassFrequency : SoundproofWalls.Config.HeavyLowpassFrequency; }

            double suitFreq = SoundproofWalls.Config.LightLowpassFrequency;
            double eavesdroppingFreq = SoundproofWalls.Config.MediumLowpassFrequency;

            if (IsRadio || currentMuffleFreq <= suitFreq && currentMuffleFreq <= eavesdroppingFreq) { return; }

            bool isSuitMuffled = Listener.IsWearingDivingSuit && SoundproofWalls.Config.MuffleDivingSuits && !IgnoreAll;
            bool isEavesdropMuffled = Eavesdropped && SoundproofWalls.Config.MuffleEavesdropping && !IgnoreAll;

            // Wearing a suit uses the light obstruction frequency.
            MuffleReason reason = Reason;
            if (isSuitMuffled && suitFreq < currentMuffleFreq)
            {
                currentMuffleFreq = suitFreq;
                reason = MuffleReason.LightObstruction;
            }
            // Eavesdropping uses the medium frequency.
            if (isEavesdropMuffled && eavesdroppingFreq < currentMuffleFreq)
            {
                reason = MuffleReason.MediumObstruction;
            }

            Reason = reason;
            Reverb = !Channel.Looping && !Muffle && SoundHull != null;
        }

        private void UpdateBaseMuffleReason()
        {
            // Muffle radio comms underwater to make room for bubble sounds.
            if (IsRadio)
            {
                Character player = VoiceOwner.Character;
                bool wearingDivingGear = SoundproofWalls.IsCharacterWearingDivingGear(player);
                bool oxygenReqMet = wearingDivingGear && player.Oxygen < 11 || !wearingDivingGear && player.OxygenAvailable < 96;
                bool ignoreBubbles = SoundproofWalls.StringHasKeyword(player.Name, SoundproofWalls.Config.BubbleIgnoredNames) || IgnoreSurface;
                if (oxygenReqMet && inWater && !ignoreBubbles)
                {
                    Reason = MuffleReason.MediumObstruction;
                }
                // Return because radio comms aren't muffled under any other circumstances.
                return;
            }

            // Spectators get some muffled sounds too :)
            if (Listener.IsSpectating)
            {
                if (inWater && !IgnoreSurface)
                {
                    Reason = MuffleReason.SoundInWater;
                }
                else if (inContainer)
                {
                    Reason = MuffleReason.MediumObstruction;
                }
                return;
            }

            // Hydrophone check. Muffle sounds inside your own sub while still hearing sounds in other subs/structures.
            if (Listener.IsUsingHydrophonesNow)
            {
                // Sound can be heard clearly outside in the water.
                if (SoundHull == null)
                {
                    Reason = MuffleReason.LightObstruction;
                }
                // Sound is in a station or other submarine.
                else if (SoundHull != null && SoundHull.Submarine != LightManager.ViewTarget?.Submarine)
                {
                    Reason = MuffleReason.MediumObstruction;
                }
                // Sound is in own submarine.
                else
                {
                    Reason = MuffleReason.HeavyObstruction;
                }
                return;
            }

            // If soundHull and focusedHull are not null (and therefore might be connected to each other),
            // use a more approximate distance because it considers pathing around corners.
            // If the sound can't find a path to the focused hull, localDistance will be equal to float.MaxValue.
            bool propagated = false;
            if (Listener.FocusedHull != null && Listener.FocusedHull != SoundHull)
            {
                HashSet<Hull> listenerConnectedHulls = new HashSet<Hull>();
                // Run this even if SoundHull might be null for the sake of collecting listenerConnectedHulls for later.
                float distance = GetApproximateDistance(Listener.LocalPos, localPos, Listener.FocusedHull, SoundHull, Channel.Far, listenerConnectedHulls);

                if (distance >= float.MaxValue)
                {
                    noPath = true;
                }

                // Only switch to this distance if both the listenHull and soundHull are not null.
                if (SoundHull != null)
                {
                    localDistance = distance;
                }

                // Allow certain sounds near a wall to be faintly heard on the opposite side.
                if (PropagateWalls)
                {
                    foreach (Hull hull in listenerConnectedHulls)
                    {
                        if (ShouldSoundPropagateToHull(hull, worldPos))
                        {
                            LuaCsLogger.Log($"{Path.GetFileName(Channel.Sound.Filename)} SOUND HAS PROPAGATED TO {hull}");
                            propagated = true;
                            break;
                        }
                    }
                    if (propagated)
                    {
                        Reason = MuffleReason.MediumObstruction;
                        if (Listener.IsEavesdropping) // TODO test this
                        {
                            Eavesdropped = true;
                        }
                        return;
                    }
                }
            }

            // Sound could not find an open path to the player.
            if (noPath && !propagated)
            {
                if (IgnorePath)
                {
                    // Switch back to euclidean distance if the sound ignores paths.
                    localDistance = null;
                    // Add a slight muffle to these sounds.
                    Reason = MuffleReason.LightObstruction;
                }
                else
                {
                    Reason = MuffleReason.HeavyObstruction;
                    return;
                }
            }

            // Eavesdropping and this code being below the path check means any remaining unmuffled sounds are eavesdropped.
            if (Listener.IsEavesdropping && !IgnorePath)
            {
                Eavesdropped = true;
            }

            // Mildly muffle sounds in containers. Don't return yet because the container could be submerged with the sound inside.
            if (inContainer)
            {
                Reason = MuffleReason.MediumObstruction;
            }

            // For players without extended sounds, muffle the annoying vanilla exosuit sound for the wearer. TODO TEST THIS
            if (!SoundproofWalls.Config.StaticFx &&
                !SoundproofWalls.Config.MuffleDivingSuits &&
                Listener.IsWearingExoSuit &&
                worldDistance <= 1 && // Only muffle player's own exosuit.
                Channel.Sound.Filename.EndsWith("WEAPONS_chargeUp.ogg")) // Exosuit sound file name.
            {
                Reason = MuffleReason.HeavyObstruction;
                return;
            }

            bool earsInWater = Listener.IsSubmerged;
            bool ignoreSubmersion = IgnoreSubmersion || (Listener.IsCharacter ? !SoundproofWalls.Config.MuffleSubmergedPlayer : !SoundproofWalls.Config.MuffleSubmergedViewTarget);
            bool ignoreSurface = IgnoreSurface || PropagateWalls;

            // Exceptions to water:
            // Neither in water.
            if (!earsInWater && !inWater)
            {
                return;
            }
            // Both in water, but submersion is ignored.
            else if (earsInWater && inWater && ignoreSubmersion)
            {
                Reason = MuffleReason.LightObstruction;
                return;
            }
            // Sound is under, ears are above, but water surface is ignored.
            else if (ignoreSurface && inWater && !earsInWater)
            {
                if (PropagateWalls)
                {
                    Reason = MuffleReason.MediumObstruction;
                }
                return;
            }
            // Sound is above, ears are below, but water surface and submersion is ignored.
            else if (ignoreSurface && !inWater && earsInWater && ignoreSubmersion)
            {
                if (PropagateWalls)
                {
                    Reason = MuffleReason.MediumObstruction;
                }
                else
                {
                    Reason = MuffleReason.LightObstruction;
                }
                return;
            }

            // Enable muffling because either the sound is on the opposite side of the water's surface...
            Reason = inWater ? MuffleReason.SoundInWater : MuffleReason.EarsInWater;

            // ... or both the player and sound are submerged.
            // This unique reason is used to boost volume if both the player and sound are submerged (and have a path to each other).
            if (inWater && earsInWater) { Reason = MuffleReason.BothInWater; }
        }

        public static bool ShouldSoundPropagateToHull(Hull targetHull, Vector2 soundWorldPos)
        {
            if (targetHull == null) { return false; }

            float soundRadius = SoundproofWalls.Config.SoundPropagationRange;

            Rectangle soundBounds = new Rectangle(
                (int)(soundWorldPos.X - soundRadius),
                (int)(soundWorldPos.Y - soundRadius),
                (int)(soundRadius * 2),
                (int)(soundRadius * 2)
            );

            // Check if the rectangles intersect
            return soundBounds.Intersects(targetHull.WorldRect);
        }

        // Gets the distance between two localised positions going through gaps. Returns MaxValue if no path or out of range.
        public static float GetApproximateDistance(Vector2 startPos, Vector2 endPos, Hull startHull, Hull endHull, float maxDistance, HashSet<Hull> connectedHulls, float distanceMultiplierPerClosedDoor = 0)
        {
            return GetApproximateHullDistance(startPos, endPos, startHull, endHull, connectedHulls, 0.0f, maxDistance, distanceMultiplierPerClosedDoor);
        }

        private static float GetApproximateHullDistance(Vector2 startPos, Vector2 endPos, Hull startHull, Hull endHull, HashSet<Hull> connectedHulls, float distance, float maxDistance, float distanceMultiplierFromDoors = 0)
        {
            if (distance >= maxDistance) { return float.MaxValue; }
            if (startHull == endHull)
            {
                return distance + Vector2.Distance(startPos, endPos);
            }

            connectedHulls.Add(startHull);
            foreach (Gap g in startHull.ConnectedGaps)
            {
                float distanceMultiplier = 1;
                // For doors.
                if (g.ConnectedDoor != null && !g.ConnectedDoor.IsBroken)
                {
                    // Gap blocked if the door is closed or is curently closing and 90% closed.
                    if ((!g.ConnectedDoor.PredictedState.HasValue && g.ConnectedDoor.IsClosed || g.ConnectedDoor.PredictedState.HasValue && !g.ConnectedDoor.PredictedState.Value) && g.ConnectedDoor.OpenState < 0.1f)
                    {
                        if (distanceMultiplierFromDoors <= 0) { continue; }
                        distanceMultiplier *= distanceMultiplierFromDoors;
                    }
                }
                // For holes in hulls.
                else if (g.Open <= 0.33f)
                {
                    continue;
                }

                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    if (g.linkedTo[i] is Hull newStartHull && !connectedHulls.Contains(newStartHull))
                    {
                        Vector2 optimalGapPos = GetGapIntersectionPos(startPos, endPos, g);
                        float dist = GetApproximateHullDistance(optimalGapPos, endPos, newStartHull, endHull, connectedHulls, distance + Vector2.Distance(startPos, optimalGapPos) * distanceMultiplier, maxDistance);
                        if (dist < float.MaxValue)
                        {
                            return dist;
                        }
                    }
                }
            }

            return float.MaxValue;
        }

        public static Vector2 GetGapIntersectionPos(Vector2 startPos, Vector2 endPos, Gap gap)
        {
            Vector2 gapPos = gap.Position;

            if (!gap.IsHorizontal)
            {
                float gapSize = gap.Rect.Width;
                float slope = (endPos.Y - startPos.Y) / (endPos.X - startPos.X);
                float intersectX = (gapPos.Y - startPos.Y + slope * startPos.X) / slope;

                // Clamp the x-coordinate to within the gap's width
                intersectX = Math.Clamp(intersectX, gapPos.X - gapSize / 2, gapPos.X + gapSize / 2);

                return new Vector2(intersectX, gapPos.Y);
            }
            else
            {
                float gapSize = gap.Rect.Height;
                float slope = (endPos.X - startPos.X) / (endPos.Y - startPos.Y);
                float intersectY = (gapPos.X - startPos.X + slope * startPos.Y) / slope;

                // Clamp the y-coordinate to within the gap's height
                intersectY = Math.Clamp(intersectY, gapPos.Y - gapSize / 2, gapPos.Y + gapSize / 2);

                return new Vector2(gapPos.X, intersectY);
            }
        }

        private static bool GetCustomSoundData(string inputString, out float gainMult, out float sidechainMult, out float release)
        {
            string s = inputString.ToLower();
            gainMult = 1; sidechainMult = 0; release = 60;

            foreach (var sound in SoundproofWalls.Config.CustomSounds)
            {
                if (s.Contains(sound.Name.ToLower()))
                {
                    bool excluded = false;
                    foreach (string exclusion in sound.Exclusions)
                    {
                        if (s.Contains(exclusion.ToLower())) { excluded = true; }
                    }
                    if (excluded) { continue; }

                    gainMult = sound.GainMultiplier;
                    sidechainMult = sound.SidechainMultiplier * SoundproofWalls.Config.SidechainIntensityMaster;
                    release = sound.Release + SoundproofWalls.Config.SidechainReleaseMaster;
                    return true;
                }
            }
            return false;
        }

        private static bool IsContainedWithinContainer(Item? item)
        {
            while (item?.ParentInventory != null)
            {
                if (item.ParentInventory.Owner is Item parent && parent.HasTag("container"))
                {
                    return true;
                }
                item = item.ParentInventory.Owner as Item;
            }
            return false;
        }
    }
}
