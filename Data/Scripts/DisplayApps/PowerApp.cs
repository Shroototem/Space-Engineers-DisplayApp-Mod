using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using Battery = Sandbox.ModAPI.IMyBatteryBlock;
using Solar = SpaceEngineers.Game.ModAPI.IMySolarPanel;
using Wind = Sandbox.ModAPI.IMyWindTurbine;
using Reactor = Sandbox.ModAPI.IMyReactor;
using Engine = Sandbox.ModAPI.IMyPowerProducer;

namespace DisplayApps
{
    [MyTextSurfaceScript("PowerInfo", "Info Power")]
    public class PowerApp : AppBase
    {
        /// <summary>One battery's scan-side state. All ModAPI property reads and
        /// row strings are resolved once per grid per window in the scan
        /// (shared by every display), so the draw side makes no block API
        /// calls and only builds strings for visible rows.</summary>
        class BatteryRow
        {
            public string Name;
            public float Stored, Max, In, Out;
            public bool Charging;
            public float Ratio;
            public string State;
            public Color StateColor;
            public string Details;
        }

        class PowerDrawRow
        {
            public string Name;
            public string Icon;
            public float PowerMW;
            public float Ratio;
            public string Value;
        }

        class PowerScan : IScanData
        {
            public readonly List<BatteryRow> Batteries = new List<BatteryRow>();
            readonly List<BatteryRow> _rowPool = new List<BatteryRow>();
            public readonly List<PowerDrawRow> TopDraws = new List<PowerDrawRow>();
            public readonly List<PowerDrawRow> _drawPool = new List<PowerDrawRow>();
            public float SolarCur, SolarMax, WindCur, WindMax, ReactCur, ReactMax, EngCur, EngMax;
            public BatterySummary Bat;
            public int SolarCount, WindCount, ReactCount, EngCount, BatCount;

            // Grid-wide strings, built once per grid per window in the scan.
            // Per-battery row strings are built in the scan too - the scan is
            // shared per grid per window, so precomputing them is cheaper than
            // per-display reads and formatting.
            public string StorageText, FlowText, FlowShortText, BatHeader, DrawHeader;
            public readonly string[] CatTexts = new string[4];
            public readonly float[] CatRatios = new float[4];

            public void Clear()
            {
                _rowPool.AddRange(Batteries);
                _drawPool.AddRange(TopDraws);
                TopDraws.Clear();
                Batteries.Clear();
                SolarCur = 0f;
                SolarMax = 0f;
                WindCur = 0f;
                WindMax = 0f;
                ReactCur = 0f;
                ReactMax = 0f;
                EngCur = 0f;
                EngMax = 0f;
                Bat = default(BatterySummary);
                SolarCount = 0;
                WindCount = 0;
                ReactCount = 0;
                EngCount = 0;
                BatCount = 0;
                StorageText = null;
                FlowText = null;
                FlowShortText = null;
                BatHeader = null;
                DrawHeader = null;
                for (int i = 0; i < CatTexts.Length; i++)
                {
                    CatTexts[i] = null;
                    CatRatios[i] = 0f;
                }
            }

            public BatteryRow RentBatteryRow()
            {
                if (_rowPool.Count > 0)
                {
                    BatteryRow row = _rowPool[_rowPool.Count - 1];
                    _rowPool.RemoveAt(_rowPool.Count - 1);
                    return row;
                }
                return new BatteryRow();
            }

            public PowerDrawRow RentDrawRow()
            {
                if (_drawPool.Count > 0)
                {
                    var r = _drawPool[_drawPool.Count-1];
                    _drawPool.RemoveAt(_drawPool.Count-1);
                    return r;
                }
                return new PowerDrawRow();
            }
        }

        readonly Func<PowerScan> _scanFunc;
        MySpriteDrawFrame _frame;
        PowerScan _scan;
        readonly Action<int, float> _drawBatteryRow;
        readonly Action<int, float> _drawTopRow;

        public PowerApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawBatteryRow = DrawBatteryRow;
            _drawTopRow = DrawTopRow;
        }

