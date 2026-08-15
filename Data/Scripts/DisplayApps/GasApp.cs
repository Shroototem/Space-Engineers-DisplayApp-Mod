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
            public readonly List<GasRow> All = new List<GasRow>();
            readonly List<GasRow> _pool = new List<GasRow>();
            public float H2Stored, H2Max, O2Stored, O2Max, FarmOutput;
            public int FarmsProducing, GensOn, H2Count, O2Count, VentCount;
            public float VentLevel;

            public void Clear()
            {
                _pool.AddRange(Tanks);
                _pool.AddRange(Vents);
                Tanks.Clear();
                Vents.Clear();
                All.Clear();
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

            using (var frame = BeginAppFrame("O2 / H2 STATUS", "LIFE SUPPORT & FUEL MONITOR", "IconOxygen", new Color(90, 160, 255)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                float h2Stored = scan.H2Stored, h2Max = scan.H2Max;
                float o2Stored = scan.O2Stored, o2Max = scan.O2Max;
                float farmOutput = scan.FarmOutput;
                int farmsProducing = scan.FarmsProducing, gensOn = scan.GensOn;
                int h2Count = scan.H2Count, o2Count = scan.O2Count, ventCount = scan.VentCount;
                float ventLevel = scan.VentLevel;

                float avgVent = ventCount > 0 ? ventLevel / ventCount : 0f;

                if (h2Count + o2Count + ventCount + farmsProducing + gensOn == 0)
                {
                    DrawEmpty(frame, "NO GAS BLOCKS ON GRID");
                    return;
                }

                float h2Ratio = h2Max > 0f ? h2Stored / h2Max : 0f;
                AddText(frame, "HYDROGEN STORAGE", new Vector2(Left, 52f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, $"{FormatVolume(h2Stored)} / {FormatVolume(h2Max)} ({h2Ratio * 100f:0}%)", new Vector2(Right, 52f * S), 0.50f * S, new Color(80, 200, 230), TextAlignment.RIGHT);

                RectangleF h2Bar = new RectangleF(new Vector2(Left, 68f * S), new Vector2(Right - Left, 14f * S));
                DrawBar(frame, h2Bar, h2Ratio, new Color(80, 200, 230));

                float o2Ratio = o2Max > 0f ? o2Stored / o2Max : 0f;
                AddText(frame, "OXYGEN STORAGE", new Vector2(Left, 88f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, $"{FormatVolume(o2Stored)} / {FormatVolume(o2Max)} ({o2Ratio * 100f:0}%)", new Vector2(Right, 88f * S), 0.50f * S, new Color(90, 160, 255), TextAlignment.RIGHT);

                RectangleF o2Bar = new RectangleF(new Vector2(Left, 104f * S), new Vector2(Right - Left, 14f * S));
                DrawBar(frame, o2Bar, o2Ratio, new Color(90, 160, 255));

                DrawDivider(frame, 126f);
                AddText(frame, "OXYGEN PRODUCTION", new Vector2(Left, 132f * S), 0.48f * S, new Color(180, 190, 205), TextAlignment.LEFT);

                string genText = gensOn > 0 ? $"GENERATORS: {gensOn} ACTIVE" : "GENERATORS: NONE";
                AddText(frame, $"FARM: {farmOutput:0.0} L/s   |   {genText}", new Vector2(Left, 148f * S), 0.46f * S, farmsProducing > 0 || gensOn > 0 ? new Color(50, 210, 90) : new Color(140, 145, 155), TextAlignment.LEFT);

                string ventText = ventCount > 0 ? $"AVG ROOM O2: {avgVent * 100f:0}%" : "AIR VENTS: NONE";
                AddText(frame, $"AIR VENTS: {ventCount}   |   {ventText}", new Vector2(Left, 164f * S), 0.46f * S, avgVent > 0.5f ? new Color(50, 210, 90) : (avgVent > 0.2f ? new Color(230, 200, 60) : new Color(220, 70, 60)), TextAlignment.LEFT);

                DrawDivider(frame, 184f);
                int totalRows = h2Count + o2Count + ventCount;
                AddText(frame, $"GAS TANKS & VENTS ({totalRows})", new Vector2(Left, 190f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);

                float rowsTop = 210f * S;
                float rowH = 36f * S;
                var tanks = scan.Tanks;
                var vents = scan.Vents;

                if (ConfigScroll)
                {
                    float headerH = 20f * S;
                    float gap = 8f * S;
                    float groupH = ListGroupHeight(Bottom - rowsTop, 2, headerH, gap);

                    DrawListGroup(frame, 0, $"GAS TANKS ({tanks.Count})", tanks.Count, rowsTop, headerH, groupH, rowH, _drawTankRow);

                    DrawDivider(frame, (rowsTop + headerH + groupH + gap / 2f) / S);

                    DrawListGroup(frame, 1, $"AIR VENTS ({vents.Count})", vents.Count,
                        ListGroupTop(rowsTop, 1, groupH, headerH, gap), headerH, groupH, rowH, _drawVentRow);
                }
                else
                {
                    int maxRows = Math.Max(0, (int)((Bottom - rowsTop) / rowH));
                    if (maxRows > 0)
                    {
                        int drawn = 0;
                        int startIndex = ScrollStart(0, scan.All.Count, maxRows);
                        for (int i = startIndex; i < scan.All.Count && drawn < maxRows; i++)
                        {
                            DrawRow(frame, scan.All[i], rowsTop + drawn++ * rowH);
                        }

                        if (scan.All.Count > drawn)
                            DrawMore(frame, $"+{scan.All.Count - drawn} MORE");
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
                if (b is GasTank)
                {
                    GasTank t = (GasTank)b;
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
                    r.BarColor = isH2 ? new Color(80, 200, 230) : new Color(90, 160, 255);
                    scan.Tanks.Add(r);
                }
                else if (b is OxygenFarm)
                {
                    OxygenFarm f = (OxygenFarm)b;
                    if (f.IsWorking)
                    {
                        scan.FarmOutput += (float)f.GetOutput();
                        if (f.CanProduce) scan.FarmsProducing++;
                    }
                }
                else if (b is AirVent)
                {
                    AirVent v = (AirVent)b;
                    scan.VentLevel += (float)v.GetOxygenLevel();
                    scan.VentCount++;

                    GasRow r = scan.RentRow();
                    r.Name = Truncate(BlockName(v), 22);
                    r.Ratio = (float)v.GetOxygenLevel();
                    r.Value = $"O2 LEVEL {r.Ratio * 100f:0}%";
                    r.Icon = "IconOxygen";
                    r.BarColor = new Color(50, 210, 90);
                    scan.Vents.Add(r);
                }
                else if (b is GasGenerator)
                {
                    GasGenerator g = (GasGenerator)b;
                    if (g.IsProducing) scan.GensOn++;
                }
            }
            scan.All.AddRange(scan.Tanks);
            scan.All.AddRange(scan.Vents);
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