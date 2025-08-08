
namespace SoundproofWalls
{
    using Barotrauma;
    using Microsoft.Xna.Framework.Graphics;
    using Microsoft.Xna.Framework;
    using System;
    using System.Diagnostics;
    using System.Linq;

    public static class ProfileEvents
    {
        public const string ListenerUpdate = "Listener";
        public const string EavesdroppingUpdate = "Eavesdropping";
        public const string HydrophonesUpdate = "Hydrophones";
        public const string BubblesUpdate = "Bubbles";
        public const string SoundInfoCtor = "SoundInfo";
        public const string ChannelInfoUpdate = "ChannelInfo";
        public const string ChannelInfoManagerUpdate = "ChannelInfoManager";
        public const string SidechainUpdate = "Sidechain";
        public const string EffectsManagerUpdate = "DynamicEffectsManager";
        public const string RadioFilterUpdate = "RadioEffects";
    }

    /// <summary>
    /// A sealed singleton class for profiling performance.
    /// Uses a stack to handle nested timings and prevent double-counting.
    /// </summary>
    public sealed class PerformanceProfiler
    {
        private static readonly Lazy<PerformanceProfiler> lazyInstance =
            new Lazy<PerformanceProfiler>(() => new PerformanceProfiler());

        public static PerformanceProfiler Instance => lazyInstance.Value;

        // --- Data Storage ---
        private readonly Dictionary<string, Queue<TimeSpan>> eventHistory;
        private readonly Dictionary<string, TimeSpan> averagedTimings;
        private readonly Stack<Tuple<string, Stopwatch>> stopwatchStack;
        private readonly HashSet<string> eventsUpdatedThisFrame;
        private const int HistoryLength = 10; // Number of samples to average over.
        private readonly Graph modTotalTimeGraph;
        private TimeSpan lastFrameTotalTime = TimeSpan.Zero;
        private const double TargetFrameTimeMilliseconds = 1000.0 / 60.0;

        private PerformanceProfiler()
        {
            eventHistory = new Dictionary<string, Queue<TimeSpan>>();
            averagedTimings = new Dictionary<string, TimeSpan>();
            stopwatchStack = new Stack<Tuple<string, Stopwatch>>();
            eventsUpdatedThisFrame = new HashSet<string>();
            modTotalTimeGraph = new Graph(500);
        }

        public void StartTimingEvent(string eventName)
        {
            if (!ConfigManager.LocalConfig.ShowPerformance) return;

            if (stopwatchStack.Count > 0)
            {
                stopwatchStack.Peek().Item2.Stop();
            }

            stopwatchStack.Push(new Tuple<string, Stopwatch>(eventName, Stopwatch.StartNew()));
        }

        public void StopTimingEvent()
        {
            if (!ConfigManager.LocalConfig.ShowPerformance || stopwatchStack.Count == 0) return;

            var stoppedTuple = stopwatchStack.Pop();
            stoppedTuple.Item2.Stop();
            string eventName = stoppedTuple.Item1;
            TimeSpan elapsed = stoppedTuple.Item2.Elapsed;

            eventsUpdatedThisFrame.Add(eventName);

            // Store the raw timing for this frame for averaging
            if (!eventHistory.ContainsKey(eventName))
            {
                eventHistory[eventName] = new Queue<TimeSpan>();
            }
            var queue = eventHistory[eventName];
            queue.Enqueue(elapsed);
            while (queue.Count > HistoryLength)
            {
                queue.Dequeue();
            }

            if (stopwatchStack.Count > 0)
            {
                stopwatchStack.Peek().Item2.Start();
            }
        }

