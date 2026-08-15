using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using MyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace DisplayApps
{
    [MyTextSurfaceScript("StorageInfo", "Info Storage")]
    public class StorageApp : AppBase
    {
        class ContainerRow
        {
            public string Name;
            public string Value;
            public float Vol;
            public float Mass;
            public float Ratio;
        }

        class StorageScan : IScanData
        {
            public readonly List<ContainerRow> Containers = new List<ContainerRow>();
            readonly List<ContainerRow> _pool = new List<ContainerRow>();
            public float TotalVol, TotalMax, TotalMass;
            public float OreVol, IngotVol, CompVol, AmmoVol, ToolVol, OtherVol;
            public float OreMass, IngotMass, CompMass, AmmoMass, ToolMass, OtherMass;
            public int OreCount, IngotCount, CompCount, AmmoCount, ToolCount, OtherCount;

            // Display strings and ratios, built once per grid per window in
            // the scan so every display draws without formatting.
            public string TotalText, MassText, ContainersHeader;
            public float VolRatio;
            public readonly string[] CatTexts = new string[6];
            public readonly float[] CatRatios = new float[6];

            public void Clear()
            {
                _pool.AddRange(Containers);
                Containers.Clear();
                TotalVol = 0f;
                TotalMax = 0f;
                TotalMass = 0f;
                OreVol = 0f;
                IngotVol = 0f;
                CompVol = 0f;
                AmmoVol = 0f;
                ToolVol = 0f;
                OtherVol = 0f;
                OreMass = 0f;
                IngotMass = 0f;
                CompMass = 0f;
                AmmoMass = 0f;
                ToolMass = 0f;
                OtherMass = 0f;
                OreCount = 0;
                IngotCount = 0;
                CompCount = 0;
                AmmoCount = 0;
                ToolCount = 0;
                OtherCount = 0;
                TotalText = null;
                MassText = null;
                ContainersHeader = null;
                VolRatio = 0f;
                for (int i = 0; i < CatTexts.Length; i++)
                {
                    CatTexts[i] = null;
                    CatRatios[i] = 0f;
                }
            }

            public ContainerRow RentRow()
            {
                if (_pool.Count > 0)
                {
                    ContainerRow row = _pool[_pool.Count - 1];
                    _pool.RemoveAt(_pool.Count - 1);
                    return row;
                }
                return new ContainerRow();
            }
        }

        // No named Comparison<T> field - see the note in AssemblerApp.

        readonly Func<StorageScan> _scanFunc;
        MySpriteDrawFrame _frame;
        StorageScan _scan;
        readonly Action<int, float> _drawContainerRow;

        public StorageApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawContainerRow = DrawContainerRow;
        }

        protected override void RunApp()
        {
            StorageScan scan = GetGridScan(_scanFunc);
            _scan = scan;

            using (var frame = BeginAppFrame("INVENTORY STATUS", "GRID STORAGE & MATERIALS MONITOR", "MyObjectBuilder_Package/Package", new Color(200, 180, 80)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;

                var containers = scan.Containers;

                if (containers.Count == 0)
                {
                    DrawEmpty(frame, "NO CONTAINERS ON GRID");
                    return;
                }

                AddText(frame, "TOTAL STORAGE", new Vector2(Left, 52f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, scan.TotalText, new Vector2(Right, 52f * S), 0.50f * S, new Color(200, 205, 215), TextAlignment.RIGHT);

                RectangleF volBar = new RectangleF(new Vector2(Left, 68f * S), new Vector2(Right - Left, 14f * S));
                DrawBar(frame, volBar, scan.VolRatio, BarColor(scan.VolRatio));

                AddText(frame, "TOTAL MASS", new Vector2(Left, 88f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, scan.MassText, new Vector2(Right, 88f * S), 0.50f * S, new Color(200, 205, 215), TextAlignment.RIGHT);

                DrawDivider(frame, 126f);
                AddText(frame, "MATERIAL BREAKDOWN", new Vector2(Left, 132f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);

                float catY = 150f * S;
                float catH = 26f * S;

                int catIdx = 0;
                if (scan.OreCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "ORES", "MyObjectBuilder_Ore/Iron", scan.OreCount, scan.CatTexts[0], scan.CatRatios[0], CatColor(scan.OreCount), catY + catIdx++ * catH);
                if (scan.IngotCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "INGOTS", "MyObjectBuilder_Ingot/Iron", scan.IngotCount, scan.CatTexts[1], scan.CatRatios[1], CatColor(scan.IngotCount), catY + catIdx++ * catH);
                if (scan.CompCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "COMPONENTS", "MyObjectBuilder_Component/SteelPlate", scan.CompCount, scan.CatTexts[2], scan.CatRatios[2], CatColor(scan.CompCount), catY + catIdx++ * catH);
                if (scan.AmmoCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "AMMO", "MyObjectBuilder_AmmoMagazine/NATO_5p56x45mm", scan.AmmoCount, scan.CatTexts[3], scan.CatRatios[3], CatColor(scan.AmmoCount), catY + catIdx++ * catH);
                if (scan.ToolCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "TOOLS & CANISTERS", "MyObjectBuilder_PhysicalGunObject/HandDrillItem", scan.ToolCount, scan.CatTexts[4], scan.CatRatios[4], CatColor(scan.ToolCount), catY + catIdx++ * catH);
                if (scan.OtherCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "OTHER", "MyObjectBuilder_Component/SmallTube", scan.OtherCount, scan.CatTexts[5], scan.CatRatios[5], CatColor(scan.OtherCount), catY + catIdx++ * catH);

                float listY = catY + (catIdx + 0.2f) * catH;
                DrawDivider(frame, (listY) / S);
                float rowsTop = listY + 24f * S;
                int rows = DrawListGroup(frame, 0, scan.ContainersHeader, containers.Count,
                    listY + 6f * S, 18f * S, Bottom - rowsTop, 32f * S, _drawContainerRow);

                if (!ConfigScroll && containers.Count > rows)
                    DrawMore(frame, $"+{containers.Count - rows} MORE");
            }
        }

        static Color CatColor(int count)
        {
            return count > 0 ? new Color(200, 180, 80) : new Color(80, 85, 95);
        }

        void FillCat(StorageScan scan, int idx, float vol, float mass, int count, float totalVol, float totalMass)
        {
            float ratio = ConfigStorageType == 2
                ? (totalMass > 0f ? mass / totalMass : 0f)
                : (totalVol > 0f ? vol / totalVol : 0f);
            scan.CatRatios[idx] = ratio;
            scan.CatTexts[idx] = count == 0
                ? "EMPTY"
                : FormatStorage(vol, mass) + " (" + (ratio * 100f).ToString("0") + "%)";
        }

        StorageScan ScanGrid()
        {
            RefreshTerminalBlocks();

            StorageScan scan = RentScan<StorageScan>();
            _scan = scan;

            for (int i = 0; i < TerminalBlocks.Count; i++)
            {
                MyTerminalBlock tb = TerminalBlocks[i];
                if (tb == null) continue;
                if (tb is Sandbox.ModAPI.IMyGasTank) continue;
                if (tb.InventoryCount == 0) continue;

                float cVol = 0f, cMax = 0f, cMass = 0f;
                for (int inv = 0; inv < tb.InventoryCount; inv++)
                {
                    MyInventory inventory = tb.GetInventory(inv);
                    float cv = (float)inventory.CurrentVolume;
                    float mv = (float)inventory.MaxVolume;
                    float cm = (float)inventory.CurrentMass;
                    scan.TotalVol += cv;
                    scan.TotalMax += mv;
                    scan.TotalMass += cm;
                    cVol += cv;
                    cMax += mv;
                    cMass += cm;

                    // Item pass shares this traversal - one GetInventory per
                    // inventory instead of a second full block walk.
                    var items = FillItems(inventory);
                    for (int k = 0; k < items.Count; k++)
                        OnItem(items[k]);
                }
                ContainerRow row = scan.RentRow();
                row.Name = Truncate(BlockName(tb), 22);
                row.Vol = cVol;
                row.Mass = cMass;
                row.Ratio = cMax > 0f ? cVol / cMax : 0f;
                row.Value = FormatStorage(cVol, cMass) + " (" + (row.Ratio * 100f).ToString("0") + "%)";
                scan.Containers.Add(row);
            }

            scan.Containers.Sort((a, b) => b.Ratio.CompareTo(a.Ratio));

            // Display strings - pure functions of the totals above.
            float totalMatVol = scan.OreVol + scan.IngotVol + scan.CompVol + scan.AmmoVol + scan.ToolVol + scan.OtherVol;
            float totalMatMass = scan.OreMass + scan.IngotMass + scan.CompMass + scan.AmmoMass + scan.ToolMass + scan.OtherMass;
            scan.VolRatio = scan.TotalMax > 0f ? scan.TotalVol / scan.TotalMax : 0f;
            scan.TotalText = FormatVolume(scan.TotalVol) + " / " + FormatVolume(scan.TotalMax) + " (" + (scan.VolRatio * 100f).ToString("0") + "%)";
            scan.MassText = FormatMass(scan.TotalMass);
            scan.ContainersHeader = "CONTAINERS (" + scan.Containers.Count + ")";
            FillCat(scan, 0, scan.OreVol, scan.OreMass, scan.OreCount, totalMatVol, totalMatMass);
            FillCat(scan, 1, scan.IngotVol, scan.IngotMass, scan.IngotCount, totalMatVol, totalMatMass);
            FillCat(scan, 2, scan.CompVol, scan.CompMass, scan.CompCount, totalMatVol, totalMatMass);
            FillCat(scan, 3, scan.AmmoVol, scan.AmmoMass, scan.AmmoCount, totalMatVol, totalMatMass);
            FillCat(scan, 4, scan.ToolVol, scan.ToolMass, scan.ToolCount, totalMatVol, totalMatMass);
            FillCat(scan, 5, scan.OtherVol, scan.OtherMass, scan.OtherCount, totalMatVol, totalMatMass);
            return scan;
        }

        void OnItem(MyInventoryItem item)
        {
            ItemStats stats = GetItemStats(item.Type);
            float amt = (float)item.Amount;
            float v = amt * stats.Volume;
            float m = amt * stats.Mass;

            switch (stats.Category)
            {
                case CatOre: _scan.OreVol += v; _scan.OreMass += m; _scan.OreCount++; break;
                case CatIngot: _scan.IngotVol += v; _scan.IngotMass += m; _scan.IngotCount++; break;
                case CatComponent: _scan.CompVol += v; _scan.CompMass += m; _scan.CompCount++; break;
                case CatAmmo: _scan.AmmoVol += v; _scan.AmmoMass += m; _scan.AmmoCount++; break;
                case CatTool: _scan.ToolVol += v; _scan.ToolMass += m; _scan.ToolCount++; break;
                default: _scan.OtherVol += v; _scan.OtherMass += m; _scan.OtherCount++; break;
            }
        }

        void DrawContainerRow(int idx, float rowTop)
        {
            ContainerRow row = _scan.Containers[idx];
            DrawProgressRow(_frame, rowTop, "MyObjectBuilder_Package/Package", row.Name,
                row.Value, row.Ratio, BarColor(row.Ratio));
        }
    }
}
