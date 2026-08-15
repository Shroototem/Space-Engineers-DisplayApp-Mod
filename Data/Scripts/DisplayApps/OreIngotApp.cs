using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.IMyInventoryItem;

namespace DisplayApps
{
    [MyTextSurfaceScript("OreIngotInfo", "Info Ores & Ingots")]
    public class OreIngotApp : AppBase
    {
        class ItemRow
        {
            public string SpriteKey;
            public string Name;
            public float Vol;
            public float Mass;
        }

        class OreScan : IScanData
        {
            public readonly Dictionary<string, float> Ores = new Dictionary<string, float>();
            public readonly Dictionary<string, float> Ingots = new Dictionary<string, float>();
            public readonly Dictionary<string, float> OreMasses = new Dictionary<string, float>();
            public readonly Dictionary<string, float> IngotMasses = new Dictionary<string, float>();
            public readonly List<ItemRow> RowOres = new List<ItemRow>();
            public readonly List<ItemRow> RowIngots = new List<ItemRow>();
            readonly List<ItemRow> _pool = new List<ItemRow>();
            public float OreTotal, IngotTotal;
            public float OreMassTotal, IngotMassTotal;
            public bool AnyStock;

            public void Clear()
            {
                Ores.Clear();
                Ingots.Clear();
                OreMasses.Clear();
                IngotMasses.Clear();
                _pool.AddRange(RowOres);
                _pool.AddRange(RowIngots);
                RowOres.Clear();
                RowIngots.Clear();
                OreTotal = 0f;
                IngotTotal = 0f;
                OreMassTotal = 0f;
                IngotMassTotal = 0f;
                AnyStock = false;
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

        const string OreType = "MyObjectBuilder_Ore";
        const string IngotType = "MyObjectBuilder_Ingot";

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

                float oreTotal = scan.OreTotal, ingotTotal = scan.IngotTotal;
                float oreMassTotal = scan.OreMassTotal, ingotMassTotal = scan.IngotMassTotal;

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
                        DrawListGroup(frame, slot, $"ORES ({scan.RowOres.Count})", scan.RowOres.Count, y0, headerH, groupH, 24f * S, _drawOreRow);
                        slot++;
                        if (showIngots)
                            DrawDivider(frame, (y0 + headerH + groupH + gap / 2f) / S);
                    }
                    if (showIngots)
                    {
                        DrawListGroup(frame, slot, $"INGOTS ({scan.RowIngots.Count})", scan.RowIngots.Count,
                            ListGroupTop(y0, slot, groupH, headerH, gap), headerH, groupH, 24f * S, _drawIngotRow);
                    }
                }
                else
                {
                    float y = 76f * S;
                    int drawn = 0;

                    if (showOres)
                    {
                        AddText(frame, $"ORES ({scan.RowOres.Count})", new Vector2(Left, 56f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                        AddText(frame, "MOST ABUNDANT FIRST", new Vector2(Right, 56f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);

                        for (int i = 0; i < scan.RowOres.Count; i++)
                        {
                            if (y + 24f * S > bottom) break;
                            DrawItemRow(frame, scan.RowOres[i], oreTotal, oreMassTotal, new Color(210, 170, 90), y);
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
                            AddText(frame, $"INGOTS ({scan.RowIngots.Count})", new Vector2(Left, y), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                            y += 18f * S;
                        }
                        else
                        {
                            AddText(frame, $"INGOTS ({scan.RowIngots.Count})", new Vector2(Left, 56f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);
                            AddText(frame, "MOST ABUNDANT FIRST", new Vector2(Right, 56f * S), 0.46f * S, new Color(120, 130, 145), TextAlignment.RIGHT);
                        }

                        for (int i = 0; i < scan.RowIngots.Count; i++)
                        {
                            if (y + 24f * S > bottom) break;
                            DrawItemRow(frame, scan.RowIngots[i], ingotTotal, ingotMassTotal, new Color(150, 190, 220), y);
                            y += 24f * S;
                            drawn++;
                        }
                    }

                    if (totalItems > drawn)
                        DrawMore(frame, $"+{totalItems - drawn} MORE ITEM(S)");
                }
            }
        }

        OreScan ScanGrid()
        {
            RefreshTerminalBlocks();

            OreScan scan = RentScan<OreScan>();
            _scan = scan;
            ForEachItem(TerminalBlocks, _onItem);

            foreach (var kv in scan.Ores) scan.OreTotal += kv.Value;
            foreach (var kv in scan.Ingots) scan.IngotTotal += kv.Value;
            foreach (var kv in scan.OreMasses) scan.OreMassTotal += kv.Value;
            foreach (var kv in scan.IngotMasses) scan.IngotMassTotal += kv.Value;
            scan.AnyStock = scan.Ores.Count + scan.Ingots.Count > 0;

            if (ConfigFullList)
            {
                EnsureFullListEntries(scan.Ores, SpriteLookup.Ores, OreType);
                EnsureFullListEntries(scan.Ingots, SpriteLookup.Ingots, IngotType);
            }

            foreach (var kv in scan.Ores)
            {
                ItemRow row = scan.RentRow();
                row.Vol = kv.Value;
                float m;
                scan.OreMasses.TryGetValue(kv.Key, out m);
                row.Mass = m;
                row.Name = FormatItemName(kv.Key.Substring(kv.Key.IndexOf('/') + 1));
                row.SpriteKey = kv.Key;
                scan.RowOres.Add(row);
            }
            scan.RowOres.Sort((a, b) => ConfigStorageType == 2 ? b.Mass.CompareTo(a.Mass) : b.Vol.CompareTo(a.Vol));
            foreach (var kv in scan.Ingots)
            {
                ItemRow row = scan.RentRow();
                row.Vol = kv.Value;
                float m;
                scan.IngotMasses.TryGetValue(kv.Key, out m);
                row.Mass = m;
                row.Name = FormatItemName(kv.Key.Substring(kv.Key.IndexOf('/') + 1));
                row.SpriteKey = kv.Key;
                scan.RowIngots.Add(row);
            }
            scan.RowIngots.Sort((a, b) => ConfigStorageType == 2 ? b.Mass.CompareTo(a.Mass) : b.Vol.CompareTo(a.Vol));
            return scan;
        }

        void OnItem(MyInventoryItem item)
        {
            var content = item.Content;
            if (content == null) return;
            string typeId = content.TypeId.ToString();
            if (typeId != OreType && typeId != IngotType) return;
            string subtype = content.SubtypeName;
            string key = typeId + "/" + subtype;

            float itemVol;
            if (!ItemVolumeCache.TryGetValue(key, out itemVol))
            {
                try { itemVol = (float)new MyItemType(typeId, subtype).GetItemInfo().Volume; } catch { itemVol = 0f; }
                ItemVolumeCache[key] = itemVol;
            }
            float itemMass;
            if (!ItemMassCache.TryGetValue(key, out itemMass))
            {
                try { itemMass = (float)new MyItemType(typeId, subtype).GetItemInfo().Mass; } catch { itemMass = 0f; }
                ItemMassCache[key] = itemMass;
            }
            float amt = (float)item.Amount;
            float v = amt * itemVol;
            float m = amt * itemMass;
            if (v <= 0f && m <= 0f) return;

            var vols = typeId == OreType ? _scan.Ores : _scan.Ingots;
            var masses = typeId == OreType ? _scan.OreMasses : _scan.IngotMasses;
            float curV;
            if (!vols.TryGetValue(key, out curV)) vols[key] = v;
            else vols[key] = curV + v;

            float curM;
            if (!masses.TryGetValue(key, out curM)) masses[key] = m;
            else masses[key] = curM + m;
        }

        void DrawOreRow(int idx, float y)
        {
            DrawItemRow(_frame, _scan.RowOres[idx], _scan.OreTotal, _scan.OreMassTotal, new Color(210, 170, 90), y);
        }

        void DrawIngotRow(int idx, float y)
        {
            DrawItemRow(_frame, _scan.RowIngots[idx], _scan.IngotTotal, _scan.IngotMassTotal, new Color(150, 190, 220), y);
        }

        void DrawItemRow(MySpriteDrawFrame frame, ItemRow row, float totalVol, float totalMass, Color barColor, float y)
        {
            float total = ConfigStorageType == 2 ? totalMass : totalVol;
            float val = ConfigStorageType == 2 ? row.Mass : row.Vol;
            float ratio = total > 0f ? val / total : 0f;
            bool hasStock = row.Vol > 0.001f || row.Mass > 0.001f;
            DrawProgressRow(frame, y, row.SpriteKey, row.Name, $"{FormatStorage(row.Vol, row.Mass)} ({ratio * 100f:0}%)", ratio, barColor, hasStock);
        }
    }
}