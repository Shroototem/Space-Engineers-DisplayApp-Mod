using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace DisplayApps
{
    [MyTextSurfaceScript("ComponentsInfo", "Info Components")]
    public class ComponentsApp : AppBase
    {
        class CompRow
        {
            public string Name;
            public string Icon;
            public string Value;
            public int Count;
        }

        class ComponentScan : IScanData
        {
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
            public readonly List<CompRow> Rows = new List<CompRow>();
            readonly List<CompRow> _pool = new List<CompRow>();
            public long TotalItems;
            public int MaxCount = 1;
            public string TypesText, TotalText;

            public void Clear()
            {
                Counts.Clear();
                _pool.AddRange(Rows);
                Rows.Clear();
                TotalItems = 0;
                MaxCount = 1;
                TypesText = null;
                TotalText = null;
            }

            public CompRow RentRow()
            {
                if (_pool.Count > 0)
                {
                    CompRow row = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return row;
                }
                return new CompRow();
            }
        }

        // No named Comparison<T> field - see the note in AssemblerApp.

        const string CompType = "MyObjectBuilder_Component";

        /// <summary>Display name and sprite id per component subtype - pure
        /// functions of the subtype, resolved once for the session.</summary>
        static readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        static readonly Dictionary<string, string> _iconCache = new Dictionary<string, string>();

        readonly Func<ComponentScan> _scanFunc;
        readonly Action<MyInventoryItem> _onItem;
        MySpriteDrawFrame _frame;
        ComponentScan _scan;
        readonly Action<int, float> _drawRow;

        public ComponentsApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _onItem = OnItem;
            _drawRow = DrawItemRow;
        }

        protected override void RunApp()
        {
            ComponentScan scan = GetGridScan(_scanFunc);
            _scan = scan;

            using (var frame = BeginAppFrame("COMPONENT STOCK", "ALL COMPONENTS ON GRID", "MyObjectBuilder_Component/SteelPlate", new Color(140, 210, 160)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                var rows = scan.Rows;

                if (rows.Count == 0)
                {
                    DrawEmpty(frame, "NO COMPONENTS ON GRID");
                    return;
                }

                AddText(frame, scan.TypesText, new Vector2(Left, 48f * S), 0.46f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, scan.TotalText, new Vector2(Right, 48f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y = 74f * S;
                float bottom = Bottom;
                int drawn = DrawListGroup(frame, 0, null, rows.Count, y, 0f, bottom - y, 24f * S, _drawRow);

                if (!ConfigScroll && rows.Count > drawn)
                    DrawMore(frame, $"+{rows.Count - drawn} MORE TYPE(S)");
            }
        }

        static string NameFor(string subtype)
        {
            string name;
            if (!_nameCache.TryGetValue(subtype, out name))
            {
                name = FormatItemName(subtype);
                _nameCache[subtype] = name;
            }
            return name;
        }

        static string IconFor(string subtype)
        {
            string icon;
            if (!_iconCache.TryGetValue(subtype, out icon))
            {
                icon = CompType + "/" + subtype;
                _iconCache[subtype] = icon;
            }
            return icon;
        }

        ComponentScan ScanGrid()
        {
            RefreshTerminalBlocks();

            ComponentScan scan = RentScan<ComponentScan>();
            _scan = scan;
            ForEachItem(TerminalBlocks, _onItem);

            if (ConfigFullList)
                EnsureFullListEntries(scan.Counts, SpriteLookup.Components);

            foreach (var kv in scan.Counts)
            {
                CompRow row = scan.RentRow();
                row.Count = kv.Value;
                row.Name = NameFor(kv.Key);
                row.Icon = IconFor(kv.Key);
                row.Value = kv.Value > 0 ? "x" + kv.Value.ToString("N0") : "x0";
                scan.Rows.Add(row);
            }
            scan.Rows.Sort((a, b) => b.Count.CompareTo(a.Count));
            scan.MaxCount = scan.Rows.Count > 0 && scan.Rows[0].Count > 0 ? scan.Rows[0].Count : 1;
            scan.TypesText = "COMPONENT TYPES: " + scan.Rows.Count;
            scan.TotalText = "TOTAL: " + scan.TotalItems.ToString("N0") + " ITEM(S)";
            return scan;
        }

        void OnItem(MyInventoryItem item)
        {
            ItemStats stats = GetItemStats(item.Type);
            if (stats.Category != CatComponent) return;
            string subtype = item.Type.SubtypeId;
            int count = (int)item.Amount;
            _scan.TotalItems += count;
            int cur;
            _scan.Counts.TryGetValue(subtype, out cur);
            _scan.Counts[subtype] = cur + count;
        }

        void DrawItemRow(int idx, float y)
        {
            CompRow row = _scan.Rows[idx];
            float ratio = (float)row.Count / _scan.MaxCount;
            bool hasStock = row.Count > 0;
            DrawProgressRow(_frame, y, row.Icon, row.Name, row.Value, ratio, new Color(140, 210, 160), hasStock);
        }
    }
}
