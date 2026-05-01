namespace SoundproofWalls
{
    /// <summary>
    /// A utility class to find the shortest/lowest-cost path for sound to travel between hulls.
    /// It uses an A* search algorithm and treats gaps as nodes in a dynamic graph.
    /// Paths are cached and periodically cleared to avoid redundant searches with simultaneous sounds.
    /// </summary>
    public static class SoundPathfinder
    {
        public const float PenaltyClosedDoor = 1000f;
        public const float PenaltyWaterSurface = 2000f;

        // Stores only the stable topology of a path (gap sequence, door/water counts).
        // Note: NotFound results are not cached because they may be caused by a position-specific maxRawDistance limit.
        private static readonly Dictionary<PathCacheKey, CachedPath> PathCache = new();
        private static double _lastCacheClearTime;

        public static void InvalidateCache()
        {
            PathCache.Clear();
            _lastCacheClearTime = Timing.TotalTime;
        }

        private readonly struct PathCacheKey : IEquatable<PathCacheKey>
        {
            public readonly Hull SourceHull;
            public readonly Hull ListenerHull;
            public readonly Gap IgnoredGap;

            public PathCacheKey(Hull source, Hull listener, Gap ignoredGap)
            {
                SourceHull = source;
                ListenerHull = listener;
                IgnoredGap = ignoredGap;
            }

            public bool Equals(PathCacheKey other) =>
                SourceHull == other.SourceHull &&
                ListenerHull == other.ListenerHull &&
                IgnoredGap == other.IgnoredGap;

            public override bool Equals(object obj) => obj is PathCacheKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(SourceHull, ListenerHull, IgnoredGap);
        }

        private readonly struct CachedPath
        {
            public readonly IReadOnlyList<Gap> Gaps;
            public readonly int ClosedDoorCount;
            public readonly int WaterCrossings;

            public CachedPath(List<Gap> gaps, int closedDoorCount, int waterCrossings)
            {
                Gaps = gaps;
                ClosedDoorCount = closedDoorCount;
                WaterCrossings = waterCrossings;
            }
        }

        public readonly struct PathfindingResult
        {
            public readonly float RawDistance;
            public readonly int ClosedDoorCount;
            public readonly int WaterCrossings;
            public readonly Vector2 LastIntersectionPos;

            public PathfindingResult(float rawDistance, int closedDoorCount, int waterCrossings, Vector2 lastIntersection)
            {
                RawDistance = rawDistance;
                ClosedDoorCount = closedDoorCount;
                WaterCrossings = waterCrossings;
                LastIntersectionPos = lastIntersection;
            }

            public static readonly PathfindingResult NotFound = new PathfindingResult(float.MaxValue, 0, 0, Vector2.Zero);
        }

        /// <summary>
        /// Finds the shortest/lowest-cost sound path from a source to a listener.
        /// </summary>
        /// <param name="sourcePos">The local position of the sound source.</param>
        /// <param name="sourceHull">The hull containing the sound source.</param>
        /// <param name="listenerPos">The local position of the listener.</param>
        /// <param name="listenerHull">The hull containing the listener.</param>
        /// <param name="maxRawDistance">The maximum geometric distance a path can have. Paths longer than this are pruned.</param>
        /// <param name="ignoredGap">A specific gap to ignore when calculating penalties (e.g., door sounds on both sides of the door & and self-eavesdropping).</param>
        /// <returns>A PathfindingResult representing the best path found, or PathfindingResult.NotFound if no valid path exists.</returns>
        public static PathfindingResult FindShortestPath(Vector2 sourcePos, Hull sourceHull, Vector2 listenerPos, Hull listenerHull, float maxRawDistance = float.MaxValue, Gap ignoredGap = null)
        {
            if (sourceHull == null || listenerHull == null)
                return PathfindingResult.NotFound;

            // Clear the cache periodically to reflect changes in gaps opening in walls and water obstructions.
            if (Timing.TotalTime >= _lastCacheClearTime + ConfigManager.Config.AStarCacheUpdateInterval)
            {
                InvalidateCache();
            }

            // Source and Listener are in the same hull.
            if (sourceHull == listenerHull)
            {
                float directDistance = Vector2.Distance(sourcePos, listenerPos);
                return directDistance <= maxRawDistance
                    ? new PathfindingResult(directDistance, 0, 0, sourcePos)
                    : PathfindingResult.NotFound;
            }

            // Check the cache before running the search. On a hit, recalculate the position-dependent
            // values from the cached topology to reflect current source and listener positions.
            var cacheKey = new PathCacheKey(sourceHull, listenerHull, ignoredGap);
            if (PathCache.TryGetValue(cacheKey, out CachedPath cached))
            {
                (float cachedRawDistance, Vector2 cachedIntersectionPos) =
                    CalculateAccuratePathDetails(sourcePos, listenerPos, cached.Gaps);

                return cachedRawDistance <= maxRawDistance
                    ? new PathfindingResult(cachedRawDistance, cached.ClosedDoorCount, cached.WaterCrossings, cachedIntersectionPos)
                    : PathfindingResult.NotFound;
            }

            var openSet = new PriorityQueue<Gap, float>();
            var closedSet = new HashSet<Gap>();
            var predecessors = new Dictionary<Gap, Gap>();
            var gScore = new Dictionary<Gap, float>();    // Total cost from start to current node
            var rawGScore = new Dictionary<Gap, float>(); // Geometric distance only, for pruning

            // Cache gaps around listener for early termination. Once all are settled, their optimal costs are known.
            var listenerGaps = new HashSet<Gap>(listenerHull.ConnectedGaps.Where(g => g != null));
            int listenerGapsSettled = 0;

            // Seed the open set with gaps connected to the source hull.
            foreach (var startGap in sourceHull.ConnectedGaps)
            {
                if (startGap == null) continue;

                float initialRawDist = Vector2.Distance(sourcePos, startGap.Position);
                if (initialRawDist > maxRawDistance) continue;

                gScore[startGap] = initialRawDist;
                rawGScore[startGap] = initialRawDist;
                openSet.Enqueue(startGap, initialRawDist + Heuristic(startGap, listenerPos));
                predecessors[startGap] = null;
            }

            // Main loop.
            while (openSet.TryDequeue(out Gap currentGap, out _))
            {
                // Skip stale duplicate entries left in the queue.
                if (!closedSet.Add(currentGap)) continue;

                // Early termination. When every listener-hull gap is settled, their costs are optimal.
                if (listenerGaps.Contains(currentGap) && ++listenerGapsSettled >= listenerGaps.Count)
                    break;

                float costToReachCurrentRaw = rawGScore[currentGap];
                float traversalPenalty = (currentGap == ignoredGap) ? 0f : GetGapTraversalPenalty(currentGap);
                if (traversalPenalty >= float.MaxValue) continue;

                float costAfterCrossingCurrent = gScore[currentGap] + traversalPenalty;

                // Explore neighbours (any other gap in a hull adjacent to the current gap)
                foreach (var linked in currentGap.linkedTo)
                {
                    if (linked is not Hull adjacentHull || adjacentHull.ConnectedGaps == null) continue;

                    foreach (var neighborGap in adjacentHull.ConnectedGaps)
                    {
                        if (neighborGap == null || neighborGap == currentGap) continue;

                        // No point relaxing a node that is already optimally settled.
                        if (closedSet.Contains(neighborGap)) continue;

                        float segmentDistance = Vector2.Distance(currentGap.Position, neighborGap.Position);
                        float tentativeRawGScore = costToReachCurrentRaw + segmentDistance;

                        // If the raw geometric distance is already too high, abandon this path.
                        if (tentativeRawGScore > maxRawDistance) continue;

                        float tentativeGScore = costAfterCrossingCurrent + segmentDistance;
                        float currentNeighborGScore = gScore.GetValueOrDefault(neighborGap, float.MaxValue);

                        // If a better path to the neighbour is found, save it.
                        if (tentativeGScore < currentNeighborGScore)
                        {
                            predecessors[neighborGap] = currentGap;
                            gScore[neighborGap] = tentativeGScore;
                            rawGScore[neighborGap] = tentativeRawGScore;
                            openSet.Enqueue(neighborGap, tentativeGScore + Heuristic(neighborGap, listenerPos));
                        }
                    }
                }
            }

            // Find the best settled gap in the listener's hull.
            Gap bestFinalGap = null;
            float bestTotalCost = float.MaxValue;
            foreach (var finalGap in listenerHull.ConnectedGaps)
            {
                if (finalGap == null || !gScore.TryGetValue(finalGap, out float pathCost)) continue;

                float penalty = (finalGap == ignoredGap) ? 0f : GetGapTraversalPenalty(finalGap);
                if (penalty >= float.MaxValue) continue;

                float totalCost = pathCost + penalty + Vector2.Distance(finalGap.Position, listenerPos);
                if (totalCost < bestTotalCost)
                {
                    bestTotalCost = totalCost;
                    bestFinalGap = finalGap;
                }
            }

            if (bestFinalGap == null) return PathfindingResult.NotFound;

            // Reconstruct path and calculate details
            List<Gap> path = ReconstructPath(predecessors, bestFinalGap);
            if (path.Count == 0) return PathfindingResult.NotFound;

            (float accurateRawDistance, Vector2 lastIntersectionPos) = CalculateAccuratePathDetails(sourcePos, listenerPos, path);
            if (accurateRawDistance > maxRawDistance) return PathfindingResult.NotFound;

            int closedDoorCount = 0;
            int waterSurfaceCrossings = 0;
            var countedDoors = new HashSet<Gap>();

            foreach (Gap gapInPath in path)
            {
                if (gapInPath == ignoredGap)
                    continue;
                if (Util.IsDoorClosed(gapInPath.ConnectedDoor))
                    closedDoorCount++;
                if (CheckIfSurfaceCrossing(gapInPath))
                    waterSurfaceCrossings++;
            }

            // Cache the stable topology for future calls with the same hull pair and ignoredGap.
            PathCache[cacheKey] = new CachedPath(path, closedDoorCount, waterSurfaceCrossings);

            return new PathfindingResult(accurateRawDistance, closedDoorCount, waterSurfaceCrossings, lastIntersectionPos);
        }

        private static float GetGapTraversalPenalty(Gap gap)
        {
            if (gap == null) return float.MaxValue;

            float penalty = 0.0f;

            // Check door.
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
            // Check gap in wall.
            else if (gap.Open < ConfigManager.Config.OpenWallThreshold)
            {
                return float.MaxValue; // Wall is not open enough to pass sound.
            }

            // Check for crossing an air/water barrier
            if (CheckIfSurfaceCrossing(gap))
            {
                penalty += PenaltyWaterSurface;
            }

            return penalty;
        }

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
                if (isSubmergedA != isSubmergedB)
                {
                    float closenessA = Math.Abs(gapY - waterSurfaceYA);
                    float closenessB = Math.Abs(gapY - waterSurfaceYB);

                    // If one side is very close to the water level just treat it as being on the same side
                    if (closenessA < epsilon) isSubmergedA = isSubmergedB;
                    else if (closenessB < epsilon) isSubmergedB = isSubmergedA;
                }

                return isSubmergedA != isSubmergedB;
            }

            return false;
        }

        private static float GetWaterSurfaceY(float x, Hull hull)
        {
            if (hull == null || hull.WaveY == null || hull.WaveY.Length == 0) return float.MinValue;

            int xIndex = (int)MathF.Round(x - hull.Rect.X);
            xIndex = Math.Clamp(xIndex, 0, hull.WaveY.Length - 1);

            return hull.Surface + hull.WaveY[xIndex];
        }

        private static float Heuristic(Gap node, Vector2 targetPos)
        {
            if (node == null) return float.MaxValue;
            return Vector2.Distance(node.Position, targetPos);
        }

        private static List<Gap> ReconstructPath(Dictionary<Gap, Gap> predecessors, Gap endGap)
        {
            var path = new LinkedList<Gap>();
            Gap current = endGap;
            int safetyBreak = predecessors.Count + 10; // Just in case.
            while (current != null && safetyBreak > 0)
            {
                path.AddFirst(current);
                if (!predecessors.TryGetValue(current, out current))
                {
                    current = null; // Reached the start of the path.
                }
                safetyBreak--;
            }
            // If safety break was hit, return an empty path.
            return safetyBreak <= 0 ? new List<Gap>() : path.ToList();
        }

        /// <summary>
        /// Calculates a more accurate total raw distance of a path by finding the intersection point on each gap.
        /// Also returns the position of the final intersection before the listener.
        /// </summary>
        private static (float totalDistance, Vector2 lastIntersection) CalculateAccuratePathDetails(Vector2 sourcePos, Vector2 listenerPos, IReadOnlyList<Gap> path)
        {
            if (path.Count == 0)
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