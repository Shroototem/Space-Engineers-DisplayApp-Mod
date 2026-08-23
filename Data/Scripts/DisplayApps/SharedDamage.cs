using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace DisplayApps
{
    static class SharedDamage
    {
        struct DamageInfo
        {
            public long Window;
            public int TotalBlocks;
            public int DamagedBlocks;
            public int ProjectorMissing;
            public float DamagePercent;
        }

        static readonly Dictionary<long, DamageInfo> _cache = new Dictionary<long, DamageInfo>();
        static readonly List<IMySlimBlock> _tmpBlocks = new List<IMySlimBlock>();
        static readonly List<Sandbox.ModAPI.IMyTerminalBlock> _tmpTerms = new List<Sandbox.ModAPI.IMyTerminalBlock>();

        public static float GetDamagePercent(IMyCubeGrid grid, long window)
        {
            if (grid == null) return 0f;
            long gid = grid.EntityId;
            DamageInfo info;
            if (_cache.TryGetValue(gid, out info) && info.Window == window) return info.DamagePercent;
            info = Compute(grid, window);
            _cache[gid] = info;
            return info.DamagePercent;
        }

        public static int GetMissingBlocks(IMyCubeGrid grid, long window)
        {
            if (grid == null) return 0;
            long gid = grid.EntityId;
            DamageInfo info;
            if (_cache.TryGetValue(gid, out info) && info.Window == window) return info.DamagedBlocks + info.ProjectorMissing;
            info = Compute(grid, window);
            _cache[gid] = info;
            return info.DamagedBlocks + info.ProjectorMissing;
        }

        static DamageInfo Compute(IMyCubeGrid grid, long window)
        {
            DamageInfo d = new DamageInfo();
            d.Window = window;
            try
            {
                _tmpBlocks.Clear();
                grid.GetBlocks(_tmpBlocks);
                d.TotalBlocks = _tmpBlocks.Count;
                int damaged = 0;
                for(int i=0;i<_tmpBlocks.Count;i++)
                {
                    var slim = _tmpBlocks[i];
                    if (slim.IsFullIntegrity) continue;
                    damaged++;
                }
                d.DamagedBlocks = damaged;
                // Projectors on same grid with Load Repair projection (treated as any projector with RemainingBlocks>0 and IsProjecting or CustomData contains "Repair")
                _tmpTerms.Clear();
                var ts = Sandbox.ModAPI.MyAPIGateway.TerminalActionsHelper != null ? Sandbox.ModAPI.MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid) : null;
                if (ts != null)
                {
                    ts.GetBlocks(_tmpTerms);
                    int missing = 0;
                    for(int i=0;i<_tmpTerms.Count;i++)
                    {
                        var proj = _tmpTerms[i] as Sandbox.ModAPI.IMyProjector;
                        if (proj == null) continue;
                        bool consider = false;
                        try
                        {
                            string cd = proj.CustomData ?? "";
                            string name = proj.CustomName ?? "";
                            if (cd.IndexOf("Repair", System.StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("Repair", System.StringComparison.OrdinalIgnoreCase)>=0)
                                consider = true;
                            else if (proj.IsProjecting) consider = true;
                        }
                        catch { consider = false; }
                        if (!consider) continue;
                        int rem = 0;
                        try { rem = proj.RemainingBlocks; } catch{}
                        missing += rem;
                    }
                    d.ProjectorMissing = missing;
                }
                int totalDamage = damaged + d.ProjectorMissing;
                if (d.TotalBlocks > 0) d.DamagePercent = (float)totalDamage / d.TotalBlocks * 100f;
                else d.DamagePercent = d.ProjectorMissing > 0 ? 100f : 0f;
            }
            catch{}
            return d;
        }
    }
}
