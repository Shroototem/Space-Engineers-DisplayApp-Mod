using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using GasTank = Sandbox.ModAPI.IMyGasTank;
using OxygenFarm = SpaceEngineers.Game.ModAPI.IMyOxygenFarm;
using AirVent = SpaceEngineers.Game.ModAPI.IMyAirVent;
using GasGenerator = Sandbox.ModAPI.IMyGasGenerator;

namespace DisplayApps
{
    [MyTextSurfaceScript("O2H2", "Info O2 / H2")]
    public class GasApp : AppBase
    {
        class GasRow
        {
            public string Name;
            public float Ratio;
            public string Value;
            public string Icon;
            public Color BarColor;
        }

        class GasScan : IScanData
        {
            public readonly List<GasRow> Tanks = new List<GasRow>();
            public readonly List<GasRow> Vents = new List<GasRow>();
            readonly List<GasRow> _pool = new List<GasRow>();
            public float H2Stored, H2Max, O2Stored, O2Max, FarmOutput;
            public int FarmsProducing, GensOn, H2Count, O2Count, VentCount;
            public float VentLevel;

            // Summary strings, built once per grid per window in the scan so
            // every display draws them without formatting.
            public string H2Text, O2Text, ProdText, VentText;
            public string TotalHeader, TanksHeader, VentsHeader;

            public void Clear()
            {
                _pool.AddRange(Tanks);
                _pool.AddRange(Vents);
                Tanks.Clear();
                Vents.Clear();
                H2Stored = 0f;
                H2Max = 0f;
                O2Stored = 0f;
                O2Max = 0f;
                FarmOutput = 0f;
                FarmsProducing = 0;
                GensOn = 0;
                H2Count = 0;
                O2Count = 0;
                VentCount = 0;
                VentLevel = 0f;
                H2Text = null;
                O2Text = null;
                ProdText = null;
                VentText = null;
                TotalHeader = null;
                TanksHeader = null;
                VentsHeader = null;
            }

            public GasRow RentRow()
            {
                if (_pool.Count > 0)
                {
                    GasRow row = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return row;
                }
                return new GasRow();
            }
        }

        static readonly Color H2Color = new Color(80, 200, 230);
        static readonly Color O2Color = new Color(90, 160, 255);
        static readonly Color VentColor = new Color(50, 210, 90);

        readonly Func<GasScan> _scanFunc;
        MySpriteDrawFrame _frame;
        GasScan _scan;
        readonly Action<int, float> _drawTankRow;
        readonly Action<int, float> _drawVentRow;

        public GasApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawTankRow = DrawTankRow;
            _drawVentRow = DrawVentRow;
        }

