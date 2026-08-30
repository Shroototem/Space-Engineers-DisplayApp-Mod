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
            public readonly HashSet<long> Applied = new HashSet<long>();
        }

        static readonly Dictionary<long, GridHighlights> _hlByGrid = new Dictionary<long, GridHighlights>();

        /// <summary>Reused "currently wanted" set - cleared per update instead of
        /// allocating a fresh HashSet every frame.</summary>
        static readonly HashSet<long> _wanted = new HashSet<long>();

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
            public readonly List<DamageRow> ProjRows = new List<DamageRow>();
            readonly List<DamageRow> _projPool = new List<DamageRow>();

            /// <summary>EntityIds of the worst HighlightCount fat blocks, collected
            /// scan-side so every display shares the selection. Stored as
            /// EntityId (long) not Name string so renaming the block does not
            /// orphan the highlight (old Name string would no longer resolve
            /// via TryGetEntityByName and the -1 clear would be lost).</summary>
            public readonly List<long> HlNames = new List<long>();

            public string CountText;
            public string ProjText;
            public int ProjMissing;
            public float DamagePercent;

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
                for (int i = 0; i < ProjRows.Count; i++)
                {
                    DamageRow row = ProjRows[i];
                    row.Block = null;
                    row.Name = null;
                    row.Icon = null;
                    if (_projPool.Count < PoolCap) _projPool.Add(row);
                }
                Rows.Clear();
                ProjRows.Clear();
                HlNames.Clear();
                CountText = null;
                ProjText = null;
                ProjMissing = 0;
                DamagePercent = 0f;
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

            public DamageRow RentProjRow()
            {
                if (_projPool.Count > 0)
                {
                    DamageRow row = _projPool[_projPool.Count - 1];
                    _projPool.RemoveAt(_projPool.Count - 1);
                    return row;
                }
                return new DamageRow();
            }
        }

        // No named Comparison<T> field - see the note in AssemblerApp.

        readonly Func<DamageScan> _scanFunc;
        MySpriteDrawFrame _frame;
        DamageScan _scan;
        readonly Action<int, float> _drawRow;
        readonly Action<int, float> _drawProjRow;
        long _hlGridId;

        public DamageApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawRow = DrawRow;
            _drawProjRow = DrawProjRow;
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

                bool showDamage = GetSectionVisible("ShowDamaged", true);
                bool showProjector = GetSectionVisible("ShowProjector", true);

                if (!showDamage && !showProjector)
                {
                    DrawEmpty(frame, "ALL SECTIONS HIDDEN");
                    return;
                }

                int totalRows = (showDamage?scan.Rows.Count:0) + (showProjector?scan.ProjRows.Count:0);
                if (totalRows == 0)
                {
                    DrawEmpty(frame, "NO DAMAGED BLOCKS");
                    return;
                }

                // Header includes overall damage % shared with DockedShips
                string header = scan.CountText;
                if (scan.DamagePercent > 0.01f) header += $"  |  DAMAGE {scan.DamagePercent.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}%";
                AddText(frame, header, new Vector2(Left, 48f * S), 0.46f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, "MOST COMPLETE FIRST", new Vector2(Right, 48f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y = 74f * S;
                float bottom = Bottom;

                if (showDamage && showProjector && scan.ProjRows.Count>0)
                {
                    if (ConfigScroll)
                    {
                        float headerH = 16f*S;
                        float gap = 6f*S;
                        float gh = ListGroupHeight(bottom - y, 2, headerH, gap);
                        DrawListGroup(frame, 0, $"DAMAGED ({scan.Rows.Count})", scan.Rows.Count, y, headerH, gh, 24f*S, _drawRow);
                        DrawDivider(frame, (y+headerH+gh+gap/2f)/S);
                        DrawListGroup(frame, 1, scan.ProjText, scan.ProjRows.Count, ListGroupTop(y,1,gh,headerH,gap), headerH, gh, 24f*S, _drawProjRow);
                    }
                    else
                    {
                        // stacked without scroll: show damaged first
                        int maxRows = Math.Max(1,(int)((bottom-y)/ (24f*S)));
                        int drawn=0;
                        int start = ScrollStart(0, scan.Rows.Count, maxRows/2);
                        for(int i=start;i<scan.Rows.Count && drawn<maxRows/2;i++) { DrawRow(i, y+drawn*24f*S); drawn++; }
                        float y2 = y + drawn*24f*S + 10f*S;
                        if (y2+24f*S < bottom)
                        {
                            AddText(frame, scan.ProjText, new Vector2(Left,y2),0.44f*S,new Color(180,190,205),TextAlignment.LEFT);
                            y2+=16f*S;
                            for(int i=0;i<scan.ProjRows.Count && y2+24f*S<=bottom;i++) { DrawProjRow(i,y2); y2+=24f*S; }
                        }
                        if (totalRows > drawn) DrawMore(frame,$"+{(totalRows-drawn).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} MORE");
                    }
                }
                else if (showDamage)
                {
                    int drawn = DrawListGroup(frame, 0, null, scan.Rows.Count, y, 0f, bottom - y, 24f * S, _drawRow);
                    if (!ConfigScroll && scan.Rows.Count > drawn)
                        DrawMore(frame, $"+{(scan.Rows.Count - drawn).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} MORE");
                }
                else if (showProjector)
                {
                    if (scan.ProjRows.Count==0) { DrawEmpty(frame,"NO PROJECTOR DAMAGE"); return; }
                    int drawn = DrawListGroup(frame, 0, scan.ProjText, scan.ProjRows.Count, y, 16f*S, bottom - y, 24f*S, _drawProjRow);
                    if (!ConfigScroll && scan.ProjRows.Count > drawn)
                        DrawMore(frame, $"+{(scan.ProjRows.Count - drawn).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} MORE");
                }
            }
        }

        /// <summary>Keeps the highlight outline on the worst HighlightCount
        /// blocks of this display's scan grid. The candidate EntityIds come
        /// from the shared scan, so the diff and the SetHighlightLocal calls
        /// run once per grid per window, no matter how many displays show it.
        /// Wanted highlights are re-applied every window because the game
        /// drops the outline when a block's damage model state changes. When
        /// no display on the grid has wanted highlights for over a window,
        /// they are cleared.</summary>
        void UpdateHighlights(DamageScan scan)
        {
            long gridId = ScanGridId;
            if (gridId == 0) return;
            long window = Window();

            // If the scan grid changed (e.g. SubGrids toggled, RemoteGrid
            // changed, or ship split) the previous grid's highlights would
            // otherwise stay orphaned in _hlByGrid forever - no display will
            // ever update that old gridId again. Clear the old.
            if (_hlGridId != 0 && _hlGridId != gridId)
            {
                GridHighlights oldHl;
                if (_hlByGrid.TryGetValue(_hlGridId, out oldHl))
                    ClearGrid(_hlGridId, oldHl);
            }

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

                foreach (var id in hl.Applied)
                {
                    if (!_wanted.Contains(id))
                        SetBlockHighlight(id, false);
                }
                foreach (var id in _wanted)
                {
                    SetBlockHighlight(id, true);
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
            foreach (var id in hl.Applied)
                SetBlockHighlight(id, false);
            hl.Applied.Clear();
            _hlByGrid.Remove(gridId);
        }

        static void SetBlockHighlight(long entityId, bool on)
        {
            try
            {
                var entity = MyAPIGateway.Entities.GetEntityById(entityId);
                string curName = null;
                if (entity != null)
                {
                    var term = entity as Sandbox.ModAPI.IMyTerminalBlock;
                    if (term != null) curName = term.Name;
                    if (string.IsNullOrEmpty(curName))
                    {
                        // Original code mutated Name to EntityId at scan time
                        // so TryGetEntityByName(idStr) could find empty-Name
                        // blocks. Do it here at highlight time as well.
                        try { var e = entity as VRage.ModAPI.IMyEntity; if (e != null) e.Name = entityId.ToString(); } catch { }
                        try { if (term != null) term.Name = entityId.ToString(); } catch { }
                        curName = entityId.ToString();
                    }
                }
                string idStr = entityId.ToString();
                string primary = !string.IsNullOrEmpty(curName) ? curName : idStr;
                if (on)
                    MyVisualScriptLogicProvider.SetHighlightLocal(primary, thickness: 2, pulseTimeInFrames: 0, color: new Color(220, 60, 50));
                else
                    MyVisualScriptLogicProvider.SetHighlightLocal(primary, thickness: -1);
                if (entity != null && curName != null && curName != idStr && !string.IsNullOrEmpty(curName))
                {
                    if (on)
                        MyVisualScriptLogicProvider.SetHighlightLocal(idStr, thickness: 2, pulseTimeInFrames: 0, color: new Color(220, 60, 50));
                    else
                        MyVisualScriptLogicProvider.SetHighlightLocal(idStr, thickness: -1);
                }
            }
            catch { }
        }

        // Legacy overload kept for any external callers (not used internally)
        static void SetBlockHighlight(string name, bool on)
        {
            if (string.IsNullOrEmpty(name)) return;
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
            RefreshTerminalBlocks();

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
            scan.Rows.Sort((a, b) => b.Ratio.CompareTo(a.Ratio));

            // Worst HighlightCount fat blocks (the end of the list - sorted
            // most complete first). Stored as stable EntityId and also
            // ensure empty-Name blocks get a Name so SetHighlightLocal can
            // find them (original behaviour, now at highlight time as well).
            int found = 0;
            for (int i = scan.Rows.Count - 1; i >= 0 && found < HighlightCount; i--)
            {
                var fb = scan.Rows[i].Block.FatBlock;
                if (fb == null) continue;
                // Keep empty-Name blocks findable (original mutated Name here)
                try { if (string.IsNullOrEmpty(fb.Name)) fb.Name = fb.EntityId.ToString(); } catch { }
                scan.HlNames.Add(fb.EntityId);
                found++;
            }

            // Grab projectors on same grid if set as Load Repair Projection
            int projMissing = 0;
            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                var proj = TerminalBlocks[i] as Sandbox.ModAPI.IMyProjector;
                if (proj == null) continue;
                bool consider = false;
                try
                {
                    string cd = proj.CustomData ?? "";
                    string name = proj.CustomName ?? "";
                    if (cd.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0 || cd.IndexOf("Load", StringComparison.OrdinalIgnoreCase)>=0)
                        consider = true;
                    else if (proj.IsProjecting) consider = true;
                }
                catch { consider = false; }
                if (!consider) continue;
                int rem = 0; int tot = 0;
                try { rem = proj.RemainingBlocks; tot = proj.TotalBlocks; } catch {}
                if (rem <= 0) continue;
                projMissing += rem;
                {
                    DamageRow r = scan.RentProjRow();
                    r.Block = null;
                    r.Ratio = 0f;
                    r.Name = Truncate(BlockName(proj),22) + " MISSING";
                    r.Icon = "MyObjectBuilder_Projector/Projector";
                    scan.ProjRows.Add(r);
                }
            }
            scan.ProjMissing = projMissing;
            scan.ProjText = "PROJECTOR MISSING ("+scan.ProjRows.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)+" types, "+projMissing.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)+" blocks)";
            // Overall damage percent including projectors - shared with DockedShips
            int totalBlocks = Math.Max(GridBlocks.Count,1);
            int damagedTotal = scan.Rows.Count + projMissing;
            scan.DamagePercent = (float)damagedTotal / totalBlocks * 100f;
            if (scan.DamagePercent>100f) scan.DamagePercent=100f;
            scan.CountText = "WELDING REQUIRED: " + scan.Rows.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " BLOCK(S)";
            if (projMissing>0) scan.CountText += $" +{projMissing.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} PROJ";
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

        void DrawProjRow(int idx, float y)
        {
            DamageRow row = _scan.ProjRows[idx];
            // Projector missing blocks are 0% complete (needs building)
            DrawProgressRow(_frame, y, row.Icon, row.Name, "MISSING", 0f, new Color(230,180,60), true, null, new Color(230,180,60));
        }
    }
}
