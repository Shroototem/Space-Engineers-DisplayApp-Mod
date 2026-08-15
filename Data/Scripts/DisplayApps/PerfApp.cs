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

        /// <summary>Shared text builder for the dump - reused across updates
        /// so the buffer never re-grows past the first build.</summary>
        static readonly StringBuilder _sb = new StringBuilder(8192);

        /// <summary>Cap for the per-display section of the advanced dump. The
        /// list is sorted worst-first, so the tail carries no information, and
        /// without a cap the dump grows with every LCD in the world.</summary>
        const int MaxInstanceRows = 30;

        /// <summary>Text-tab refresh happens every Nth update - the Text tab
        /// is a manual-inspection feature and each write is a synced, saved
        /// surface property, so it does not need 1.67 s freshness.</summary>
        const int TextRefreshEvery = 6;

        sealed class StatMaxDesc : IComparer<KeyValuePair<string, PerfStat>>
        {
            public static readonly StatMaxDesc Instance = new StatMaxDesc();
            public int Compare(KeyValuePair<string, PerfStat> a, KeyValuePair<string, PerfStat> b)
            {
                return b.Value.MaxMs.CompareTo(a.Value.MaxMs);
            }
        }

        sealed class InstMaxDesc : IComparer<KeyValuePair<string, InstanceStat>>
        {
            public static readonly InstMaxDesc Instance = new InstMaxDesc();
            public int Compare(KeyValuePair<string, InstanceStat> a, KeyValuePair<string, InstanceStat> b)
            {
                return b.Value.MaxMs.CompareTo(a.Value.MaxMs);
            }
        }

        sealed class SlowDesc : IComparer<SlowEvent>
        {
            public static readonly SlowDesc Instance = new SlowDesc();
            public int Compare(SlowEvent a, SlowEvent b)
            {
                return b.Ms.CompareTo(a.Ms);
            }
        }

        // Per-display state: PerfLog is per-display config, so the transition
        // tracking must be per instance - a static here made two PerfApp LCDs
        // with different settings wipe the stats on every update.
        bool _prevPerfLog;
        int _textCooldown;
        string _lastWritten;

        public PerfApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size) { }

        protected override void RunApp()
        {
            if (ConfigPerfLog != _prevPerfLog)
            {
                Perf.Clear();
                _prevPerfLog = ConfigPerfLog;
            }

            string dump = null;
            if (Perf.Stats.Count > 0)
            {
                _statBuffer.Clear();
                _statBuffer.AddRange(Perf.Stats);
                _statBuffer.Sort(StatMaxDesc.Instance);

                if (ConfigPerfLog)
                {
                    dump = AdvancedText();
                    if (--_textCooldown <= 0)
                    {
                        _textCooldown = TextRefreshEvery;
                        WriteTextIfChanged(dump);
                    }
                }
                else if (--_textCooldown <= 0)
                {
                    _textCooldown = TextRefreshEvery;
                    WriteTextIfChanged(SimpleText());
                }
            }

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

                for (int i = 0; i < _statBuffer.Count; i++)
                {
                    if (y2 + 18f * S > Bottom) break;
                    PerfStat st = _statBuffer[i].Value;
                    string name = ShortName(_statBuffer[i].Key);
                    Color msColor = st.MaxMs > 5.0 ? new Color(230, 190, 60) : new Color(140, 210, 160);

                    AddText(frame, name, new Vector2(Left, y2), 0.44f * S, FgColor, TextAlignment.LEFT);
                    AddText(frame, st.Count.ToString(), new Vector2(Right - 260f * S, y2), 0.44f * S, new Color(170, 175, 185), TextAlignment.RIGHT);
                    AddText(frame, st.AvgMs.ToString("0.0") + "  " + st.MaxMs.ToString("0.0"), new Vector2(Right - 130f * S, y2), 0.44f * S, msColor, TextAlignment.RIGHT);
                    AddText(frame, st.ScanAvgMs.ToString("0.0"), new Vector2(Right, y2), 0.44f * S, new Color(170, 175, 185), TextAlignment.RIGHT);
                    y2 += 18f * S;
                }

                y2 += 4f * S;
                if (y2 + 16f * S <= Bottom)
                    AddText(frame, "SCAN: EVERY UPDATE, SHARED PER GRID", new Vector2(Left, y2), 0.38f * S, new Color(110, 115, 125), TextAlignment.LEFT);
            }
        }

        /// <summary>Pushes the dump to the surface text only when it actually
        /// changed - WriteText stores a synced, saved property, so identical
        /// rewrites are pure network/save churn.</summary>
        void WriteTextIfChanged(string dump)
        {
            if (string.Equals(dump, _lastWritten, StringComparison.Ordinal)) return;
            _lastWritten = dump;
            Surface.WriteText(dump, false);
        }

        static Color LineColor(string line)
        {
            if (line.StartsWith("==") || line.StartsWith("--")) return new Color(180, 190, 205);
            if (line.StartsWith("#")) return new Color(110, 115, 125);
            if (line.StartsWith("DISPLAYAPPS")) return new Color(140, 200, 230);
            return FgDefault;
        }

        static readonly Color FgDefault = new Color(200, 205, 215);

        /// <summary>"PowerApp" -> "POWER" etc., memoized - bounded by the
        /// number of app types.</summary>
        static readonly Dictionary<string, string> _shortNames = new Dictionary<string, string>();

        static string ShortName(string key)
        {
            string n;
            if (!_shortNames.TryGetValue(key, out n))
            {
                n = key.Replace("App", "").ToUpperInvariant();
                _shortNames[key] = n;
            }
            return n;
        }

        /// <summary>Appends s left-aligned in a w-wide column (PadRight without
        /// the intermediate string).</summary>
        static void PadR(StringBuilder sb, string s, int w)
        {
            sb.Append(s);
            if (s.Length < w) sb.Append(' ', w - s.Length);
        }

        /// <summary>Appends s right-aligned in a w-wide column (PadLeft without
        /// the intermediate string).</summary>
        static void PadL(StringBuilder sb, string s, int w)
        {
            if (s.Length < w) sb.Append(' ', w - s.Length);
            sb.Append(s);
        }

        static void AppendHist(StringBuilder sb, int[] hist)
        {
            for (int i = 0; i < hist.Length; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(hist[i]);
            }
        }

        /// <summary>Copies the basic stats into the display's text content so
        /// they can be copied from the block's terminal (Text tab). Reads the
        /// pre-sorted _statBuffer filled by RunApp.</summary>
        static string SimpleText()
        {
            var sb = _sb;
            sb.Clear();
            sb.AppendLine("DISPLAYAPPS PERFORMANCE");
            sb.Append("PLAYTIME: ").AppendLine(MyAPIGateway.Session.ElapsedPlayTime.ToString(@"hh\:mm\:ss"));
            sb.AppendLine("NAME".PadRight(18) + "UPD".PadLeft(6) + "AVG".PadLeft(8) + "MAX".PadLeft(8) + "SCANS".PadLeft(7) + "SCN_AVG".PadLeft(9) + "SCN_MAX".PadLeft(9));

            for (int i = 0; i < _statBuffer.Count; i++)
            {
                PerfStat st = _statBuffer[i].Value;
                PadR(sb, _statBuffer[i].Key, 18);
                PadL(sb, st.Count.ToString(), 6);
                PadL(sb, st.AvgMs.ToString("0.0"), 8);
                PadL(sb, st.MaxMs.ToString("0.0"), 8);
                PadL(sb, st.Scans.ToString(), 7);
                PadL(sb, st.ScanAvgMs.ToString("0.0"), 9);
                PadL(sb, st.ScanMaxMs.ToString("0.0"), 9);
                sb.AppendLine();
            }
            sb.AppendLine("ALL TIMES IN MS PER UPDATE. SET PerfLog: true FOR FULL DATA.");
            return sb.ToString();
        }

        /// <summary>Full advanced dump: histograms, update-interval jitter,
        /// scan cost per block, per-display breakdown and the slow update
        /// list. Reads the pre-sorted _statBuffer filled by RunApp.</summary>
        static string AdvancedText()
        {
            var sb = _sb;
            sb.Clear();
            sb.AppendLine("DISPLAYAPPS PERFORMANCE - ADVANCED");
            sb.Append("PLAYTIME: ").AppendLine(MyAPIGateway.Session.ElapsedPlayTime.ToString(@"hh\:mm\:ss"));
            sb.AppendLine();

            sb.AppendLine("== UPDATE TIMES (ms per update) ==");
            sb.AppendLine("APP".PadRight(16) + "UPD".PadLeft(6) + "AVG".PadLeft(8) + "MIN".PadLeft(8) + "MAX".PadLeft(8)
                + "IV_AVG".PadLeft(9) + "IV_MAX".PadLeft(9) + "  HIST: <0.25/0.5/1/2/4/8/16/32/64/>64");
            for (int i = 0; i < _statBuffer.Count; i++)
            {
                PerfStat st = _statBuffer[i].Value;
                PadR(sb, _statBuffer[i].Key, 16);
                PadL(sb, st.Count.ToString(), 6);
                PadL(sb, st.AvgMs.ToString("0.00"), 8);
                PadL(sb, st.MinMs < double.MaxValue ? st.MinMs.ToString("0.00") : "--", 8);
                PadL(sb, st.MaxMs.ToString("0.00"), 8);
                PadL(sb, st.IntervalAvgMs.ToString("0"), 9);
                PadL(sb, st.IntervalMaxMs.ToString("0"), 9);
                sb.Append("  ");
                AppendHist(sb, st.Hist);
                sb.AppendLine();
            }
            sb.AppendLine();

            sb.AppendLine("== SCANS (every update, shared per grid) ==");
            sb.AppendLine("APP".PadRight(16) + "SCN".PadLeft(6) + "AVG".PadLeft(8) + "MIN".PadLeft(8) + "MAX".PadLeft(8)
                + "BLK_AVG".PadLeft(10) + "MS_1K".PadLeft(8) + "  HIST: <0.25/0.5/1/2/4/8/16/>16");
            for (int i = 0; i < _statBuffer.Count; i++)
            {
                PerfStat st = _statBuffer[i].Value;
                PadR(sb, _statBuffer[i].Key, 16);
                PadL(sb, st.Scans.ToString(), 6);
                PadL(sb, st.ScanAvgMs.ToString("0.00"), 8);
                PadL(sb, st.ScanMinMs < double.MaxValue ? st.ScanMinMs.ToString("0.00") : "--", 8);
                PadL(sb, st.ScanMaxMs.ToString("0.00"), 8);
                PadL(sb, st.AvgBlocks.ToString("0"), 10);
                PadL(sb, (st.PerBlockAvgMs * 1000.0).ToString("0.00"), 8);
                sb.Append("  ");
                AppendHist(sb, st.ScanHist);
                sb.AppendLine();
            }
            sb.AppendLine();

            sb.AppendLine("== INSTANCES (per display) ==");
            sb.AppendLine("DISPLAY".PadRight(40) + "UPD".PadLeft(6) + "AVG".PadLeft(8) + "MIN".PadLeft(8) + "MAX".PadLeft(8)
                + "IV_AVG".PadLeft(9) + "IV_MAX".PadLeft(9));
            _instBuffer.Clear();
            _instBuffer.AddRange(Perf.Instances);
            _instBuffer.Sort(InstMaxDesc.Instance);
            int shown = Math.Min(_instBuffer.Count, MaxInstanceRows);
            for (int i = 0; i < shown; i++)
            {
                InstanceStat s = _instBuffer[i].Value;
                string label = _instBuffer[i].Key;
                if (label.Length > 40) label = label.Substring(0, 40);
                PadR(sb, label, 40);
                PadL(sb, s.Count.ToString(), 6);
                PadL(sb, s.AvgMs.ToString("0.00"), 8);
                PadL(sb, s.MinMs < double.MaxValue ? s.MinMs.ToString("0.00") : "--", 8);
                PadL(sb, s.MaxMs.ToString("0.00"), 8);
                PadL(sb, s.IntervalAvgMs.ToString("0"), 9);
                PadL(sb, s.IntervalMaxMs.ToString("0"), 9);
                sb.AppendLine();
            }
            if (_instBuffer.Count > shown)
                sb.Append("... +").Append(_instBuffer.Count - shown).AppendLine(" MORE DISPLAYS");
            sb.AppendLine();

            sb.Append("== SLOW EVENTS (>= ").Append(Perf.SlowMs.ToString("0")).Append("ms, worst ").Append(Perf.SlowCap).AppendLine(") ==");
            _slowBuffer.Clear();
            _slowBuffer.AddRange(Perf.SlowEvents);
            _slowBuffer.Sort(SlowDesc.Instance);
            if (_slowBuffer.Count == 0)
            {
                sb.AppendLine("NONE");
            }
            else
            {
                sb.AppendLine("#".PadRight(4) + "APP".PadRight(16) + "MS".PadLeft(8) + "SCN_MS".PadLeft(8) + "  AT");
                for (int i = 0; i < _slowBuffer.Count; i++)
                {
                    PadR(sb, (i + 1).ToString(), 4);
                    PadR(sb, _slowBuffer[i].App, 16);
                    PadL(sb, _slowBuffer[i].Ms.ToString("0.00"), 8);
                    PadL(sb, _slowBuffer[i].ScanMs.ToString("0.00"), 8);
                    sb.Append("  ").Append(_slowBuffer[i].PlayTime).Append("  ").AppendLine(_slowBuffer[i].Instance);
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
    }
}
