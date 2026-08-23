using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using MyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;

namespace DisplayApps
{
    [MyTextSurfaceScript("ProjectorInfo", "Info Projector")]
    public class ProjectorApp : AppBase
    {
        class ProjectorRow
        {
            public string Name;
            public string Icon;
            public int Remaining;
            public int Total;
            public float Ratio;
            public string Value;
        }

        class ProjectorMissingRow
        {
            public string BlockName;
            public string Icon;
            public int Count;
            public string Value;
        }

        class ProjectorScan : IScanData
        {
            public readonly List<ProjectorRow> Projectors = new List<ProjectorRow>();
            public readonly List<ProjectorMissingRow> Missing = new List<ProjectorMissingRow>();
            readonly List<ProjectorRow> _poolP = new List<ProjectorRow>();
            readonly List<ProjectorMissingRow> _poolM = new List<ProjectorMissingRow>();
            public int TotalRemaining;
            public int TotalBlocks;
            public string Header;
            public string MissingHeader;

            public void Clear()
            {
                _poolP.AddRange(Projectors);
                _poolM.AddRange(Missing);
                Projectors.Clear();
                Missing.Clear();
                TotalRemaining = 0;
                TotalBlocks = 0;
                Header = null;
                MissingHeader = null;
            }

            public ProjectorRow RentP()
            {
                if (_poolP.Count > 0) { var r = _poolP[_poolP.Count-1]; _poolP.RemoveAt(_poolP.Count-1); return r; }
                return new ProjectorRow();
            }
            public ProjectorMissingRow RentM()
            {
                if (_poolM.Count > 0) { var r = _poolM[_poolM.Count-1]; _poolM.RemoveAt(_poolM.Count-1); return r; }
                return new ProjectorMissingRow();
            }
        }

        readonly Func<ProjectorScan> _scanFunc;
        MySpriteDrawFrame _frame;
        ProjectorScan _scan;
        readonly Action<int,float> _drawProj;
        readonly Action<int,float> _drawMiss;

        public ProjectorApp(MySurface surface, MyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawProj = DrawProjRow;
            _drawMiss = DrawMissRow;
        }