        protected override void RunApp()
        {
            PowerScan scan = GetGridScan(_scanFunc);
            _scan = scan;

            using (var frame = BeginAppFrame("POWER STATUS", "GRID POWER & ENERGY MONITOR", "IconEnergy", new Color(80, 200, 230)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                float batStored = scan.Bat.Stored, batMaxStored = scan.Bat.Max;
                float batMaxOut = scan.Bat.MaxOut;
                int solarCount = scan.SolarCount, windCount = scan.WindCount;
                int reactCount = scan.ReactCount, engCount = scan.EngCount, batCount = scan.BatCount;

                float netBat = scan.Bat.NetFlow;

                if (solarCount + windCount + reactCount + engCount + batCount == 0)
                {
                    DrawEmpty(frame, "NO POWER BLOCKS ON GRID");
                    return;
                }

                bool showStorage = GetSectionVisible("ShowStorage", true);
                bool showFlow = GetSectionVisible("ShowFlow", true);
                bool showBreakdown = GetSectionVisible("ShowBreakdown", true);
                bool showBatteries = GetSectionVisible("ShowBatteries", true);
                bool showTopDraws = GetSectionVisible("ShowTopDraws", true);

                float y = 52f * S;
                if (showStorage)
                {
                    float batRatio = batMaxStored > 0f ? batStored / batMaxStored : 0f;
                    AddText(frame, "BATTERY STORAGE", new Vector2(Left, y), 0.50f * S, FgColor, TextAlignment.LEFT);
                    AddText(frame, scan.StorageText, new Vector2(Right, y), 0.50f * S, new Color(200, 205, 215), TextAlignment.RIGHT);
                    y += 16f * S;
                    if (showFlow)
                    {
                        AddText(frame, scan.FlowShortText, new Vector2(Left, y), 0.42f * S,
                            netBat > 0.001f ? new Color(50, 210, 90) : (netBat < -0.001f ? new Color(230, 60, 50) : new Color(130, 135, 145)),
                            TextAlignment.LEFT);
                        // MAX white / IN green / OUT red - static times at max output rate
                        string inT = batMaxOut > 0.001f ? FormatTimeHours((batMaxStored - batStored) / batMaxOut) : "--";
                        string outT = batMaxOut > 0.001f ? FormatTimeHours(batStored / batMaxOut) : "--";
                        DrawMaxInOut(frame, y, inT, outT);
                        y += 18f * S;
                    }
                    else y += 2f * S;
                    RectangleF batBar = new RectangleF(new Vector2(Left, y), new Vector2(Right - Left, 14f * S));
                    float netForBar = showFlow ? netBat : 0f;
                    float maxForBar = showFlow ? (batMaxOut > 0f ? batMaxOut : 10f) : 100f;
                    // Reuse CombinedBar class for percentage (R->Y->G) + net on top, white same height as net
                    DrawCombinedBar(frame, batBar, batRatio, BarColor(batRatio), netForBar, maxForBar);
                    y += 20f * S;
                }
                else if (showFlow)
                {
                    AddText(frame, "NET POWER FLOW", new Vector2(Left, y), 0.48f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                    AddText(frame, scan.FlowText, new Vector2(Right, y), 0.48f * S, netBat > 0.001f ? new Color(50, 210, 90) : (netBat < -0.001f ? new Color(230, 60, 50) : new Color(160, 170, 185)), TextAlignment.RIGHT);
                    y += 16f * S;
                    RectangleF flowBar = new RectangleF(new Vector2(Left, y), new Vector2(Right - Left, 14f * S));
                    DrawCenterFlowBar(frame, flowBar, netBat, batMaxOut > 0f ? batMaxOut : 10f);
                    y += 20f * S;
                }

                if (showBreakdown)
                {
                    DrawDivider(frame, y / S);
                    y += 6f * S;
                    AddText(frame, "POWER SOURCE BREAKDOWN", new Vector2(Left, y), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                    y += 18f * S;

                    float catH = 26f * S;
                    int catIdx = 0;
                    if (solarCount > 0 || ConfigFullList)
                    {
                        DrawCategoryRow(frame, "SOLAR PANELS", "MyObjectBuilder_Component/SolarCell", solarCount, scan.CatTexts[0], scan.CatRatios[0], CatColor(solarCount, scan.SolarCur), y + catIdx * catH);
                        catIdx++;
                    }
                    if (windCount > 0 || ConfigFullList)
                    {
                        DrawCategoryRow(frame, "WIND TURBINES", "MyObjectBuilder_Component/Motor", windCount, scan.CatTexts[1], scan.CatRatios[1], CatColor(windCount, scan.WindCur), y + catIdx * catH);
                        catIdx++;
                    }
                    if (reactCount > 0 || ConfigFullList)
                    {
                        DrawCategoryRow(frame, "NUCLEAR REACTORS", "MyObjectBuilder_Component/Reactor", reactCount, scan.CatTexts[2], scan.CatRatios[2], CatColor(reactCount, scan.ReactCur), y + catIdx * catH);
                        catIdx++;
                    }
                    if (engCount > 0 || ConfigFullList)
                    {
                        DrawCategoryRow(frame, "HYDRO ENGINES", "IconHydrogen", engCount, scan.CatTexts[3], scan.CatRatios[3], CatColor(engCount, scan.EngCur), y + catIdx * catH);
                        catIdx++;
                    }
                    y += catIdx * catH + 6f * S;
                }
                else
                {
                    y += 6f * S;
                }

                float listY = y;
                bool hasBatteries = showBatteries && batCount > 0;
                bool hasTop = showTopDraws && scan.TopDraws.Count > 0;
                int groups = (hasBatteries?1:0)+(hasTop?1:0);
                if (groups == 0) return;

                float availH = Bottom - listY;
                if (availH < 40f * S) return;

                if (groups == 2 && availH > 80f * S)
                {
                    float headerH = 18f * S;
                    float gap = 6f * S;
                    float gh = ListGroupHeight(availH, 2, headerH, gap);
                    float top = listY;
                    if (hasBatteries)
                    {
                        DrawListGroup(frame, 0, scan.BatHeader, scan.Batteries.Count, top, headerH, gh, 40f*S, _drawBatteryRow);
                        top = ListGroupTop(listY, 1, gh, headerH, gap);
                        if (hasTop)
                        {
                            DrawDivider(frame, (listY+headerH+gh+gap/2f)/S);
                            DrawListGroup(frame, 1, scan.DrawHeader, scan.TopDraws.Count, top, headerH, gh, 24f*S, _drawTopRow);
                        }
                    }
                    else if (hasTop)
                    {
                        DrawListGroup(frame, 0, scan.DrawHeader, scan.TopDraws.Count, top, headerH, gh, 24f*S, _drawTopRow);
                    }
                }
                else if (hasBatteries)
                {
                    DrawDivider(frame, (listY) / S);
                    float rowTopStart = listY + 24f * S;
                    int rows = DrawListGroup(frame, 0, scan.BatHeader, scan.Batteries.Count,
                        listY + 6f * S, 18f * S, Bottom - rowTopStart, 40f * S, _drawBatteryRow);
                    if (!ConfigScroll && batCount > rows)
                        DrawMore(frame, $"+{(batCount - rows).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} MORE BATTERY(IES)");
                }
                else if (hasTop)
                {
                    DrawDivider(frame, (listY) / S);
                    float rowTopStart = listY + 24f * S;
                    int rows = DrawListGroup(frame, 0, scan.DrawHeader, scan.TopDraws.Count,
                        listY + 6f * S, 18f * S, Bottom - rowTopStart, 24f*S, _drawTopRow);
                    if (!ConfigScroll && scan.TopDraws.Count > rows)
                        DrawMore(frame, $"+{(scan.TopDraws.Count - rows).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} MORE");
                }
            }
        }

        PowerScan ScanGrid()
        {
            RefreshTerminalBlocks();

            PowerScan scan = RentScan<PowerScan>();
            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                var b = TerminalBlocks[i];
                Battery bat = b as Battery;
                if (bat != null)
                {
                    AccumulateBattery(ref scan.Bat, bat);
                    scan.BatCount++;

                    // Interface property reads are virtual sync-var lookups -
                    // each is read exactly once per grid per window here,
                    // shared by every display showing this grid.
                    BatteryRow row = scan.RentBatteryRow();
                    row.Name = Truncate(BlockName(bat), 20);
                    row.Stored = (float)bat.CurrentStoredPower;
                    row.Max = (float)bat.MaxStoredPower;
                    row.In = (float)bat.CurrentInput;
                    row.Out = (float)bat.CurrentOutput;
                    row.Charging = bat.IsCharging;
                    row.Ratio = row.Max > 0f ? row.Stored / row.Max : 0f;

                    string state;
                    Color stateColor;
                    if (row.Charging)
                    {
                        state = $"CHARGING (+{row.In.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW)";
                        stateColor = new Color(50, 210, 90);
                    }
                    else if (bat.ChargeMode == Sandbox.ModAPI.Ingame.ChargeMode.Recharge)
                    {
                        state = $"RECHARGE (+{row.In.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW)";
                        stateColor = new Color(80, 200, 230);
                    }
                    else if (row.Out > 0.005f)
                    {
                        state = $"DISCHARGING (-{row.Out.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW)";
                        stateColor = new Color(230, 60, 50);
                    }
                    else
                    {
                        state = "STANDBY";
                        stateColor = new Color(140, 145, 155);
                    }
                    row.State = state;
                    row.StateColor = stateColor;
                    row.Details = $"{row.Stored.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} / {row.Max.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MWh ({(row.Ratio * 100f).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}%)";
                    scan.Batteries.Add(row);
                    continue;
                }
                Solar s = b as Solar;
                if (s != null)
                {
                    scan.SolarCur += (float)s.CurrentOutput;
                    scan.SolarMax += (float)s.MaxOutput;
                    scan.SolarCount++;
                    continue;
                }
                Wind w = b as Wind;
                if (w != null)
                {
                    scan.WindCur += (float)w.CurrentOutput;
                    scan.WindMax += (float)w.MaxOutput;
                    scan.WindCount++;
                    continue;
                }
                Reactor r = b as Reactor;
                if (r != null)
                {
                    scan.ReactCur += (float)r.CurrentOutput;
                    scan.ReactMax += (float)r.MaxOutput;
                    scan.ReactCount++;
                    continue;
                }
                Engine e = b as Engine;
                if (e != null)
                {
                    scan.EngCur += (float)e.CurrentOutput;
                    scan.EngMax += (float)e.MaxOutput;
                    scan.EngCount++;
                    continue;
                }
                // Top power draws: estimate consumption for functional blocks (no reflection, whitelist safe)
                try
                {
                    var func = b as Sandbox.ModAPI.IMyFunctionalBlock;
                    if (func != null && func.Enabled)
                    {
                        float draw = 0f;
                        string tName = "";
                        string sub = "";
                        try { tName = b.BlockDefinition.TypeIdString ?? ""; } catch{}
                        try { sub = b.BlockDefinition.SubtypeId ?? ""; } catch{}
                        if (tName.Contains("Refinery") || sub.Contains("Refinery")) draw = 0.56f;
                        else if (tName.Contains("Assembler")) draw = 0.56f;
                        else if (tName.Contains("Laser") || sub.Contains("Laser")) draw = 2f;
                        else if (tName.Contains("Thruster") && func.IsWorking) draw = 1f;
                        else if (func.IsWorking) draw = 0.1f;
                        else draw = 0f;
                        if (draw>0.001f)
                        {
                            var row = scan.RentDrawRow();
                            row.Name = Truncate(BlockName(b), 18);
                            row.Icon = "MyObjectBuilder_Component/PowerCell";
                            row.PowerMW = draw;
                            row.Value = draw.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + " MW";
                            scan.TopDraws.Add(row);
                        }
                    }
                }
                catch{}
            }

            // Grid-wide strings - pure functions of the totals above.
            float batStored = scan.Bat.Stored, batMaxStored = scan.Bat.Max;
            float batMaxOut = scan.Bat.MaxOut;
            float netBat = scan.Bat.NetFlow;
            float batRatio = batMaxStored > 0f ? batStored / batMaxStored : 0f;
            scan.StorageText = $"{batStored.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} / {batMaxStored.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MWh ({(batRatio * 100f).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}%)";

            string flowLabel = "0.00 MW (IDLE)";
            if (netBat > 0.001f)
                flowLabel = $"+{netBat.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW IN ({FormatTimeHours((batMaxStored - batStored) / netBat)} TO FULL)";
            else if (netBat < -0.001f)
                flowLabel = $"{netBat.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW OUT ({FormatTimeHours(batStored / -netBat)} TO EMPTY)";
            scan.FlowText = flowLabel;

            if (netBat > 0.001f) scan.FlowShortText = $"+{netBat.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW";
            else if (netBat < -0.001f) scan.FlowShortText = $"{netBat.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW";
            else scan.FlowShortText = "0.00 MW (IDLE)";

            scan.CatTexts[0] = CatValue(scan.SolarCount, scan.SolarCur, scan.SolarMax);
            scan.CatRatios[0] = CatRatio(scan.SolarCur, scan.SolarMax);
            scan.CatTexts[1] = CatValue(scan.WindCount, scan.WindCur, scan.WindMax);
            scan.CatRatios[1] = CatRatio(scan.WindCur, scan.WindMax);
            scan.CatTexts[2] = CatValue(scan.ReactCount, scan.ReactCur, scan.ReactMax);
            scan.CatRatios[2] = CatRatio(scan.ReactCur, scan.ReactMax);
            scan.CatTexts[3] = CatValue(scan.EngCount, scan.EngCur, scan.EngMax);
            scan.CatRatios[3] = CatRatio(scan.EngCur, scan.EngMax);
            scan.BatHeader = "INDIVIDUAL BATTERIES (" + scan.BatCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + ")";
            // Top draws: sort by power descending, keep top 8
            if (scan.TopDraws.Count > 0)
            {
                scan.TopDraws.Sort((a,b)=> b.PowerMW.CompareTo(a.PowerMW));
                if (scan.TopDraws.Count > 8)
                {
                    // return excess to pool
                    for(int i=scan.TopDraws.Count-1;i>=8;i--) { scan._drawPool.Add(scan.TopDraws[i]); }
                    scan.TopDraws.RemoveRange(8, scan.TopDraws.Count-8);
                }
                float maxDraw = scan.TopDraws[0].PowerMW;
                if (maxDraw < 0.001f) maxDraw = 1f;
                for(int i=0;i<scan.TopDraws.Count;i++)
                {
                    var r = scan.TopDraws[i];
                    r.Ratio = r.PowerMW / maxDraw;
                    r.Value = $"{r.PowerMW.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW ({(r.Ratio*100f).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}%)";
                }
            }
            scan.DrawHeader = "TOP POWER DRAWS ("+scan.TopDraws.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)+")";
            return scan;
        }

        static string CatValue(int count, float cur, float max)
        {
            if (count == 0) return "NONE";
            return $"{cur.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} / {max.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} MW ({((max > 0f ? cur / max : 0f) * 100f).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}%)";
        }

        static float CatRatio(float cur, float max)
        {
            return max > 0f ? cur / max : 0f;
        }

        static Color CatColor(int count, float cur)
        {
            if (count == 0) return new Color(80, 85, 95);
            return cur > 0.001f ? new Color(80, 200, 230) : new Color(140, 145, 155);
        }

        void DrawBatteryRow(int idx, float rowTop)
        {
            BatteryRow row = _scan.Batteries[idx];
            // Pure render from the scan-side row: no block API calls, the
            // strings and colors were resolved once per grid per window.
            AddText(_frame, row.Name, new Vector2(Left, rowTop + 1f * S), 0.48f * S, FgColor, TextAlignment.LEFT);
            AddText(_frame, row.State, new Vector2(Right, rowTop + 1f * S), 0.44f * S, row.StateColor, TextAlignment.RIGHT);

            AddText(_frame, row.Details, new Vector2(Left, rowTop + 15f * S), 0.44f * S, new Color(170, 175, 185), TextAlignment.LEFT);

            RectangleF bar = new RectangleF(new Vector2(Left, rowTop + 29f * S), new Vector2(Right - Left, 6f * S));
            DrawBar(_frame, bar, row.Ratio, row.Charging ? new Color(80, 200, 230) : BarColor(row.Ratio));
        }

        void DrawTopRow(int idx, float y)
        {
            var r = _scan.TopDraws[idx];
            DrawProgressRow(_frame, y, r.Icon, r.Name, r.Value, r.Ratio, new Color(230,80,60));
        }
    }
}
