using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyAssembler = Sandbox.ModAPI.IMyAssembler;

namespace DisplayApps
{
    [MyTextSurfaceScript("AssemblerInfo", "Info Assembler")]
    public class AssemblerApp : AppBase
    {
        class QueueEntry
        {
            public string Subtype;
            public string Display;
            public string Icon;
            public float Amount;
            public string AmountText;
        }

        class AssemblerData
        {
            public string Name;
            public string Display;
            public bool Assembling;
            public bool Producing;
            public readonly List<QueueEntry> Items = new List<QueueEntry>();
            readonly List<QueueEntry> _pool = new List<QueueEntry>();

            public void Clear()
            {
                Name = "";
                Display = "";
                Assembling = false;
                Producing = false;
                _pool.AddRange(Items);
                Items.Clear();
            }

            public QueueEntry RentEntry()
            {
                if (_pool.Count > 0)
                {
                    QueueEntry entry = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return entry;
                }
                return new QueueEntry();
            }
        }

        class AssemblerScan : IScanData
        {
            public readonly List<AssemblerData> Assemblers = new List<AssemblerData>();
            readonly List<AssemblerData> _pool = new List<AssemblerData>();
            public int AsmCount, AssembleCount, DisassembleCount, QueueItems;
            public string HeaderText, QueueText;

            public void Clear()
            {
                for (int i = 0; i < Assemblers.Count; i++) Assemblers[i].Clear();
                _pool.AddRange(Assemblers);
                Assemblers.Clear();
                AsmCount = 0;
                AssembleCount = 0;
                DisassembleCount = 0;
                QueueItems = 0;
                HeaderText = null;
                QueueText = null;
            }

            public AssemblerData RentData()
            {
                if (_pool.Count > 0)
                {
                    AssemblerData data = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return data;
                }
                return new AssemblerData();
            }
        }

        sealed class NameComparer : IComparer<AssemblerData>
        {
            public static readonly NameComparer Instance = new NameComparer();
            public int Compare(AssemblerData x, AssemblerData y)
            {
                return string.Compare(x.Name, y.Name, false);
            }
        }

        /// <summary>Reused queue read buffer - the ingame GetQueue overload
        /// fills it instead of allocating a copy per assembler per scan.</summary>
        static readonly List<Sandbox.ModAPI.Ingame.MyProductionItem> _queueBuf = new List<Sandbox.ModAPI.Ingame.MyProductionItem>();

        /// <summary>Reused subtype -> Items index map for the per-assembler
        /// queue merge (was a fresh Dictionary + List per assembler).</summary>
        static readonly Dictionary<string, int> _queueIndex = new Dictionary<string, int>();

        /// <summary>Display name and sprite id per blueprint subtype - pure
        /// functions of the subtype, resolved once for the session.</summary>
        static readonly Dictionary<string, string> _displayCache = new Dictionary<string, string>();
        static readonly Dictionary<string, string> _iconCache = new Dictionary<string, string>();

        readonly Func<AssemblerScan> _scanFunc;

        public AssemblerApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
        }

