using Barotrauma;
using Microsoft.Xna.Framework;

namespace SoundproofWalls
{
    /// <summary>
    /// A utility class to find the shortest/lowest-cost paths for sound to travel between hulls in a submarine.
    /// It uses an A* search algorithm, treating gaps (doors, breaches) as nodes in a dynamic graph.
    /// </summary>
    public static class SoundPathfinder
    {
        // --- Configuration ---
        public static float PenaltyClosedDoor { get; private set; } = 1000;
        public static float PenaltyWaterSurface { get; private set; } = 2000;

        /// <summary>
        /// Represents the result of a pathfinding operation.
        /// </summary>
        public readonly struct PathfindingResult
        {
            public readonly float RawDistance;
            public readonly int ClosedDoorCount;
            public readonly int WaterSurfaceCrossings;
            public readonly Vector2 LastIntersectionPos;

            public PathfindingResult(float rawDist, int doors, int waterCrossings, Vector2 lastIntersection)
            {
                RawDistance = rawDist;
                ClosedDoorCount = doors;
                WaterSurfaceCrossings = waterCrossings;
                LastIntersectionPos = lastIntersection;
            }

            /// <summary>
            /// Represents a failed pathfinding attempt.
            /// </summary>
            public static PathfindingResult NotFound => new PathfindingResult(float.MaxValue, 0, 0, Vector2.Zero);
        }

