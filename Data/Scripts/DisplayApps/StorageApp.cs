using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using MyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.IMyInventoryItem;

namespace DisplayApps
{
    [MyTextSurfaceScript("StorageInfo", "Info Storage")]
    public class StorageApp : AppBase
    {
        class ContainerRow
        {
            public string Name;
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

        const string OreType = "MyObjectBuilder_Ore";
        const string IngotType = "MyObjectBuilder_Ingot";
        const string CompType = "MyObjectBuilder_Component";
        const string AmmoType = "MyObjectBuilder_AmmoMagazine";
        const string GunType = "MyObjectBuilder_PhysicalGunObject";
        const string O2Type = "MyObjectBuilder_OxygenContainerObject";
        const string H2Type = "MyObjectBuilder_GasContainerObject";

        readonly Func<StorageScan> _scanFunc;
        readonly Action<MyInventoryItem> _onItem;
        MySpriteDrawFrame _frame;
        StorageScan _scan;
        readonly Action<int, float> _drawContainerRow;

        public StorageApp(MySurface surface, MyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _onItem = OnItem;
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

                float totalVol = scan.TotalVol, totalMax = scan.TotalMax, totalMass = scan.TotalMass;
                float oreVol = scan.OreVol, ingotVol = scan.IngotVol, compVol = scan.CompVol;
                float ammoVol = scan.AmmoVol, toolVol = scan.ToolVol, otherVol = scan.OtherVol;
                float oreMass = scan.OreMass, ingotMass = scan.IngotMass, compMass = scan.CompMass;
                float ammoMass = scan.AmmoMass, toolMass = scan.ToolMass, otherMass = scan.OtherMass;
                int oreCount = scan.OreCount, ingotCount = scan.IngotCount, compCount = scan.CompCount;
                int ammoCount = scan.AmmoCount, toolCount = scan.ToolCount, otherCount = scan.OtherCount;
                var containers = scan.Containers;

                float volRatio = totalMax > 0f ? totalVol / totalMax : 0f;
                float totalMatVol = oreVol + ingotVol + compVol + ammoVol + toolVol + otherVol;
                float totalMatMass = oreMass + ingotMass + compMass + ammoMass + toolMass + otherMass;

                if (containers.Count == 0)
                {
                    DrawEmpty(frame, "NO CONTAINERS ON GRID");
                    return;
                }

                AddText(frame, "TOTAL STORAGE", new Vector2(Left, 52f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, $"{FormatVolume(totalVol)} / {FormatVolume(totalMax)} ({volRatio * 100f:0}%)", new Vector2(Right, 52f * S), 0.50f * S, new Color(200, 205, 215), TextAlignment.RIGHT);

                RectangleF volBar = new RectangleF(new Vector2(Left, 68f * S), new Vector2(Right - Left, 14f * S));
                DrawBar(frame, volBar, volRatio, BarColor(volRatio));

                AddText(frame, "TOTAL MASS", new Vector2(Left, 88f * S), 0.50f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, FormatMass(totalMass), new Vector2(Right, 88f * S), 0.50f * S, new Color(200, 205, 215), TextAlignment.RIGHT);

                DrawDivider(frame, 126f);
                AddText(frame, "MATERIAL BREAKDOWN", new Vector2(Left, 132f * S), 0.50f * S, new Color(180, 190, 205), TextAlignment.LEFT);

                float catY = 150f * S;
                float catH = 26f * S;

                int catIdx = 0;
                if (oreCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "ORES", "MyObjectBuilder_Ore/Iron", oreCount, CatValue(oreVol, oreMass, totalMatVol, totalMatMass, oreCount), CatRatio(oreVol, oreMass, totalMatVol, totalMatMass), CatColor(oreCount), catY + catIdx++ * catH);
                if (ingotCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "INGOTS", "MyObjectBuilder_Ingot/Iron", ingotCount, CatValue(ingotVol, ingotMass, totalMatVol, totalMatMass, ingotCount), CatRatio(ingotVol, ingotMass, totalMatVol, totalMatMass), CatColor(ingotCount), catY + catIdx++ * catH);
                if (compCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "COMPONENTS", "MyObjectBuilder_Component/SteelPlate", compCount, CatValue(compVol, compMass, totalMatVol, totalMatMass, compCount), CatRatio(compVol, compMass, totalMatVol, totalMatMass), CatColor(compCount), catY + catIdx++ * catH);
                if (ammoCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "AMMO", "MyObjectBuilder_AmmoMagazine/NATO_5p56x45mm", ammoCount, CatValue(ammoVol, ammoMass, totalMatVol, totalMatMass, ammoCount), CatRatio(ammoVol, ammoMass, totalMatVol, totalMatMass), CatColor(ammoCount), catY + catIdx++ * catH);
                if (toolCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "TOOLS & CANISTERS", "MyObjectBuilder_PhysicalGunObject/HandDrillItem", toolCount, CatValue(toolVol, toolMass, totalMatVol, totalMatMass, toolCount), CatRatio(toolVol, toolMass, totalMatVol, totalMatMass), CatColor(toolCount), catY + catIdx++ * catH);
                if (otherCount > 0 || ConfigFullList)
                    DrawCategoryRow(frame, "OTHER", "MyObjectBuilder_Component/SmallTube", otherCount, CatValue(otherVol, otherMass, totalMatVol, totalMatMass, otherCount), CatRatio(otherVol, otherMass, totalMatVol, totalMatMass), CatColor(otherCount), catY + catIdx++ * catH);

                float listY = catY + (catIdx + 0.2f) * catH;
                DrawDivider(frame, (listY) / S);
                float rowsTop = listY + 24f * S;
                int rows = DrawListGroup(frame, 0, $"CONTAINERS ({containers.Count})", containers.Count,
                    listY + 6f * S, 18f * S, Bottom - rowsTop, 32f * S, _drawContainerRow);

                if (!ConfigScroll && containers.Count > rows)
                    DrawMore(frame, $"+{containers.Count - rows} MORE");
            }
        }