        protected override void RunApp()
        {
            ProjectorScan scan = GetGridScan(_scanFunc);
            _scan = scan;
            using (var frame = BeginAppFrame("PROJECTOR STATUS", "MISSING BLOCKS FROM PROJECTION", "MyObjectBuilder_Projector/Projector", new Color(120, 180, 230)))
            {
                _frame = frame;
                if (GuardRemoteGrid(frame, scan)) return;
                if (scan.Projectors.Count == 0)
                {
                    DrawEmpty(frame, "NO PROJECTORS ON GRID");
                    return;
                }
                bool showProjectors = ParseSectionBool("ShowProjectors", true);
                bool showMissing = ParseSectionBool("ShowMissing", true);

                AddText(frame, scan.Header, new Vector2(Left, 48f * S), 0.46f * S, FgColor, TextAlignment.LEFT);
                AddText(frame, "REMAINING: " + scan.TotalRemaining + "/" + scan.TotalBlocks, new Vector2(Right, 48f * S), 0.46f * S, new Color(120,130,145), TextAlignment.RIGHT);
                DrawDivider(frame, 60f);

                float y0 = 74f * S;
                float bottom = Bottom;
                if (!showProjectors && !showMissing)
                {
                    DrawEmpty(frame, "ALL SECTIONS HIDDEN");
                    return;
                }
                if (showProjectors && showMissing && ConfigScroll)
                {
                    float headerH = 18f * S;
                    float gap = 6f * S;
                    float gh = ListGroupHeight(bottom - y0, 2, headerH, gap);
                    DrawListGroup(frame, 0, "PROJECTORS ("+scan.Projectors.Count+")", scan.Projectors.Count, y0, headerH, gh, 28f*S, _drawProj);
                    DrawDivider(frame, (y0+headerH+gh+gap/2f)/S);
                    DrawListGroup(frame, 1, scan.MissingHeader, scan.Missing.Count, ListGroupTop(y0,1,gh,headerH,gap), headerH, gh, 24f*S, _drawMiss);
                }
                else if (showProjectors && showMissing)
                {
                    // stacked without scroll
                    float y = y0;
                    AddText(frame, "PROJECTORS ("+scan.Projectors.Count+")", new Vector2(Left, y), 0.44f*S, new Color(180,190,205), TextAlignment.LEFT);
                    y += 18f*S;
                    int drawn=0;
                    for (int i=0;i<scan.Projectors.Count;i++)
                    {
                        if (y+28f*S > bottom) break;
                        DrawProjRow(i,y); y+=28f*S; drawn++;
                    }
                    if (y+24f*S < bottom)
                    {
                        DrawDivider(frame, y/S);
                        y+=6f*S;
                        AddText(frame, scan.MissingHeader, new Vector2(Left,y),0.44f*S,new Color(180,190,205),TextAlignment.LEFT);
                        y+=18f*S;
                        for (int i=0;i<scan.Missing.Count;i++)
                        {
                            if (y+24f*S > bottom) break;
                            DrawMissRow(i,y); y+=24f*S;
                        }
                    }
                }
                else if (showProjectors)
                {
                    int drawn = DrawListGroup(frame,0,null,scan.Projectors.Count,y0,0f,bottom-y0,28f*S,_drawProj);
                    if (!ConfigScroll && scan.Projectors.Count>drawn) DrawMore(frame,$"+{scan.Projectors.Count-drawn} MORE");
                }
                else if (showMissing)
                {
                    int drawn = DrawListGroup(frame,0,scan.MissingHeader,scan.Missing.Count,y0,18f*S,bottom-y0,24f*S,_drawMiss);
                    if (!ConfigScroll && scan.Missing.Count>drawn) DrawMore(frame,$"+{scan.Missing.Count-drawn} MORE");
                }
            }
        }

        void DrawProjRow(int idx,float y)
        {
            var r = _scan.Projectors[idx];
            DrawProgressRow(_frame, y, r.Icon, r.Name, r.Value, r.Ratio, new Color(120,180,230));
        }
        void DrawMissRow(int idx,float y)
        {
            var r = _scan.Missing[idx];
            float ratio = _scan.TotalRemaining>0? (float)r.Count/_scan.TotalRemaining:0f;
            DrawProgressRow(_frame, y, r.Icon, r.BlockName, r.Value, ratio, new Color(230,180,90));
        }

        bool ParseSectionBool(string key, bool fallback)
        {
            string v;
            if (TryGetSectionConfig(key, out v))
            {
                bool b; if (bool.TryParse(v,out b)) return b;
            }
            return fallback;
        }

        bool TryGetSectionConfig(string key, out string value)
        {
            // use base TryGetConfigValue via reflection? It's private, so we re-parse via AppBase.ReadConfigValue static
            var tb = Block as Sandbox.ModAPI.IMyTerminalBlock;
            if (tb != null)
            {
                string rv = AppBase.ReadConfigValue(tb, AppRegionName, key);
                if (rv != null) { value=rv; return true;}
                rv = AppBase.ReadConfigValue(tb, "DEFAULT", key);
                if (rv != null) { value=rv; return true;}
            }
            value=null; return false;
        }

