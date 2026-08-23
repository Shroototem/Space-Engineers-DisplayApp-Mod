using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;

namespace DisplayApps
{
    /// <summary>
    /// Player locator + auto door handler.
    /// - Door locations cached slow poll (Update100 = every 100 ticks)
    /// - Player location fast poll (Update10 = every 10 ticks) -> open/close fast
    /// Doors are auto-opened when player is within threshold and auto-closed otherwise.
    /// Uses GPS/world position distance check.
    /// </summary>
    static class DoorProximityHandler
    {
        class CachedDoor
        {
            public long EntityId;
            public Vector3D WorldPos;
            public VRage.Game.ModAPI.IMyCubeGrid Grid;
        }

        static readonly List<CachedDoor> _doors = new List<CachedDoor>();
        static readonly Dictionary<long, CachedDoor> _doorById = new Dictionary<long, CachedDoor>();
        static long _lastDoorCacheWindow = -1;
        static Vector3D _lastPlayerPos;
        static bool _hasPlayerPos;
        static readonly HashSet<long> _openDoors = new HashSet<long>();

        // Configurable distance in meters (GPS radius)
        const float OpenDistance = 4.0f;
        const float CloseDistance = 5.0f; // hysteresis to avoid flicker

        static readonly List<Vector3D> _broadcastPos = new List<Vector3D>();
        public static void Tick(int gameplayFrame)
        {
            try
            {
                long window100 = gameplayFrame / 100;
                if (window100 != _lastDoorCacheWindow)
                {
                    RefreshDoorCache(window100);
                }
                _broadcastPos.Clear();
                GetBroadcastingPlayerPositions(_broadcastPos);
                if (_broadcastPos.Count > 0)
                {
                    _lastPlayerPos = _broadcastPos[0];
                    _hasPlayerPos = true;
                    UpdateDoorsMulti(_broadcastPos);
                }
                else
                {
                    _hasPlayerPos = false;
                    // No broadcasting player -> close all doors
                    foreach(var id in _openDoors) SetDoorState(id, false);
                    _openDoors.Clear();
                }
            }
            catch { }
        }

        static void GetBroadcastingPlayerPositions(List<Vector3D> outPos)
        {
            outPos.Clear();
            try
            {
                var s = MyAPIGateway.Session;
                if (s == null) return;
                List<VRage.Game.ModAPI.IMyGps> gpsList = null;
                try { gpsList = new List<VRage.Game.ModAPI.IMyGps>(); s.GPS.GetGpsList(0, gpsList); } catch { gpsList = null; }
                if (gpsList != null)
                {
                    bool hasGps = gpsList.Count > 0;
                    for(int i=0;i<gpsList.Count;i++) { var g=gpsList[i]; if(g==null) continue; bool show=false; try{show=g.ShowOnHud;}catch{continue;} if(!show) continue; Vector3D c; try{c=g.Coords;}catch{continue;} outPos.Add(c); }
                    if(outPos.Count>0) return;
                    if(hasGps) return; // has GPS but none broadcasting -> respect broadcast off, no fallback
                }
                // No GPS at all -> fallback to character positions (assume broadcasting on for new worlds)
                try
                {
                    var players = new List<VRage.Game.ModAPI.IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(players);
                    for(int i=0;i<players.Count;i++) { var p=players[i]; if(p==null||p.Character==null) continue; Vector3D pos; try{pos=p.GetPosition();}catch{try{pos=p.Character.GetPosition();}catch{continue;}} outPos.Add(pos); }
                    if(outPos.Count>0) return;
                } catch {}
                Vector3D pp;
                if(TryGetPlayerPositionFallback(out pp)) outPos.Add(pp);
            } catch {}
        }

        static bool TryGetPlayerPositionFallback(out Vector3D pos)
        {
            pos = default(Vector3D);
            try
            {
                var session = MyAPIGateway.Session;
                if (session == null) return false;
                try { var local = session.LocalHumanPlayer; if (local != null && local.Character != null) { pos = local.Character.GetPosition(); return true; } } catch { }
                try { var p = session.Player; if (p != null && p.Character != null) { pos = p.Character.GetPosition(); return true; } } catch { }
                try { var cam = session.Camera; if (cam != null) { pos = cam.WorldMatrix.Translation; return true; } } catch { }
            }
            catch { }
            return false;
        }

        static bool TryGetPlayerPosition(out Vector3D pos)
        {
            pos = default(Vector3D);
            var tmp = new List<Vector3D>();
            GetBroadcastingPlayerPositions(tmp);
            if(tmp.Count>0){pos=tmp[0]; return true;}
            return false; // GPS+broadcast only
        }

