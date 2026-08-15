using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.IMyInventoryItem;

namespace DisplayApps
{
    [MyTextSurfaceScript("ComponentsInfo", "Info Components")]
    public class ComponentsApp : AppBase
    {
        class CompRow
        {
            public string Name;
            public string Icon;
            public int Count;
        }

        class ComponentScan : IScanData
        {
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
            public readonly List<CompRow> Rows = new List<CompRow>();
            readonly List<CompRow> _pool = new List<CompRow>();
            public long TotalItems;

            public void Clear()
            {
                Counts.Clear();
                _pool.AddRange(Rows);
                Rows.Clear();
                TotalItems = 0;
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

        const string CompType = "MyObjectBuilder_Component";

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

                AddText(frame, $"COMPONENT TYPES: {rows.Count}", new Vector2(Left, 48f * S), 0.46f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, $"TOTAL: {scan.TotalItems:N0} ITEM(S)", new Vector2(Right, 48f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y = 74f * S;
                float bottom = Bottom;
                int maxCount = rows.Count > 0 ? rows[0].Count : 1;
                int drawn = DrawListGroup(frame, 0, null, rows.Count, y, 0f, bottom - y, 24f * S, _drawRow);

                if (!ConfigScroll && rows.Count > drawn)
                    DrawMore(frame, $"+{rows.Count - drawn} MORE TYPE(S)");
            }
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
                row.Name = FormatItemName(kv.Key);
                row.Icon = CompType + "/" + kv.Key;
                scan.Rows.Add(row);
            }
            scan.Rows.Sort((a, b) => b.Count.CompareTo(a.Count));
            return scan;
        }

        void OnItem(MyInventoryItem item)
        {
            var content = item.Content;
            if (content == null) return;
            if (content.TypeId.ToString() != CompType) return;
            string subtype = content.SubtypeName;
            int count = (int)item.Amount;
            _scan.TotalItems += count;
            int cur;
            if (_scan.Counts.TryGetValue(subtype, out cur)) _scan.Counts[subtype] = cur + count;
            else _scan.Counts[subtype] = count;
        }

        void DrawItemRow(int idx, float y)
        {
            CompRow row = _scan.Rows[idx];
            int maxCount = _scan.Rows[0].Count;
            float ratio = maxCount > 0 ? (float)row.Count / maxCount : 0f;
            bool hasStock = row.Count > 0;
            DrawProgressRow(_frame, y, row.Icon, row.Name, hasStock ? $"x{row.Count:N0}" : "x0", ratio, new Color(140, 210, 160), hasStock);
        }
    }
}