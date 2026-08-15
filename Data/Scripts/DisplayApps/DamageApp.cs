using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace DisplayApps
{
    [MyTextSurfaceScript("DamageInfo", "Info Damaged")]
    public class DamageApp : AppBase
    {
        const int HighlightCount = 10;

        /// <summary>Applied highlights per scan grid. Keyed by grid so
        /// displays on different grids never fight over each other's
        /// outlines, and applied once per grid per window (Window stamp)
        /// instead of once per display.</summary>
        class GridHighlights
        {
            public long Window = -1;
            public long WantWindow = -1;
            public readonly HashSet<string> Applied = new HashSet<string>();
        }

        static readonly Dictionary<long, GridHighlights> _hlByGrid = new Dictionary<long, GridHighlights>();

        /// <summary>Reused "currently wanted" set - cleared per update instead of
        /// allocating a fresh HashSet every frame.</summary>
        static readonly HashSet<string> _wanted = new HashSet<string>();

        /// <summary>Display name and icon per block definition. Damaged armor
        /// fields share a handful of definitions, so the localization lookup
        /// and icon string concat run once per definition instead of once per
        /// damaged block per scan.</summary>
        static readonly Dictionary<object, string> _defNames = new Dictionary<object, string>();
        static readonly Dictionary<object, string> _defIcons = new Dictionary<object, string>();

        /// <summary>"0%".."100%" - integrity labels without per-row formatting.</summary>
        static readonly string[] _pctText = BuildPctTable();

        static string[] BuildPctTable()
        {
            string[] t = new string[101];
            for (int i = 0; i <= 100; i++) t[i] = i + "%";
            return t;
        }

        class DamageRow
        {
            public MySlimBlock Block;
            public float Ratio;
            public string Name;
            public string Icon;
        }

        class DamageScan : IScanData
        {
            public readonly List<DamageRow> Rows = new List<DamageRow>();
            readonly List<DamageRow> _pool = new List<DamageRow>();

            /// <summary>Names of the worst HighlightCount fat blocks, collected
            /// scan-side so every display shares the selection.</summary>
            public readonly List<string> HlNames = new List<string>();

            public string CountText;

            const int PoolCap = 256;

            public void Clear()
            {
                // Null the references so pooled rows don't pin repaired or
                // destroyed blocks (and their grid) for the session, and cap
                // the pool so one catastrophic damage event doesn't inflate
                // it permanently.
                for (int i = 0; i < Rows.Count; i++)
                {
                    DamageRow row = Rows[i];
                    row.Block = null;
                    row.Name = null;
                    row.Icon = null;
                    if (_pool.Count < PoolCap) _pool.Add(row);
                }
                Rows.Clear();
                HlNames.Clear();
                CountText = null;
            }

            public DamageRow RentRow()
            {
                if (_pool.Count > 0)
                {
                    DamageRow row = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return row;
                }
                return new DamageRow();
            }
        }

        // Comparison<T>, not IComparer<T> - see the note in AssemblerApp.
        static readonly Comparison<DamageRow> RatioDesc = (a, b) => b.Ratio.CompareTo(a.Ratio);

        readonly Func<DamageScan> _scanFunc;
        MySpriteDrawFrame _frame;
        DamageScan _scan;
        readonly Action<int, float> _drawRow;
        long _hlGridId;

        public DamageApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawRow = DrawRow;
        }

        public override void Dispose()
        {
            // Clear only this display's grid. If another display on the grid
            // still wants the highlights it re-applies them next window.
            GridHighlights hl;
            if (_hlGridId != 0 && _hlByGrid.TryGetValue(_hlGridId, out hl))
                ClearGrid(_hlGridId, hl);
            base.Dispose();
        }

        protected override void RunApp()
        {
            DamageScan scan = GetGridScan(_scanFunc);
            _scan = scan;
            UpdateHighlights(scan);

            using (var frame = BeginAppFrame("DAMAGE REPORT", "BLOCKS NEEDING WELDING", "Danger", new Color(220, 90, 70)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                if (scan.Rows.Count == 0)
                {
                    DrawEmpty(frame, "NO DAMAGED BLOCKS");
                    return;
                }

                AddText(frame, scan.CountText, new Vector2(Left, 48f * S), 0.46f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, "MOST COMPLETE FIRST", new Vector2(Right, 48f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y = 74f * S;
                float bottom = Bottom;
                int drawn = DrawListGroup(frame, 0, null, scan.Rows.Count, y, 0f, bottom - y, 24f * S, _drawRow);

                if (!ConfigScroll && scan.Rows.Count > drawn)
                    DrawMore(frame, $"+{scan.Rows.Count - drawn} MORE");
            }
        }

        /// <summary>Keeps the highlight outline on the worst HighlightCount
        /// blocks of this display's scan grid. The candidate names come from
        /// the shared scan, so the diff and the SetHighlightLocal calls run
        /// once per grid per window, no matter how many displays show it.
        /// Wanted highlights are re-applied every window because the game
        /// drops the outline when a block's damage model state changes. When
        /// no display on the grid has wanted highlights for over a window,
        /// they are cleared.</summary>
        void UpdateHighlights(DamageScan scan)
        {
            long gridId = ScanGridId;
            if (gridId == 0) return;
            long window = Window();

            GridHighlights hl;
            _hlByGrid.TryGetValue(gridId, out hl);

            if (ConfigHighlightDamaged && scan != null)
            {
                _hlGridId = gridId;
                if (hl == null)
                {
                    hl = new GridHighlights();
                    _hlByGrid[gridId] = hl;
                }
                hl.WantWindow = window;
                if (hl.Window == window) return;
                hl.Window = window;

                _wanted.Clear();
                for (int i = 0; i < scan.HlNames.Count; i++)
                    _wanted.Add(scan.HlNames[i]);

                foreach (var name in hl.Applied)
                {
                    if (!_wanted.Contains(name))
                        SetBlockHighlight(name, false);
                }
                foreach (var name in _wanted)
                {
                    SetBlockHighlight(name, true);
                }

                hl.Applied.Clear();
                hl.Applied.UnionWith(_wanted);
            }
            else if (hl != null && window - hl.WantWindow > 1)
            {
                ClearGrid(gridId, hl);
            }
        }

        static void ClearGrid(long gridId, GridHighlights hl)
        {
            foreach (var name in hl.Applied)
                SetBlockHighlight(name, false);
            hl.Applied.Clear();
            _hlByGrid.Remove(gridId);
        }

        static void SetBlockHighlight(string name, bool on)
        {
            if (on)
                MyVisualScriptLogicProvider.SetHighlightLocal(name, thickness: 2, pulseTimeInFrames: 0, color: new Color(220, 60, 50));
            else
                MyVisualScriptLogicProvider.SetHighlightLocal(name, thickness: -1);
        }

        static string DefName(MySlimBlock slim)
        {
            object def = slim.BlockDefinition;
            string name;
            if (!_defNames.TryGetValue(def, out name))
            {
                name = Truncate(slim.BlockDefinition.DisplayNameText ?? "", 22);
                _defNames[def] = name;
            }
            return name;
        }

        static string DefIcon(MySlimBlock slim)
        {
            object def = slim.BlockDefinition;
            string icon;
            if (!_defIcons.TryGetValue(def, out icon))
            {
                icon = BlockIcon(slim, "MyObjectBuilder_Component/Construction");
                _defIcons[def] = icon;
            }
            return icon;
        }

        DamageScan ScanGrid()
        {
            RefreshGridBlocks();

            DamageScan scan = RentScan<DamageScan>();
            for (int i = 0; i < GridBlocks.Count; i++)
            {
                MySlimBlock slim = GridBlocks[i];
                if (slim.IsFullIntegrity) continue;
                float max = Math.Max(slim.MaxIntegrity, 0.1f);
                float ratio = slim.Integrity / max;

                DamageRow row = scan.RentRow();
                row.Block = slim;
                row.Ratio = ratio;
                if (slim.FatBlock != null)
                {
                    row.Name = Truncate(BlockName(slim.FatBlock), 22);
                    if (row.Name.Length == 0) row.Name = DefName(slim);
                }
                else
                {
                    row.Name = DefName(slim);
                }
                if (row.Name.Length == 0) row.Name = "BLOCK";
                row.Icon = DefIcon(slim);
                scan.Rows.Add(row);
            }
            scan.Rows.Sort(RatioDesc);

            // Worst HighlightCount fat blocks (the end of the list - sorted
            // most complete first). Blocks with an empty entity name get one
            // registered (EntityId string) - the highlight system resolves
            // entities by name and silently ignores unknown ones.
            int found = 0;
            for (int i = scan.Rows.Count - 1; i >= 0 && found < HighlightCount; i--)
            {
                var fb = scan.Rows[i].Block.FatBlock;
                if (fb == null) continue;
                if (string.IsNullOrEmpty(fb.Name))
                    fb.Name = fb.EntityId.ToString();
                scan.HlNames.Add(fb.Name);
                found++;
            }

            scan.CountText = "WELDING REQUIRED: " + scan.Rows.Count + " BLOCK(S)";
            return scan;
        }

        void DrawRow(int idx, float y)
        {
            DamageRow row = _scan.Rows[idx];
            int pct = (int)(row.Ratio * 100f + 0.5f);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            DrawProgressRow(_frame, y, row.Icon, row.Name, _pctText[pct], row.Ratio, BarColor(row.Ratio), true, null, new Color(220, 90, 70));
        }
    }
}
