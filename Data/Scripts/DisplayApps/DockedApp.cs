using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using MyConnector = Sandbox.ModAPI.IMyShipConnector;
using MyGrid = VRage.Game.ModAPI.IMyCubeGrid;
using Battery = Sandbox.ModAPI.IMyBatteryBlock;
using GasTank = Sandbox.ModAPI.IMyGasTank;

namespace DisplayApps
{
    [MyTextSurfaceScript("DockedInfo", "Info Docked Ships")]
    public class DockedApp : AppBase
    {
        static readonly string[] EmoteIcons =
        {
            "LCD_Emote_Angry", "LCD_Emote_Annoyed", "LCD_Emote_Confused", "LCD_Emote_Crying",
            "LCD_Emote_Dead", "LCD_Emote_Evil", "LCD_Emote_Happy", "LCD_Emote_Love",
            "LCD_Emote_Neutral", "LCD_Emote_Sad", "LCD_Emote_Shocked", "LCD_Emote_Skeptical",
            "LCD_Emote_Sleepy", "LCD_Emote_Suspicious_Left", "LCD_Emote_Suspicious_Right", "LCD_Emote_Wink"
        };

        class DockedShip
        {
            public string GridName;
            public string ConnectorName;
            public string Icon = "MyObjectBuilder_Component/MetalGrid";
            public bool HasH2;
            public bool HasO2;
            public BatterySummary Bat;
            public float CargoVol, CargoMax;
        }

        class DockedScan : IScanData
        {
            public readonly List<DockedShip> Ships = new List<DockedShip>();
            readonly List<DockedShip> _pool = new List<DockedShip>();
            public readonly HashSet<long> SeenGrids = new HashSet<long>();
            public int Connectors, Connected;

            public void Clear()
            {
                _pool.AddRange(Ships);
                Ships.Clear();
                SeenGrids.Clear();
                Connectors = 0;
                Connected = 0;
            }

            public DockedShip RentShip()
            {
                if (_pool.Count > 0)
                {
                    DockedShip ship = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return ship;
                }
                return new DockedShip();
            }
        }

        readonly Func<DockedScan> _scanFunc;
        MySpriteDrawFrame _frame;
        DockedScan _scan;
        readonly Action<int, float> _drawShipRow;
        readonly List<MyTerminalBlock> _dockedBlocks = new List<MyTerminalBlock>();

        public DockedApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawShipRow = DrawShipRow;
        }