        protected override void RunApp()
        {
            GasScan scan = GetGridScan(_scanFunc);
            _scan = scan;

            using (var frame = BeginAppFrame("O2 / H2 STATUS", "LIFE SUPPORT & FUEL MONITOR", "IconOxygen", O2Color))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                int farmsProducing = scan.FarmsProducing, gensOn = scan.GensOn;
                int h2Count = scan.H2Count, o2Count = scan.O2Count, ventCount = scan.VentCount;
                float avgVent = ventCount > 0 ? scan.VentLevel / ventCount : 0f;

                if (h2Count + o2Count + ventCount + farmsProducing + gensOn == 0)
                {
                    DrawEmpty(frame, "NO GAS BLOCKS ON GRID");
                    return;
                }

                float h2Ratio = scan.H2Max > 0f ? scan.H2Stored / scan.H2Max : 0f;
                AddText(frame, "HYDROGEN STORAGE", new Vector2(Left, 52f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, scan.H2Text, new Vector2(Right, 52f * S), 0.50f * S, H2Color, TextAlignment.RIGHT);

                RectangleF h2Bar = new RectangleF(new Vector2(Left, 68f * S), new Vector2(Right - Left, 14f * S));
                DrawBar(frame, h2Bar, h2Ratio, H2Color);

                float o2Ratio = scan.O2Max > 0f ? scan.O2Stored / scan.O2Max : 0f;
                AddText(frame, "OXYGEN STORAGE", new Vector2(Left, 88f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, scan.O2Text, new Vector2(Right, 88f * S), 0.50f * S, O2Color, TextAlignment.RIGHT);

                RectangleF o2Bar = new RectangleF(new Vector2(Left, 104f * S), new Vector2(Right - Left, 14f * S));
                DrawBar(frame, o2Bar, o2Ratio, O2Color);

                DrawDivider(frame, 126f);
                AddText(frame, "OXYGEN PRODUCTION", new Vector2(Left, 132f * S), 0.48f * S, new Color(180, 190, 205), TextAlignment.LEFT);

                AddText(frame, scan.ProdText, new Vector2(Left, 148f * S), 0.46f * S, farmsProducing > 0 || gensOn > 0 ? new Color(50, 210, 90) : new Color(140, 145, 155), TextAlignment.LEFT);

                AddText(frame, scan.VentText, new Vector2(Left, 164f * S), 0.46f * S, avgVent > 0.5f ? new Color(50, 210, 90) : (avgVent > 0.2f ? new Color(230, 200, 60) : new Color(220, 70, 60)), TextAlignment.LEFT);

                DrawDivider(frame, 184f);
                AddText(frame, scan.TotalHeader, new Vector2(Left, 190f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);

                float rowsTop = 210f * S;
                float rowH = 36f * S;
                var tanks = scan.Tanks;
                var vents = scan.Vents;

                if (ConfigScroll)
                {
                    float headerH = 20f * S;
                    float gap = 8f * S;
                    float groupH = ListGroupHeight(Bottom - rowsTop, 2, headerH, gap);

                    DrawListGroup(frame, 0, scan.TanksHeader, tanks.Count, rowsTop, headerH, groupH, rowH, _drawTankRow);

                    DrawDivider(frame, (rowsTop + headerH + groupH + gap / 2f) / S);

                    DrawListGroup(frame, 1, scan.VentsHeader, vents.Count,
                        ListGroupTop(rowsTop, 1, groupH, headerH, gap), headerH, groupH, rowH, _drawVentRow);
                }
                else
                {
                    int maxRows = Math.Max(0, (int)((Bottom - rowsTop) / rowH));
                    if (maxRows > 0)
                    {
                        int totalRows = tanks.Count + vents.Count;
                        int drawn = 0;
                        int startIndex = ScrollStart(0, totalRows, maxRows);
                        for (int i = startIndex; i < totalRows && drawn < maxRows; i++)
                        {
                            GasRow r = i < tanks.Count ? tanks[i] : vents[i - tanks.Count];
                            DrawRow(frame, r, rowsTop + drawn++ * rowH);
                        }

                        if (totalRows > drawn)
                            DrawMore(frame, $"+{totalRows - drawn} MORE");
                    }
                }
            }
        }

        GasScan ScanGrid()
        {
            RefreshTerminalBlocks();

            GasScan scan = RentScan<GasScan>();
            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                var b = TerminalBlocks[i];
                GasTank t = b as GasTank;
                if (t != null)
                {
                    float cap = (float)t.Capacity;
                    float fill = (float)t.FilledRatio;
                    bool isH2 = IsHydrogenTank(t);
                    if (isH2)
                    {
                        scan.H2Stored += cap * fill;
                        scan.H2Max += cap;
                        scan.H2Count++;
                    }
                    else
                    {
                        scan.O2Stored += cap * fill;
                        scan.O2Max += cap;
                        scan.O2Count++;
                    }

                    GasRow r = scan.RentRow();
                    r.Name = Truncate(BlockName(t), 22);
                    r.Ratio = fill;
                    r.Value = $"{FormatVolume(cap * fill)} ({fill * 100f:0}%)";
                    r.Icon = isH2 ? "IconHydrogen" : "IconOxygen";
                    r.BarColor = isH2 ? H2Color : O2Color;
                    scan.Tanks.Add(r);
                    continue;
                }

                OxygenFarm f = b as OxygenFarm;
                if (f != null)
                {
                    if (f.IsWorking)
                    {
                        scan.FarmOutput += (float)f.GetOutput();
                        if (f.CanProduce) scan.FarmsProducing++;
                    }
                    continue;
                }

                AirVent v = b as AirVent;
                if (v != null)
                {
                    float lvl = (float)v.GetOxygenLevel();
                    scan.VentLevel += lvl;
                    scan.VentCount++;

                    GasRow r = scan.RentRow();
                    r.Name = Truncate(BlockName(v), 22);
                    r.Ratio = lvl;
                    r.Value = $"O2 LEVEL {lvl * 100f:0}%";
                    r.Icon = "IconOxygen";
                    r.BarColor = VentColor;
                    scan.Vents.Add(r);
                    continue;
                }

                GasGenerator g = b as GasGenerator;
                if (g != null && g.IsProducing) scan.GensOn++;
            }

            // Summary strings - pure functions of the totals above.
            float h2Ratio2 = scan.H2Max > 0f ? scan.H2Stored / scan.H2Max : 0f;
            float o2Ratio2 = scan.O2Max > 0f ? scan.O2Stored / scan.O2Max : 0f;
            scan.H2Text = $"{FormatVolume(scan.H2Stored)} / {FormatVolume(scan.H2Max)} ({h2Ratio2 * 100f:0}%)";
            scan.O2Text = $"{FormatVolume(scan.O2Stored)} / {FormatVolume(scan.O2Max)} ({o2Ratio2 * 100f:0}%)";
            string genText = scan.GensOn > 0 ? $"GENERATORS: {scan.GensOn} ACTIVE" : "GENERATORS: NONE";
            scan.ProdText = $"FARM: {scan.FarmOutput:0.0} L/s   |   {genText}";
            float avgVent2 = scan.VentCount > 0 ? scan.VentLevel / scan.VentCount : 0f;
            string ventText = scan.VentCount > 0 ? $"AVG ROOM O2: {avgVent2 * 100f:0}%" : "AIR VENTS: NONE";
            scan.VentText = $"AIR VENTS: {scan.VentCount}   |   {ventText}";
            scan.TotalHeader = "GAS TANKS & VENTS (" + (scan.H2Count + scan.O2Count + scan.VentCount) + ")";
            scan.TanksHeader = "GAS TANKS (" + scan.Tanks.Count + ")";
            scan.VentsHeader = "AIR VENTS (" + scan.Vents.Count + ")";
            return scan;
        }

        void DrawRow(MySpriteDrawFrame frame, GasRow r, float rowTop)
        {
            DrawProgressRow(frame, rowTop, r.Icon, r.Name, r.Value, r.Ratio, r.BarColor);
        }

        void DrawTankRow(int idx, float y)
        {
            DrawRow(_frame, _scan.Tanks[idx], y);
        }

        void DrawVentRow(int idx, float y)
        {
            DrawRow(_frame, _scan.Vents[idx], y);
        }
    }
}