        public void Update()
        {
            if (!ConfigManager.LocalConfig.ShowPerformance) return;

            if (stopwatchStack.Count > 0)
            {
                stopwatchStack.Clear();
            }

            var knownEvents = eventHistory.Keys.ToList();
            foreach (var eventName in knownEvents)
            {
                // If a known event was NOT updated this frame, it didn't run.
                // Feed a zero into its history to make its average decay over time.
                if (!eventsUpdatedThisFrame.Contains(eventName))
                {
                    var queue = eventHistory[eventName];
                    queue.Enqueue(TimeSpan.Zero);
                    while (queue.Count > HistoryLength)
                    {
                        queue.Dequeue();
                    }
                }
            }

            // --- Recalculate all averages ---
            var eventKeys = eventHistory.Keys.ToList();
            foreach (var eventName in eventKeys)
            {
                var queue = eventHistory[eventName];
                if (queue.Count > 0)
                {
                    long averageTicks = (long)queue.Average(ts => ts.Ticks);
                    TimeSpan averageTime = TimeSpan.FromTicks(averageTicks);

                    // If the average has decayed to zero, remove the event entirely.
                    if (averageTime <= TimeSpan.Zero)
                    {
                        averagedTimings.Remove(eventName);
                        eventHistory.Remove(eventName);
                    }
                    else
                    {
                        averagedTimings[eventName] = averageTime;
                    }
                }
            }

            // Store the total from the frame that just finished
            lastFrameTotalTime = TimeSpan.FromTicks(averagedTimings.Values.Sum(ts => ts.Ticks));

            // Update the graph with the averaged total
            modTotalTimeGraph.Update((float)lastFrameTotalTime.TotalMilliseconds);

            // Clear the set for the next frame.
            eventsUpdatedThisFrame.Clear();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            // --- Draw Performance Column ---
            if (ConfigManager.LocalConfig.ShowPerformance && averagedTimings.Count > 0)
            {
                float x = 700;
                float y = 10;
                float yStep = 18;

                string totalLabel = $"Soundproof Walls - Avg: {modTotalTimeGraph.Average():F4} ms | Max: {modTotalTimeGraph.LargestValue():F4} ms";
                GUI.DrawString(spriteBatch, new Vector2(x, y), totalLabel, GUIStyle.Green, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                y += yStep;
                modTotalTimeGraph.Draw(spriteBatch, new Rectangle((int)x, (int)y, 170, 50), color: GUIStyle.Green);
                y += yStep * 4;

                double totalAvgMs = lastFrameTotalTime.TotalMilliseconds;
                double framesCost = totalAvgMs / TargetFrameTimeMilliseconds;
                GUI.DrawString(spriteBatch, new Vector2(x, y), $"Equivalent Frame Cost (60 FPS): {framesCost:F2}", Color.White, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                y += yStep;

                y += yStep / 2;

                foreach (var timing in averagedTimings.OrderBy(kvp => kvp.Key))
                {
                    string eventName = timing.Key;
                    double avgEventMs = timing.Value.TotalMilliseconds;
                    double percentage = totalAvgMs > 0 ? (avgEventMs / totalAvgMs) * 100 : 0;

                    string label = $"{eventName}: {avgEventMs:F4} ms ({percentage:F1}%)";
                    GUI.DrawString(spriteBatch, new Vector2(x, y), label, Color.LightGreen, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                    y += yStep;
                }
            }

            // --- Draw Channel Info Column ---
            if (ConfigManager.LocalConfig.ShowPlayingSounds)
            {
                float x = 1100;
                float y = 10;
                float yStep = 18;
                int i = 1;
                bool stopDrawing = false;

                List<ChannelInfo> activeChannelInfos = ChannelInfoManager.ActiveChannelInfos.ToList();

                GUI.DrawString(spriteBatch, new Vector2(x, y), $"Currently Playing Sounds ({activeChannelInfos.Count}):", Color.White, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                y += yStep * 1.5f;

                foreach (ChannelInfo info in activeChannelInfos)
                {
                    if (stopDrawing) break;

                    if (y > GameMain.GraphicsHeight - 40) { stopDrawing = true; GUI.DrawString(spriteBatch, new Vector2(x, y), $"{activeChannelInfos.Count - i} more...", Color.White, Color.Black * 0.8f, font: GUIStyle.SmallFont); continue; }
                    string longName = $"{i}. {info.LongName}";
                    GUI.DrawString(spriteBatch, new Vector2(x, y), longName, Color.LightGreen, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                    y += yStep;

                    if (ConfigManager.LocalConfig.ShowChannelInfo)
                    {
                        if (y > GameMain.GraphicsHeight - 40) { stopDrawing = true; GUI.DrawString(spriteBatch, new Vector2(x, y), $"{activeChannelInfos.Count - i} more...", Color.White, Color.Black * 0.8f, font: GUIStyle.SmallFont); continue; }

                        string[] obstructionLines = info.DebugObstructionsList.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        string firstObstructionLine = obstructionLines.Length > 0 ? obstructionLines[0].Trim() : "";

                        string infoLine = $"Gain: {MathF.Round(info.Gain, 2)}   Pitch: {MathF.Round(info.Pitch, 2)}   Range: {MathF.Round(info.Channel.Far)}   Distance: {MathF.Round(info.Distance)}   Muffle: {MathF.Round(info.MuffleStrength, 3)}   Obstructions: {firstObstructionLine}";
                        GUI.DrawString(spriteBatch, new Vector2(x + 10, y), infoLine, Color.LightGray, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                        y += yStep;

                        if (obstructionLines.Length > 1)
                        {
                            for (int j = 1; j < obstructionLines.Length; j++)
                            {
                                string line = obstructionLines[j];
                                if (y > GameMain.GraphicsHeight - 40) { stopDrawing = true; GUI.DrawString(spriteBatch, new Vector2(x, y), $"{activeChannelInfos.Count - i} more...", Color.White, Color.Black * 0.8f, font: GUIStyle.SmallFont); break; }
                                GUI.DrawString(spriteBatch, new Vector2(x + 20, y), line.Trim(), Color.LightGray, Color.Black * 0.8f, font: GUIStyle.SmallFont);
                                y += yStep;
                            }
                        }
                    }
                    i++;
                }
            }
        }
    }
}