        ProjectorScan ScanGrid()
        {
            RefreshTerminalBlocks();
            ProjectorScan scan = RentScan<ProjectorScan>();
            var missingCounts = new Dictionary<string,int>(StringComparer.Ordinal);
            var missingIcons = new Dictionary<string,string>(StringComparer.Ordinal);
            var tmpBlocks = new List<MySlimBlock>();
            for (int i=0;i<TerminalBlocks.Count;i++)
            {
                var tb = TerminalBlocks[i];
                var proj = tb as Sandbox.ModAPI.IMyProjector;
                if (proj == null) continue;
                int total = 0, remaining = 0;
                bool isProjecting = false;
                try { isProjecting = proj.IsProjecting; } catch { isProjecting = proj.Enabled; }
                try { total = proj.TotalBlocks; } catch { total = 0; }
                try { remaining = proj.RemainingBlocks; } catch { remaining = 0; }

                scan.TotalBlocks += total;
                scan.TotalRemaining += remaining;
                var row = scan.RentP();
                row.Name = Truncate(BlockName(proj), 20);
                row.Total = total;
                row.Remaining = remaining;
                row.Ratio = total>0 ? (float)remaining/total : 0f;
                row.Value = remaining + "/" + total + " (" + (row.Ratio*100f).ToString("0")+"%)";
                row.Icon = "MyObjectBuilder_Projector/Projector";
                scan.Projectors.Add(row);

                if (isProjecting && remaining>0)
                {
                    MyCubeGrid projected = null;
                    try { projected = proj.ProjectedGrid as MyCubeGrid; } catch {}
                    if (projected != null && total > 0)
                    {
                        tmpBlocks.Clear();
                        try { projected.GetBlocks(tmpBlocks); } catch {}
                        var projCounts = new Dictionary<string,int>(StringComparer.Ordinal);
                        var projIcons = new Dictionary<string,string>(StringComparer.Ordinal);
                        for (int k = 0; k < tmpBlocks.Count; k++)
                        {
                            var slim = tmpBlocks[k];
                            if (slim == null || slim.BlockDefinition == null) continue;
                            string subtype = "";
                            try { subtype = slim.BlockDefinition.Id.SubtypeId.ToString(); } catch { try { subtype = slim.BlockDefinition.DisplayNameText ?? "Block"; } catch { subtype = "Block"; } }
                            string display = FormatItemName(subtype);
                            if (display.Length == 0) display = "BLOCK";
                            display = Truncate(display, 22);
                            int curP = 0;
                            projCounts.TryGetValue(display, out curP);
                            projCounts[display] = curP + 1;
                            if (!projIcons.ContainsKey(display))
                            {
                                string icon = BlockIcon(slim, "MyObjectBuilder_Component/Construction");
                                projIcons[display] = icon;
                            }
                        }
                        float factor = total > 0 ? (float)remaining / total : 1f;
                        foreach (var kv in projCounts)
                        {
                            int scaled = factor >= 0.999f ? kv.Value : (int)(kv.Value * factor + 0.5f);
                            if (scaled <= 0 && kv.Value > 0) scaled = 1;
                            int cur = 0;
                            missingCounts.TryGetValue(kv.Key, out cur);
                            missingCounts[kv.Key] = cur + scaled;
                            if (!missingIcons.ContainsKey(kv.Key)) missingIcons[kv.Key] = projIcons[kv.Key];
                        }
                    }
                    else
                    {
                        // Fallback: per-projector generic when projected grid not available
                        int cur2 = 0;
                        string key2 = Truncate(BlockName(proj), 22) + " missing";
                        missingCounts.TryGetValue(key2, out cur2);
                        missingCounts[key2] = cur2 + remaining;
                        if (!missingIcons.ContainsKey(key2)) missingIcons[key2] = "MyObjectBuilder_Component/SteelPlate";
                    }
                }
            }
            scan.Projectors.Sort((a,b)=> b.Remaining.CompareTo(a.Remaining));
            foreach(var kv in missingCounts)
            {
                var r = scan.RentM();
                r.BlockName = kv.Key;
                r.Count = kv.Value;
                r.Icon = missingIcons[kv.Key];
                r.Value = "x"+kv.Value;
                scan.Missing.Add(r);
            }
            scan.Missing.Sort((a,b)=> b.Count.CompareTo(a.Count));
            scan.Header = "PROJECTORS: "+scan.Projectors.Count;
            scan.MissingHeader = "MISSING BLOCKS ("+scan.Missing.Count+" types, "+scan.TotalRemaining+" total)";
            return scan;
        }
    }
}