        protected override void RunApp()
        {
            DockedScan scan = GetGridScan(_scanFunc);
            _scan = scan;

            using (var frame = BeginAppFrame("DOCKED SHIPS", "CONNECTED VESSELS & STATIONS", "MyObjectBuilder_Component/MetalGrid", new Color(230, 180, 90)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                var ships = scan.Ships;
                int connectors = scan.Connectors;
                int connected = scan.Connected;

                if (ships.Count == 0)
                {
                    DrawEmpty(frame, "NO DOCKED SHIPS");
                    return;
                }

                AddText(frame, $"DOCKED: {ships.Count} GRID(S)", new Vector2(Left, 48f * S), 0.46f * S, new Color(50, 210, 90), TextAlignment.LEFT);
                AddText(frame, $"CONNECTORS: {connected}/{connectors}", new Vector2(Right, 48f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y = 74f * S;
                float bottom = Bottom;
                float rowH = 36f * S;
                int drawn = DrawListGroup(frame, 0, null, ships.Count, y, 0f, bottom - y, rowH, _drawShipRow);

                if (!ConfigScroll && ships.Count > drawn)
                    DrawMore(frame, $"+{ships.Count - drawn} MORE");
            }
        }

        DockedScan ScanGrid()
        {
            RefreshTerminalBlocks();

            DockedScan scan = RentScan<DockedScan>();
            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                MyConnector c = TerminalBlocks[i] as MyConnector;
                if (c == null) continue;
                scan.Connectors++;
                if (c.OtherConnector == null) continue;
                scan.Connected++;
                MyConnector other = c.OtherConnector;
                if (other == null || other.CubeGrid == null) continue;
                MyGrid grid = other.CubeGrid;
                if (!scan.SeenGrids.Add(grid.EntityId)) continue;

                DockedShip ship = scan.RentShip();
                ship.GridName = grid.CustomName;
                if (string.IsNullOrEmpty(ship.GridName)) ship.GridName = grid.DisplayName;
                ship.GridName = Truncate(ship.GridName, 18);
                ship.ConnectorName = Truncate(BlockName(c), 16);
                ship.Icon = EmoteIcons[(int)(grid.EntityId % EmoteIcons.Length)];

                if (MyAPIGateway.TerminalActionsHelper != null)
                {
                    var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                    if (ts != null)
                    {
                        _dockedBlocks.Clear();
                        ts.GetBlocks(_dockedBlocks);
                        for (int k = 0; k < _dockedBlocks.Count; k++)
                        {
                            MyTerminalBlock fb = _dockedBlocks[k];
                            if (fb.CubeGrid != grid) continue;
                            if (fb is Battery)
                            {
                                Battery bat = (Battery)fb;
                                AccumulateBattery(ref ship.Bat, bat);
                            }
                            else if (fb is GasTank)
                            {
                                GasTank t = (GasTank)fb;
                                if (IsHydrogenTank(t)) ship.HasH2 = true;
                                else ship.HasO2 = true;
                            }

                            if (!(fb is Sandbox.ModAPI.IMyGasTank) && fb.InventoryCount > 0)
                            {
                                for (int inv = 0; inv < fb.InventoryCount; inv++)
                                {
                                    VRage.Game.ModAPI.IMyInventory invb = fb.GetInventory(inv);
                                    ship.CargoVol += (float)invb.CurrentVolume;
                                    ship.CargoMax += (float)invb.MaxVolume;
                                }
                            }
                        }
                    }
                }
                scan.Ships.Add(ship);
            }

            scan.Ships.Sort((x, y) => string.Compare(x.GridName, y.GridName, false));
            return scan;
        }

        void DrawShipRow(int idx, float rowTop)
        {
            DockedShip ship = _scan.Ships[idx];
            _frame.Add(Icon(ship.Icon, new Vector2(Left + 20f * S, rowTop + 18f * S), 32f * S, Color.White));
            AddText(_frame, ship.GridName, new Vector2(Left + 42f * S, rowTop), 0.46f * S, FgColor, TextAlignment.LEFT);

            string gas = (ship.HasH2 ? "H2 " : "") + (ship.HasO2 ? "O2" : "");
            float connRight = Right;
            if (gas.Length > 0)
            {
                AddText(_frame, gas.TrimEnd(), new Vector2(Right, rowTop), 0.42f * S, new Color(80, 200, 230), TextAlignment.RIGHT);
                connRight = Right - 42f * S;
            }
            AddText(_frame, "via " + ship.ConnectorName, new Vector2(connRight, rowTop), 0.44f * S, new Color(80, 220, 120), TextAlignment.RIGHT);

            float y2 = rowTop + 18f * S;
            float netIn = ship.Bat.NetFlow;
            Color pColor;
            string pTime;
            if (ship.Bat.Max <= 0.001f)
            {
                pColor = new Color(140, 145, 155);
                pTime = "--";
            }
            else if (netIn > 0.001f)
            {
                pColor = new Color(50, 210, 90);
                pTime = FormatEta((ship.Bat.Max - ship.Bat.Stored) / netIn);
            }
            else if (netIn < -0.001f)
            {
                pColor = new Color(230, 60, 50);
                pTime = FormatEta(ship.Bat.Stored / -netIn);
            }
            else
            {
                pColor = new Color(140, 145, 155);
                pTime = "--";
            }

            _frame.Add(Icon("IconEnergy", new Vector2(Left + 48f * S, y2 + 7f * S), 13f * S, pColor));
            AddText(_frame, "T-" + pTime, new Vector2(Left + 58f * S, y2), 0.42f * S, pColor, TextAlignment.LEFT);

            RectangleF pwrBar = new RectangleF(new Vector2(Left + 114f * S, y2 + 6f * S), new Vector2(50f * S, 3f * S));
            DrawCenterFlowBar(_frame, pwrBar, netIn, ship.Bat.MaxOut > 0f ? ship.Bat.MaxOut : 10f);

            float cargoRatio = ship.CargoMax > 0f ? ship.CargoVol / ship.CargoMax : 0f;
            AddText(_frame, $"Cargo {cargoRatio * 100f:0}%", new Vector2(Right - 58f * S, y2), 0.42f * S, new Color(170, 175, 185), TextAlignment.RIGHT);

            RectangleF cargoBar = new RectangleF(new Vector2(Right - 50f * S, y2 + 6f * S), new Vector2(50f * S, 3f * S));
            DrawBar(_frame, cargoBar, cargoRatio, BarColor(cargoRatio));
        }
    }
}