        /// <summary>
        /// Finds the top N shortest/lowest-cost sound paths from a source to a listener,
        /// where each path is defined by a unique final gap it passes through before reaching the listener's hull.
        /// </summary>
        /// <param name="sourcePos">The local-space position of the sound source.</param>
        /// <param name="sourceHull">The hull containing the sound source.</param>
        /// <param name="listenerPos">The local-space position of the listener.</param>
        /// <param name="listenerHull">The hull containing the listener.</param>
        /// <param name="submarine">The submarine context for the search.</param>
        /// <param name="n">The maximum number of unique paths to return.</param>
        /// <param name="maxRawDistance">The maximum geometric distance a path can have. Paths longer than this are pruned.</param>
        /// <param name="ignoredGap">A specific gap to ignore when calculating penalties (e.g., a vent the listener is using).</param>
        /// <returns>An ordered list of PathfindingResult, from best to worst. The list is empty if no paths are found.</returns>
        public static List<PathfindingResult> FindShortestPaths(Vector2 sourcePos, Hull sourceHull, Vector2 listenerPos, Hull listenerHull, Submarine submarine, int n = 1, float maxRawDistance = float.MaxValue, Gap ignoredGap = null)
        {
            var results = new List<PathfindingResult>();
            if (sourceHull == null || listenerHull == null || submarine == null || n <= 0)
            {
                return results;
            }

            // --- Case 1: Source and Listener are in the same hull ---
            // The path is a direct line, with no gaps, doors, or water crossings.
            if (sourceHull == listenerHull)
            {
                float directDistance = Vector2.Distance(sourcePos, listenerPos);
                if (directDistance <= maxRawDistance)
                {
                    // NOTE: LastIntersectionPos is sourcePos as no gaps were traversed.
                    results.Add(new PathfindingResult(directDistance, 0, 0, sourcePos));
                }
                return results;
            }

            // --- Case 2: Pathfinding through gaps is required ---

            // A* Data Structures
            var predecessors = new Dictionary<Gap, Gap>();
            var gScore = new Dictionary<Gap, float>(); // Total cost from start to current node
            var rawGScore = new Dictionary<Gap, float>(); // Geometric distance cost only, for pruning
            var openSet = new PriorityQueue<Gap, float>();

            // Initialize scores for all gaps in the submarine.
            // This is more efficient than checking for key existence repeatedly.
            foreach (var hull in Hull.HullList.Where(h => h.Submarine == submarine))
            {
                if (hull?.ConnectedGaps == null) continue;
                foreach (var gap in hull.ConnectedGaps)
                {
                    if (gap == null) continue;
                    gScore[gap] = float.MaxValue;
                    rawGScore[gap] = float.MaxValue;
                }
            }

            // Initialize the open set with the gaps connected to the source hull.
            foreach (var startGap in sourceHull.ConnectedGaps)
            {
                if (startGap == null) continue;

                float initialRawDist = Vector2.Distance(sourcePos, startGap.Position);

                if (initialRawDist > maxRawDistance) continue;

                gScore[startGap] = initialRawDist;
                rawGScore[startGap] = initialRawDist;
                float fScore = initialRawDist + Heuristic(startGap, listenerPos);
                openSet.Enqueue(startGap, fScore);
                predecessors[startGap] = null;
            }

            // --- A* Main Loop ---
            while (openSet.TryDequeue(out Gap currentGap, out _))
            {
                // The cost to reach the threshold of the current gap.
                float costToReachCurrentRaw = rawGScore[currentGap];

                // Calculate the penalty for traversing this gap. If impassable, skip.
                float traversalPenalty = (currentGap == ignoredGap) ? 0f : GetGapTraversalPenalty(currentGap);
                if (traversalPenalty >= float.MaxValue) continue;

                // The total cost after crossing the current gap.
                float costAfterCrossingCurrent = gScore[currentGap] + traversalPenalty;

                // Explore neighbors: any other gap in a hull adjacent to the current gap.
                // This correctly models sound traveling *across* a hull.
                foreach (var adjacentHull in currentGap.linkedTo.OfType<Hull>())
                {
                    if (adjacentHull?.ConnectedGaps == null) continue;

                    foreach (var neighborGap in adjacentHull.ConnectedGaps)
                    {
                        if (neighborGap == null || neighborGap == currentGap) continue;

                        // TODO: When converting to local positions, this will need updating.
                        float segmentDistance = Vector2.Distance(currentGap.Position, neighborGap.Position);
                        float tentativeRawGScore = costToReachCurrentRaw + segmentDistance;

                        // Pruning check: if the raw geometric distance is already too high, abandon this path.
                        if (tentativeRawGScore > maxRawDistance) continue;

                        float tentativeGScore = costAfterCrossingCurrent + segmentDistance;

                        // If we found a better path to the neighbor, record it.
                        if (gScore.TryGetValue(neighborGap, out float currentNeighborGScore) && tentativeGScore < currentNeighborGScore)
                        {
                            predecessors[neighborGap] = currentGap;
                            gScore[neighborGap] = tentativeGScore;
                            rawGScore[neighborGap] = tentativeRawGScore;
                            float fScore = tentativeGScore + Heuristic(neighborGap, listenerPos);
                            openSet.Enqueue(neighborGap, fScore);
                        }
                    }
                }
            }

            // --- Reconstruct and Collect Results ---
            var potentialEndpoints = new List<(Gap gap, float cost)>();
            foreach (var finalGap in listenerHull.ConnectedGaps)
            {
                if (finalGap == null || !gScore.ContainsKey(finalGap) || gScore[finalGap] == float.MaxValue) continue;

                // The final cost is the path cost to the gap + penalty for crossing it + final leg to listener.
                float penalty = (finalGap == ignoredGap) ? 0f : GetGapTraversalPenalty(finalGap);
                float finalLegDist = Vector2.Distance(finalGap.Position, listenerPos);
                float totalApproxCost = gScore[finalGap] + penalty + finalLegDist;

                potentialEndpoints.Add((finalGap, totalApproxCost));
            }

            // Sort potential paths by their approximate total cost to process the best ones first.
            potentialEndpoints.Sort((a, b) => a.cost.CompareTo(b.cost));

            var addedFinalGaps = new HashSet<Gap>();
            foreach (var (finalGap, _) in potentialEndpoints)
            {
                if (results.Count >= n) break;

                // Ensure we only add one result per unique final gap.
                if (addedFinalGaps.Add(finalGap))
                {
                    List<Gap> path = ReconstructPath(predecessors, finalGap);
                    if (path.Count == 0) continue;

                    (float accurateRawDistance, Vector2 lastIntersectionPos) = CalculateAccuratePathDetails(sourcePos, listenerPos, path);

                    // Final check against the raw distance limit with the more accurate calculation.
                    if (accurateRawDistance > maxRawDistance) continue;

                    int closedDoorCount = 0;
                    int waterSurfaceCrossings = 0;
                    var countedDoors = new HashSet<Gap>();

                    foreach (Gap gapInPath in path)
                    {
                        if (gapInPath == ignoredGap) continue;

                        if (Util.IsDoorClosed(gapInPath.ConnectedDoor) && countedDoors.Add(gapInPath))
                        {
                            closedDoorCount++;
                        }
                        if (CheckIfSurfaceCrossing(gapInPath))
                        {
                            waterSurfaceCrossings++;
                        }
                    }

                    results.Add(new PathfindingResult(accurateRawDistance, closedDoorCount, waterSurfaceCrossings, lastIntersectionPos));
                }
            }

            return results;
        }

