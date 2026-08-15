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

        /// <summary>Highlighted block entity names. Static so any instance can
        /// clear highlights set by an older instance (script instances are
        /// recreated on world reload, but the game keeps the highlights).</summary>
        static readonly HashSet<string> _hlNames = new HashSet<string>();

        /// <summary>Reused "currently wanted" set - cleared per update instead of
        /// allocating a fresh HashSet every frame.</summary>
        static readonly HashSet<string> _wanted = new HashSet<string>();

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

            public void Clear()
            {
                _pool.AddRange(Rows);
                Rows.Clear();
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

        readonly Func<DamageScan> _scanFunc;
        MySpriteDrawFrame _frame;
        DamageScan _scan;
        readonly Action<int, float> _drawRow;

        public DamageApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawRow = DrawRow;
        }

        public override void Dispose()
        {
            ClearHighlights();
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

                AddText(frame, $"WELDING REQUIRED: {scan.Rows.Count} BLOCK(S)", new Vector2(Left, 48f * S), 0.46f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, "MOST COMPLETE FIRST", new Vector2(Right, 48f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y = 74f * S;
                float bottom = Bottom;
                int drawn = DrawListGroup(frame, 0, null, scan.Rows.Count, y, 0f, bottom - y, 24f * S, _drawRow);

                if (!ConfigScroll && scan.Rows.Count > drawn)
                    DrawMore(frame, $"+{scan.Rows.Count - drawn} MORE");
            }
        }

        /// <summary>Keeps the highlight outline on the HighlightCount most
        /// damaged blocks (the end of the list - sorted most complete first).
        /// Highlights are cleared when a block is repaired, drops out of the
        /// worst group, or the option is off. Blocks with an empty entity
        /// name get one registered (EntityId string) - the highlight system
        /// resolves entities by name and silently ignores unknown ones.
        /// Wanted highlights are re-applied on every update because the game
        /// drops the outline when a block's damage model state changes.</summary>
        void UpdateHighlights(DamageScan scan)
        {
            _wanted.Clear();
            if (ConfigHighlightDamaged && scan != null)
            {
                int found = 0;
                for (int i = scan.Rows.Count - 1; i >= 0 && found < HighlightCount; i--)
                {
                    var fb = scan.Rows[i].Block.FatBlock;
                    if (fb == null) continue;
                    if (string.IsNullOrEmpty(fb.Name))
                        fb.Name = fb.EntityId.ToString();
                    _wanted.Add(fb.Name);
                    found++;
                }
            }

            foreach (var name in _hlNames)
            {
                if (!_wanted.Contains(name))
                    SetBlockHighlight(name, false);
            }
            foreach (var name in _wanted)
            {
                SetBlockHighlight(name, true);
            }

            _hlNames.Clear();
            _hlNames.UnionWith(_wanted);
        }

        static void ClearHighlights()
        {
            foreach (var name in _hlNames)
                SetBlockHighlight(name, false);
            _hlNames.Clear();
        }

        static void SetBlockHighlight(string name, bool on)
        {
            if (on)
                MyVisualScriptLogicProvider.SetHighlightLocal(name, thickness: 2, pulseTimeInFrames: 0, color: new Color(220, 60, 50));
            else
                MyVisualScriptLogicProvider.SetHighlightLocal(name, thickness: -1);
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
                    if (row.Name.Length == 0) row.Name = Truncate(slim.BlockDefinition.DisplayNameText, 22);
                }
                else
                {
                    row.Name = Truncate(slim.BlockDefinition.DisplayNameText, 22);
                }
                if (row.Name.Length == 0) row.Name = "BLOCK";
                row.Icon = BlockIcon(slim, "MyObjectBuilder_Component/Construction");
                scan.Rows.Add(row);
            }
            scan.Rows.Sort((a, b) => b.Ratio.CompareTo(a.Ratio));
            return scan;
        }

        void DrawRow(int idx, float y)
        {
            DamageRow row = _scan.Rows[idx];
            DrawProgressRow(_frame, y, row.Icon, row.Name, $"{row.Ratio * 100f:0}%", row.Ratio, BarColor(row.Ratio), true, null, new Color(220, 90, 70));
        }
    }
}