        protected override void RunApp()
        {
            AssemblerScan scan = GetGridScan(_scanFunc);

            using (var frame = BeginAppFrame("ASSEMBLER STATUS", "PRODUCTION & QUEUE MONITOR", "MyObjectBuilder_Component/Construction", new Color(120, 210, 200)))
            {
                if (GuardRemoteGrid(frame, scan)) return;

                var assemblers = scan.Assemblers;
                int asmCount = scan.AsmCount;
                int queueItems = scan.QueueItems;

                AddText(frame, scan.HeaderText, new Vector2(Left, 48f * S), 0.46f * S, asmCount > 0 ? new Color(50, 210, 90) : new Color(140, 145, 155), TextAlignment.LEFT);
                AddText(frame, scan.QueueText, new Vector2(Right, 48f * S), 0.46f * S, queueItems > 0 ? FgColor : new Color(110, 115, 125), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                if (asmCount == 0)
                {
                    DrawEmpty(frame, "NO ASSEMBLERS ON GRID");
                    return;
                }

                float y = 74f * S;
                float bottom = Bottom;
                int drawn = 0;
                bool clipped = false;
                int scrollMaxRows = Math.Max(1, (int)((bottom - y) / (22f * S)));
                int startAi = ScrollStart(0, assemblers.Count, scrollMaxRows);

                for (int ai = startAi; ai < assemblers.Count && !clipped; ai++)
                {
                    AssemblerData data = assemblers[ai];

                    if (y + 22f * S > bottom) { clipped = true; break; }

                    AddText(frame, data.Display, new Vector2(Left, y), 0.50f * S, new Color(120, 210, 200), TextAlignment.LEFT);
                    AddText(frame, data.Assembling ? "ASSEMBLING" : "DISASSEMBLING", new Vector2(Right, y), 0.46f * S, data.Producing ? new Color(50, 210, 90) : new Color(140, 145, 155), TextAlignment.RIGHT);
                    y += 22f * S;
                    drawn++;

                    frame.Add(Square("SquareSimple", new Vector2(Cx, y - 4f * S), new Vector2(Right - Left, 1f * S), new Color(70, 80, 95)));
                    y += 1f * S;

                    for (int k = 0; k < data.Items.Count; k++)
                    {
                        if (y + 24f * S > bottom) { clipped = true; break; }

                        QueueEntry entry = data.Items[k];
                        bool isCurrent = data.Producing && k == 0;
                        Color itemColor = isCurrent ? new Color(80, 230, 120) : FgColor;
                        Color amtColor = isCurrent ? new Color(80, 230, 120) : new Color(170, 175, 185);
                        frame.Add(Icon(entry.Icon, new Vector2(Left + 10f * S, y + 5f * S), 16f * S, new Color(200, 210, 225)));
                        AddText(frame, entry.Display, new Vector2(Left + 24f * S, y), 0.44f * S, itemColor, TextAlignment.LEFT);
                        AddText(frame, entry.AmountText, new Vector2(Right, y), 0.44f * S, amtColor, TextAlignment.RIGHT);

                        y += 22f * S;
                    }
                }

                DrawScrollBar(frame, 0, assemblers.Count, scrollMaxRows, 74f * S, bottom);

                if (!ConfigScroll && clipped)
                {
                    int remaining = assemblers.Count - drawn + 1;
                    DrawMore(frame, $"+{remaining} MORE ASSEMBLER(S)");
                }
            }
        }

        static string DisplayFor(string subtype)
        {
            string display;
            if (!_displayCache.TryGetValue(subtype, out display))
            {
                display = subtype.Replace('_', ' ');
                _displayCache[subtype] = display;
            }
            return display;
        }

        static string IconFor(string subtype)
        {
            string icon;
            if (!_iconCache.TryGetValue(subtype, out icon))
            {
                icon = SpriteLookup.ForItem(subtype);
                _iconCache[subtype] = icon;
            }
            return icon;
        }

        AssemblerScan ScanGrid()
        {
            RefreshTerminalBlocks();

            AssemblerScan scan = RentScan<AssemblerScan>();
            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                MyAssembler a = TerminalBlocks[i] as MyAssembler;
                if (a == null) continue;
                scan.AsmCount++;

                AssemblerData data = scan.RentData();
                data.Name = BlockName(a);
                data.Display = Truncate(data.Name, 20);
                data.Assembling = a.Mode == Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly;
                data.Producing = a.IsProducing;
                if (data.Assembling) scan.AssembleCount++; else scan.DisassembleCount++;

                _queueBuf.Clear();
                a.GetQueue(_queueBuf);
                _queueIndex.Clear();
                for (int k = 0; k < _queueBuf.Count; k++)
                {
                    string subtype = _queueBuf[k].BlueprintId.SubtypeId.ToString();
                    float amt = (float)_queueBuf[k].Amount;
                    int idx;
                    if (_queueIndex.TryGetValue(subtype, out idx))
                    {
                        data.Items[idx].Amount += amt;
                    }
                    else
                    {
                        QueueEntry entry = data.RentEntry();
                        entry.Subtype = subtype;
                        entry.Display = DisplayFor(subtype);
                        entry.Icon = IconFor(subtype);
                        entry.Amount = amt;
                        _queueIndex[subtype] = data.Items.Count;
                        data.Items.Add(entry);
                    }
                }
                for (int k = 0; k < data.Items.Count; k++)
                {
                    QueueEntry entry = data.Items[k];
                    entry.AmountText = "x" + entry.Amount.ToString("0.##");
                }
                scan.QueueItems += data.Items.Count;
                scan.Assemblers.Add(data);
            }

            scan.Assemblers.Sort(NameComparer.Instance);
            scan.HeaderText = "ASSEMBLERS: " + scan.AsmCount + "   (" + scan.AssembleCount + " ASSEMBLING / " + scan.DisassembleCount + " DISASSEMBLING)";
            scan.QueueText = "QUEUE: " + scan.QueueItems + " ITEM(S)";
            return scan;
        }
    }
}