        string CatValue(float vol, float mass, float totalVol, float totalMass, int count)
        {
            if (count == 0) return "EMPTY";
            float ratio = CatRatio(vol, mass, totalVol, totalMass);
            return $"{FormatStorage(vol, mass)} ({ratio * 100f:0}%)";
        }

        float CatRatio(float vol, float mass, float totalVol, float totalMass)
        {
            if (ConfigStorageType == 2)
                return totalMass > 0f ? mass / totalMass : 0f;
            return totalVol > 0f ? vol / totalVol : 0f;
        }

        static Color CatColor(int count)
        {
            return count > 0 ? new Color(200, 180, 80) : new Color(80, 85, 95);
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
                }
                ContainerRow row = scan.RentRow();
                row.Name = Truncate(BlockName(tb), 22);
                row.Vol = cVol;
                row.Mass = cMass;
                row.Ratio = cMax > 0f ? cVol / cMax : 0f;
                scan.Containers.Add(row);
            }
            ForEachItem(TerminalBlocks, _onItem);

            scan.Containers.Sort((a, b) => b.Ratio.CompareTo(a.Ratio));
            return scan;
        }

        void OnItem(MyInventoryItem item)
        {
            var content = item.Content;
            if (content == null) return;
            string typeId = content.TypeId.ToString();
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

            if (typeId == OreType) { _scan.OreVol += v; _scan.OreMass += m; _scan.OreCount++; }
            else if (typeId == IngotType) { _scan.IngotVol += v; _scan.IngotMass += m; _scan.IngotCount++; }
            else if (typeId == CompType) { _scan.CompVol += v; _scan.CompMass += m; _scan.CompCount++; }
            else if (typeId == AmmoType) { _scan.AmmoVol += v; _scan.AmmoMass += m; _scan.AmmoCount++; }
            else if (typeId == GunType || typeId == O2Type || typeId == H2Type) { _scan.ToolVol += v; _scan.ToolMass += m; _scan.ToolCount++; }
            else { _scan.OtherVol += v; _scan.OtherMass += m; _scan.OtherCount++; }
        }

        void DrawContainerRow(int idx, float rowTop)
        {
            ContainerRow row = _scan.Containers[idx];
            DrawProgressRow(_frame, rowTop, "MyObjectBuilder_Package/Package", row.Name,
                $"{FormatStorage(row.Vol, row.Mass)} ({row.Ratio * 100f:0}%)", row.Ratio, BarColor(row.Ratio));
        }
    }
}