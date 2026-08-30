using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Utils;
using VRageMath;
// (VRage.Game.ModAPI.Ingame also supplies the MyItemType.GetItemInfo extension)

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using MyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace DisplayApps
{
    /// <summary>
    /// Shared base for all display apps: handles scaling, positioning and the
    /// common drawing helpers (bars, text, sprites) used by every app.
    /// </summary>
    public abstract class AppBase : MyTextSurfaceScriptBase
    {
        protected readonly string AppRegionName;
        protected float S, Mx, Left, Right, Cx, Top, Bottom;
        protected readonly List<MySlimBlock> GridBlocks = new List<MySlimBlock>();
        protected readonly List<MyTerminalBlock> TerminalBlocks = new List<MyTerminalBlock>();
        protected readonly List<string> ConfigGroups = new List<string>();
        protected float[] TextPadding = new float[] { 16f, 16f, 16f, 16f };
        protected float TextScale = 1f;
        protected bool ConfigScroll;
        protected bool ConfigSubGrids;
        protected bool ConfigFullList = true;
        protected bool ConfigHighlightDamaged;
        protected bool ConfigPerfLog;
        protected int ConfigStorageType = 1;
        protected int ConfigOreIngotType = 1;
        protected string ConfigRemoteName = "";
        VRage.Game.ModAPI.IMyCubeGrid _scanGrid;
        int _lastScanBlocks;
        string _lastCustomData;
        string _perfAppName;
        string _perfLabel;
        string _perfLabelSource;
        readonly int _updateSlot;
        readonly int[] ScrollPos = new int[4];
        readonly int[] ScrollShown = new int[4];
        readonly List<VRage.Game.ModAPI.IMyCubeGrid> _subGrids = new List<VRage.Game.ModAPI.IMyCubeGrid>();
        static readonly Dictionary<string, RemoteLookup> _remoteLookup = new Dictionary<string, RemoteLookup>();
        static readonly HashSet<VRage.ModAPI.IMyEntity> _entityBuffer = new HashSet<VRage.ModAPI.IMyEntity>();
        protected Color BgColor;
        protected Color FgColor;

        static readonly Dictionary<string, string> AppClassToRegion = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "AssemblerApp", "AssemblerInfo" },
            { "AutoDoorApp", "AutoDoors" },
            { "ComponentsApp", "ComponentsInfo" },
            { "DamageApp", "DamageInfo" },
            { "DockedApp", "DockedInfo" },
            { "GasApp", "O2H2" },
            { "OreIngotApp", "OreIngotInfo" },
            { "PerfApp", "PerfInfo" },
            { "PowerApp", "PowerInfo" },
            { "StorageApp", "StorageInfo" },
            { "ProjectorApp", "ProjectorInfo" }
        };

        static readonly string[] KnownAppRegions =
        {
            "AssemblerInfo",
            "AutoDoors",
            "ComponentsInfo",
            "DamageInfo",
            "DockedInfo",
            "O2H2",
            "OreIngotInfo",
            "PerfInfo",
            "PowerInfo",
            "StorageInfo",
            "ProjectorInfo"
        };

        static readonly string[] DefaultOptionKeys =
        {
            "TextPadding",
            "TextScale",
            "TextScroll",
            "SubGrids",
            "FullList",
            "HighlightDamaged",
            "StorageType",
            "OreIngotType",
            "Groups",
            "RemoteGrid",
            "PerfLog"
        };

        static readonly string[] DefaultOptionDefaults =
        {
            "[16,16,16,16]",
            "1.0",
            "false",
            "false",
            "true",
            "false",
            "1",
            "1",
            "",
            "",
            "false"
        };

        const string ConfigMarker = "@region";
        const string ConfigTemplate =
            "@region DEFAULT\n" +
            "# DisplayApps settings - one option per line: \"Option: value\".\n" +
            "# Lines starting with '#' are ignored.\n" +
            "# TextPadding: Padding in pixels as [UP, DOWN, LEFT, RIGHT] (default: [16,16,16,16]).\n" +
            "TextPadding: [16,16,16,16]\n" +
            "\n" +
            "# TextScale: Multiplier for the whole layout - text, icons, bars and spacing\n" +
            "#   scale and shift together (default: 1.0).\n" +
            "TextScale: 1.0\n" +
            "\n" +
            "# TextScroll: Auto-scroll lists that don't fit on screen (true/false, default: false).\n" +
            "TextScroll: false\n" +
            "\n" +
            "# SubGrids: Also scan blocks on subgrids (rotors/pistons/hinges) and\n" +
            "#   connector-docked grids (Physical group) (true/false, default: false).\n" +
            "SubGrids: false\n" +
            "\n" +
            "# FullList: Show all item types, also ones with 0 stock, or only the\n" +
            "#   types you have (true/false, default: true).\n" +
            "FullList: true\n" +
            "\n" +
            "# HighlightDamaged: Highlight the 10 most damaged blocks in the\n" +
            "#   world with a solid outline (true/false, default: false).\n" +
            "HighlightDamaged: false\n" +
            "\n" +
            "# StorageType: Unit display for storage - 1: Both (kg & L), 2: kg only, 3: L only (default: 1).\n" +
            "StorageType: 1\n" +
            "\n" +
            "# OreIngotType: Sections shown by the Ores & Ingots app - 1: Both, 2: Ores only, 3: Ingots only (default: 1).\n" +
            "OreIngotType: 1\n" +
            "\n" +
            "# Groups: Comma separated names of terminal block groups. When set,\n" +
            "#   ONLY blocks in these groups are shown. Leave empty for the whole grid.\n" +
            "Groups: \n" +
            "\n" +
            "# RemoteGrid: Name of another grid to pull data from instead of the grid\n" +
            "#   the display is on. Leave empty for the local grid.\n" +
            "RemoteGrid: \n" +
            "\n" +
            "# PerfLog: Advanced performance stats on the Performance app (timing\n" +
            "#   histograms, per-display breakdown, slow update list).\n" +
            "PerfLog: false\n" +
            "@end region\n" +
            "\n" +
            "@region AssemblerInfo\n" +
            "@end region\n" +
            "\n" +
            "@region ComponentsInfo\n" +
            "@end region\n" +
            "\n" +
            "@region DamageInfo\n" +
            "@end region\n" +
            "\n" +
            "@region DockedInfo\n" +
            "@end region\n" +
            "\n" +
            "@region O2H2\n" +
            "@end region\n" +
            "\n" +
            "@region OreIngotInfo\n" +
            "@end region\n" +
            "\n" +
            "@region PerfInfo\n" +
            "@end region\n" +
            "\n" +
            "@region PowerInfo\n" +
            "@end region\n" +
            "\n" +
            "@region StorageInfo\n" +
            "@end region\n" +
            "\n" +
            "@region ProjectorInfo\n" +
            "@end region\n" +
            "\n" +
            "@region AutoDoors\n" +
            "@end region";
        readonly Dictionary<string, Dictionary<string, string>> _regionValues = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<long> _groupIds = new HashSet<long>();
        readonly List<IMyBlockGroup> _blockGroups = new List<IMyBlockGroup>();
        readonly List<MyTerminalBlock> _groupBlocks = new List<MyTerminalBlock>();

        protected AppBase(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            string region;
            if (!AppClassToRegion.TryGetValue(GetType().Name, out region))
                region = GetType().Name;
            AppRegionName = region;
            _perfAppName = GetType().Name;
            // Fire-point slot 0..9 within each 100-tick window (the engine
            // calls Run every 10 ticks, so 10 fire-points per window).
            _updateSlot = block != null ? (int)(block.EntityId % 10) : 0;
            if (block != null && block.EntityId != 0)
            {
                AppTerminalControls.EnsureRegistered(block as MyTerminalBlock);
                int surfaceIndex = -1;
                var provider = block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
                if (provider != null)
                {
                    for (int i = 0; i < provider.SurfaceCount; i++)
                    {
                        if (object.ReferenceEquals(provider.GetSurface(i), surface))
                        {
                            surfaceIndex = i;
                            break;
                        }
                    }
                }
                AppTerminalControls.RecordAppSelection(block.EntityId, region, surfaceIndex);
            }
            BgColor = surface.ScriptBackgroundColor;
            FgColor = surface.ScriptForegroundColor;
            // Default to White text on Black background when no CustomData or first app selection.
            try
            {
                var tb0 = block as MyTerminalBlock;
                string cd0 = tb0 != null ? (tb0.CustomData ?? "") : "";
                bool noConfig = cd0.IndexOf("@region", StringComparison.OrdinalIgnoreCase) < 0;
                if (noConfig)
                {
                    // Only override when surface still has stock grey colors - respect user custom colors.
                    // Stock LCD bg is typically dark grey (27,27,27) and fg greyish; force to Black/White.
                    bool isDefaultBg = BgColor.R < 30 && BgColor.G < 30 && BgColor.B < 30 ? false : true;
                    // Simpler: always set to Black/White when no config, as requested "If the user selects an app and there is no Custom Data, then set colors to White and Black."
                    surface.ScriptBackgroundColor = new Color(0, 0, 0);
                    surface.ScriptForegroundColor = new Color(255, 255, 255);
                    BgColor = new Color(0, 0, 0);
                    FgColor = new Color(255, 255, 255);
                }
            }
            catch { }
            S = Math.Min(m_scale.X, m_scale.Y);
            if (S < 0.75f) S = 0.75f;
            Mx = 16f * S;
            Left = Mx;
            Right = m_size.X - 16f * S;
            Cx = (Left + Right) / 2f;
            Top = 16f * S;
            Bottom = m_size.Y - 32f * S;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public override void Run()
        {
            // Work splitting: ScriptUpdate has no per-tick member (the enum
            // only offers Update10/100/1000/10000 - an undefined cast value
            // like (ScriptUpdate)1 is rejected by the engine and never
            // called), so Update10 is the finest cadence available: the
            // engine calls Run every 10 ticks, ten times per 100-tick
            // window. Each display does its full update only at the
            // fire-point matching its slot, exactly once per window - the
            // load spreads across the window's ten fire-points while the
            // data still refreshes at the Update100 cadence.
            int tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (Perf.LivePerfApps > 0)
                Perf.CountInvocation(_perfAppName, tick);
            if ((tick / 10) % 10 != _updateSlot) return;
            try
            {
                AppTerminalControls.EnsureRegistered(Block as MyTerminalBlock);
                BgColor = Surface.ScriptBackgroundColor;
                FgColor = Surface.ScriptForegroundColor;
                long t0 = Stopwatch.GetTimestamp();
                LoadConfig();
                RunApp();
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                MyTerminalBlock tb = Block as MyTerminalBlock;
                string source = (tb != null && tb.CustomName.Length > 0) ? tb.CustomName : null;
                if (_perfLabel == null || !string.Equals(source, _perfLabelSource, StringComparison.Ordinal))
                {
                    _perfLabelSource = source;
                    _perfLabel = _perfAppName + " [" + (source ?? ("Block " + Block.EntityId)) + "]";
                }
                if (Perf.LivePerfApps > 0)
                    Perf.Record(_perfAppName, _perfLabel, ms, MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds);
                if (ms > 50.0)
                    MyLog.Default.WriteLine("DisplayApps " + _perfAppName + ": slow update " + ms.ToString("0.0") + " ms");
            }
            catch
            {
            }
        }

        /// <summary>Current scan window index - advances once per 100 sim ticks.
        /// Derived from the tick counter so the scan cache stamps stay
        /// consistent with the Update100 invocation cadence.</summary>
        protected long Window()
        {
            return MyAPIGateway.Session.GameplayFrameCounter / 100;
        }

        protected abstract void RunApp();

        /// <summary>Refills GridBlocks with the blocks of the scan grid (the LCD's own
        /// grid, or the RemoteGrid when configured - plus all mechanically
        /// connected subgrids when SubGrids is enabled), then applies the
        /// configured group filter (if any).</summary>
        protected void RefreshGridBlocks()
        {
            GridBlocks.Clear();
            var grid = CurrentScanGrid;
            if (ConfigSubGrids && MyAPIGateway.GridGroups != null)
            {
                _subGrids.Clear();
                // Use Physical so connector-docked grids (ship via connector)
                // are also included when SubGrids is enabled - Mechanical
                // alone misses them (user report: ship connected with
                // connector shows no damaged blocks). Physical includes
                // Mechanical+Logical, so rotors/pistons/hinges/wheels and
                // connectors all share the scan.
                MyAPIGateway.GridGroups.GetGroup(grid, VRage.Game.ModAPI.GridLinkTypeEnum.Physical, _subGrids);
                for (int i = 0; i < _subGrids.Count; i++)
                {
                    if (_subGrids[i] != null) _subGrids[i].GetBlocks(GridBlocks);
                }
            }
            else
            {
                grid.GetBlocks(GridBlocks);
            }
            ApplyGroupFilter();
            _lastScanBlocks = GridBlocks.Count;
        }

        /// <summary>Refills TerminalBlocks with the terminal blocks of the scan
        /// grid's terminal system - a game-maintained list, so no armor cubes
        /// and no fat-block lookups. The terminal system spans the whole
        /// logical group (mechanical and merge connections), so when SubGrids
        /// is off only blocks directly on the scan grid are kept. When
        /// SubGrids is on the Physical group is used so connector-docked
        /// grids (ship via connector) are also included. Applies the
        /// configured group filter.</summary>
        protected void RefreshTerminalBlocks()
        {
            TerminalBlocks.Clear();
            var grid = CurrentScanGrid;
            if (MyAPIGateway.TerminalActionsHelper == null) return;
            var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (ts == null) return;
            ts.GetBlocks(TerminalBlocks);
            if (!ConfigSubGrids)
            {
                // Compaction pass: one write index, then a single RemoveRange
                // (reverse RemoveAt would shift the tail per removal - O(n^2)
                // on groups with many subgrid blocks).
                int w = 0;
                for (int i = 0; i < TerminalBlocks.Count; i++)
                    if (TerminalBlocks[i].CubeGrid == grid) TerminalBlocks[w++] = TerminalBlocks[i];
                if (w < TerminalBlocks.Count) TerminalBlocks.RemoveRange(w, TerminalBlocks.Count - w);
            }
            else if (MyAPIGateway.GridGroups != null)
            {
                // Logical group already contains Mechanical (rotors/pistons/
                // hinges). For connector-docked ships the Physical group is
                // larger - merge those terminal blocks as well when SubGrids
                // is enabled so Damage/Storage/etc. see the whole ship.
                _subGrids.Clear();
                MyAPIGateway.GridGroups.GetGroup(grid, VRage.Game.ModAPI.GridLinkTypeEnum.Physical, _subGrids);
                // _subGrids now holds the Physical group. Any grid in it
                // whose CubeGrid != grid and not already in TerminalBlocks
                // needs its terminal blocks added.
                for (int i = 0; i < _subGrids.Count; i++)
                {
                    var g = _subGrids[i];
                    if (g == null || g == grid) continue;
                    // Avoid duplicate work if this grid's blocks already
                    // came via the Logical terminal system (Mechanical
                    // subgrids already in ts.GetBlocks).
                    bool already = false;
                    for (int k = 0; k < TerminalBlocks.Count; k++)
                        if (TerminalBlocks[k].CubeGrid == g) { already = true; break; }
                    if (already) continue;
                    var otherTs = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(g);
                    if (otherTs == null) continue;
                    int before = TerminalBlocks.Count;
                    otherTs.GetBlocks(TerminalBlocks);
                    // otherTs.GetBlocks appends; ensure we don't double-add
                    // the same grid's blocks if GetBlocks returned the whole
                    // Physical group already (defensive).
                    if (TerminalBlocks.Count > before + 2000) break; // sanity
                }
            }
            ApplyGroupFilter();
            _lastScanBlocks = TerminalBlocks.Count;
        }

        /// <summary>Shared buffer for inventory item reads - the ingame
        /// GetItems overload fills a caller-provided list of structs, so no
        /// list or item objects are allocated per inventory per scan (the
        /// ModAPI GetItems() builds a fresh list each call).</summary>
        static readonly List<MyInventoryItem> _itemBuffer = new List<MyInventoryItem>(64);

        /// <summary>Fills the shared item buffer with the inventory's items and
        /// returns it. The buffer is reused - consume it before the next call.</summary>
        protected static List<MyInventoryItem> FillItems(VRage.Game.ModAPI.IMyInventory inventory)
        {
            _itemBuffer.Clear();
            if (inventory != null) inventory.GetItems(_itemBuffer, null);
            return _itemBuffer;
        }

        /// <summary>Iterates every inventory item on the given terminal blocks
        /// (gas tanks and blocks without inventories are skipped) and passes
        /// each item to onItem. Shared by all inventory-scanning apps so the
        /// nested block/inventory traversal lives in one place.</summary>
        protected void ForEachItem(List<MyTerminalBlock> blocks, Action<MyInventoryItem> onItem)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b is Sandbox.ModAPI.IMyGasTank) continue;
                if (b.InventoryCount == 0) continue;
                for (int inv = 0; inv < b.InventoryCount; inv++)
                {
                    var items = FillItems(b.GetInventory(inv));
                    for (int k = 0; k < items.Count; k++)
                        onItem(items[k]);
                }
            }
        }

        /// <summary>
        /// Reads the "@region DEFAULT" and app-specific "@region AppId" config sections from CustomData.
        /// Writes pre-filled template on first run so the settings are visible and easy to edit.
        /// </summary>
        protected void LoadConfig()
        {
            MyTerminalBlock tb = Block as MyTerminalBlock;
            if (tb == null)
            {
                ApplyLayout();
                return;
            }

            string data = tb.CustomData ?? "";
            if (data == _lastCustomData)
            {
                ApplyLayout();
                return;
            }
            _lastCustomData = data;

            ConfigGroups.Clear();
            _regionValues.Clear();
            TextPadding = new float[] { 16f, 16f, 16f, 16f };
            TextScale = 1f;
            ConfigScroll = false;
            ConfigSubGrids = false;
            ConfigFullList = true;
            ConfigHighlightDamaged = false;
            ConfigPerfLog = false;
            ConfigStorageType = 1;
            ConfigOreIngotType = 1;
            ConfigRemoteName = "";

            if (data.IndexOf("@region", StringComparison.OrdinalIgnoreCase) < 0)
            {
                tb.CustomData = (data.Length > 0 ? data.TrimEnd() + "\n\n" : "") + ConfigTemplate;
                // Default colors: White text on Black background when first configuring.
                try
                {
                    Surface.ScriptBackgroundColor = new Color(0, 0, 0);
                    Surface.ScriptForegroundColor = new Color(255, 255, 255);
                    BgColor = new Color(0, 0, 0);
                    FgColor = new Color(255, 255, 255);
                }
                catch { }
                ApplyLayout();
                return;
            }

            var parsed = ParseConfigRegionsCached(data);
            foreach (var kv in parsed)
                _regionValues[kv.Key] = kv.Value;

            string groupsVal;
            if (TryGetConfigValue("Groups", out groupsVal))
            {
                string[] parts = groupsVal.Split(new char[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (int k = 0; k < parts.Length; k++)
                {
                    string groupName = parts[k].Trim();
                    if (groupName.Length > 0) ConfigGroups.Add(groupName);
                }
            }

            string remoteVal;
            if (TryGetConfigValue("RemoteGrid", out remoteVal))
            {
                ConfigRemoteName = remoteVal;
            }

            TextPadding = ParseConfigPadding("TextPadding", new float[] { 16f, 16f, 16f, 16f });
            TextScale = Math.Max(0.1f, Math.Min(10f, ParseConfigFloat("TextScale", 1f)));
            ConfigScroll = ParseConfigBool("TextScroll", false);
            ConfigSubGrids = ParseConfigBool("SubGrids", false);
            ConfigFullList = ParseConfigBool("FullList", true);
            ConfigHighlightDamaged = ParseConfigBool("HighlightDamaged", false);
            ConfigPerfLog = ParseConfigBool("PerfLog", false);
            ConfigStorageType = ParseConfigStorageType("StorageType", 1);
            ConfigOreIngotType = ParseConfigOreIngotType("OreIngotType", 1);
            ApplyLayout();

            EnsureConfigOptions(tb);
        }

        /// <summary>
        /// Parses the "@region Name ... @end region" sections of CustomData into
        /// a region -> (option -> value) map. Shared by the apps (LoadConfig) and
        /// the terminal controls (AppTerminalControls). First value per option
        /// wins, comments and unlabeled lines are skipped.
        /// </summary>
        static Dictionary<string, Dictionary<string, string>> ParseConfigRegions(string data)
        {
            var regions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(data)) return regions;

            string currentRegion = null;
            int pos = 0;
            while (pos < data.Length)
            {
                int nl = data.IndexOf('\n', pos);
                int end = nl < 0 ? data.Length : nl;
                string line = data.Substring(pos, end - pos).Trim();
                pos = nl < 0 ? data.Length : nl + 1;
                if (line.Length == 0) continue;

                if (line.StartsWith("@region", StringComparison.OrdinalIgnoreCase))
                {
                    currentRegion = line.Substring(7).Trim();
                    if (currentRegion.Length > 0 && !regions.ContainsKey(currentRegion))
                        regions[currentRegion] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                if (line.StartsWith("@end", StringComparison.OrdinalIgnoreCase))
                {
                    currentRegion = null;
                    continue;
                }

                if (line[0] == '#' || line[0] == ';') continue;
                if (currentRegion == null) continue;

                int idx = line.IndexOf(':');
                if (idx <= 0) continue;
                string key = line.Substring(0, idx).Trim();
                string value = line.Substring(idx + 1).Trim();

                Dictionary<string, string> dict;
                if (regions.TryGetValue(currentRegion, out dict))
                {
                    if (!dict.ContainsKey(key)) dict[key] = value;
                }
            }
            return regions;
        }

        /// <summary>One-entry parse memo: terminal control getters call
        /// ReadConfigValue many times per GUI frame for the same block, so the
        /// full CustomData parse only runs when the text actually changed
        /// (WriteConfigValue always installs a new string instance).</summary>
        static string _cfgCacheData;
        static Dictionary<string, Dictionary<string, string>> _cfgCacheParsed;

        static Dictionary<string, Dictionary<string, string>> ParseConfigRegionsCached(string data)
        {
            if (!ReferenceEquals(data, _cfgCacheData) &&
                !string.Equals(data, _cfgCacheData, StringComparison.Ordinal))
            {
                _cfgCacheParsed = ParseConfigRegions(data);
                _cfgCacheData = data;
            }
            return _cfgCacheParsed;
        }

        /// <summary>Reads an option for the given app region (the app's own
        /// region first, then DEFAULT), used by the terminal controls to show
        /// the effective value in the block's options menu.</summary>
        public static string ReadConfigValue(MyTerminalBlock tb, string region, string key)
        {
            if (tb == null || string.IsNullOrEmpty(key)) return null;
            var regions = ParseConfigRegionsCached(tb.CustomData ?? "");
            Dictionary<string, string> dict;
            string value;
            if (!string.IsNullOrEmpty(region) && regions.TryGetValue(region, out dict) && dict.TryGetValue(key, out value))
                return value;
            if (regions.TryGetValue("DEFAULT", out dict) && dict.TryGetValue(key, out value))
                return value;
            return null;
        }

        /// <summary>Writes an option into the given app region of CustomData,
        /// creating the "@region ... @end region" block when it does not exist
        /// yet. Existing options are replaced in place, new ones are inserted
        /// before the region's @end marker.</summary>
        public static void WriteConfigValue(MyTerminalBlock tb, string region, string key, string value)
        {
            if (tb == null || string.IsNullOrEmpty(region) || string.IsNullOrEmpty(key)) return;
            string data = tb.CustomData ?? "";

            // No-op guard: skip the split/rebuild (and the CustomData sync it
            // triggers in multiplayer) when the region already holds this value.
            // Terminal setters fire per GUI frame, mostly with unchanged values.
            var parsedRegions = ParseConfigRegionsCached(data);
            Dictionary<string, string> regionDict;
            string existing;
            if (parsedRegions.TryGetValue(region, out regionDict) &&
                regionDict.TryGetValue(key, out existing) &&
                string.Equals(existing, value, StringComparison.Ordinal))
                return;

            string[] lines = data.Split('\n');

            int regionStart = -1;
            int regionEnd = -1;
            string cur = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("@region", StringComparison.OrdinalIgnoreCase))
                {
                    cur = line.Substring(7).Trim();
                    if (regionStart < 0 && string.Equals(cur, region, StringComparison.OrdinalIgnoreCase))
                        regionStart = i;
                }
                else if (line.StartsWith("@end", StringComparison.OrdinalIgnoreCase))
                {
                    if (regionStart >= 0 && regionEnd < 0 && string.Equals(cur, region, StringComparison.OrdinalIgnoreCase))
                        regionEnd = i;
                    cur = null;
                }
            }

            string entry = key + ": " + value;
            if (regionStart < 0)
            {
                string block = "@region " + region + "\n" + entry + "\n@end region";
                tb.CustomData = data.TrimEnd().Length == 0 ? block : data.TrimEnd() + "\n\n" + block;
                return;
            }

            int insertAt = regionEnd >= 0 ? regionEnd : lines.Length;
            bool replaced = false;
            for (int i = regionStart + 1; i < insertAt; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t[0] == '#' || t[0] == ';') continue;
                int idx = t.IndexOf(':');
                if (idx <= 0) continue;
                if (string.Equals(t.Substring(0, idx).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = entry;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                var newLines = new List<string>(lines);
                newLines.Insert(insertAt, entry);
                lines = newLines.ToArray();
            }
            tb.CustomData = string.Join("\n", lines);
        }

        /// <summary>
        /// Looks up a configuration value, checking the app-specific region first,
        /// then the app class name region, and finally falling back to the DEFAULT region.
        /// </summary>
        bool TryGetConfigValue(string key, out string value)
        {
            Dictionary<string, string> appDict;
            if (AppRegionName != null && _regionValues.TryGetValue(AppRegionName, out appDict) && appDict.TryGetValue(key, out value))
                return true;
            if (_regionValues.TryGetValue(GetType().Name, out appDict) && appDict.TryGetValue(key, out value))
                return true;
            if (_regionValues.TryGetValue("DEFAULT", out appDict) && appDict.TryGetValue(key, out value))
                return true;

            value = null;
            return false;
        }

        protected bool GetSectionVisible(string key, bool fallback)
        {
            string v;
            if (TryGetConfigValue(key, out v))
            {
                bool b;
                if (bool.TryParse(v, out b)) return b;
                // also accept 0/1
                if (v == "0") return false;
                if (v == "1") return true;
            }
            return fallback;
        }

        /// <summary>
        /// Ensures the DEFAULT region has all known options and ensures all known app
        /// regions exist as empty @region ... @end region blocks.
        /// </summary>
        void EnsureConfigOptions(MyTerminalBlock tb)
        {
            string data = tb.CustomData ?? "";
            if (data.IndexOf("@region DEFAULT", StringComparison.OrdinalIgnoreCase) < 0)
            {
                tb.CustomData = (data.Length > 0 ? data.TrimEnd() + "\n\n" : "") + ConfigTemplate;
                return;
            }

            Dictionary<string, string> defaultDict;
            _regionValues.TryGetValue("DEFAULT", out defaultDict);

            string missingInDefault = "";
            for (int i = 0; i < DefaultOptionKeys.Length; i++)
            {
                string key = DefaultOptionKeys[i];
                if (defaultDict == null || !defaultDict.ContainsKey(key))
                {
                    missingInDefault += "\n" + key + ": " + DefaultOptionDefaults[i];
                }
            }

            bool modified = false;
            if (missingInDefault.Length > 0)
            {
                int defIdx = data.IndexOf("@region DEFAULT", StringComparison.OrdinalIgnoreCase);
                if (defIdx >= 0)
                {
                    int endIdx = data.IndexOf("@end", defIdx, StringComparison.OrdinalIgnoreCase);
                    if (endIdx >= 0)
                    {
                        data = data.Substring(0, endIdx).TrimEnd() + missingInDefault + "\n" + data.Substring(endIdx);
                        modified = true;
                    }
                }
            }

            for (int i = 0; i < KnownAppRegions.Length; i++)
            {
                string regionName = KnownAppRegions[i];
                if (data.IndexOf("@region " + regionName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    data = data.TrimEnd() + "\n\n@region " + regionName + "\n@end region";
                    modified = true;
                }
            }

            if (modified)
            {
                tb.CustomData = data;
                _lastCustomData = data;
            }
        }

        float[] ParseConfigPadding(string key, float[] fallback)
        {
            string value;
            if (!TryGetConfigValue(key, out value)) return (float[])fallback.Clone();
            if (string.IsNullOrWhiteSpace(value)) return (float[])fallback.Clone();

            string cleaned = value.Trim();
            if (cleaned.StartsWith("[") && cleaned.EndsWith("]"))
                cleaned = cleaned.Substring(1, cleaned.Length - 2);

            string[] parts = cleaned.Split(new char[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (float[])fallback.Clone();

            float[] parsed = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                float val;
                if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val))
                    return (float[])fallback.Clone();
                parsed[i] = Math.Max(0f, Math.Min(512f, val));
            }

            if (parsed.Length >= 4)
                return new float[] { parsed[0], parsed[1], parsed[2], parsed[3] };
            if (parsed.Length == 1)
                return new float[] { parsed[0], parsed[0], parsed[0], parsed[0] };
            if (parsed.Length == 2)
                return new float[] { parsed[0], parsed[0], parsed[1], parsed[1] };

            return (float[])fallback.Clone();
        }

        float ParseConfigFloat(string key, float fallback)
        {
            string value;
            if (!TryGetConfigValue(key, out value)) return fallback;
            float result;
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;
            return fallback;
        }

        bool ParseConfigBool(string key, bool fallback)
        {
            string value;
            if (!TryGetConfigValue(key, out value)) return fallback;
            bool result;
            if (bool.TryParse(value, out result)) return result;
            return fallback;
        }

        int ParseConfigStorageType(string key, int fallback)
        {
            string value;
            if (!TryGetConfigValue(key, out value)) return fallback;
            value = value.Trim();
            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("both", StringComparison.OrdinalIgnoreCase) || value.Equals("all", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (value.Equals("2", StringComparison.OrdinalIgnoreCase) || value.Equals("kg", StringComparison.OrdinalIgnoreCase) || value.Equals("mass", StringComparison.OrdinalIgnoreCase) || value.Equals("weight", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (value.Equals("3", StringComparison.OrdinalIgnoreCase) || value.Equals("l", StringComparison.OrdinalIgnoreCase) || value.Equals("liter", StringComparison.OrdinalIgnoreCase) || value.Equals("liters", StringComparison.OrdinalIgnoreCase) || value.Equals("vol", StringComparison.OrdinalIgnoreCase) || value.Equals("volume", StringComparison.OrdinalIgnoreCase))
                return 3;
            int result;
            if (int.TryParse(value, out result) && result >= 1 && result <= 3)
                return result;
            return fallback;
        }

        int ParseConfigOreIngotType(string key, int fallback)
        {
            string value;
            if (!TryGetConfigValue(key, out value)) return fallback;
            value = value.Trim();
            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("both", StringComparison.OrdinalIgnoreCase) || value.Equals("all", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (value.Equals("2", StringComparison.OrdinalIgnoreCase) || value.Equals("ores", StringComparison.OrdinalIgnoreCase) || value.Equals("ore", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (value.Equals("3", StringComparison.OrdinalIgnoreCase) || value.Equals("ingots", StringComparison.OrdinalIgnoreCase) || value.Equals("ingot", StringComparison.OrdinalIgnoreCase))
                return 3;
            int result;
            if (int.TryParse(value, out result) && result >= 1 && result <= 3)
                return result;
            return fallback;
        }

        /// <summary>Scroll position for the given list slot that overflows the screen
        /// (TextScroll). Returns the first index to draw; advances one row per
        /// update and wraps around. Always 0 when the whole list fits or
        /// scrolling is disabled. Apps with several list groups use one slot
        /// per group so each scrolls independently.</summary>
        protected int ScrollStart(int slot, int total, int maxRows)
        {
            if (!ConfigScroll || total <= maxRows)
            {
                ScrollPos[slot] = 0;
                ScrollShown[slot] = 0;
                return 0;
            }
            ScrollShown[slot] = ScrollPos[slot];
            ScrollPos[slot]++;
            if (ScrollPos[slot] > total - maxRows) ScrollPos[slot] = 0;
            return ScrollShown[slot];
        }

        /// <summary>Rows band height for each group when splitting a list area evenly
        /// between several groups (headers and gaps are accounted for).</summary>
        protected float ListGroupHeight(float totalHeight, int groups, float headerH, float gap)
        {
            if (groups <= 0) return 0f;
            return (totalHeight - groups * headerH - (groups - 1) * gap) / groups;
        }

        /// <summary>Top of group i in an evenly split list area.</summary>
        protected float ListGroupTop(float areaTop, int i, float groupH, float headerH, float gap)
        {
            return areaTop + i * (groupH + headerH + gap);
        }

        /// <summary>
        /// Draws one list group: optional header, its scrolled rows and its own
        /// scroll bar. drawRow(index, y) renders the row for the given index at y.
        /// Returns how many rows were drawn.
        /// </summary>
        protected int DrawListGroup(MySpriteDrawFrame frame, int slot, string header, int total,
            float groupTop, float headerH, float groupH, float rowH, Action<int, float> drawRow)
        {
            float rowsTop = groupTop;
            if (header != null && header.Length > 0)
            {
                AddText(frame, header, new Vector2(Left, groupTop), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                rowsTop = groupTop + headerH;
            }
            int maxRows = Math.Max(0, (int)(groupH / rowH));
            int start = ScrollStart(slot, total, maxRows);
            int drawn = 0;
            for (int i = start; i < total && drawn < maxRows; i++)
                drawRow(i, rowsTop + drawn++ * rowH);
            DrawScrollBar(frame, slot, total, maxRows, rowsTop, rowsTop + groupH);
            return drawn;
        }

        /// <summary>Draws a small scroll bar on the right showing how far the
        /// given list slot has scrolled. No-op when scrolling is off or the
        /// whole list fits.</summary>
        protected void DrawScrollBar(MySpriteDrawFrame frame, int slot, int total, int maxRows, float trackTop, float trackBottom)
        {
            if (!ConfigScroll || total <= maxRows || maxRows <= 0 || trackBottom <= trackTop) return;

            float x = Math.Min(Right + 4f * S, m_size.X - 8f * S);
            float trackH = trackBottom - trackTop;
            float thumbH = Math.Max(8f * S, trackH * maxRows / total);
            float frac = (float)ScrollShown[slot] / (total - maxRows);
            float thumbY = trackTop + (trackH - thumbH) * frac;

            frame.Add(Square("SquareSimple", new Vector2(x, (trackTop + trackBottom) / 2f), new Vector2(1.5f * S, trackH), new Color(60, 70, 85)));
            frame.Add(Square("SquareSimple", new Vector2(x, thumbY + thumbH / 2f), new Vector2(3.5f * S, thumbH), new Color(150, 160, 175)));
        }

        /// <summary>Applies TextPadding ([UP, DOWN, LEFT, RIGHT]) and TextScale to the layout. TextScale is folded
        /// into S so every element (text, icons, bars and spacing) scales and shifts
        /// together. Top/Bottom are in content space: everything drawn via
        /// AddText/Square is shifted down by Top, so the visible bottom edge of
        /// content = Bottom + Top.</summary>
        void ApplyLayout()
        {
            S = Math.Max(0.75f, Math.Min(m_scale.X, m_scale.Y)) * TextScale;
            float padUp = TextPadding != null && TextPadding.Length > 0 ? TextPadding[0] : 16f;
            float padDown = TextPadding != null && TextPadding.Length > 1 ? TextPadding[1] : 16f;
            float padLeft = TextPadding != null && TextPadding.Length > 2 ? TextPadding[2] : 16f;
            float padRight = TextPadding != null && TextPadding.Length > 3 ? TextPadding[3] : 16f;

            Mx = padLeft * S;
            Top = padUp * S;
            Bottom = m_size.Y - (padUp + padDown) * S;
            Left = padLeft * S;
            Right = m_size.X - padRight * S;
            Cx = (Left + Right) / 2f;
        }

        /// <summary>Fills outIds with the entity ids of every block in the
        /// configured Groups. Returns true when Groups are configured and the
        /// lookup ran - an empty set then means no block matched, so callers
        /// must treat it as "allow nothing", never fall back to whole grid.
        /// Returns false when no Groups are set (whole-grid mode).</summary>
        protected bool TryBuildGroupFilter(HashSet<long> outIds)
        {
            outIds.Clear();
            if (ConfigGroups.Count == 0) return false;
            if (MyAPIGateway.TerminalActionsHelper == null) return false;

            var grid = CurrentScanGrid;
            IMyGridTerminalSystem ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (ts == null) return false;

            _blockGroups.Clear();
            ts.GetBlockGroups(_blockGroups);

            for (int i = 0; i < _blockGroups.Count; i++)
            {
                IMyBlockGroup g = _blockGroups[i];
                if (g == null) continue;
                bool match = false;
                for (int k = 0; k < ConfigGroups.Count; k++)
                {
                    if (string.Equals(g.Name, ConfigGroups[k], StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }
                if (!match) continue;

                _groupBlocks.Clear();
                g.GetBlocks(_groupBlocks);
                for (int k = 0; k < _groupBlocks.Count; k++)
                {
                    if (_groupBlocks[k] != null) outIds.Add(_groupBlocks[k].EntityId);
                }
            }
            return true;
        }

        /// <summary>Keeps only blocks that belong to one of the configured groups.
        /// Blocks can only be in a terminal group when they have a fat block.</summary>
        void ApplyGroupFilter()
        {
            if (!TryBuildGroupFilter(_groupIds)) return;

            if (_groupIds.Count == 0)
            {
                GridBlocks.Clear();
                TerminalBlocks.Clear();
                return;
            }
            // Predicate<T> itself is on the script whitelist's prohibited
            // list (even though List.RemoveAll is allowed), so the filter
            // is a manual compaction pass instead of RemoveAll(predicate) -
            // one write index plus a single RemoveRange, not reverse RemoveAt.
            int w = 0;
            for (int i = 0; i < GridBlocks.Count; i++)
            {
                var fb = GridBlocks[i].FatBlock;
                if (fb != null && _groupIds.Contains(fb.EntityId)) GridBlocks[w++] = GridBlocks[i];
            }
            if (w < GridBlocks.Count) GridBlocks.RemoveRange(w, GridBlocks.Count - w);
            w = 0;
            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                if (_groupIds.Contains(TerminalBlocks[i].EntityId)) TerminalBlocks[w++] = TerminalBlocks[i];
            }
            if (w < TerminalBlocks.Count) TerminalBlocks.RemoveRange(w, TerminalBlocks.Count - w);
        }

        protected static string BlockName(VRage.Game.ModAPI.IMyCubeBlock block)
        {
            MyTerminalBlock tb = block as MyTerminalBlock;
            return tb != null ? tb.CustomName : "";
        }

        protected void DrawBackground(MySpriteDrawFrame frame)
        {
            frame.Add(Square("SquareSimple", new Vector2(Cx, m_size.Y / 2f), m_size, BgColor, false));
        }

        protected void DrawHeader(MySpriteDrawFrame frame, string title, string subtitle, string icon, Color iconColor)
        {
            float iconOffset = Math.Max(90f, title.Length * 7.5f) * S;
            frame.Add(Icon(icon, new Vector2(Cx - iconOffset, 18f * S), 22f * S, iconColor));
            AddText(frame, title, new Vector2(Cx, 8f * S), 0.85f * S, FgColor, TextAlignment.CENTER);
            AddText(frame, subtitle, new Vector2(Cx, 34f * S), 0.48f * S, new Color(140, 145, 155), TextAlignment.CENTER);
        }

        /// <summary>Standard app screen start: opens the frame, draws the
        /// background and the title header. Keep the returned frame in a
        /// using block - Dispose ends the frame.</summary>
        protected MySpriteDrawFrame BeginAppFrame(string title, string subtitle, string icon, Color iconColor)
        {
            var frame = Surface.DrawFrame();
            DrawBackground(frame);
            DrawHeader(frame, title, subtitle, icon, iconColor);
            return frame;
        }

        /// <summary>Shows the standard "REMOTE GRID NOT FOUND" state when the
        /// scan result is null. Returns true when the caller should return.</summary>
        protected bool GuardRemoteGrid(MySpriteDrawFrame frame, object scan)
        {
            if (scan != null) return false;
            DrawEmpty(frame, "REMOTE GRID NOT FOUND");
            return true;
        }

        /// <summary>Standard summary row: "LABEL (count)" left, value right, a
        /// fill bar underneath. Used by the breakdown sections.</summary>
        protected void DrawCategoryRow(MySpriteDrawFrame frame, string label, string icon, int count, string valueText, float ratio, Color color, float y)
        {
            frame.Add(Icon(icon, new Vector2(Left + 10f * S, y + 7f * S), 18f * S, new Color(200, 210, 225)));
            AddText(frame, $"{label} ({count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)})", new Vector2(Left + 26f * S, y), 0.46f * S, count > 0 ? FgColor : new Color(110, 115, 125), TextAlignment.LEFT);
            AddText(frame, valueText, new Vector2(Right, y), 0.46f * S, color, TextAlignment.RIGHT);

            RectangleF bar = new RectangleF(new Vector2(Left, y + 15f * S), new Vector2(Right - Left, 5f * S));
            DrawBar(frame, bar, ratio, color);
        }

        /// <summary>Standard list row: icon left, label and value on the top
        /// line, thin fill bar underneath. hasStock=false dims everything and
        /// darkens the bar (used for zero-stock entries). All apps use this
        /// layout so coordinates stay consistent.</summary>
        protected void DrawProgressRow(MySpriteDrawFrame frame, float y, string icon, string label, string value,
            float ratio, Color barColor, bool hasStock = true, Color? textColor = null, Color? valueColor = null)
        {
            Color iconColor = hasStock ? new Color(200, 210, 225) : new Color(100, 105, 115);
            Color labelColor = textColor ?? (hasStock ? FgColor : new Color(110, 115, 125));
            Color valColor = valueColor ?? (hasStock ? new Color(170, 175, 185) : new Color(110, 115, 125));
            Color fillColor = hasStock ? barColor : new Color(60, 70, 85);

            frame.Add(Icon(icon, new Vector2(Left + 10f * S, y + 5f * S), 16f * S, iconColor));
            AddText(frame, label, new Vector2(Left + 24f * S, y), 0.44f * S, labelColor, TextAlignment.LEFT);
            AddText(frame, value, new Vector2(Right, y), 0.44f * S, valColor, TextAlignment.RIGHT);

            RectangleF bar = new RectangleF(new Vector2(Left + 24f * S, y + 16f * S), new Vector2(Right - Left - 24f * S, 4f * S));
            DrawBar(frame, bar, ratio, fillColor);
        }

        protected void DrawDivider(MySpriteDrawFrame frame, float y)
        {
            frame.Add(Square("SquareSimple", new Vector2(Cx, y * S), new Vector2(Right - Left, 1.5f * S), new Color(60, 70, 85)));
        }

        protected void DrawBar(MySpriteDrawFrame frame, RectangleF bar, float ratio, Color color)
        {
            ratio = Math.Max(0f, Math.Min(1f, ratio));

            frame.Add(Square("SquareSimple", bar.Center, bar.Size, new Color(22, 26, 36)));

            Vector2 fillSize = new Vector2(bar.Size.X * ratio, bar.Size.Y);
            if (fillSize.X > 0.5f)
                frame.Add(Square("SquareSimple", new Vector2(bar.X + fillSize.X / 2f, bar.Center.Y), fillSize, color));

            frame.Add(Square("SquareHollow", bar.Center, bar.Size, new Color(80, 90, 105)));
        }

        protected void DrawCenterFlowBar(MySpriteDrawFrame frame, RectangleF bar, float netMW, float maxMW)
        {
            frame.Add(Square("SquareSimple", bar.Center, bar.Size, new Color(22, 26, 36)));

            if (maxMW > 0.0001f)
            {
                float halfWidth = bar.Size.X / 2f;
                float ratio = Math.Max(-1f, Math.Min(1f, netMW / maxMW));

                if (Math.Abs(ratio) > 0.001f)
                {
                    float fillWidth = Math.Abs(ratio) * halfWidth * 0.5f;
                    Color color;
                    Vector2 fillCenter;
                    if (ratio < 0f)
                    {
                        color = new Color(230, 60, 50);
                        fillCenter = new Vector2(bar.Center.X - fillWidth / 2f, bar.Center.Y);
                    }
                    else
                    {
                        color = new Color(50, 210, 90);
                        fillCenter = new Vector2(bar.Center.X + fillWidth / 2f, bar.Center.Y);
                    }
                    // 2px black border - 0.5x smaller
                    Vector2 borderSize = new Vector2(fillWidth + 4f * S, bar.Size.Y * 0.5f + 4f * S);
                    Vector2 fillSize = new Vector2(fillWidth, bar.Size.Y * 0.5f);
                    frame.Add(Square("SquareSimple", fillCenter, borderSize, new Color(0, 0, 0)));
                    frame.Add(Square("SquareSimple", fillCenter, fillSize, color));
                }
            }
            // white center tick on top
            frame.Add(Square("SquareSimple", new Vector2(bar.Center.X, bar.Center.Y), new Vector2(2f, bar.Size.Y), new Color(220, 230, 240)));

            frame.Add(Square("SquareHollow", bar.Center, bar.Size, new Color(80, 90, 105)));
        }

        // Combined bar reused via CombinedBar class - percentage (R->Y->G) + net on top, white same height as net and never removed
        protected void DrawCombinedBar(MySpriteDrawFrame frame, RectangleF bar, float storageRatio, Color storageColor, float netFlow, float maxFlow)
        {
            CombinedBar.Draw(frame, bar, storageRatio, storageColor, netFlow, maxFlow, S, Top);
        }

        protected void AddText(MySpriteDrawFrame frame, string text, Vector2 position, float size, Color color, TextAlignment alignment)
        {
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(position.X, position.Y + Top),
                RotationOrScale = size,
                Color = color,
                Alignment = alignment,
                FontId = "White"
            });
        }

        static readonly Dictionary<string, float> TextWidthCache = new Dictionary<string, float>();
        static readonly StringBuilder MeasureBuffer = new StringBuilder(64);

        /// <summary>Exact pixel width of text rendered like AddText (White font, given scale),
        /// via the same measurement the game uses for its own surface scripts.</summary>
        protected float MeasureTextWidth(string text, float size)
        {
            string key = size.ToString("0.####") + "|" + text;
            float w;
            if (TextWidthCache.TryGetValue(key, out w)) return w;
            MeasureBuffer.Clear();
            MeasureBuffer.Append(text);
            w = Surface.MeasureStringInPixels(MeasureBuffer, "White", size).X;
            if (TextWidthCache.Count > 256) TextWidthCache.Clear();
            TextWidthCache[key] = w;
            return w;
        }

        /// <summary>Right-aligned "MAX IN {t} / OUT {t}": MAX white, IN green,
        /// OUT red, positioned by exact measured widths.</summary>
        protected void DrawMaxInOut(MySpriteDrawFrame frame, float y, string inTime, string outTime)
        {
            float size = 0.36f * S;
            string maxStr = "MAX";
            string inStr = "IN " + inTime;
            string slash = "/";
            string outStr = "OUT " + outTime;
            float gap = 4f * S;
            float maxW = MeasureTextWidth(maxStr, size);
            float inW = MeasureTextWidth(inStr, size);
            float slashW = MeasureTextWidth(slash, size);
            float outW = MeasureTextWidth(outStr, size);
            float startX = Right - (maxW + inW + slashW + outW + 3f * gap);
            if (startX < Left + 140f * S) startX = Left + 140f * S;
            float x = startX;
            AddText(frame, maxStr, new Vector2(x, y), size, FgColor, TextAlignment.LEFT); x += maxW + gap;
            AddText(frame, inStr, new Vector2(x, y), size, new Color(50, 210, 90), TextAlignment.LEFT); x += inW + gap;
            AddText(frame, slash, new Vector2(x, y), size, new Color(130, 135, 145), TextAlignment.LEFT); x += slashW + gap;
            AddText(frame, outStr, new Vector2(x, y), size, new Color(230, 60, 50), TextAlignment.LEFT);
        }

        protected void DrawEmpty(MySpriteDrawFrame frame, string message)
        {
            AddText(frame, message, new Vector2(Cx, m_size.Y / 2f - Top), 0.55f * S, FgColor, TextAlignment.CENTER);
        }

        /// <summary>Draws an overflow indicator ("+N MORE") just above the bottom padding.</summary>
        protected void DrawMore(MySpriteDrawFrame frame, string text)
        {
            AddText(frame, text, new Vector2(Cx, Bottom - 18f * S), 0.46f * S, new Color(140, 145, 155), TextAlignment.CENTER);
        }

        /// <summary>Adds a sprite, shifted down by the top padding. Layout sprites
        /// (background) can opt out via shiftY.</summary>
        protected MySprite Square(string spriteId, Vector2 position, Vector2 size, Color color, bool shiftY = true)
        {
            return new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = spriteId,
                Position = shiftY ? new Vector2(position.X, position.Y + Top) : position,
                Size = size,
                Color = color,
                Alignment = TextAlignment.CENTER
            };
        }

        /// <summary>Adds an icon sprite (size already includes the scaled S).</summary>
        protected MySprite Icon(string spriteId, Vector2 position, float size, Color color)
        {
            return Square(spriteId, position, new Vector2(size), color);
        }

        protected static Color BarColor(float ratio)
        {
            if (ratio > 0.5f) return new Color(60, 200, 90);
            if (ratio > 0.25f) return new Color(230, 200, 60);
            return new Color(220, 70, 60);
        }

        protected static string FormatVolume(float liters)
        {
            if (liters >= 1000000f) return $"{liters / 1000000f:0.00} ML";
            if (liters >= 1000f) return $"{liters / 1000f:0.0} kL";
            return $"{liters:#,0} L";
        }

        protected static string FormatMass(float kg)
        {
            return $"{kg:#,0} kg";
        }

        protected static string FormatStorage(float liters, float kg, int storageType)
        {
            switch (storageType)
            {
                case 2:
                    return FormatMass(kg);
                case 3:
                    return FormatVolume(liters);
                case 1:
                default:
                    return $"{FormatMass(kg)} / {FormatVolume(liters)}";
            }
        }

        protected string FormatStorage(float liters, float kg)
        {
            return FormatStorage(liters, kg, ConfigStorageType);
        }

        protected static string Truncate(string name, int max)
        {
            if (name.Length > max) name = name.Substring(0, max);
            return name;
        }

        /// <summary>
        /// Returns a guaranteed-to-render icon for the given block: the sprite of
        /// its primary component (e.g. armor -> SteelPlate, reactor -> Reactor).
        /// Block definition icons are texture paths that do not render on text
        /// surfaces, so only sprite library entries are used here.
        /// </summary>
        protected static string BlockIcon(MySlimBlock slim, string fallback)
        {
            Sandbox.Definitions.MyCubeBlockDefinition def = slim.BlockDefinition as Sandbox.Definitions.MyCubeBlockDefinition;
            if (def != null && def.Components != null && def.Components.Length > 0)
            {
                string subtype = def.Components[0].Definition.Id.SubtypeId.ToString();
                if (SpriteLookup.ComponentSet.Contains(subtype))
                    return "MyObjectBuilder_Component/" + subtype;
            }
            return fallback;
        }

        /// <summary>Formats a duration in hours as "Xh YYm" (or "--" when not meaningful).</summary>
        protected static string FormatTimeHours(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours) || hours < 0f || hours >= 999f) return "--";
            return $"{Math.Floor(hours):0}h {((hours - Math.Floor(hours)) * 60f):00}m";
        }

        /// <summary>Compact duration: "3h12m" (or "45m" under an hour, or "--"
        /// when not meaningful).</summary>
        protected static string FormatEta(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours) || hours < 0f || hours >= 100f) return "--";
            int h = (int)hours;
            int m = (int)((hours - h) * 60f);
            if (h < 1) return m + "m";
            return h + "h" + m.ToString("00") + "m";
        }

        /// <summary>Subtype string to a display name: underscores to spaces,
        /// length clamped to the standard row width.</summary>
        protected static string FormatItemName(string subtype)
        {
            string name = subtype.Replace('_', ' ');
            if (name.Length > 24) name = name.Substring(0, 24);
            return name;
        }

        /// <summary>Zero-fills a subtype-keyed dictionary with every known
        /// subtype that's missing, so FullList mode shows all types.</summary>
        protected static void EnsureFullListEntries(Dictionary<string, int> target, List<string> known)
        {
            for (int i = 0; i < known.Count; i++)
            {
                string subtype = known[i];
                if (!target.ContainsKey(subtype)) target[subtype] = 0;
            }
        }

        /// <summary>Aggregated battery power state, shared by PowerApp and
        /// DockedApp.</summary>
        protected struct BatterySummary
        {
            public float Stored, Max, In, Out, MaxOut;

            public float NetFlow { get { return In - Out; } }
        }

        /// <summary>Adds one battery's power state to the summary.</summary>
        protected static void AccumulateBattery(ref BatterySummary s, Sandbox.ModAPI.IMyBatteryBlock b)
        {
            s.Stored += (float)b.CurrentStoredPower;
            s.Max += (float)b.MaxStoredPower;
            s.In += (float)b.CurrentInput;
            s.Out += (float)b.CurrentOutput;
            s.MaxOut += (float)b.MaxOutput;
        }

        /// <summary>Per-unit volume/mass and category for one item type. The
        /// definition lookup and the category string compares only run on the
        /// first sighting of a type; the hot per-item path is a single
        /// struct-keyed dictionary probe with no string work at all.</summary>
        public struct ItemStats
        {
            public float Volume;
            public float Mass;
            public byte Category;
        }

        public const byte CatOre = 0;
        public const byte CatIngot = 1;
        public const byte CatComponent = 2;
        public const byte CatAmmo = 3;
        public const byte CatTool = 4;
        public const byte CatOther = 5;

        static readonly Dictionary<VRage.Game.ModAPI.Ingame.MyItemType, ItemStats> _itemStatsCache =
            new Dictionary<VRage.Game.ModAPI.Ingame.MyItemType, ItemStats>();

        protected static ItemStats GetItemStats(VRage.Game.ModAPI.Ingame.MyItemType type)
        {
            ItemStats stats;
            if (_itemStatsCache.TryGetValue(type, out stats)) return stats;
            try
            {
                var info = type.GetItemInfo();
                stats.Volume = info.Volume;
                stats.Mass = info.Mass;
            }
            catch
            {
                stats.Volume = 0f;
                stats.Mass = 0f;
            }
            string typeId = type.TypeId;
            if (typeId == "MyObjectBuilder_Ore") stats.Category = CatOre;
            else if (typeId == "MyObjectBuilder_Ingot") stats.Category = CatIngot;
            else if (typeId == "MyObjectBuilder_Component") stats.Category = CatComponent;
            else if (typeId == "MyObjectBuilder_AmmoMagazine") stats.Category = CatAmmo;
            else if (typeId == "MyObjectBuilder_PhysicalGunObject"
                || typeId == "MyObjectBuilder_OxygenContainerObject"
                || typeId == "MyObjectBuilder_GasContainerObject") stats.Category = CatTool;
            else stats.Category = CatOther;
            _itemStatsCache[type] = stats;
            return stats;
        }

        static readonly Dictionary<string, bool> _gasTypeCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>True when the given gas tank block is a hydrogen tank
        /// (else it is treated as an oxygen tank). BlockDefinition.SubtypeId
        /// is already a string, so no MyStringHash round-trip is needed -
        /// the substring check runs once per subtype and is cached.</summary>
        protected static bool IsHydrogenTank(Sandbox.ModAPI.IMyGasTank tank)
        {
            string subtype = tank.BlockDefinition.SubtypeId;
            bool isH2;
            if (!_gasTypeCache.TryGetValue(subtype, out isH2))
            {
                isH2 = subtype.IndexOf("Hydrogen", StringComparison.Ordinal) >= 0;
                _gasTypeCache[subtype] = isH2;
            }
            return isH2;
        }

        /// <summary>
        /// Returns the scan result for this display's scan grid, shared by ALL
        /// displays of the same app type on that grid: the expensive
        /// block/inventory pass runs once per grid per update window
        /// (~1.67 s) instead of once per display. Displays with different
        /// configs (RemoteGrid, Groups, SubGrids, FullList) get their own
        /// cache entries. Returns default(T) when a configured RemoteGrid
        /// can't be found - check for null in the app and show an error.
        /// scan() must only gather data, never draw.
        /// </summary>
        protected T GetGridScan<T>(Func<T> scan) where T : class, new()
        {
            long window = Window();
            _scanGrid = ResolveScanGrid(window);
            if (_scanGrid == null) return default(T);

            var grid = _scanGrid;
            long key = grid.EntityId;
            if (ConfigSubGrids)
            {
                key = GetMechanicalGroupKey(grid);
            }
            key = key * 397 + (ConfigSubGrids ? 1 : 0);
            key = key * 397 + (ConfigRemoteName.Length > 0 ? 1 : 0);
            key = key * 397 + (ConfigFullList ? 1 : 0);
            key = key * 397 + ConfigStorageType;
            if (ConfigGroups.Count > 0)
            {
                // No intermediate join string: fold the group names into the
                // key directly.
                int h = 0;
                for (int i = 0; i < ConfigGroups.Count; i++)
                    h = h * 397 + ConfigGroups[i].GetHashCode();
                key = key * 397 + h;
            }

            var map = ScanCache<T>.Map;
            CacheEntry<T> entry;
            if (map.TryGetValue(key, out entry))
            {
                if (entry.Window == window)
                    return entry.Data;
                // Stale entry for this key: recycle its container and entry
                // BEFORE scanning, so the scan's RentScan reuses them instead
                // of allocating fresh objects every window. Safe because all
                // consumers refetch through GetGridScan at the start of each
                // Run - no reference to the old window's data survives.
                ScanCache<T>.Return(entry.Data);
                ScanCache<T>.ReturnEntry(entry);
                map.Remove(key);
            }

            long t0 = Stopwatch.GetTimestamp();
            T data = scan();
            double scanMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            if (Perf.LivePerfApps > 0)
                Perf.RecordScan(_perfAppName ?? GetType().Name, scanMs, _lastScanBlocks);
            map[key] = ScanCache<T>.RentEntry(window, data);

            if (map.Count > 64 && (++ScanCache<T>.EvictCounter & 7) == 0)
            {
                var stale = ScanCache<T>.Stale;
                stale.Clear();
                foreach (var kv in map)
                    if (kv.Value.Window != window) stale.Add(kv.Key);
                for (int i = 0; i < stale.Count; i++)
                {
                    CacheEntry<T> ev;
                    if (map.TryGetValue(stale[i], out ev))
                    {
                        ScanCache<T>.Return(ev.Data);
                        ScanCache<T>.ReturnEntry(ev);
                        map.Remove(stale[i]);
                    }
                }
                stale.Clear();
            }
            return data;
        }

        /// <summary>Returns a cleared scan container from the per-app-type pool,
        /// or a fresh one. Apps must fill every field of the returned object.</summary>
        protected T RentScan<T>() where T : class, new()
        {
            return ScanCache<T>.Rent();
        }

        /// <summary>EntityId of the grid the current scan targets (remote grid
        /// when configured, else the display's own grid). When SubGrids is
        /// enabled the mechanical group is scanned, so the id is the group's
        /// representative (shared by all LCDs on that ship and its subgrids)
        /// so highlights and cache entries are shared; otherwise it is the
        /// single grid's id. 0 when unknown.</summary>
        protected long ScanGridId
        {
            get
            {
                var g = _scanGrid ?? (Block != null ? (VRage.Game.ModAPI.IMyCubeGrid)Block.CubeGrid : null);
                if (g == null) return 0;
                if (ConfigSubGrids) return GetMechanicalGroupKey(g);
                return g.EntityId;
            }
        }

        long GetMechanicalGroupKey(VRage.Game.ModAPI.IMyCubeGrid grid)
        {
            if (grid == null) return 0;
            if (MyAPIGateway.GridGroups == null)
            {
                while (grid.Parent != null) grid = (VRage.Game.ModAPI.IMyCubeGrid)grid.Parent;
                return grid.EntityId;
            }
            // Use a temporary buffer so we don't clobber the instance _subGrids
            // that RefreshGridBlocks also uses (GetGridScan runs just before the
            // scan's RefreshGridBlocks, which will repopulate _subGrids anyway).
            var buf = _scanGroupBuffer;
            buf.Clear();
            MyAPIGateway.GridGroups.GetGroup(grid, VRage.Game.ModAPI.GridLinkTypeEnum.Physical, buf);
            if (buf.Count == 0) return grid.EntityId;
            long min = buf[0].EntityId;
            for (int i = 1; i < buf.Count; i++)
            {
                var e = buf[i];
                if (e != null && e.EntityId < min) min = e.EntityId;
            }
            return min;
        }

        static readonly List<VRage.Game.ModAPI.IMyCubeGrid> _scanGroupBuffer = new List<VRage.Game.ModAPI.IMyCubeGrid>(8);

        /// <summary>The grid the current scan targets: the remote grid when
        /// configured, else the LCD's own grid. Named CurrentScanGrid so it
        /// can't collide with the apps' own ScanGrid() scan methods.</summary>
        protected VRage.Game.ModAPI.IMyCubeGrid CurrentScanGrid
        {
            get { return _scanGrid ?? (VRage.Game.ModAPI.IMyCubeGrid)Block.CubeGrid; }
        }

        /// <summary>Returns the grid to scan: the remote grid when RemoteGrid is
        /// configured, otherwise the LCD's own grid. Null when the remote grid
        /// can't be found.</summary>
        VRage.Game.ModAPI.IMyCubeGrid ResolveScanGrid(long window)
        {
            if (ConfigRemoteName.Length == 0)
                return (VRage.Game.ModAPI.IMyCubeGrid)Block.CubeGrid;

            long id = ResolveRemoteGridId(ConfigRemoteName, window);
            if (id == 0) return null;
            var entity = MyAPIGateway.Entities.GetEntityById(id);
            return entity as VRage.Game.ModAPI.IMyCubeGrid;
        }

        /// <summary>Finds the EntityId of the grid with the given name. The
        /// result is cached per name across windows - the expensive global
        /// entity scan only runs when the configured name changes, when a
        /// previous lookup failed (retried periodically), or on a slow
        /// periodic refresh. 0 means not found.</summary>
        long ResolveRemoteGridId(string name, long window)
        {
            const long RefreshEvery = 120;
            const long RetryEvery = 40;

            RemoteLookup hit;
            bool cached = _remoteLookup.TryGetValue(name, out hit);
            if (cached && hit.Window == window)
                return hit.EntityId;
            if (cached)
            {
                long since = window - hit.Window;
                if (hit.EntityId != 0 && since < RefreshEvery) return hit.EntityId;
                if (hit.EntityId == 0 && since < RetryEvery) return 0;
            }

            long id = 0;
            if (MyAPIGateway.Entities != null)
            {
                _entityBuffer.Clear();
                MyAPIGateway.Entities.GetEntities(_entityBuffer, e => e is VRage.Game.ModAPI.IMyCubeGrid);
                foreach (var entity in _entityBuffer)
                {
                    var grid = entity as VRage.Game.ModAPI.IMyCubeGrid;
                    if (grid == null) continue;
                    if (string.Equals(grid.CustomName, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(grid.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        id = grid.EntityId;
                        break;
                    }
                }
            }
            _remoteLookup[name] = new RemoteLookup { Window = window, EntityId = id };
            return id;
        }
    }

    /// <summary>Resolved EntityId of a named remote grid for one window.</summary>
    class RemoteLookup
    {
        public long Window;
        public long EntityId;
    }

    /// <summary>Per-app update timing stats, shared by all instances of an app
    /// type. Collects averages, extremes, histograms, update-interval jitter,
    /// scan timing and scanned block counts.</summary>
    public class PerfStat
    {
        public int Count, Scans;
        public double SumMs, MaxMs;
        public double MinMs = double.MaxValue;
        public double ScanSumMs, ScanMaxMs;
        public double ScanMinMs = double.MaxValue;
        public double LastScanMs;
        public double BlocksSum, PerBlockSum, PerBlockMax;
        public double IntervalSumMs, IntervalMaxMs;
        public int IntervalCount;
        public readonly int[] Hist = new int[Perf.UpdateBuckets];
        public readonly int[] ScanHist = new int[Perf.ScanBuckets];

        // Engine-call diagnostics: how often the engine invokes Run() vs how
        // many calls pass the slot gate (Count). InvokeGapAvgTicks is the
        // average sim-tick gap between consecutive engine calls - 10 means
        // every 10th tick, large values mean the engine skipped calls
        // (render range / out of view).
        public int Invoked;
        public int LastInvokeTick = -1;
        public long InvokeGapSum;
        public int InvokeGapCount;
        public double InvokeGapAvgTicks { get { return InvokeGapCount > 0 ? InvokeGapSum / (double)InvokeGapCount : 0.0; } }

        public double AvgMs { get { return Count > 0 ? SumMs / Count : 0.0; } }
        public double ScanAvgMs { get { return Scans > 0 ? ScanSumMs / Scans : 0.0; } }
        public double IntervalAvgMs { get { return IntervalCount > 0 ? IntervalSumMs / IntervalCount : 0.0; } }
        public double AvgBlocks { get { return Scans > 0 ? BlocksSum / Scans : 0.0; } }
        public double PerBlockAvgMs { get { return Scans > 0 ? PerBlockSum / Scans : 0.0; } }
    }

    /// <summary>Per-display timing stats (one entry per LCD/block). The update
    /// interval is measured here - it is a per-instance quantity.</summary>
    public class InstanceStat
    {
        public int Count;
        public double SumMs, MaxMs;
        public double MinMs = double.MaxValue;
        public double LastUpdateMs;
        public double IntervalSumMs, IntervalMaxMs;
        public int IntervalCount;
        public double AvgMs { get { return Count > 0 ? SumMs / Count : 0.0; } }
        public double IntervalAvgMs { get { return IntervalCount > 0 ? IntervalSumMs / IntervalCount : 0.0; } }
    }

    /// <summary>One recorded slow update (>= Perf.SlowMs).</summary>
    public class SlowEvent
    {
        public string App, Instance;
        public double Ms, ScanMs;
        public string PlayTime;
    }

    /// <summary>Update timing collector. Every display update is measured;
    /// the Performance app shows the stats live and dumps them as text.</summary>
    public static class Perf
    {
        public const int UpdateBuckets = 10;
        public const int ScanBuckets = 8;
        public static readonly double[] UpdateBounds = { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0, 64.0 };
        public static readonly double[] ScanBounds = { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0 };
        public const double SlowMs = 2.0;
        public const int SlowCap = 50;

        public static readonly Dictionary<string, PerfStat> Stats = new Dictionary<string, PerfStat>();
        public static readonly Dictionary<string, InstanceStat> Instances = new Dictionary<string, InstanceStat>();
        public static readonly List<SlowEvent> SlowEvents = new List<SlowEvent>();

        /// <summary>Instances entries are keyed by app+CustomName, so renamed
        /// or removed displays leave dead keys behind. Periodically drop
        /// entries that stopped updating so the map stays bounded.</summary>
        const double InstanceStaleMs = 300000.0;
        static int _recordCounter;
        static readonly List<string> _staleInstances = new List<string>();

        /// <summary>Number of live PerfApp displays. Recording is skipped
        /// entirely while this is zero - there is no consumer for the data
        /// and the per-update dictionary writes would be pure overhead.</summary>
        public static int LivePerfApps;

        /// <summary>Playtime baseline in ms. The PLAYTIME shown in the dumps
        /// is the session time elapsed since the last Perf.Clear() (the
        /// PerfLog toggle) - not the whole session - so toggling the
        /// advanced info off and on restarts the clock with the stats.</summary>
        public static double StartPlayMs = -1;

        /// <summary>Session playtime since the last reset, for the dump
        /// headers. Falls back to the absolute session time when no reset
        /// happened yet.</summary>
        public static TimeSpan ElapsedSinceStart()
        {
            double now = MyAPIGateway.Session != null ? MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds : 0;
            double ms = StartPlayMs > 0 ? now - StartPlayMs : now;
            if (ms < 0) ms = 0;
            return TimeSpan.FromMilliseconds(ms);
        }

        public static void Clear()
        {
            Stats.Clear();
            Instances.Clear();
            SlowEvents.Clear();
            StartPlayMs = MyAPIGateway.Session != null ? MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds : -1;
        }

        /// <summary>Counts an engine Run() invocation for an app (called
        /// before the slot gate) and tracks the tick gap between calls, so
        /// PerfApp can show how often the engine calls vs how many calls
        /// actually do work.</summary>
        public static void CountInvocation(string app, int tick)
        {
            PerfStat s = Stat(app);
            s.Invoked++;
            if (s.LastInvokeTick >= 0)
            {
                s.InvokeGapSum += tick - s.LastInvokeTick;
                s.InvokeGapCount++;
            }
            s.LastInvokeTick = tick;
        }

        public static void Record(string app, string instance, double ms, double playMs)
        {
            if (StartPlayMs < 0) StartPlayMs = playMs;
            if (++_recordCounter >= 512)
            {
                _recordCounter = 0;
                _staleInstances.Clear();
                foreach (var kv in Instances)
                    if (playMs - kv.Value.LastUpdateMs > InstanceStaleMs) _staleInstances.Add(kv.Key);
                for (int i = 0; i < _staleInstances.Count; i++)
                    Instances.Remove(_staleInstances[i]);
                _staleInstances.Clear();
            }

            PerfStat s = Stat(app);
            s.Count++;
            s.SumMs += ms;
            if (ms > s.MaxMs) s.MaxMs = ms;
            if (ms < s.MinMs) s.MinMs = ms;
            s.Hist[Bucket(UpdateBounds, ms)]++;

            InstanceStat ins;
            if (!Instances.TryGetValue(instance, out ins))
            {
                ins = new InstanceStat();
                Instances[instance] = ins;
            }
            ins.Count++;
            ins.SumMs += ms;
            if (ms > ins.MaxMs) ins.MaxMs = ms;
            if (ms < ins.MinMs) ins.MinMs = ms;
            if (ins.LastUpdateMs > 0.0)
            {
                double iv = playMs - ins.LastUpdateMs;
                ins.IntervalSumMs += iv;
                ins.IntervalCount++;
                if (iv > ins.IntervalMaxMs) ins.IntervalMaxMs = iv;
                s.IntervalSumMs += iv;
                s.IntervalCount++;
                if (iv > s.IntervalMaxMs) s.IntervalMaxMs = iv;
            }
            ins.LastUpdateMs = playMs;

            if (ms >= SlowMs)
            {
                if (SlowEvents.Count >= SlowCap)
                {
                    int w = 0;
                    for (int i = 1; i < SlowEvents.Count; i++)
                        if (SlowEvents[i].Ms < SlowEvents[w].Ms) w = i;
                    if (SlowEvents[w].Ms >= ms) return;
                    SlowEvents.RemoveAt(w);
                }
                SlowEvent e = new SlowEvent();
                e.App = app;
                e.Instance = instance;
                e.Ms = ms;
                e.ScanMs = s.LastScanMs;
                e.PlayTime = TimeSpan.FromMilliseconds(StartPlayMs > 0 ? playMs - StartPlayMs : playMs).ToString(@"hh\:mm\:ss");
                SlowEvents.Add(e);
            }
        }

        public static void RecordScan(string app, double ms, int blocks)
        {
            PerfStat s = Stat(app);
            s.Scans++;
            s.ScanSumMs += ms;
            if (ms > s.ScanMaxMs) s.ScanMaxMs = ms;
            if (ms < s.ScanMinMs) s.ScanMinMs = ms;
            s.LastScanMs = ms;
            s.ScanHist[Bucket(ScanBounds, ms)]++;
            s.BlocksSum += blocks;
            double per = blocks > 0 ? ms / blocks : 0.0;
            s.PerBlockSum += per;
            if (per > s.PerBlockMax) s.PerBlockMax = per;
        }

        public static int Bucket(double[] bounds, double v)
        {
            for (int i = 0; i < bounds.Length; i++)
                if (v < bounds[i]) return i;
            return bounds.Length;
        }

        static PerfStat Stat(string app)
        {
            PerfStat s;
            if (!Stats.TryGetValue(app, out s))
            {
                s = new PerfStat();
                Stats[app] = s;
            }
            return s;
        }
    }

    /// <summary>Cached scan result for one grid and update window.</summary>
    class CacheEntry<T>
    {
        public long Window;
        public T Data;
    }

    /// <summary>Scan result containers implement this so the per-app scan pool
    /// can clear and reuse them instead of allocating new objects every window.</summary>
    public interface IScanData
    {
        void Clear();
    }

    /// <summary>One static map per app data type, so every app type keeps its
    /// own grid cache (generic type argument makes each app a separate map).
    /// Scan containers and cache entries are pooled: evicted entries return
    /// their objects to the pool and new scans rent from it instead of
    /// allocating fresh objects every window.</summary>
    static class ScanCache<T> where T : class, new()
    {
        public static readonly Dictionary<long, CacheEntry<T>> Map = new Dictionary<long, CacheEntry<T>>();

        /// <summary>Reused buffer for the stale-entry purge (no per-scan allocation).</summary>
        public static readonly List<long> Stale = new List<long>();

        /// <summary>Gate for the stale-entry purge: once the map is over its
        /// size limit the purge would walk the whole map on every scan, so it
        /// only runs on every 8th scan.</summary>
        public static int EvictCounter;
        static readonly List<T> _pool = new List<T>();
        static readonly List<CacheEntry<T>> _entries = new List<CacheEntry<T>>();

        /// <summary>Returns a pooled scan container (cleared via IScanData) or a fresh one.</summary>
        public static T Rent()
        {
            if (_pool.Count > 0)
            {
                T item = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                IScanData reusable = item as IScanData;
                if (reusable != null) reusable.Clear();
                return item;
            }
            return new T();
        }

        /// <summary>Pools a scan container for reuse. Only IScanData containers
        /// are pooled - other result types just get garbage collected.</summary>
        public static void Return(T item)
        {
            if (item is IScanData) _pool.Add(item);
        }

        public static CacheEntry<T> RentEntry(long window, T data)
        {
            if (_entries.Count > 0)
            {
                CacheEntry<T> entry = _entries[_entries.Count - 1];
                _entries.RemoveAt(_entries.Count - 1);
                entry.Window = window;
                entry.Data = data;
                return entry;
            }
            return new CacheEntry<T> { Window = window, Data = data };
        }

        public static void ReturnEntry(CacheEntry<T> entry)
        {
            entry.Data = default(T);
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// Maps production blueprint subtypes to their item sprite id, using the
    /// known sprite list. Unknown subtypes (disassembled blocks, mod items)
    /// fall back to a generic icon so nothing renders blank.
    /// </summary>
    public static class SpriteLookup
    {
        static readonly HashSet<string> _ammo = new HashSet<string>(StringComparer.Ordinal)
        {
            "AutocannonClip", "AutomaticRifleGun_Mag_20rd", "ElitePistolMagazine", "FireworksBoxBlue", "FireworksBoxGreen",
            "FireworksBoxPink", "FireworksBoxRainbow", "FireworksBoxRed", "FireworksBoxYellow", "FlareClip",
            "FullAutoPistolMagazine", "LargeCalibreAmmo", "LargeRailgunAmmo", "MediumCalibreAmmo", "Missile200mm",
            "NATO_25x184mm", "NATO_5p56x45mm", "PreciseAutomaticRifleGun_Mag_5rd", "RapidFireAutomaticRifleGun_Mag_50rd",
            "SemiAutoPistolMagazine", "SmallRailgunAmmo", "UltimateAutomaticRifleGun_Mag_30rd"
        };

        static readonly HashSet<string> _tools = new HashSet<string>(StringComparer.Ordinal)
        {
            "AdvancedHandHeldLauncherItem", "AngleGrinder2Item", "AngleGrinder3Item", "AngleGrinder4Item", "AngleGrinderItem",
            "AutomaticRifleItem", "BasicHandHeldLauncherItem", "ElitePistolItem", "FlareGunItem", "FullAutoPistolItem",
            "GoodAIRewardPunishmentTool", "HandDrill2Item", "HandDrill3Item", "HandDrill4Item", "HandDrillItem",
            "PreciseAutomaticRifleItem", "RapidFireAutomaticRifleItem", "SemiAutoPistolItem", "UltimateAutomaticRifleItem",
            "Welder2Item", "Welder3Item", "Welder4Item", "WelderItem"
        };

        static readonly HashSet<string> _consumables = new HashSet<string>(StringComparer.Ordinal)
        {
            "ClangCola", "CosmicCoffee", "Fruit", "InsectMeatCooked", "InsectMeatRaw", "MammalMeatCooked", "MammalMeatRaw",
            "MealPack_BananaBeef", "MealPack_Burrito", "MealPack_Chili", "MealPack_ClangCrunchies", "MealPack_Curry",
            "MealPack_Dumplings", "MealPack_ExpiredSlop", "MealPack_Flatbread", "MealPack_FoodPaste", "MealPack_FrontierStew",
            "MealPack_FruitBar", "MealPack_FruitPastry", "MealPack_GardenSlaw", "MealPack_GreenPellets", "MealPack_Hardtack",
            "MealPack_KelpCrisp", "MealPack_Lasagna", "MealPack_Ramen", "MealPack_RedPellets", "MealPack_SearedSabiroid",
            "MealPack_Spaghetti", "MealPack_SteakDinner", "MealPack_SynthLoaf", "MealPack_Unknown", "MealPack_VeggieBurger",
            "Medkit", "Mushrooms", "Powerkit", "RadiationKit", "Vegetables"
        };

        /// <summary>All vanilla ore subtypes with an icon in the sprite library.</summary>
        public static readonly List<string> Ores = new List<string>
        {
            "Stone", "Ice", "Iron", "Gold", "Silver", "Nickel", "Cobalt",
            "Magnesium", "Silicon", "Uranium", "Platinum"
        };

        /// <summary>All vanilla ingot subtypes with an icon in the sprite library.</summary>
        public static readonly List<string> Ingots = new List<string>
        {
            "Gravel", "Iron", "Gold", "Silver", "Nickel", "Cobalt",
            "Magnesium", "Silicon", "Uranium", "Platinum"
        };

        /// <summary>All vanilla component subtypes with an icon in the sprite library.</summary>
        public static readonly List<string> Components = new List<string>
        {
            "BulletproofGlass", "Canvas", "Computer", "Construction", "Detector", "Display", "EngineerPlushie",
            "EngineerPlushieSE2", "Explosives", "Girder", "GravityGenerator", "InteriorPlate", "LargeTube", "Medical",
            "MetalGrid", "Motor", "PowerCell", "PrototechCapacitor", "PrototechCircuitry", "PrototechCoolingUnit",
            "PrototechFrame", "PrototechMachinery", "PrototechPanel", "PrototechPropulsionUnit", "RadioCommunication",
            "Reactor", "SabiroidPlushie", "SmallTube", "SolarCell", "SteelPlate", "Superconductor", "Thrust", "ZoneChip"
        };

        /// <summary>Same set as Components for O(1) membership checks.</summary>
        public static readonly HashSet<string> ComponentSet = new HashSet<string>(Components, StringComparer.Ordinal);

        public static string ForItem(string subtype)
        {
            if (_ammo.Contains(subtype)) return "MyObjectBuilder_AmmoMagazine/" + subtype;
            if (_tools.Contains(subtype)) return "MyObjectBuilder_PhysicalGunObject/" + subtype;
            if (_consumables.Contains(subtype)) return "MyObjectBuilder_ConsumableItem/" + subtype;
            if (subtype == "Datapad") return "MyObjectBuilder_Datapad/Datapad";
            if (subtype == "HydrogenBottle") return "MyObjectBuilder_GasContainerObject/HydrogenBottle";
            if (subtype == "OxygenBottle") return "MyObjectBuilder_OxygenContainerObject/OxygenBottle";
            if (ComponentSet.Contains(subtype)) return "MyObjectBuilder_Component/" + subtype;
            if (subtype.Contains("Thrust")) return "MyObjectBuilder_Component/Thrust";
            if (subtype.Contains("Computer")) return "MyObjectBuilder_Component/Computer";
            if (subtype.Contains("Motor")) return "MyObjectBuilder_Component/Motor";
            if (subtype.Contains("Display")) return "MyObjectBuilder_Component/Display";
            if (subtype.Contains("Detector")) return "MyObjectBuilder_Component/Detector";
            if (subtype.Contains("Reactor")) return "MyObjectBuilder_Component/Reactor";
            return "MyObjectBuilder_Component/Construction";
        }
    }
}