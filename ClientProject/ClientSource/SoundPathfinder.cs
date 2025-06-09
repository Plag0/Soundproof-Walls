using Barotrauma;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    //TODO really this class should use exclusively local positions and not world positions.
    public static class SoundPathfinder
    {
        public static float PenaltyClosedDoor { get; private set; } = 1000;
        public static float PenaltyWaterSurface { get; private set; } = 2000;

        private static Dictionary<Gap, List<(Gap neighbor, float baseDistance)>> s_hullConnectivityGraph =
            new Dictionary<Gap, List<(Gap, float)>>();
        private static Submarine s_lastKnownSubmarine = null;

        public readonly struct PathfindingResult
        {
            public readonly bool PathFound;
            public readonly float RawDistance;
            public readonly int ClosedDoorCount;
            public readonly int WaterSurfaceCrossings;
            public readonly Gap LastGapBeforeListener;
            public readonly Vector2 LastIntersectionPos;
            public readonly float ApproxTotalCost;

            public PathfindingResult(bool found, float rawDist, int doors, int waterCrossings, Gap lastGap, Vector2 lastIntersection, float approxCost)
            {
                PathFound = found;
                RawDistance = rawDist;
                ClosedDoorCount = doors;
                WaterSurfaceCrossings = waterCrossings; // ADDED Assignment
                LastGapBeforeListener = lastGap;
                LastIntersectionPos = lastIntersection;
                ApproxTotalCost = approxCost;
            }
            public static PathfindingResult NotFound => new PathfindingResult(false, float.MaxValue, 0, 0, null, Vector2.Zero, float.MaxValue);
        }

        public static void InitializeGraph(Submarine submarine)
        {
            if (submarine == null) { s_hullConnectivityGraph.Clear(); s_lastKnownSubmarine = null; return; }

            if (s_lastKnownSubmarine == submarine && s_hullConnectivityGraph.Count > 0) { return; }

            s_hullConnectivityGraph.Clear(); 
            s_lastKnownSubmarine = submarine; 
            var allHulls = submarine.GetHulls(true); 
            var uniqueGaps = new HashSet<Gap>();
            foreach (Hull hull in allHulls)
            {
                if (hull?.ConnectedGaps == null) continue; 
                List<Gap> gapsInHull = hull.ConnectedGaps.Where(g => g != null).ToList();
                foreach (Gap gap in gapsInHull) 
                { 
                    uniqueGaps.Add(gap); 
                    if (!s_hullConnectivityGraph.ContainsKey(gap)) s_hullConnectivityGraph[gap] = new List<(Gap, float)>(); 
                }
                for (int i = 0; i < gapsInHull.Count; i++) 
                { 
                    for (int j = i + 1; j < gapsInHull.Count; j++) 
                    { 
                        Gap gapA = gapsInHull[i]; Gap gapB = gapsInHull[j]; 
                        float distance = Vector2.Distance(gapA.WorldPosition, gapB.WorldPosition); 
                        s_hullConnectivityGraph[gapA].Add((gapB, distance)); 
                        s_hullConnectivityGraph[gapB].Add((gapA, distance)); 
                    } 
                }
            }
        }

        /// <summary>
        /// Gets the approximate world Y coordinate of the water surface at a given world X coordinate within a hull.
        /// Returns a very low value if hull is invalid or has no water info.
        /// </summary>
        private static float GetWaterSurfaceY(float localX, Hull hull)
        {
            if (hull == null || hull.WaveY == null || hull.WaveY.Length == 0) return float.MinValue;
         
            int xIndex = (int)MathF.Round(localX - hull.Rect.X);
            xIndex = Math.Clamp(xIndex, 0, hull.WaveY.Length - 1);
            return hull.Surface + hull.WaveY[xIndex] - 1; // Reduce by 1 to stop rounding errors
        }

        /// <summary>
        /// Determines if traversing this gap involves crossing an air/water interface.
        /// </summary>
        private static bool CheckIfSurfaceCrossing(Gap gap)
        {
            if (gap == null) return false;

            Hull? hullA = gap.linkedTo.Count > 0 ? gap.linkedTo[0] as Hull : null;
            Hull? hullB = gap.linkedTo.Count > 1 ? gap.linkedTo[1] as Hull : null;

            // Only check if crossing between two valid hulls
            if (hullA != null && hullB != null)
            {
                float gapHeight = gap.Rect.Y;
                float waterYA = GetWaterSurfaceY(gap.Position.X, hullA);
                float waterYB = GetWaterSurfaceY(gap.Position.X, hullB);

                // TODO Vertical gaps can cause problems since if the water level fills up to submerge the lower hull then the gap is also below the upper hulls surface so both hulls count as submerged so no penalty is applied.
                bool isSubmergedA = gapHeight < waterYA;
                bool isSubmergedB = gapHeight < waterYB;

                //LuaCsLogger.Log($"gapHeight {gapHeight}\nhullA {hullA} hullB {hullB}\nwaterYA {waterYA} waterYB {waterYB}\nisSubmergedA {isSubmergedA} isSubmergedB {isSubmergedB}");

                // Return true if one side submerged and other is not
                return isSubmergedA != isSubmergedB;
            }
            // Assume no surface crossing if not between two hulls
            return false;
        }

        /// <summary>
        /// Calculates the penalty for traversing a gap, considering its
        /// physical state (door/wall) and whether crossing it involves an air/water interface.
        /// </summary>
        private static float GetGapTraversalPenalty(Gap gap)
        {
            if (gap == null) return float.MaxValue;

            float penalty = 0.0f;

            // 1. Check Door/Wall state
            if (gap.ConnectedDoor != null)
            {
                // Block duct block based on config.
                if (!ConfigManager.Config.TraverseWaterDucts && gap.ConnectedDoor.Item.HasTag("ductblock")) { return float.MaxValue; }

                if (Util.IsDoorClosed(gap.ConnectedDoor))
                {
                    penalty += PenaltyClosedDoor;
                }
            }
            else if (gap.Open < ConfigManager.Config.OpenWallThreshold)
            {
                return float.MaxValue;
            }

            if (CheckIfSurfaceCrossing(gap))
            {
                penalty += PenaltyWaterSurface;
            }

            return penalty;
        }

        // --- A* Heuristic Function ---
        private static float Heuristic(Gap node, Vector2 targetPos) 
        {
            if (node == null) { return float.MaxValue; } 
            return Vector2.Distance(node.WorldPosition, targetPos); 
        }

        private static Vector2 GetGapIntersectionPos(Vector2 startPos, Vector2 endPos, Gap gap)
        {
            if (gap == null) { return startPos; }
            Vector2 gapCenter = gap.WorldPosition; 
            RectangleF gapWorldRect = gap.WorldRect; 
            const float epsilon = 0.001f;

            if (gap.IsHorizontal) 
            { 
                float intersectX; 
                float gapMinX = gapWorldRect.X; 
                float gapMaxX = gapWorldRect.Right; 
                float gapY = gapCenter.Y; 
                if (Math.Abs(endPos.X - startPos.X) < epsilon) { intersectX = startPos.X; } 
                else if (Math.Abs(endPos.Y - startPos.Y) < epsilon) { intersectX = (startPos.X + endPos.X) / 2.0f; } 
                else { intersectX = startPos.X + (endPos.X - startPos.X) / (endPos.Y - startPos.Y) * (gapY - startPos.Y); } 
                return new Vector2(Math.Clamp(intersectX, gapMinX, gapMaxX), gapY); }
            else 
            { 
                float intersectY; 
                float gapMinY = gapWorldRect.Y; 
                float gapMaxY = gapWorldRect.Bottom; 
                float gapX = gapCenter.X; 
                if (Math.Abs(endPos.Y - startPos.Y) < epsilon) { intersectY = startPos.Y; } 
                else if (Math.Abs(endPos.X - startPos.X) < epsilon) { intersectY = (startPos.Y + endPos.Y) / 2.0f; } 
                else { intersectY = startPos.Y + (endPos.Y - startPos.Y) / (endPos.X - startPos.X) * (gapX - startPos.X); } 
                return new Vector2(gapX, Math.Clamp(intersectY, gapMinY, gapMaxY)); }
        }

        /// <summary>
        /// Finds the top N shortest/lowest-cost sound paths ending at unique final gaps.
        /// </summary>
        /// <param name="n">Maximum number of unique paths to return.</param>
        /// <returns>An ordered list of PathfindingResult, best path first. Empty if no path found.</returns>
        public static List<PathfindingResult> FindShortestPaths(Vector2 sourcePos, Hull? sourceHull, Vector2 listenerPos, Hull? listenerHull, Submarine? submarine, int n = 1, float maxRawDistance = float.MaxValue, bool isDoorSound = false)
        {
            var results = new List<PathfindingResult>(); // Initialize empty results list
            if (sourceHull == null || listenerHull == null || submarine == null || n <= 0 || maxRawDistance <= 0) return results; // Return empty list for invalid input

            if (!ConfigManager.Config.RealSoundDirectionsEnabled || ConfigManager.Config.RealSoundDirectionsMax < 1) { maxRawDistance = float.MaxValue; }

            // 1. Direct Path Check (Same Hull) - Only one path possible
            if (sourceHull == listenerHull)
            {
                float dist = Vector2.Distance(sourcePos, listenerPos);
                if (dist <= maxRawDistance)
                {
                    results.Add(new PathfindingResult(true, dist, 0, 0, null, sourcePos, dist));
                }
                return results;
            }

            // 2. Pre-checks and Graph Initialization
            if (submarine != s_lastKnownSubmarine || s_hullConnectivityGraph.Count == 0)
            {
                InitializeGraph(submarine);
                if (s_hullConnectivityGraph.Count == 0) return results;
            }

            // 3. A* Initialization
            Gap? ignoredGap = Util.GetDoorSoundGap(isDoorSound, sourceHull, sourcePos);
            var predecessors = new Dictionary<Gap, Gap>(); 
            var gScore = new Dictionary<Gap, float>();
            var rawGScore = new Dictionary<Gap, float>(); // Geometric cost for distance pruning.
            var fScore = new Dictionary<Gap, float>();
            var openSet = new PriorityQueue<Gap, float>();
            foreach (var gap in s_hullConnectivityGraph.Keys) 
            { 
                gScore[gap] = float.MaxValue;
                rawGScore[gap] = float.MaxValue;
                fScore[gap] = float.MaxValue; 
            }

            // 4. Initialize Source Connections
            foreach (var startGap in sourceHull.ConnectedGaps.Where(g => g != null && s_hullConnectivityGraph.ContainsKey(g)))
            {
                float initialRawGScore = Vector2.Distance(sourcePos, startGap.WorldPosition);

                if (initialRawGScore > maxRawDistance) continue; // Prune if initial raw distance is too long

                rawGScore[startGap] = initialRawGScore; 
                gScore[startGap] = initialRawGScore;
                fScore[startGap] = initialRawGScore + Heuristic(startGap, listenerPos);

                openSet.Enqueue(startGap, fScore[startGap]); 
                predecessors[startGap] = null;
            }

            // 5. A* Main Loop
            while (openSet.Count > 0)
            {
                Gap currentGap = openSet.Dequeue();

                if (gScore[currentGap] == float.MaxValue) continue;

                // Calculate penalty for exiting/crossing currentGap
                bool shouldIgnoreGap = currentGap == ignoredGap;
                float penaltyForExitingCurrent = shouldIgnoreGap ? 0f : GetGapTraversalPenalty(currentGap);
                if (penaltyForExitingCurrent >= float.MaxValue - 1.0f) continue; // Impassable gap

                float costToCurrentGapRaw = rawGScore[currentGap]; // Raw geometric cost to reach currentGap's threshold
                float costBeforeSegment = gScore[currentGap] + penaltyForExitingCurrent;

                // --- A. Move to neighbors within the same hull ---
                if (s_hullConnectivityGraph.TryGetValue(currentGap, out var intraHullNeighbors))
                {
                    foreach (var (neighborGap, baseDistance) in intraHullNeighbors)
                    {
                        if (!gScore.ContainsKey(neighborGap)) continue;

                        float tentative_rawGScore = costToCurrentGapRaw + baseDistance;
                        if (tentative_rawGScore > maxRawDistance) continue; // Prune: Raw distance for this path segment too long

                        // Cost = CostToCurrent + PenaltyForExitingCurrent + DistanceToNeighbor
                        float tentative_gScore = costBeforeSegment + baseDistance;
                        if (tentative_gScore < gScore[neighborGap])
                        {
                            predecessors[neighborGap] = currentGap; 
                            gScore[neighborGap] = tentative_gScore;
                            rawGScore[neighborGap] = tentative_rawGScore;
                            fScore[neighborGap] = tentative_gScore + Heuristic(neighborGap, listenerPos);
                            openSet.Enqueue(neighborGap, fScore[neighborGap]);
                        }
                    }
                }

                // --- B. Move through currentGap to adjacent hulls ---
                float gScoreAfterCrossing = costBeforeSegment; // Renaming for clarity in this block. The cost is costBeforeSegment calculated above.

                foreach (Hull adjacentHull in currentGap.linkedTo.OfType<Hull>())
                {
                    if (adjacentHull == null) continue;

                    foreach (Gap nextGap in adjacentHull.ConnectedGaps.Where(g => g != null && gScore.ContainsKey(g)))
                    {
                        float segmentDist = Vector2.Distance(currentGap.WorldPosition, nextGap.WorldPosition);

                        float tentative_rawGScore = costToCurrentGapRaw + segmentDist; // Raw path to nextGap
                        if (tentative_rawGScore > maxRawDistance) continue; // Prune: Raw distance for this path segment too long

                        float tentative_gScore = gScoreAfterCrossing + segmentDist;
                        if (tentative_gScore < gScore[nextGap])
                        {
                            predecessors[nextGap] = currentGap; gScore[nextGap] = tentative_gScore;
                            gScore[nextGap] = tentative_gScore;
                            rawGScore[nextGap] = tentative_rawGScore;
                            fScore[nextGap] = tentative_gScore + Heuristic(nextGap, listenerPos);
                            openSet.Enqueue(nextGap, fScore[nextGap]);
                        }
                    }
                }
            } // End A* loop

            // 6. Collect Potential Endpoints Reaching Listener Hull
            var potentialEndpoints = new List<(Gap gap, float approxTotalCost)>();
            foreach (var finalGap in listenerHull.ConnectedGaps.Where(g => g != null && gScore.ContainsKey(g)))
            {
                float actualCostToGap = gScore[finalGap];
                if (actualCostToGap == float.MaxValue) continue; // Gap wasn't reached

                // Get the penalty for traversing this final gap itself. I feel like I shouldn't need this but the code seems to think otherwise
                bool shouldIgnoreGap = finalGap == ignoredGap;
                float penaltyForFinalGap = shouldIgnoreGap ? 0f : GetGapTraversalPenalty(finalGap);

                float finalSegmentDistApprox = Vector2.Distance(finalGap.WorldPosition, listenerPos);
                float approxTotalCost = actualCostToGap + penaltyForFinalGap + finalSegmentDistApprox;

                potentialEndpoints.Add((finalGap, approxTotalCost));
            }
            potentialEndpoints.Sort((a, b) => a.approxTotalCost.CompareTo(b.approxTotalCost));

            // 7:. Reconstruct and Gather Results
            var addedFinalGaps = new HashSet<Gap>();
            foreach (var endpoint in potentialEndpoints)
            {
                if (results.Count >= n) break;
                Gap finalGap = endpoint.gap;
                if (addedFinalGaps.Add(finalGap))
                {
                    List<Gap> path = ReconstructPath(predecessors, finalGap);
                    if (path != null && path.Count > 0)
                    {
                        // Calculate approx distance and ideal last intersection position
                        (float accurateRawDistance, Vector2 lastIntersectionPos) = CalculateAccuratePathDetails(sourcePos, listenerPos, path);

                        if (accurateRawDistance > maxRawDistance) continue; // Skip this path

                        // Calculate closed door count
                        int doorCount = 0;
                        HashSet<Gap> doorsCounted = new HashSet<Gap>();
                        foreach (Gap gapInPath in path)
                        { 
                            if (gapInPath == null) continue; 
                            if (!isDoorSound && Util.IsDoorClosed(gapInPath.ConnectedDoor)) 
                            { 
                                if (doorsCounted.Add(gapInPath)) { doorCount++; } 
                            } 
                        }

                        // Calculate water surface crossings
                        int waterSurfaceCrossings = 0;
                        foreach (Gap gapInPath in path)
                        {
                            if (gapInPath == null) continue;
                            // check if crossing this gap involved a surface transition
                            if (CheckIfSurfaceCrossing(gapInPath))
                            {
                                waterSurfaceCrossings++;
                            }
                        }

                        // Add the result
                        results.Add(new PathfindingResult(true, accurateRawDistance, doorCount, waterSurfaceCrossings, finalGap, lastIntersectionPos, endpoint.approxTotalCost));
                    }
                }
            }

            return results;
        }

        private static List<Gap> ReconstructPath(Dictionary<Gap, Gap> predecessors, Gap endGap)
        {
            var path = new LinkedList<Gap>(); 
            Gap current = endGap; 
            int safetyBreak = s_hullConnectivityGraph.Count > 0 ? (s_hullConnectivityGraph.Count * 2) : 1000;
            while (current != null && safetyBreak > 0) 
            { 
                path.AddFirst(current);
                if (!predecessors.TryGetValue(current, out current)) { current = null; }
                safetyBreak--; 
            }
            if (safetyBreak <= 0) return new List<Gap>(); // Return empty list on error
            return path.ToList();
        }

        private static (float totalDistance, Vector2 lastIntersection) CalculateAccuratePathDetails(Vector2 sourcePos, Vector2 listenerPos, List<Gap> path)
        {
            if (path == null || path.Count == 0) { return (Vector2.Distance(sourcePos, listenerPos), sourcePos); }

            float totalDistance = 0; 
            Vector2 currentPos = sourcePos; 

            for (int i = 0; i < path.Count; i++) 
            { 
                Gap currentGap = path[i]; 
                if (currentGap == null) continue; 
                Vector2 intersectionPoint = GetGapIntersectionPos(currentPos, listenerPos, currentGap);
                totalDistance += Vector2.Distance(currentPos, intersectionPoint); 
                currentPos = intersectionPoint; 
            }

            totalDistance += Vector2.Distance(currentPos, listenerPos);
            return (totalDistance, currentPos);
        }
    }
}
