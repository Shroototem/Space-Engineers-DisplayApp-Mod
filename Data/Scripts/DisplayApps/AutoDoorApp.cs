using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using MySurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using MyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using MyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace DisplayApps
{
    [MyTextSurfaceScript("AutoDoors", "Info Auto Doors")]
    public class AutoDoorApp : AppBase
    {
        // Override update frequency: need fast poll for player (Update10) but door cache slow (Update100)
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        class DoorRow
        {
            public string Name;
            public Vector3D WorldPos;
            public float Distance;
            public bool IsOpen;
            public string StateText;
            public Color StateColor;
            public MyTerminalBlock Block;
        }

        class AutoDoorScan : IScanData
        {
            public readonly List<DoorRow> Doors = new List<DoorRow>();
            readonly List<DoorRow> _pool = new List<DoorRow>();
            public Vector3D PlayerPos;
            public bool HasPlayer;
            public string GpsText;
            public string Header;
            public int OpenCount;
            public int TotalCount;
            public float Range;
            public long CachedWindow = -1;
            // Cached door locations (slow poll)
            public readonly List<DoorRow> CachedDoors = new List<DoorRow>();

            public void Clear()
            {
                _pool.AddRange(Doors);
                Doors.Clear();
                HasPlayer = false;
                GpsText = null;
                Header = null;
                OpenCount = 0;
            }
            public DoorRow Rent()
            {
                if (_pool.Count>0){ var r=_pool[_pool.Count-1]; _pool.RemoveAt(_pool.Count-1); return r; }
                return new DoorRow();
            }
        }

        readonly Func<AutoDoorScan> _scanFunc;
        MySpriteDrawFrame _frame;
        AutoDoorScan _scan;
        readonly Action<int,float> _drawDoorRow;

        // Fast vs slow poll tracking
        long _lastCacheWindow = -1;
        readonly List<MyTerminalBlock> _doorBlocks = new List<MyTerminalBlock>();
        static readonly Dictionary<long, List<Vector3D>> _gridDoorCache = new Dictionary<long, List<Vector3D>>();
        static readonly Dictionary<long, long> _gridCacheWindow = new Dictionary<long, long>();

        public AutoDoorApp(MySurface surface, MyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
            _scanFunc = ScanGrid;
            _drawDoorRow = DrawDoorRow;
        }

        // Override Run to bypass slot gating - need Update10 fast poll for player, Update100 for cache
        public override void Run()
        {
            int tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (Perf.LivePerfApps > 0) Perf.CountInvocation("AutoDoorApp", tick);
            try
            {
                AppTerminalControls.EnsureRegistered(Block as MyTerminalBlock);
                BgColor = Surface.ScriptBackgroundColor;
                FgColor = Surface.ScriptForegroundColor;
                // Fast poll: player position and door open/close every Update10
                FastTick(tick);
                // Slow poll: draw only at Update100 slot to avoid per-10 drawing cost
                if ((tick / 10) % 10 != GetUpdateSlot()) return;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                LoadConfig();
                RunApp();
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                MyTerminalBlock tb = Block as MyTerminalBlock;
                string source = (tb != null && tb.CustomName.Length > 0) ? tb.CustomName : null;
                string label = "AutoDoorApp [" + (source ?? ("Block " + Block.EntityId)) + "]";
                if (Perf.LivePerfApps > 0) Perf.Record("AutoDoorApp", label, ms, MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds);
            }
            catch { }
        }

        int GetUpdateSlot()
        {
            return Block != null ? (int)(Block.EntityId % 10) : 0;
        }

        readonly List<Vector3D> _broadcastPos = new List<Vector3D>();
        void FastTick(int tick)
        {
            try
            {
                long window100 = tick / 100;
                bool cacheNow = window100 != _lastCacheWindow;
                if (cacheNow)
                {
                    _lastCacheWindow = window100;
                    RefreshDoorCache(window100);
                }
                _broadcastPos.Clear();
                GetBroadcastingPlayerPositions(_broadcastPos);
                if (_broadcastPos.Count > 0)
                {
                    UpdateDoorStatesMulti(_broadcastPos);
                }
                else
                {
                    // No broadcasting player -> close all doors (optional, keep current state but ensure closed)
                    // Close all cached doors
                    for (int i = 0; i < _doorBlocks.Count; i++)
                    {
                        var door = _doorBlocks[i] as Sandbox.ModAPI.IMyDoor;
                        if (door == null) continue;
                        try
                        {
                            string st = door.Status.ToString();
                            if (st == "Open" || st == "Opening") door.CloseDoor();
                        } catch {}
                    }
                }
            }
            catch { }
        }

        void GetBroadcastingPlayerPositions(List<Vector3D> outPos)
        {
            outPos.Clear();
            try
            {
                var s = MyAPIGateway.Session;
                if (s == null) return;

                // Gate = the suit radio broadcaster, toggled in-game with the
                // default "O" keybind (MyCharacter.SwitchBroadcasting).
                // It flips MyCharacter.RadioBroadcaster.WantsToBeEnabled.
                // Players with broadcasting off are skipped entirely - doors
                // do not auto-open for them. Position is always the real
                // character position (GPS markers are NOT positions).
                var players = new List<VRage.Game.ModAPI.IMyPlayer>();
                try { MyAPIGateway.Players.GetPlayers(players); } catch { return; }
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || p.Character == null) continue;
                    bool broadcasting = IsSuitBroadcasting(p);
                    if (!broadcasting) continue;
                    bool isLocal = false;
                    try { isLocal = ReferenceEquals(p, s.LocalHumanPlayer) || ReferenceEquals(p, s.Player); } catch {}
                    Vector3D pos;
                    if (isLocal)
                    {
                        if (!TryGetLocalCharacterPos(out pos)) continue;
                    }
                    else
                    {
                        try { pos = p.GetPosition(); } catch { try { pos = p.Character.GetPosition(); } catch { continue; } }
                    }
                    outPos.Add(pos);
                }
            }
            catch {}
        }

        /// <summary>Live suit broadcasting state (default keybind O). Reads
        /// EnabledBroadcasting off the controlled entity - the same flag the
        /// HUD "player broadcasting" stat uses. Whitelist-safe: only the
        /// Sandbox.Game.Entities.IMyControllableEntity interface (allowed
        /// namespace) is referenced, never concrete MyCharacter.</summary>
        static bool IsSuitBroadcasting(VRage.Game.ModAPI.IMyPlayer p)
        {
            try
            {
                var c = p.Controller;
                if (c == null) return false;
                var ice = c.ControlledEntity as Sandbox.Game.Entities.IMyControllableEntity;
                if (ice != null) return ice.EnabledBroadcasting;
            }
            catch {}
            return false;
        }

        bool TryGetLocalCharacterPos(out Vector3D pos)
        {
            pos = default(Vector3D);
            try
            {
                var s = MyAPIGateway.Session;
                if (s == null) return false;
                try { var l = s.LocalHumanPlayer; if (l != null && l.Character != null) { pos = l.Character.GetPosition(); return true; } } catch {}
                try { var p = s.Player; if (p != null && p.Character != null) { pos = p.Character.GetPosition(); return true; } } catch {}
                try { var cam = s.Camera; if (cam != null) { pos = cam.WorldMatrix.Translation; return true; } } catch {}
            }
            catch {}
            return false;
        }

        void RefreshDoorCache(long window)
        {
            // Doors added/removed at Update100 via AppCore Window() - not fast scanned.
            // AppCore.GetGridScan caches ScanGrid per Window (100 ticks); this cache mirrors that
            // so the displayed door list (ScanGrid) and the prox cache stay in sync at Update100.
            // Player position and open/close are fast (Update10) in UpdateDoorStates.
            try
            {
                var grid = CurrentScanGrid;
                if (grid == null) return;
                long gid = grid.EntityId;
                // Reuse static per-grid cache at Update100 - AppCore controls add/remove speed
                long w;
                if (_gridCacheWindow.TryGetValue(gid, out w) && w == window) return;
                _gridCacheWindow[gid] = window;

                _doorBlocks.Clear();
                var ts = MyAPIGateway.TerminalActionsHelper != null ? MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid) : null;
                if (ts == null) return;
                var tmp = new List<MyTerminalBlock>();
                ts.GetBlocks(tmp);
                // Filter doors
                _doorBlocks.Clear();
                for(int i=0;i<tmp.Count;i++)
                {
                    var d = tmp[i] as Sandbox.ModAPI.IMyDoor;
                    if (d==null) continue;
                    // Respect opt-out CustomData
                    try { string cd = d.CustomData ?? ""; if(cd.IndexOf("AutoDoor: false",StringComparison.OrdinalIgnoreCase)>=0) continue; } catch{}
                    // Check enabled auto toggle via CustomData "AutoDoor: true" default true
                    _doorBlocks.Add((MyTerminalBlock)d);
                }
                // Store positions for fast poll
                var posList = new List<Vector3D>(_doorBlocks.Count);
                for(int i=0;i<_doorBlocks.Count;i++)
                {
                    try { posList.Add(_doorBlocks[i].GetPosition()); }
                    catch { posList.Add(_doorBlocks[i].WorldMatrix.Translation); }
                }
                _gridDoorCache[gid]=posList;
            }
            catch{}
        }

        void UpdateDoorStates(Vector3D playerPos)
        {
            var tmp = new List<Vector3D>(1);
            tmp.Add(playerPos);
            UpdateDoorStatesMulti(tmp);
        }

        void UpdateDoorStatesMulti(List<Vector3D> playerPositions)
        {
            try
            {
                var grid = CurrentScanGrid;
                if (grid==null) return;
                long gid = grid.EntityId;
                List<Vector3D> posList;
                if(!_gridDoorCache.TryGetValue(gid,out posList)) return;
                if(_doorBlocks.Count != posList.Count) return;
                float openDist = GetRange();
                float closeDist = openDist + 2.5f;
                for(int i=0;i<_doorBlocks.Count;i++)
                {
                    var blk = _doorBlocks[i];
                    var door = blk as Sandbox.ModAPI.IMyDoor;
                    if(door==null) continue;
                    double minDist = double.MaxValue;
                    for(int p=0;p<playerPositions.Count;p++)
                    {
                        double d = (posList[i]-playerPositions[p]).Length();
                        if(d < minDist) minDist = d;
                    }
                    bool isOpen = false;
                    try
                    {
                        string st = door.Status.ToString();
                        isOpen = st == "Open" || st == "Opening";
                    } catch{}
                    bool shouldOpen = minDist <= openDist;
                    bool shouldClose = minDist >= closeDist;
                    if(!isOpen && shouldOpen)
                    {
                        try{ door.OpenDoor(); } catch{}
                    }
                    else if(isOpen && shouldClose)
                    {
                        try{ door.CloseDoor(); } catch{}
                    }
                }
            }
            catch{}
        }

        protected override void RunApp()
        {
            AutoDoorScan scan = GetGridScan(_scanFunc);
            _scan = scan;
            using(var frame = BeginAppFrame("AUTO DOORS", "PLAYER PROXIMITY DOOR CONTROL", "MyObjectBuilder_Door/Door", new Color(80,200,160)))
            {
                _frame = frame;
                if(GuardRemoteGrid(frame,scan)) return;

                bool showGps = GetBool("ShowGps", true);
                bool showDoors = GetBool("ShowDoors", true);
                bool autoEnabled = GetBool("AutoOpen", true);

                if(!autoEnabled)
                {
                    AddText(frame, "AUTO OPEN: DISABLED", new Vector2(Cx, 60f*S), 0.5f*S, new Color(230,60,50), TextAlignment.CENTER);
                }

                float y = 52f*S;
                if(showGps)
                {
                    AddText(frame, "PLAYER LOCATION", new Vector2(Left, y),0.46f*S,new Color(180,190,205),TextAlignment.LEFT);
                    y+=18f*S;
                    if(scan.HasPlayer)
                    {
                        // GPS format like "GPS:Player:1234:5678:9101:"
                        string gps = $"GPS:Player:{scan.PlayerPos.X:0}: {scan.PlayerPos.Y:0}: {scan.PlayerPos.Z:0}:";
                        AddText(frame, gps, new Vector2(Left, y),0.42f*S,FgColor,TextAlignment.LEFT);
                        AddText(frame, scan.GpsText, new Vector2(Right, y),0.42f*S,new Color(80,200,230),TextAlignment.RIGHT);
                    }
                    else
                    {
                        AddText(frame, "NO PLAYER FOUND", new Vector2(Left,y),0.42f*S,new Color(220,70,60),TextAlignment.LEFT);
                    }
                    y+=22f*S;
                    DrawDivider(frame, y/S);
                    y+=6f*S;
                }

                if(!showDoors) return;

                AddText(frame, scan.Header, new Vector2(Left,y),0.46f*S,FgColor,TextAlignment.LEFT);
                AddText(frame, scan.OpenCount+"/"+scan.TotalCount+" OPEN", new Vector2(Right,y),0.46f*S,new Color(120,130,145),TextAlignment.RIGHT);
                y+=20f*S;
                DrawDivider(frame, y/S);
                y+=6f*S;

                var doors = scan.Doors;
                if(doors.Count==0)
                {
                    DrawEmpty(frame,"NO DOORS ON GRID");
                    return;
                }
                int drawn = DrawListGroup(frame,0,null,doors.Count,y,0f,Bottom-y,28f*S,_drawDoorRow);
                if(!ConfigScroll && doors.Count>drawn) DrawMore(frame,$"+{doors.Count-drawn} MORE");
            }
        }

        bool GetBool(string key,bool fallback)
        {
            var tb = Block as MyTerminalBlock;
            if(tb!=null)
            {
                string v = AppBase.ReadConfigValue(tb, AppRegionName, key);
                if(v==null) v=AppBase.ReadConfigValue(tb,"DEFAULT",key);
                if(v!=null){ bool b; if(bool.TryParse(v,out b)) return b; }
            }
            return fallback;
        }

        /// <summary>Auto-open range in meters from the terminal slider (legacy
        /// DoorDistance CustomData still respected). Default 4 m.</summary>
        float GetRange()
        {
            var tb = Block as MyTerminalBlock;
            if(tb!=null)
            {
                string v = AppBase.ReadConfigValue(tb, AppRegionName, "DoorRange");
                if(v==null) v=AppBase.ReadConfigValue(tb,"DEFAULT","DoorRange");
                if(v==null) v=AppBase.ReadConfigValue(tb, AppRegionName, "DoorDistance");
                if(v==null) v=AppBase.ReadConfigValue(tb,"DEFAULT","DoorDistance");
                float d;
                if(v!=null && float.TryParse(v,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out d) && d>0.5f)
                    return d>25f ? 25f : d;
            }
            return 4f;
        }

        AutoDoorScan ScanGrid()
        {
            // Door list added/removed at Update100 via AppCore.GetGridScan cache (Window = tick/100)
            // RefreshTerminalBlocks here is only executed when cache misses (once per Window), so list updates at Update100
            RefreshTerminalBlocks();
            AutoDoorScan scan = RentScan<AutoDoorScan>();
            var bPosList = new List<Vector3D>();
            GetBroadcastingPlayerPositions(bPosList);
            Vector3D ppos = default(Vector3D);
            bool hasPos = bPosList.Count > 0;
            if(hasPos)
            {
                ppos = bPosList[0];
                scan.HasPlayer=true;
                scan.PlayerPos=ppos;
                scan.GpsText = $"{ppos.X:0.0}, {ppos.Y:0.0}, {ppos.Z:0.0} (+{bPosList.Count} broadcasting)";
                if (bPosList.Count > 1) scan.GpsText += $" x{bPosList.Count}";
            }
            // Build DoorRows from current terminal blocks (but using cached positions for speed)
            // For display we need sorted by distance to nearest broadcasting player (multiplayer)
            for(int i=0;i<TerminalBlocks.Count;i++)
            {
                var blk = TerminalBlocks[i] as Sandbox.ModAPI.IMyDoor;
                if(blk==null) continue;
                var term = blk as MyTerminalBlock;
                Vector3D wpos;
                try{ wpos = term.GetPosition(); } catch{ wpos = term.WorldMatrix.Translation; }
                float dist = 0f;
                if (hasPos)
                {
                    double minD = double.MaxValue;
                    for(int p=0;p<bPosList.Count;p++) { double d=(wpos-bPosList[p]).Length(); if(d<minD) minD=d; }
                    dist = (float)minD;
                }
                bool isOpen=false;
                try
                {
                    string st = blk.Status.ToString();
                    isOpen = st == "Open" || st == "Opening";
                } catch{}
                var row = scan.Rent();
                row.Name = Truncate(BlockName(term),18);
                row.WorldPos = wpos;
                row.Distance = dist;
                row.IsOpen = isOpen;
                row.StateText = isOpen? "OPEN" : "CLOSED";
                row.StateColor = isOpen? new Color(50,210,90): new Color(140,145,155);
                row.Block = term;
                scan.Doors.Add(row);
            }
            scan.Doors.Sort((a,b)=> a.Distance.CompareTo(b.Distance));
            int open=0;
            for(int i=0;i<scan.Doors.Count;i++) if(scan.Doors[i].IsOpen) open++;
            scan.OpenCount=open;
            scan.TotalCount=scan.Doors.Count;
            scan.Range = GetRange();
            scan.Header = "DOORS ("+scan.TotalCount+") - AUTO RANGE "+scan.Range.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"m";
            return scan;
        }

        void DrawDoorRow(int idx,float y)
        {
            var r = _scan.Doors[idx];
            float range = _scan.Range > 0.5f ? _scan.Range : 4f;
            string distText = _scan.HasPlayer ? $"{r.Distance:0.0}m" : "--";
            // Icon for door
            _frame.Add(Icon("MyObjectBuilder_Door/Door", new Vector2(Left+10f*S, y+8f*S), 16f*S, r.StateColor));
            AddText(_frame, r.Name, new Vector2(Left+24f*S,y),0.44f*S,FgColor,TextAlignment.LEFT);
            AddText(_frame, r.StateText, new Vector2(Right-50f*S,y),0.42f*S,r.StateColor,TextAlignment.RIGHT);
            AddText(_frame, distText, new Vector2(Right,y+14f*S),0.38f*S,new Color(120,130,145),TextAlignment.RIGHT);
            // thin bar indicating proximity (green when within auto-open range);
            // empty by default - only fills while a broadcasting player is tracked;
            // decays to 0 at the close range (open + 2.5 m)
            float ratio = 0f;
            if (_scan.HasPlayer)
            {
                float closeRange = range + 2.5f;
                ratio = r.Distance <= range ? 1f : (r.Distance >= closeRange ? 0f : 1f - (r.Distance-range)/(closeRange-range));
            }
            RectangleF bar = new RectangleF(new Vector2(Left+24f*S, y+16f*S), new Vector2(Right-Left-24f*S-60f*S, 4f*S));
            DrawBar(_frame, bar, ratio, ratio>0.5f?new Color(50,210,90):new Color(80,90,105));
        }
    }
}