        /// <summary>
        /// Calculates the penalty for traversing a gap, considering its state (door, wall) and water interface.
        /// </summary>
        private static float GetGapTraversalPenalty(Gap gap)
        {
            if (gap == null) return float.MaxValue;

            float penalty = 0.0f;

            // 1. Check Door/Wall state
            if (gap.ConnectedDoor != null)
            {
                // Block traversal through water ducts if configured.
                if (!ConfigManager.Config.TraverseWaterDucts && gap.ConnectedDoor.Item.HasTag("ductblock"))
                {
                    return float.MaxValue;
                }

                if (Util.IsDoorClosed(gap.ConnectedDoor))
                {
                    penalty += PenaltyClosedDoor;
                }
            }
            // This is a structural breach (wall). Check if it's large enough to be considered "open".
            else if (gap.Open < ConfigManager.Config.OpenWallThreshold)
            {
                return float.MaxValue; // Wall is not open enough to pass sound.
            }

            // 2. Check for crossing an air/water interface
            if (CheckIfSurfaceCrossing(gap))
            {
                penalty += PenaltyWaterSurface;
            }

            return penalty;
        }

        /// <summary>
        /// Determines if traversing a gap involves crossing an air/water interface.
        /// </summary>
        private static bool CheckIfSurfaceCrossing(Gap gap)
        {
            if (gap == null || gap.linkedTo.Count < 2) return false;

            Hull hullA = gap.linkedTo[0] as Hull;
            Hull hullB = gap.linkedTo[1] as Hull;

            // Only check for crossings between two valid hulls.
            if (hullA != null && hullB != null)
            {
                const float epsilon = 100f;
                float gapY = gap.Position.Y;

                float waterSurfaceYA = GetWaterSurfaceY(gap.Position.X, hullA);
                float waterSurfaceYB = GetWaterSurfaceY(gap.Position.X, hullB);

                bool isSubmergedA = gapY < waterSurfaceYA;
                bool isSubmergedB = gapY < waterSurfaceYB;

                // If the submerged states are different, a crossing occurs.
                // An additional check prevents "false" crossings if the gap is just barely touching the surface on one side.
                if (isSubmergedA != isSubmergedB)
                {
                    float closenessA = Math.Abs(gapY - waterSurfaceYA);
                    float closenessB = Math.Abs(gapY - waterSurfaceYB);

                    // If one side is very close to the water level, treat it as being on the same side as the other.
                    if (closenessA < epsilon) isSubmergedA = isSubmergedB;
                    else if (closenessB < epsilon) isSubmergedB = isSubmergedA;
                }

                // Return true only if the states are different *after* the tolerance adjustment.
                return isSubmergedA != isSubmergedB;
            }

            return false;
        }

        /// <summary>
        /// Gets the approximate local Y coordinate of the water surface at a given local X coordinate within a hull.
        /// </summary>
        private static float GetWaterSurfaceY(float x, Hull hull)
        {
            if (hull == null || hull.WaveY == null || hull.WaveY.Length == 0) return float.MinValue;

            int xIndex = (int)MathF.Round(x - hull.Rect.X);
            xIndex = Math.Clamp(xIndex, 0, hull.WaveY.Length - 1);

            return hull.Surface + hull.WaveY[xIndex];
        }