        static void RefreshDoorCache(long window)
        {
            _lastDoorCacheWindow = window;
            // Scan terminal systems for doors. To avoid expensive global scan every window,
            // we scan via all grids' terminal blocks where displays exist? For simplicity,
            // scan via MyAPIGateway.TerminalActionsHelper global enumeration of grids?
            // Use entity buffer approach: collect doors from all terminals of known host grids.
            // However we don't track host grids here; instead do lightweight global entity scan
            // only on slow poll (every 100 ticks) - acceptable.
            try
            {
                _doors.Clear();
                _doorById.Clear();
                if (MyAPIGateway.Entities == null) return;

                var grids = new HashSet<VRage.ModAPI.IMyEntity>();
                MyAPIGateway.Entities.GetEntities(grids, e => e is VRage.Game.ModAPI.IMyCubeGrid);
                var terminalBlocks = new List<Sandbox.ModAPI.IMyTerminalBlock>(128);
                foreach (var ent in grids)
                {
                    var grid = ent as VRage.Game.ModAPI.IMyCubeGrid;
                    if (grid == null) continue;
                    // Skip grids far from player if we have player pos (optimization: 200m radius)
                    if (_hasPlayerPos)
                    {
                        Vector3D gridPos = grid.WorldMatrix.Translation;
                        double distSq = (gridPos - _lastPlayerPos).LengthSquared();
                        if (distSq > 40000) // 200m^2
                            continue;
                    }
                    var ts = MyAPIGateway.TerminalActionsHelper != null ? MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid) : null;
                    if (ts == null) continue;
                    terminalBlocks.Clear();
                    ts.GetBlocks(terminalBlocks);
                    for (int i = 0; i < terminalBlocks.Count; i++)
                    {
                        var door = terminalBlocks[i] as Sandbox.ModAPI.IMyDoor;
                        if (door == null) continue;
                        // Optionally also handle hangar doors / airtight doors
                        // Only auto doors that are not locked via CustomData? Respect opt-out via CustomData "AutoDoor: false"
                        try
                        {
                            string cd = door.CustomData ?? "";
                            if (cd.IndexOf("AutoDoor: false", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        }
                        catch { }
                        var cd2 = new CachedDoor();
                        cd2.EntityId = door.EntityId;
                        try { cd2.WorldPos = door.GetPosition(); }
                        catch { cd2.WorldPos = door.WorldMatrix.Translation; }
                        cd2.Grid = grid;
                        _doors.Add(cd2);
                        _doorById[cd2.EntityId] = cd2;
                    }
                }
            }
            catch { }
        }

        static void UpdateDoors(Vector3D playerPos)
        {
            var tmp = new List<Vector3D>(1); tmp.Add(playerPos); UpdateDoorsMulti(tmp);
        }

        static void UpdateDoorsMulti(List<Vector3D> playerPositions)
        {
            for (int i = 0; i < _doors.Count; i++)
            {
                var cd = _doors[i];
                double minDist = double.MaxValue;
                for(int p=0;p<playerPositions.Count;p++) { double d=(cd.WorldPos-playerPositions[p]).Length(); if(d<minDist) minDist=d; }
                bool shouldOpen = minDist <= OpenDistance;
                bool shouldClose = minDist >= CloseDistance;
                bool isOpen = _openDoors.Contains(cd.EntityId);
                bool targetOpen;
                if (isOpen) targetOpen = !shouldClose;
                else targetOpen = shouldOpen;
                if (targetOpen != isOpen)
                {
                    SetDoorState(cd.EntityId, targetOpen);
                    if (targetOpen) _openDoors.Add(cd.EntityId);
                    else _openDoors.Remove(cd.EntityId);
                }
                else if (!isOpen && shouldOpen)
                {
                    SetDoorState(cd.EntityId, true);
                    _openDoors.Add(cd.EntityId);
                }
            }
            if (_openDoors.Count > _doors.Count)
            {
                var toRemove = new List<long>();
                foreach (var id in _openDoors) if (!_doorById.ContainsKey(id)) toRemove.Add(id);
                for (int i = 0; i < toRemove.Count; i++) _openDoors.Remove(toRemove[i]);
            }
        }

        static void SetDoorState(long entityId, bool open)
        {
            try
            {
                var ent = MyAPIGateway.Entities.GetEntityById(entityId);
                var door = ent as Sandbox.ModAPI.IMyDoor;
                if (door == null)
                {
                    // Fallback: try terminal block interface via GetEntityById as IMyTerminalBlock
                    var tb = ent as Sandbox.ModAPI.IMyTerminalBlock;
                    door = tb as Sandbox.ModAPI.IMyDoor;
                    if (door == null) return;
                }
                if (open)
                {
                    try { door.OpenDoor(); } catch { try { door.Enabled = true; } catch { } }
                }
                else
                {
                    try { door.CloseDoor(); } catch { }
                }
            }
            catch { }
        }
    }
}
