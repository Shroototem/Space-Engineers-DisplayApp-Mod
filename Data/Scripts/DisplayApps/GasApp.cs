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
            public int FarmsTotal, FarmsProducing, GensTotal, GensOn, H2Count, O2Count, VentCount;
            public float VentLevel;
            public float NetFlowH2, NetFlowO2; // L/s per gas
            public float MaxFlowH2 = 100f, MaxFlowO2 = 100f;

            // Summary strings, built once per grid per window in the scan so
            // every display draws them without formatting.
            public string H2Text, O2Text, ProdText, VentText, FlowText, FlowTextH2, FlowTextO2;
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
                FarmsTotal = 0;
                GensOn = 0;
                GensTotal = 0;
                H2Count = 0;
                O2Count = 0;
                VentCount = 0;
                VentLevel = 0f;
                NetFlowH2 = 0f;
                NetFlowO2 = 0f;
                H2Text = null;
                O2Text = null;
                ProdText = null;
                VentText = null;
                FlowText = null;
                FlowTextH2 = null;
                FlowTextO2 = null;
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

        // Per-grid previous window stored volumes for delta-based net flow (like Power In-Out)
        static readonly Dictionary<long, float> _prevH2 = new Dictionary<long, float>();
        static readonly Dictionary<long, float> _prevO2 = new Dictionary<long, float>();
        static readonly Dictionary<long, long> _prevWin = new Dictionary<long, long>();

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

                bool showH2 = GetSectionBool("ShowH2Storage", true);
                bool showO2 = GetSectionBool("ShowO2Storage", true);
                bool showBars = GetSectionBool("ShowBars", false); // false to remove current bars as requested
                bool showProd = GetSectionBool("ShowProduction", true);
                bool showFlow = GetSectionBool("ShowFlow", true);
                bool showLists = GetSectionBool("ShowTanksVents", true);

                float y = 52f * S;
                if (showH2)
                {
                    AddText(frame, "HYDROGEN STORAGE", new Vector2(Left, y), 0.50f * S, FgColor, TextAlignment.LEFT);
                    AddText(frame, scan.H2Text, new Vector2(Right, y), 0.50f * S, H2Color, TextAlignment.RIGHT);
                    y += 16f * S;
                    // Combined bar: left net +/- , right MAX IN/OUT time (like Power Max 29h)
                    AddText(frame, scan.FlowTextH2, new Vector2(Left, y), 0.42f * S, scan.NetFlowH2>0.01f?new Color(50,210,90):(scan.NetFlowH2<-0.01f?new Color(230,60,50):new Color(140,145,155)), TextAlignment.LEFT);
                    string inH2 = scan.MaxFlowH2>0.01f ? FormatTimeHours((scan.H2Max - scan.H2Stored)/scan.MaxFlowH2/3600f) : "--";
                    string outH2 = scan.MaxFlowH2>0.01f ? FormatTimeHours(scan.H2Stored/scan.MaxFlowH2/3600f) : "--";
                    // Color-coded: MAX white, IN green, OUT red - exact measured widths
                    DrawMaxInOut(frame, y, inH2, outH2);
                    y += 24f * S;
                    float h2Ratio = scan.H2Max > 0f ? scan.H2Stored / scan.H2Max : 0f;
                    RectangleF h2Bar = new RectangleF(new Vector2(Left, y), new Vector2(Right - Left, 14f * S));
                    DrawCombinedBar(frame, h2Bar, h2Ratio, BarColor(h2Ratio), scan.NetFlowH2, scan.MaxFlowH2);
                    y += 20f * S;
                }
                if (showO2)
                {
                    AddText(frame, "OXYGEN STORAGE", new Vector2(Left, y), 0.50f * S, FgColor, TextAlignment.LEFT);
                    AddText(frame, scan.O2Text, new Vector2(Right, y), 0.50f * S, O2Color, TextAlignment.RIGHT);
                    y += 16f * S;
                    AddText(frame, scan.FlowTextO2, new Vector2(Left, y), 0.42f * S, scan.NetFlowO2>0.01f?new Color(50,210,90):(scan.NetFlowO2<-0.01f?new Color(230,60,50):new Color(140,145,155)), TextAlignment.LEFT);
                    string inO2 = scan.MaxFlowO2>0.01f ? FormatTimeHours((scan.O2Max - scan.O2Stored)/scan.MaxFlowO2/3600f) : "--";
                    string outO2 = scan.MaxFlowO2>0.01f ? FormatTimeHours(scan.O2Stored/scan.MaxFlowO2/3600f) : "--";
                    DrawMaxInOut(frame, y, inO2, outO2);
                    y += 24f * S;
                    float o2Ratio = scan.O2Max > 0f ? scan.O2Stored / scan.O2Max : 0f;
                    RectangleF o2Bar = new RectangleF(new Vector2(Left, y), new Vector2(Right - Left, 14f * S));
                    DrawCombinedBar(frame, o2Bar, o2Ratio, BarColor(o2Ratio), scan.NetFlowO2, scan.MaxFlowO2);
                    y += 20f * S;
                }

                // All bars now combined (percentage + net flow) - no separate NET GAS FLOW bar
                // Keep ShowFlow toggle for backwards compat but no extra bar when per-gas combined already shown

                DrawDivider(frame, y / S);
                y += 6f * S;
                if (showProd)
                {
                    AddText(frame, "OXYGEN PRODUCTION", new Vector2(Left, y), 0.48f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                    y += 16f * S;
                    AddText(frame, scan.ProdText, new Vector2(Left, y), 0.46f * S, farmsProducing > 0 || gensOn > 0 ? new Color(50, 210, 90) : new Color(140, 145, 155), TextAlignment.LEFT);
                    y += 16f * S;
                    AddText(frame, scan.VentText, new Vector2(Left, y), 0.46f * S, avgVent > 0.5f ? new Color(50, 210, 90) : (avgVent > 0.2f ? new Color(230, 200, 60) : new Color(220, 70, 60)), TextAlignment.LEFT);
                    y += 20f * S;
                    DrawDivider(frame, y / S);
                    y += 6f * S;
                }

                if (!showLists)
                    return;
                AddText(frame, scan.TotalHeader, new Vector2(Left, y), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                y += 20f * S;

                float rowsTop = y;
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

        bool GetSectionBool(string key, bool fallback)
        {
            var tb = Block as Sandbox.ModAPI.IMyTerminalBlock;
            if (tb!=null)
            {
                string v = AppBase.ReadConfigValue(tb, AppRegionName, key);
                if (v!=null) { bool b; if(bool.TryParse(v,out b)) return b; }
                v = AppBase.ReadConfigValue(tb, "DEFAULT", key);
                if (v!=null) { bool b; if(bool.TryParse(v,out b)) return b; }
            }
            return fallback;
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
                    scan.FarmsTotal++;
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
                if (g != null)
                {
                    scan.GensTotal++;
                    if (g.IsProducing) scan.GensOn++;
                }
            }

            // Summary strings - pure functions of the totals above.
            float h2Ratio2 = scan.H2Max > 0f ? scan.H2Stored / scan.H2Max : 0f;
            float o2Ratio2 = scan.O2Max > 0f ? scan.O2Stored / scan.O2Max : 0f;
            scan.H2Text = $"{FormatVolume(scan.H2Stored)} / {FormatVolume(scan.H2Max)} ({h2Ratio2 * 100f:0}%)";
            scan.O2Text = $"{FormatVolume(scan.O2Stored)} / {FormatVolume(scan.O2Max)} ({o2Ratio2 * 100f:0}%)";
            string farmText;
            if (scan.FarmsTotal == 0) farmText = "FARMS: NONE";
            else farmText = $"FARMS: {scan.FarmsProducing}/{scan.FarmsTotal} ACTIVE";
            string genText;
            if (scan.GensTotal == 0) genText = "GENS: NONE";
            else genText = $"{scan.GensOn}/{scan.GensTotal} GENS ACTIVE";
            scan.ProdText = $"{farmText}  |  {genText}";
            // Per-gas net flows like InfoPower: delta of stored volume per second
            long gid = 0;
            try { var g = CurrentScanGrid; if(g!=null) gid = g.EntityId; } catch {}
            long win = 0;
            try { win = Window(); } catch {}
            float flowH2 = 0f, flowO2 = 0f;
            if (gid != 0 && win != 0)
            {
                long pw = 0;
                float prevH2 = 0f, prevO2 = 0f;
                bool hasPrev = false;
                if (_prevWin.TryGetValue(gid, out pw) && pw == win - 1)
                {
                    float ph = 0f, po = 0f;
                    if (_prevH2.TryGetValue(gid, out ph) && _prevO2.TryGetValue(gid, out po))
                    {
                        prevH2 = ph; prevO2 = po; hasPrev = true;
                    }
                }
                if (hasPrev)
                {
                    float dt = 1.6666667f;
                    flowH2 = (scan.H2Stored - prevH2) / dt;
                    flowO2 = (scan.O2Stored - prevO2) / dt;
                    if (Math.Abs(flowH2) < 0.05f) flowH2 = 0f;
                    if (Math.Abs(flowO2) < 0.05f) flowO2 = 0f;
                }
                else
                {
                    flowO2 = scan.FarmOutput;
                    flowH2 = 0f;
                }
                _prevH2[gid] = scan.H2Stored;
                _prevO2[gid] = scan.O2Stored;
                _prevWin[gid] = win;
            }
            else
            {
                flowO2 = scan.FarmOutput;
                flowH2 = 0f;
            }
            scan.NetFlowH2 = flowH2;
            scan.NetFlowO2 = flowO2;
            // MAX IN/OUT should be total Input and Total Output (static), not dynamic scaling - like Power's Max time
            // Use total possible flow based on storage capacity and farm/gens count so bar scale and MAX text stay stable
            // For now fixed large static max so MAX on right does not change with net
            scan.MaxFlowH2 = 100000f; // total H2 input capacity (static)
            scan.MaxFlowO2 = 100000f; // total O2 input capacity (static) - matches +89100 L/s example with ~89% bar
            // Alternative could be scan.H2Max/10 etc, but keep static for stable MAX display
            if (scan.NetFlowO2 > 0.01f) scan.FlowTextO2 = $"+{scan.NetFlowO2:0.0} L/s";
            else if (scan.NetFlowO2 < -0.01f) scan.FlowTextO2 = $"{scan.NetFlowO2:0.0} L/s";
            else scan.FlowTextO2 = "0.0 L/s (IDLE)";
            if (scan.NetFlowH2 > 0.01f) scan.FlowTextH2 = $"+{scan.NetFlowH2:0.0} L/s";
            else if (scan.NetFlowH2 < -0.01f) scan.FlowTextH2 = $"{scan.NetFlowH2:0.0} L/s";
            else scan.FlowTextH2 = "0.0 L/s (IDLE)";
            // Legacy combined for ShowBars=true compatibility
            scan.FlowText = scan.FlowTextO2;
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
