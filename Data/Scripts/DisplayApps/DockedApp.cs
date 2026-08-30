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
            public float DamagePercent;
            public string DamageText;
            public Color DamageColor;

            // Row strings/colors, precomputed in the scan so every display on
            // the grid draws without formatting anything.
            public string GasText;
            public string ViaText;
            public string EtaText;
            public Color EtaColor;
            public float CargoRatio;
            public string CargoText;
        }

        /// <summary>Battery/gas/cargo totals of one docked grid, computed once
        /// per docked grid per window and shared by every host scan that sees
        /// that grid (multiple displays with different configs, neighbouring
        /// stations docked to the same ship).</summary>
        class ShipStats
        {
            public long Window = -1;
            public BatterySummary Bat;
            public bool HasH2, HasO2;
            public float CargoVol, CargoMax;
        }

        static readonly Dictionary<long, ShipStats> _dockStats = new Dictionary<long, ShipStats>();
        static readonly List<long> _staleDockStats = new List<long>();
        static int _dockEvictCounter;

        /// <summary>Blocks of the host grid's terminal group, bucketed by
        /// owning grid and rebuilt once per window. Connector-docked grids
        /// are part of the same terminal system, so one group walk serves
        /// every docked ship's stat refresh (was one full walk per ship).</summary>
        class GridBuckets
        {
            public long Window = -1;
            public readonly Dictionary<long, List<MyTerminalBlock>> Blocks = new Dictionary<long, List<MyTerminalBlock>>();
        }

        static readonly Dictionary<long, GridBuckets> _bucketsByHost = new Dictionary<long, GridBuckets>();
        static readonly List<long> _staleBuckets = new List<long>();

        class DockedScan : IScanData
        {
            public readonly List<DockedShip> Ships = new List<DockedShip>();
            readonly List<DockedShip> _pool = new List<DockedShip>();
            public readonly HashSet<long> SeenGrids = new HashSet<long>();
            public int Connectors, Connected;
            public string DockedText, ConnText;

            public void Clear()
            {
                _pool.AddRange(Ships);
                Ships.Clear();
                SeenGrids.Clear();
                Connectors = 0;
                Connected = 0;
                DockedText = null;
                ConnText = null;
            }

            public DockedShip RentShip()
            {
                if (_pool.Count > 0)
                {
                    DockedShip ship = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    // Reset the accumulated fields - a recycled ship must not
                    // carry the previous window's totals into this one.
                    ship.Bat = default(BatterySummary);
                    ship.HasH2 = false;
                    ship.HasO2 = false;
                    ship.CargoVol = 0f;
                    ship.CargoMax = 0f;
                    return ship;
                }
                return new DockedShip();
            }
        }

        // No named Comparison<T> field - see the note in AssemblerApp.

        readonly Func<DockedScan> _scanFunc;
        MySpriteDrawFrame _frame;
        DockedScan _scan;
        readonly Action<int, float> _drawShipRow;

        /// <summary>Shared raw block buffer for the per-window group walk - only one
        /// scan runs at a time, so one static list serves all instances.</summary>
        static readonly List<MyTerminalBlock> _dockedBlocks = new List<MyTerminalBlock>();

        static readonly Color IdleColor = new Color(140, 145, 155);
        static readonly Color ChargeColor = new Color(50, 210, 90);
        static readonly Color DrainColor = new Color(230, 60, 50);

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

                if (ships.Count == 0)
                {
                    DrawEmpty(frame, "NO DOCKED SHIPS");
                    return;
                }

                AddText(frame, scan.DockedText, new Vector2(Left, 48f * S), 0.46f * S, new Color(50, 210, 90), TextAlignment.LEFT);
                AddText(frame, scan.ConnText, new Vector2(Right, 48f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y = 74f * S;
                float bottom = Bottom;
                float rowH = GetSectionVisible("ShowDamage", true) ? 42f * S : 36f * S;
                int drawn = DrawListGroup(frame, 0, null, ships.Count, y, 0f, bottom - y, rowH, _drawShipRow);

                if (!ConfigScroll && ships.Count > drawn)
                    DrawMore(frame, $"+{(ships.Count - drawn).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} MORE");
            }
        }

        /// <summary>Fills stats with the docked grid's battery/gas/cargo state.
        /// Runs at most once per docked grid per window (Window stamp).
        /// Reads the bucket of blocks built by the per-window group walk,
        /// so no terminal-system API calls happen here.</summary>
        static void RefreshShipStats(ShipStats stats, List<MyTerminalBlock> bucket, long window)
        {
            if (stats.Window == window) return;
            stats.Window = window;
            stats.Bat = default(BatterySummary);
            stats.HasH2 = false;
            stats.HasO2 = false;
            stats.CargoVol = 0f;
            stats.CargoMax = 0f;

            if (bucket == null || bucket.Count == 0) return;
            for (int k = 0; k < bucket.Count; k++)
            {
                MyTerminalBlock fb = bucket[k];

                Battery bat = fb as Battery;
                if (bat != null)
                {
                    AccumulateBattery(ref stats.Bat, bat);
                    continue;
                }

                GasTank tank = fb as GasTank;
                if (tank != null)
                {
                    if (IsHydrogenTank(tank)) stats.HasH2 = true;
                    else stats.HasO2 = true;
                    continue;
                }

                if (fb.InventoryCount > 0)
                {
                    for (int inv = 0; inv < fb.InventoryCount; inv++)
                    {
                        VRage.Game.ModAPI.IMyInventory invb = fb.GetInventory(inv);
                        stats.CargoVol += (float)invb.CurrentVolume;
                        stats.CargoMax += (float)invb.MaxVolume;
                    }
                }
            }
        }

        /// <summary>Rebuilds the per-grid block buckets for this display's
        /// scan grid group. Runs once per window; every docked ship's stat
        /// refresh then reads only its own grid's bucket.</summary>
        static void RefreshBuckets(GridBuckets buckets, VRage.Game.ModAPI.IMyCubeGrid grid, long window)
        {
            if (buckets.Window == window) return;
            buckets.Window = window;
            foreach (var kv in buckets.Blocks) kv.Value.Clear();

            if (MyAPIGateway.TerminalActionsHelper == null) return;
            var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (ts == null) return;

            _dockedBlocks.Clear();
            ts.GetBlocks(_dockedBlocks);
            for (int i = 0; i < _dockedBlocks.Count; i++)
            {
                MyTerminalBlock fb = _dockedBlocks[i];
                List<MyTerminalBlock> bucket;
                if (!buckets.Blocks.TryGetValue(fb.CubeGrid.EntityId, out bucket))
                {
                    bucket = new List<MyTerminalBlock>();
                    buckets.Blocks[fb.CubeGrid.EntityId] = bucket;
                }
                bucket.Add(fb);
            }
        }

        DockedScan ScanGrid()
        {
            RefreshTerminalBlocks();
            long window = Window();

            // One group walk per window, shared by every docked grid below.
            GridBuckets buckets = null;
            long hostId = ScanGridId;
            if (hostId != 0)
            {
                if (!_bucketsByHost.TryGetValue(hostId, out buckets))
                {
                    buckets = new GridBuckets();
                    _bucketsByHost[hostId] = buckets;
                }
                RefreshBuckets(buckets, CurrentScanGrid, window);
            }
            if (_bucketsByHost.Count > 16)
            {
                _staleBuckets.Clear();
                foreach (var kv in _bucketsByHost)
                    if (kv.Value.Window != window) _staleBuckets.Add(kv.Key);
                for (int i = 0; i < _staleBuckets.Count; i++)
                    _bucketsByHost.Remove(_staleBuckets[i]);
                _staleBuckets.Clear();
            }

            DockedScan scan = RentScan<DockedScan>();
            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                MyConnector c = TerminalBlocks[i] as MyConnector;
                if (c == null) continue;
                scan.Connectors++;
                MyConnector other = c.OtherConnector;
                if (other == null) continue;
                scan.Connected++;
                MyGrid grid = other.CubeGrid;
                if (grid == null) continue;
                if (!scan.SeenGrids.Add(grid.EntityId)) continue;

                DockedShip ship = scan.RentShip();
                ship.GridName = grid.CustomName;
                if (string.IsNullOrEmpty(ship.GridName)) ship.GridName = grid.DisplayName;
                if (string.IsNullOrEmpty(ship.GridName)) ship.GridName = "GRID";
                ship.GridName = Truncate(ship.GridName, 18);
                ship.ConnectorName = Truncate(BlockName(c), 16);
                ship.Icon = EmoteIcons[(int)(grid.EntityId & 15)];

                ShipStats stats;
                if (!_dockStats.TryGetValue(grid.EntityId, out stats))
                {
                    stats = new ShipStats();
                    _dockStats[grid.EntityId] = stats;
                }
                List<MyTerminalBlock> bucket = null;
                if (buckets != null)
                    buckets.Blocks.TryGetValue(grid.EntityId, out bucket);
                RefreshShipStats(stats, bucket, window);
                ship.Bat = stats.Bat;
                ship.HasH2 = stats.HasH2;
                ship.HasO2 = stats.HasO2;
                ship.CargoVol = stats.CargoVol;
                ship.CargoMax = stats.CargoMax;

                // Row strings - all pure functions of the values above, so
                // they are built once per grid per window, not per display.
                ship.GasText = ship.HasH2 ? (ship.HasO2 ? "H2 O2" : "H2") : (ship.HasO2 ? "O2" : "");
                ship.ViaText = "via " + ship.ConnectorName;

                float netIn = ship.Bat.NetFlow;
                string pTime;
                if (ship.Bat.Max <= 0.001f)
                {
                    ship.EtaColor = IdleColor;
                    pTime = "--";
                }
                else if (netIn > 0.001f)
                {
                    ship.EtaColor = ChargeColor;
                    pTime = FormatEta((ship.Bat.Max - ship.Bat.Stored) / netIn);
                }
                else if (netIn < -0.001f)
                {
                    ship.EtaColor = DrainColor;
                    pTime = FormatEta(ship.Bat.Stored / -netIn);
                }
                else
                {
                    ship.EtaColor = IdleColor;
                    pTime = "--";
                }
                ship.EtaText = "T-" + pTime;

                ship.CargoRatio = ship.CargoMax > 0f ? ship.CargoVol / ship.CargoMax : 0f;
                ship.CargoText = "Cargo " + (ship.CargoRatio * 100f).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "%";

                // Shared damage percent from projectors + damaged blocks (same as DamageApp)
                try
                {
                    float dmg = SharedDamage.GetDamagePercent(grid, window);
                    ship.DamagePercent = dmg;
                    if (dmg > 0.5f)
                    {
                        ship.DamageText = $"DMG {dmg.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}%";
                        ship.DamageColor = dmg > 25f ? new Color(220,60,50) : (dmg > 5f ? new Color(230,180,60) : new Color(230,200,90));
                    }
                    else
                    {
                        ship.DamageText = "OK";
                        ship.DamageColor = new Color(50,210,90);
                    }
                }
                catch { ship.DamageText = "--"; ship.DamageColor = new Color(140,145,155); }

                scan.Ships.Add(ship);
            }

            // Drop cached stats of grids that undocked (gated: the purge itself
            // walks the whole map).
            if (_dockStats.Count > 64 && (++_dockEvictCounter & 7) == 0)
            {
                _staleDockStats.Clear();
                foreach (var kv in _dockStats)
                    if (kv.Value.Window != window) _staleDockStats.Add(kv.Key);
                for (int i = 0; i < _staleDockStats.Count; i++)
                    _dockStats.Remove(_staleDockStats[i]);
                _staleDockStats.Clear();
            }

            scan.Ships.Sort((x, y) => string.Compare(x.GridName, y.GridName, StringComparison.Ordinal));
            scan.DockedText = "DOCKED: " + scan.Ships.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " GRID(S)";
            scan.ConnText = "CONNECTORS: " + scan.Connected.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "/" + scan.Connectors.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            return scan;
        }

        void DrawShipRow(int idx, float rowTop)
        {
            DockedShip ship = _scan.Ships[idx];
            _frame.Add(Icon(ship.Icon, new Vector2(Left + 20f * S, rowTop + 18f * S), 32f * S, Color.White));
            AddText(_frame, ship.GridName, new Vector2(Left + 42f * S, rowTop), 0.46f * S, FgColor, TextAlignment.LEFT);

            float connRight = Right;
            if (ship.GasText.Length > 0)
            {
                AddText(_frame, ship.GasText, new Vector2(Right, rowTop), 0.42f * S, new Color(80, 200, 230), TextAlignment.RIGHT);
                connRight = Right - 42f * S;
            }
            AddText(_frame, ship.ViaText, new Vector2(connRight, rowTop), 0.44f * S, new Color(80, 220, 120), TextAlignment.RIGHT);

            float y2 = rowTop + 18f * S;
            _frame.Add(Icon("IconEnergy", new Vector2(Left + 48f * S, y2 + 7f * S), 13f * S, ship.EtaColor));
            AddText(_frame, ship.EtaText, new Vector2(Left + 58f * S, y2), 0.42f * S, ship.EtaColor, TextAlignment.LEFT);

            RectangleF pwrBar = new RectangleF(new Vector2(Left + 114f * S, y2 + 6f * S), new Vector2(50f * S, 3f * S));
            DrawCenterFlowBar(_frame, pwrBar, ship.Bat.NetFlow, ship.Bat.MaxOut > 0f ? ship.Bat.MaxOut : 10f);

            AddText(_frame, ship.CargoText, new Vector2(Right - 58f * S, y2), 0.42f * S, new Color(170, 175, 185), TextAlignment.RIGHT);

            RectangleF cargoBar = new RectangleF(new Vector2(Right - 50f * S, y2 + 6f * S), new Vector2(50f * S, 3f * S));
            DrawBar(_frame, cargoBar, ship.CargoRatio, BarColor(ship.CargoRatio));

            // Damage % shared with DamageApp (includes projector missing)
            bool showDamage = GetSectionVisible("ShowDamage", true);
            if (showDamage)
            {
                float y3 = rowTop + 28f * S;
                AddText(_frame, ship.DamageText, new Vector2(Left + 42f * S, y3), 0.38f * S, ship.DamageColor, TextAlignment.LEFT);
                RectangleF dmgBar = new RectangleF(new Vector2(Left + 90f * S, y3 + 5f * S), new Vector2(60f*S, 3f*S));
                DrawBar(_frame, dmgBar, ship.DamagePercent/100f, ship.DamageColor);
            }
        }
    }
}
