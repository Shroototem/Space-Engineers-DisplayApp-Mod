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

        class PowerScan : IScanData
        {
            public readonly List<BatteryRow> Batteries = new List<BatteryRow>();
            readonly List<BatteryRow> _rowPool = new List<BatteryRow>();
            public float SolarCur, SolarMax, WindCur, WindMax, ReactCur, ReactMax, EngCur, EngMax;
            public BatterySummary Bat;
            public int SolarCount, WindCount, ReactCount, EngCount, BatCount;

            // Grid-wide strings, built once per grid per window in the scan.
            // Per-battery row strings are built in the scan too - the scan is
            // shared per grid per window, so precomputing them is cheaper than
            // per-display reads and formatting.
            public string StorageText, FlowText, MinTimeText, BatHeader;
            public readonly string[] CatTexts = new string[4];
            public readonly float[] CatRatios = new float[4];

            public void Clear()
            {
                _rowPool.AddRange(Batteries);
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
                MinTimeText = null;
                BatHeader = null;
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
        }

        readonly Func<PowerScan> _scanFunc;
        MySpriteDrawFrame _frame;
        PowerScan _scan;
        readonly Action<int, float> _drawBatteryRow;

        public PowerApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawBatteryRow = DrawBatteryRow;
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

                float batRatio = batMaxStored > 0f ? batStored / batMaxStored : 0f;
                AddText(frame, "BATTERY STORAGE", new Vector2(Left, 52f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, scan.StorageText, new Vector2(Right, 52f * S), 0.50f * S, new Color(200, 205, 215), TextAlignment.RIGHT);

                RectangleF batBar = new RectangleF(new Vector2(Left, 68f * S), new Vector2(Right - Left, 14f * S));
                DrawBar(frame, batBar, batRatio, BarColor(batRatio));

                AddText(frame, "NET POWER FLOW", new Vector2(Left, 88f * S), 0.48f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                AddText(frame, scan.FlowText, new Vector2(Right, 88f * S), 0.48f * S, netBat > 0.001f ? new Color(50, 210, 90) : (netBat < -0.001f ? new Color(230, 60, 50) : new Color(160, 170, 185)), TextAlignment.RIGHT);

                RectangleF flowBar = new RectangleF(new Vector2(Left, 104f * S), new Vector2(Right - Left, 14f * S));
                DrawCenterFlowBar(frame, flowBar, netBat, batMaxOut > 0f ? batMaxOut : 10f);

                AddText(frame, "TIME AT MAX OUTPUT", new Vector2(Left, 122f * S), 0.44f * S, new Color(140, 145, 155), TextAlignment.LEFT);
                AddText(frame, scan.MinTimeText, new Vector2(Right, 122f * S), 0.44f * S, new Color(170, 175, 185), TextAlignment.RIGHT);

                DrawDivider(frame, 138f);
                AddText(frame, "POWER SOURCE BREAKDOWN", new Vector2(Left, 144f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);

                float catY = 162f * S;
                float catH = 26f * S;

                int catIdx = 0;
                if (solarCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "SOLAR PANELS", "MyObjectBuilder_Component/SolarCell", solarCount, scan.CatTexts[0], scan.CatRatios[0], CatColor(solarCount, scan.SolarCur), catY + catIdx++ * catH);
                if (windCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "WIND TURBINES", "MyObjectBuilder_Component/Motor", windCount, scan.CatTexts[1], scan.CatRatios[1], CatColor(windCount, scan.WindCur), catY + catIdx++ * catH);
                if (reactCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "NUCLEAR REACTORS", "MyObjectBuilder_Component/Reactor", reactCount, scan.CatTexts[2], scan.CatRatios[2], CatColor(reactCount, scan.ReactCur), catY + catIdx++ * catH);
                if (engCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "HYDRO ENGINES", "IconHydrogen", engCount, scan.CatTexts[3], scan.CatRatios[3], CatColor(engCount, scan.EngCur), catY + catIdx++ * catH);

                float batListTop = catY + (catIdx + 0.2f) * catH;
                float availH = Bottom - batListTop;

                if (availH > 50f * S && batCount > 0)
                {
                    DrawDivider(frame, (batListTop) / S);

                    float rowTopStart = batListTop + 24f * S;
                    int rows = DrawListGroup(frame, 0, scan.BatHeader, scan.Batteries.Count,
                        batListTop + 6f * S, 18f * S, Bottom - rowTopStart, 40f * S, _drawBatteryRow);

                    if (!ConfigScroll && batCount > rows)
                        DrawMore(frame, $"+{batCount - rows} MORE BATTERY(IES)");
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
                        state = $"CHARGING (+{row.In:0.00} MW)";
                        stateColor = new Color(50, 210, 90);
                    }
                    else if (bat.ChargeMode == Sandbox.ModAPI.Ingame.ChargeMode.Recharge)
                    {
                        state = $"RECHARGE (+{row.In:0.00} MW)";
                        stateColor = new Color(80, 200, 230);
                    }
                    else if (row.Out > 0.005f)
                    {
                        state = $"DISCHARGING (-{row.Out:0.00} MW)";
                        stateColor = new Color(230, 60, 50);
                    }
                    else
                    {
                        state = "STANDBY";
                        stateColor = new Color(140, 145, 155);
                    }
                    row.State = state;
                    row.StateColor = stateColor;
                    row.Details = $"{row.Stored:0.00} / {row.Max:0.00} MWh ({row.Ratio * 100f:0}%)";
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
                }
            }

            // Grid-wide strings - pure functions of the totals above.
            float batStored = scan.Bat.Stored, batMaxStored = scan.Bat.Max;
            float batMaxOut = scan.Bat.MaxOut;
            float netBat = scan.Bat.NetFlow;
            float batRatio = batMaxStored > 0f ? batStored / batMaxStored : 0f;
            scan.StorageText = $"{batStored:0.00} / {batMaxStored:0.00} MWh ({batRatio * 100f:0}%)";

            string flowLabel = "0.00 MW (IDLE)";
            if (netBat > 0.001f)
                flowLabel = $"+{netBat:0.00} MW IN ({FormatTimeHours((batMaxStored - batStored) / netBat)} TO FULL)";
            else if (netBat < -0.001f)
                flowLabel = $"{netBat:0.00} MW OUT ({FormatTimeHours(batStored / -netBat)} TO EMPTY)";
            scan.FlowText = flowLabel;

            scan.MinTimeText = "--";
            if (batMaxOut > 0.001f && batMaxStored > 0.001f)
                scan.MinTimeText = $"EMPTY {FormatTimeHours(batStored / batMaxOut)}   |   FULL {FormatTimeHours((batMaxStored - batStored) / batMaxOut)}";

            scan.CatTexts[0] = CatValue(scan.SolarCount, scan.SolarCur, scan.SolarMax);
            scan.CatRatios[0] = CatRatio(scan.SolarCur, scan.SolarMax);
            scan.CatTexts[1] = CatValue(scan.WindCount, scan.WindCur, scan.WindMax);
            scan.CatRatios[1] = CatRatio(scan.WindCur, scan.WindMax);
            scan.CatTexts[2] = CatValue(scan.ReactCount, scan.ReactCur, scan.ReactMax);
            scan.CatRatios[2] = CatRatio(scan.ReactCur, scan.ReactMax);
            scan.CatTexts[3] = CatValue(scan.EngCount, scan.EngCur, scan.EngMax);
            scan.CatRatios[3] = CatRatio(scan.EngCur, scan.EngMax);
            scan.BatHeader = "INDIVIDUAL BATTERIES (" + scan.BatCount + ")";
            return scan;
        }

        static string CatValue(int count, float cur, float max)
        {
            if (count == 0) return "NONE";
            return $"{cur:0.00} / {max:0.00} MW ({(max > 0f ? cur / max : 0f) * 100f:0}%)";
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
    }
}