        /// <summary>
        /// A* Heuristic: The straight-line distance from the current node to the target.
        /// This is admissible because it never overestimates the actual cost.
        /// </summary>
        private static float Heuristic(Gap node, Vector2 targetPos)
        {
            if (node == null) return float.MaxValue;
            // TODO: When converting to local positions, this will need updating.
            return Vector2.Distance(node.Position, targetPos);
        }

        /// <summary>
        /// Reconstructs the path of gaps from the start to the end gap using the predecessors map.
        /// </summary>
        private static List<Gap> ReconstructPath(Dictionary<Gap, Gap> predecessors, Gap endGap)
        {
            var path = new LinkedList<Gap>();
            Gap current = endGap;
            // Safety break to prevent infinite loops in case of a bug in the predecessor map.
            int safetyBreak = predecessors.Count + 10;
            while (current != null && safetyBreak > 0)
            {
                path.AddFirst(current);
                if (!predecessors.TryGetValue(current, out current))
                {
                    current = null; // Reached the start of the path.
                }
                safetyBreak--;
            }
            // If safety break was hit, something went wrong; return an empty path.
            return safetyBreak <= 0 ? new List<Gap>() : path.ToList();
        }

        /// <summary>
        /// Calculates a more accurate total raw distance of a path by finding the intersection point on each gap.
        /// Also returns the position of the final intersection before the listener.
        /// </summary>
        private static (float totalDistance, Vector2 lastIntersection) CalculateAccuratePathDetails(Vector2 sourcePos, Vector2 listenerPos, List<Gap> path)
        {
            if (path == null || path.Count == 0)
            {
                return (Vector2.Distance(sourcePos, listenerPos), sourcePos);
            }

            float totalDistance = 0;
            Vector2 currentPos = sourcePos;

            foreach (Gap gap in path)
            {
                if (gap == null) continue;
                Vector2 intersectionPoint = GetGapIntersectionPos(currentPos, listenerPos, gap);
                totalDistance += Vector2.Distance(currentPos, intersectionPoint);
                currentPos = intersectionPoint;
            }

            totalDistance += Vector2.Distance(currentPos, listenerPos);
            return (totalDistance, currentPos);
        }

        /// <summary>
        /// Finds the point on the line segment of a gap that a line from startPos to endPos would intersect.
        /// </summary>
        private static Vector2 GetGapIntersectionPos(Vector2 startPos, Vector2 endPos, Gap gap)
        {
            if (gap == null) return startPos;

            Vector2 gapCenter = gap.Position;
            RectangleF gapRect = gap.Rect;
            const float epsilon = 0.001f;

            if (gap.IsHorizontal)
            {
                float intersectX;
                float gapY = gapCenter.Y;
                // Avoid division by zero if the line is vertical or horizontal.
                if (Math.Abs(endPos.X - startPos.X) < epsilon) { intersectX = startPos.X; }
                else if (Math.Abs(endPos.Y - startPos.Y) < epsilon) { intersectX = (startPos.X + endPos.X) / 2.0f; }
                else
                {
                    // Standard line-line intersection formula.
                    intersectX = startPos.X + (endPos.X - startPos.X) * (gapY - startPos.Y) / (endPos.Y - startPos.Y);
                }
                return new Vector2(Math.Clamp(intersectX, gapRect.X, gapRect.Right), gapY);
            }
            else // Is Vertical
            {
                float intersectY;
                float gapX = gapCenter.X;
                if (Math.Abs(endPos.Y - startPos.Y) < epsilon) { intersectY = startPos.Y; }
                else if (Math.Abs(endPos.X - startPos.X) < epsilon) { intersectY = (startPos.Y + endPos.Y) / 2.0f; }
                else
                {
                    intersectY = startPos.Y + (endPos.Y - startPos.Y) * (gapX - startPos.X) / (endPos.X - startPos.X);
                }
                return new Vector2(gapX, Math.Clamp(intersectY, gapRect.Y, gapRect.Bottom));
            }
        }
    }
}