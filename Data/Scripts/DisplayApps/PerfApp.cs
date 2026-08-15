using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace DisplayApps
{
    [MyTextSurfaceScript("PerfInfo", "Info Performance")]
    public class PerfApp : AppBase
    {
        /// <summary>Shared sort buffers - PerfApp updates sequentially, so one
        /// buffer per list type is enough and nothing allocates per frame.</summary>
        static readonly List<KeyValuePair<string, PerfStat>> _statBuffer = new List<KeyValuePair<string, PerfStat>>();
        static readonly List<KeyValuePair<string, InstanceStat>> _instBuffer = new List<KeyValuePair<string, InstanceStat>>();
        static readonly List<SlowEvent> _slowBuffer = new List<SlowEvent>();

        static bool _prevPerfLog;

        public PerfApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size) { }

        protected override void RunApp()
        {
            if (ConfigPerfLog != _prevPerfLog)
            {
                Perf.Clear();
                _prevPerfLog = ConfigPerfLog;
            }

            string dump = ConfigPerfLog ? AdvancedText() : SimpleText();
            Surface.WriteText(dump, false);

            using (var frame = BeginAppFrame("PERFORMANCE", "SCRIPT UPDATE TIMING (MS)", "IconSettings", new Color(140, 200, 230)))
            {
                if (Perf.Stats.Count == 0)
                {
                    DrawEmpty(frame, "NO UPDATE DATA YET");
                    return;
                }

                if (ConfigPerfLog)
                {
                    float y = 56f * S;
                    float bottom = Bottom;
                    int maxRows = Math.Max(1, (int)((bottom - y) / (18f * S)));
                    int chars = Math.Max(24, (int)((Right - Left) / (0.40f * S * 6.5f)));

                    int lineCount = 1;
                    int pos = 0;
                    if (ConfigScroll)
                    {
                        int ci = dump.IndexOf('\n');
                        while (ci >= 0)
                        {
                            lineCount++;
                            ci = dump.IndexOf('\n', ci + 1);
                        }
                        int start = ScrollStart(0, lineCount, maxRows);
                        int lineNo = 0;
                        while (lineNo < start && pos < dump.Length)
                        {
                            int nl = dump.IndexOf('\n', pos);
                            if (nl < 0) break;
                            pos = nl + 1;
                            lineNo++;
                        }
                    }

                    int drawn = 0;
                    while (drawn < maxRows && pos <= dump.Length)
                    {
                        int nl = dump.IndexOf('\n', pos);
                        int end = nl < 0 ? dump.Length : nl;
                        string line = dump.Substring(pos, end - pos).TrimEnd();
                        if (line.Length > chars) line = line.Substring(0, chars);
                        AddText(frame, line, new Vector2(Left, y), 0.40f * S, LineColor(line), TextAlignment.LEFT);
                        y += 18f * S;
                        drawn++;
                        if (nl < 0) break;
                        pos = nl + 1;
                    }
                    if (ConfigScroll)
                        DrawScrollBar(frame, 0, lineCount, maxRows, 56f * S, bottom);
                    return;
                }

                float y2 = 56f * S;
                AddText(frame, "APP", new Vector2(Left, y2), 0.44f * S, new Color(120, 130, 145), TextAlignment.LEFT);
                AddText(frame, "UPDATES", new Vector2(Right - 260f * S, y2), 0.44f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                AddText(frame, "AVG  MAX", new Vector2(Right - 130f * S, y2), 0.44f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                AddText(frame, "SCAN", new Vector2(Right, y2), 0.44f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                y2 += 18f * S;
                DrawDivider(frame, y2 / S);
                y2 += 6f * S;

                _statBuffer.Clear();
                _statBuffer.AddRange(Perf.Stats);
                _statBuffer.Sort((a, b) => b.Value.MaxMs.CompareTo(a.Value.MaxMs));

                for (int i = 0; i < _statBuffer.Count; i++)
                {
                    if (y2 + 18f * S > Bottom) break;
                    PerfStat st = _statBuffer[i].Value;
                    string name = _statBuffer[i].Key.Replace("App", "").ToUpperInvariant();
                    Color msColor = st.MaxMs > 5.0 ? new Color(230, 190, 60) : new Color(140, 210, 160);

                    AddText(frame, name, new Vector2(Left, y2), 0.44f * S, FgColor, TextAlignment.LEFT);
                    AddText(frame, st.Count.ToString(), new Vector2(Right - 260f * S, y2), 0.44f * S, new Color(170, 175, 185), TextAlignment.RIGHT);
                    AddText(frame, $"{st.AvgMs:0.0}  {st.MaxMs:0.0}", new Vector2(Right - 130f * S, y2), 0.44f * S, msColor, TextAlignment.RIGHT);
                    AddText(frame, st.ScanAvgMs.ToString("0.0"), new Vector2(Right, y2), 0.44f * S, new Color(170, 175, 185), TextAlignment.RIGHT);
                    y2 += 18f * S;
                }

                y2 += 4f * S;
                if (y2 + 16f * S <= Bottom)
                    AddText(frame, "SCAN: EVERY UPDATE, SHARED PER GRID", new Vector2(Left, y2), 0.38f * S, new Color(110, 115, 125), TextAlignment.LEFT);
            }
        }

        static Color LineColor(string line)
        {
            if (line.StartsWith("==") || line.StartsWith("--")) return new Color(180, 190, 205);
            if (line.StartsWith("#")) return new Color(110, 115, 125);
            if (line.StartsWith("DISPLAYAPPS")) return new Color(140, 200, 230);
            return FgDefault;
        }

        static readonly Color FgDefault = new Color(200, 205, 215);

        /// <summary>Copies the basic stats into the display's text content so
        /// they can be copied from the block's terminal (Text tab).</summary>
        static string SimpleText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DISPLAYAPPS PERFORMANCE");
            sb.AppendLine("PLAYTIME: " + MyAPIGateway.Session.ElapsedPlayTime.ToString(@"hh\:mm\:ss"));
            sb.AppendLine("NAME".PadRight(18) + "UPD".PadLeft(6) + "AVG".PadLeft(8) + "MAX".PadLeft(8) + "SCANS".PadLeft(7) + "SCN_AVG".PadLeft(9) + "SCN_MAX".PadLeft(9));

            _statBuffer.Clear();
            _statBuffer.AddRange(Perf.Stats);
            _statBuffer.Sort((a, b) => b.Value.MaxMs.CompareTo(a.Value.MaxMs));
            for (int i = 0; i < _statBuffer.Count; i++)
            {
                PerfStat st = _statBuffer[i].Value;
                sb.AppendLine(_statBuffer[i].Key.PadRight(18)
                    + st.Count.ToString().PadLeft(6)
                    + st.AvgMs.ToString("0.0").PadLeft(8)
                    + st.MaxMs.ToString("0.0").PadLeft(8)
                    + st.Scans.ToString().PadLeft(7)
                    + st.ScanAvgMs.ToString("0.0").PadLeft(9)
                    + st.ScanMaxMs.ToString("0.0").PadLeft(9));
            }
            sb.AppendLine("ALL TIMES IN MS PER UPDATE. SET PerfLog: true FOR FULL DATA.");
            return sb.ToString();
        }

        /// <summary>Full advanced dump: histograms, update-interval jitter,
        /// scan cost per block, per-display breakdown and the slow update
        /// list. Written to the display text every update.</summary>
        static string AdvancedText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DISPLAYAPPS PERFORMANCE - ADVANCED");
            sb.AppendLine("PLAYTIME: " + MyAPIGateway.Session.ElapsedPlayTime.ToString(@"hh\:mm\:ss"));
            sb.AppendLine();

            sb.AppendLine("== UPDATE TIMES (ms per update) ==");
            sb.AppendLine("APP".PadRight(16) + "UPD".PadLeft(6) + "AVG".PadLeft(8) + "MIN".PadLeft(8) + "MAX".PadLeft(8)
                + "IV_AVG".PadLeft(9) + "IV_MAX".PadLeft(9) + "  HIST: <0.25/0.5/1/2/4/8/16/32/64/>64");
            _statBuffer.Clear();
            _statBuffer.AddRange(Perf.Stats);
            _statBuffer.Sort((a, b) => b.Value.MaxMs.CompareTo(a.Value.MaxMs));
            for (int i = 0; i < _statBuffer.Count; i++)
            {
                PerfStat st = _statBuffer[i].Value;
                sb.AppendLine(_statBuffer[i].Key.PadRight(16)
                    + st.Count.ToString().PadLeft(6)
                    + st.AvgMs.ToString("0.00").PadLeft(8)
                    + (st.MinMs < double.MaxValue ? st.MinMs.ToString("0.00") : "--").PadLeft(8)
                    + st.MaxMs.ToString("0.00").PadLeft(8)
                    + st.IntervalAvgMs.ToString("0").PadLeft(9)
                    + st.IntervalMaxMs.ToString("0").PadLeft(9)
                    + "  " + HistString(st.Hist));
            }
            sb.AppendLine();

            sb.AppendLine("== SCANS (every update, shared per grid) ==");
            sb.AppendLine("APP".PadRight(16) + "SCN".PadLeft(6) + "AVG".PadLeft(8) + "MIN".PadLeft(8) + "MAX".PadLeft(8)
                + "BLK_AVG".PadLeft(10) + "MS_1K".PadLeft(8) + "  HIST: <0.25/0.5/1/2/4/8/16/>16");
            for (int i = 0; i < _statBuffer.Count; i++)
            {
                PerfStat st = _statBuffer[i].Value;
                sb.AppendLine(_statBuffer[i].Key.PadRight(16)
                    + st.Scans.ToString().PadLeft(6)
                    + st.ScanAvgMs.ToString("0.00").PadLeft(8)
                    + (st.ScanMinMs < double.MaxValue ? st.ScanMinMs.ToString("0.00") : "--").PadLeft(8)
                    + st.ScanMaxMs.ToString("0.00").PadLeft(8)
                    + st.AvgBlocks.ToString("0").PadLeft(10)
                    + (st.PerBlockAvgMs * 1000.0).ToString("0.00").PadLeft(8)
                    + "  " + HistString(st.ScanHist));
            }
            sb.AppendLine();

            sb.AppendLine("== INSTANCES (per display) ==");
            sb.AppendLine("DISPLAY".PadRight(40) + "UPD".PadLeft(6) + "AVG".PadLeft(8) + "MIN".PadLeft(8) + "MAX".PadLeft(8)
                + "IV_AVG".PadLeft(9) + "IV_MAX".PadLeft(9));
            _instBuffer.Clear();
            _instBuffer.AddRange(Perf.Instances);
            _instBuffer.Sort((a, b) => b.Value.MaxMs.CompareTo(a.Value.MaxMs));
            for (int i = 0; i < _instBuffer.Count; i++)
            {
                InstanceStat s = _instBuffer[i].Value;
                string label = _instBuffer[i].Key;
                if (label.Length > 40) label = label.Substring(0, 40);
                sb.AppendLine(label.PadRight(40)
                    + s.Count.ToString().PadLeft(6)
                    + s.AvgMs.ToString("0.00").PadLeft(8)
                    + (s.MinMs < double.MaxValue ? s.MinMs.ToString("0.00") : "--").PadLeft(8)
                    + s.MaxMs.ToString("0.00").PadLeft(8)
                    + s.IntervalAvgMs.ToString("0").PadLeft(9)
                    + s.IntervalMaxMs.ToString("0").PadLeft(9));
            }
            sb.AppendLine();

            sb.AppendLine("== SLOW EVENTS (>= " + Perf.SlowMs.ToString("0") + "ms, worst " + Perf.SlowCap + ") ==");
            _slowBuffer.Clear();
            _slowBuffer.AddRange(Perf.SlowEvents);
            _slowBuffer.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            if (_slowBuffer.Count == 0)
            {
                sb.AppendLine("NONE");
            }
            else
            {
                sb.AppendLine("#".PadRight(4) + "APP".PadRight(16) + "MS".PadLeft(8) + "SCN_MS".PadLeft(8) + "  AT");
                for (int i = 0; i < _slowBuffer.Count; i++)
                {
                    sb.AppendLine((i + 1).ToString().PadRight(4)
                        + _slowBuffer[i].App.PadRight(16)
                        + _slowBuffer[i].Ms.ToString("0.00").PadLeft(8)
                        + _slowBuffer[i].ScanMs.ToString("0.00").PadLeft(8)
                        + "  " + _slowBuffer[i].PlayTime + "  " + _slowBuffer[i].Instance);
                }
            }
            sb.AppendLine();

            sb.AppendLine("# NOTES");
            sb.AppendLine("# IV = interval between updates of one display = 100 sim ticks");
            sb.AppendLine("#   (~1.67s at 60 tps; shorter when the sim runs faster)");
            sb.AppendLine("# MS_1K = scan ms per 1000 scanned blocks");
            sb.AppendLine("# HIST buckets: <0.25, 0.25-0.5, 0.5-1, 1-2, 2-4, 4-8, 8-16, 16-32, 32-64, >64 ms");
            return sb.ToString();
        }

        static string HistString(int[] hist)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < hist.Length; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(hist[i]);
            }
            return sb.ToString();
        }
    }
}