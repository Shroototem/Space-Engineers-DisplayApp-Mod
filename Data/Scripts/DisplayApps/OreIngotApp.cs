using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace DisplayApps
{
    [MyTextSurfaceScript("OreIngotInfo", "Info Ores & Ingots")]
    public class OreIngotApp : AppBase
    {
        /// <summary>Aggregated volume+mass for one subtype - one dictionary
        /// probe per item instead of separate volume and mass maps.</summary>
        struct Amounts
        {
            public float Vol;
            public float Mass;
        }

        class ItemRow
        {
            public string SpriteKey;
            public string Name;
            public string Value;
            public float Vol;
            public float Mass;
            public float Ratio;
            public bool HasStock;
        }

        class OreScan : IScanData
        {
            public readonly Dictionary<string, Amounts> Ores = new Dictionary<string, Amounts>();
            public readonly Dictionary<string, Amounts> Ingots = new Dictionary<string, Amounts>();
            public readonly List<ItemRow> RowOres = new List<ItemRow>();
            public readonly List<ItemRow> RowIngots = new List<ItemRow>();
            readonly List<ItemRow> _pool = new List<ItemRow>();
            public float OreTotal, IngotTotal;
            public float OreMassTotal, IngotMassTotal;
            public string OresHeader, IngotsHeader;

            public void Clear()
            {
                Ores.Clear();
                Ingots.Clear();
                _pool.AddRange(RowOres);
                _pool.AddRange(RowIngots);
                RowOres.Clear();
                RowIngots.Clear();
                OreTotal = 0f;
                IngotTotal = 0f;
                OreMassTotal = 0f;
                IngotMassTotal = 0f;
                OresHeader = null;
                IngotsHeader = null;
            }

            public ItemRow RentRow()
            {
                if (_pool.Count > 0)
                {
                    ItemRow row = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return row;
                }
                return new ItemRow();
            }
        }

        sealed class VolDesc : IComparer<ItemRow>
        {
            public static readonly VolDesc Instance = new VolDesc();
            public int Compare(ItemRow a, ItemRow b)
            {
                return b.Vol.CompareTo(a.Vol);
            }
        }

        sealed class MassDesc : IComparer<ItemRow>
        {
            public static readonly MassDesc Instance = new MassDesc();
            public int Compare(ItemRow a, ItemRow b)
            {
                return b.Mass.CompareTo(a.Mass);
            }
        }

        /// <summary>Display name and sprite id per subtype - pure functions of
        /// the subtype, resolved once for the session (ores and ingots share
        /// subtype names, so each kind keeps its own map).</summary>
        class RowInfo
        {
            public string Name;
            public string Sprite;
        }

        static readonly Dictionary<string, RowInfo> _oreInfo = new Dictionary<string, RowInfo>();
        static readonly Dictionary<string, RowInfo> _ingotInfo = new Dictionary<string, RowInfo>();

        static readonly Color OreBarColor = new Color(210, 170, 90);
        static readonly Color IngotBarColor = new Color(150, 190, 220);

        readonly Func<OreScan> _scanFunc;
        readonly Action<MyInventoryItem> _onItem;
        MySpriteDrawFrame _frame;
        OreScan _scan;
        readonly Action<int, float> _drawOreRow;
        readonly Action<int, float> _drawIngotRow;

        public OreIngotApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _onItem = OnItem;
            _drawOreRow = DrawOreRow;
            _drawIngotRow = DrawIngotRow;
        }

        protected override void RunApp()
        {
            OreScan scan = GetGridScan(_scanFunc);
            _scan = scan;

            using (var frame = BeginAppFrame("ORES & INGOTS", "RAW & REFINED MATERIALS", "MyObjectBuilder_Ore/Iron", new Color(210, 170, 90)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                bool showOres = ConfigOreIngotType != 3;
                bool showIngots = ConfigOreIngotType != 2;
                int totalItems = (showOres ? scan.RowOres.Count : 0) + (showIngots ? scan.RowIngots.Count : 0);

                if (totalItems == 0)
                {
                    DrawEmpty(frame, showOres && showIngots ? "NO ORES OR INGOTS ON GRID"
                        : showOres ? "NO ORES ON GRID" : "NO INGOTS ON GRID");
                    return;
                }

                DrawDivider(frame, 50f);
                float bottom = Bottom;

                if (ConfigScroll)
                {
                    float y0 = 76f * S;
                    float headerH = 20f * S;
                    float gap = 8f * S;
                    int groups = (showOres ? 1 : 0) + (showIngots ? 1 : 0);
                    float groupH = ListGroupHeight(bottom - y0, groups, headerH, gap);
                    int slot = 0;

                    if (showOres)
                    {
                        DrawListGroup(frame, slot, scan.OresHeader, scan.RowOres.Count, y0, headerH, groupH, 24f * S, _drawOreRow);
                        slot++;
                        if (showIngots)
                            DrawDivider(frame, (y0 + headerH + groupH + gap / 2f) / S);
                    }
                    if (showIngots)
                    {
                        DrawListGroup(frame, slot, scan.IngotsHeader, scan.RowIngots.Count,
                            ListGroupTop(y0, slot, groupH, headerH, gap), headerH, groupH, 24f * S, _drawIngotRow);
                    }
                }
                else
                {
                    float y = 76f * S;
                    int drawn = 0;

                    if (showOres)
                    {
                        AddText(frame, scan.OresHeader, new Vector2(Left, 56f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                        AddText(frame, "MOST ABUNDANT FIRST", new Vector2(Right, 56f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);

                        for (int i = 0; i < scan.RowOres.Count; i++)
                        {
                            if (y + 24f * S > bottom) break;
                            DrawItemRow(frame, scan.RowOres[i], OreBarColor, y);
                            y += 24f * S;
                            drawn++;
                        }
                    }

                    if (showIngots)
                    {
                        if (showOres)
                        {
                            if (y + 34f * S <= bottom)
                            {
                                y += 8f * S;
                                DrawDivider(frame, y / S);
                                y += 6f * S;
                            }
                            AddText(frame, scan.IngotsHeader, new Vector2(Left, y), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                            y += 18f * S;
                        }
                        else
                        {
                            AddText(frame, scan.IngotsHeader, new Vector2(Left, 56f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                            AddText(frame, "MOST ABUNDANT FIRST", new Vector2(Right, 56f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                        }

                        for (int i = 0; i < scan.RowIngots.Count; i++)
                        {
                            if (y + 24f * S > bottom) break;
                            DrawItemRow(frame, scan.RowIngots[i], IngotBarColor, y);
                            y += 24f * S;
                            drawn++;
                        }
                    }

                    if (totalItems > drawn)
                        DrawMore(frame, $"+{totalItems - drawn} MORE ITEM(S)");
                }
            }
        }

        static RowInfo InfoFor(string subtype, bool ore)
        {
            var map = ore ? _oreInfo : _ingotInfo;
            RowInfo info;
            if (!map.TryGetValue(subtype, out info))
            {
                info = new RowInfo();
                info.Name = FormatItemName(subtype);
                info.Sprite = (ore ? "MyObjectBuilder_Ore/" : "MyObjectBuilder_Ingot/") + subtype;
                map[subtype] = info;
            }
            return info;
        }

        static void ZeroFill(Dictionary<string, Amounts> target, List<string> known)
        {
            for (int i = 0; i < known.Count; i++)
            {
                if (!target.ContainsKey(known[i])) target[known[i]] = default(Amounts);
            }
        }

        void BuildRow(OreScan scan, List<ItemRow> rows, string subtype, Amounts amounts, bool ore)
        {
            ItemRow row = scan.RentRow();
            row.Vol = amounts.Vol;
            row.Mass = amounts.Mass;
            RowInfo info = InfoFor(subtype, ore);
            row.Name = info.Name;
            row.SpriteKey = info.Sprite;
            float total = ConfigStorageType == 2
                ? (ore ? scan.OreMassTotal : scan.IngotMassTotal)
                : (ore ? scan.OreTotal : scan.IngotTotal);
            float val = ConfigStorageType == 2 ? amounts.Mass : amounts.Vol;
            row.Ratio = total > 0f ? val / total : 0f;
            row.HasStock = amounts.Vol > 0.001f || amounts.Mass > 0.001f;
            row.Value = FormatStorage(amounts.Vol, amounts.Mass) + " (" + (row.Ratio * 100f).ToString("0") + "%)";
            rows.Add(row);
        }

        OreScan ScanGrid()
        {
            RefreshTerminalBlocks();

            OreScan scan = RentScan<OreScan>();
            _scan = scan;
            ForEachItem(TerminalBlocks, _onItem);

            if (ConfigFullList)
            {
                ZeroFill(scan.Ores, SpriteLookup.Ores);
                ZeroFill(scan.Ingots, SpriteLookup.Ingots);
            }

            foreach (var kv in scan.Ores)
                BuildRow(scan, scan.RowOres, kv.Key, kv.Value, true);
            foreach (var kv in scan.Ingots)
                BuildRow(scan, scan.RowIngots, kv.Key, kv.Value, false);

            var cmp = ConfigStorageType == 2 ? (IComparer<ItemRow>)MassDesc.Instance : VolDesc.Instance;
            scan.RowOres.Sort(cmp);
            scan.RowIngots.Sort(cmp);

            scan.OresHeader = "ORES (" + scan.RowOres.Count + ")";
            scan.IngotsHeader = "INGOTS (" + scan.RowIngots.Count + ")";
            return scan;
        }

        void OnItem(MyInventoryItem item)
        {
            ItemStats stats = GetItemStats(item.Type);
            if (stats.Category != CatOre && stats.Category != CatIngot) return;
            float amt = (float)item.Amount;
            float v = amt * stats.Volume;
            float m = amt * stats.Mass;
            if (v <= 0f && m <= 0f) return;

            string subtype = item.Type.SubtypeId;
            Dictionary<string, Amounts> map;
            if (stats.Category == CatOre)
            {
                map = _scan.Ores;
                _scan.OreTotal += v;
                _scan.OreMassTotal += m;
            }
            else
            {
                map = _scan.Ingots;
                _scan.IngotTotal += v;
                _scan.IngotMassTotal += m;
            }
            Amounts cur;
            map.TryGetValue(subtype, out cur);
            cur.Vol += v;
            cur.Mass += m;
            map[subtype] = cur;
        }

        void DrawOreRow(int idx, float y)
        {
            DrawItemRow(_frame, _scan.RowOres[idx], OreBarColor, y);
        }

        void DrawIngotRow(int idx, float y)
        {
            DrawItemRow(_frame, _scan.RowIngots[idx], IngotBarColor, y);
        }

        void DrawItemRow(MySpriteDrawFrame frame, ItemRow row, Color barColor, float y)
        {
            DrawProgressRow(frame, y, row.SpriteKey, row.Name, row.Value, row.Ratio, barColor, row.HasStock);
        }
    }
}
