using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;

using MyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using MyTextPanel = Sandbox.ModAPI.IMyTextPanel;

namespace DisplayApps
{
    /// <summary>
    /// Registers the app settings as terminal controls. Registration must not
    /// create a block type's control list before that type's first block has
    /// constructed - the game skips adding its own controls when the list
    /// already exists, which would strip LCDs and cockpits of their built-in
    /// buttons. So controls are registered per concrete block type, triggered
    /// by the first app instance on a block of that type (by then its list is
    /// fully built and shared by all blocks of the type).
    /// </summary>
    static class AppTerminalControls
    {
        static readonly string[] AllRegions =
        {
            "AssemblerInfo", "ComponentsInfo", "DamageInfo", "DockedInfo", "O2H2",
            "OreIngotInfo", "PerfInfo", "PowerInfo", "StorageInfo"
        };

        static readonly string[] FullListRegions = { "ComponentsInfo", "OreIngotInfo", "PowerInfo", "StorageInfo" };
        static readonly string[] StorageTypeRegions = { "OreIngotInfo", "StorageInfo" };

        public static void EnsureRegistered(MyTerminalBlock block)
        {
            try
            {
                if (MyAPIGateway.TerminalControls == null || block == null) return;
                if (block is Sandbox.ModAPI.IMyCockpit) EnsureFor<IMyCockpit>();
                else if (block is Sandbox.ModAPI.IMyRemoteControl) EnsureFor<IMyRemoteControl>();
                else if (block is Sandbox.ModAPI.IMyTextPanel) EnsureFor<MyTextPanel>();
                else if (block is Sandbox.ModAPI.IMyProgrammableBlock) EnsureFor<IMyProgrammableBlock>();
            }
            catch
            {
            }
        }

        /// <summary>Registration + reposition for one block type. Called every
        /// update of every display, so the steady state must be two cheap set
        /// probes and no game API calls.</summary>
        static void EnsureFor<TBlock>()
        {
            Type t = typeof(TBlock);
            bool needRegister = !_registered.Contains(t);
            if (!needRegister && _repositioned.Contains(t)) return;
            // Block existence does not imply terminal control list creation.
            // Creating the list via GetControls/CreateControl before the game's
            // CreateTerminalControls has populated it permanently suppresses all
            // vanilla + other-mod controls (AreControlsCreated becomes true and
            // the game's early-exit prevents its own adds). Defer until the
            // factory reports the list exists - this check does NOT create the
            // list, unlike GetControls/CreateControl/EnsureControlsAreCreated.
            if (!IsFactoryReady<TBlock>()) return;
            if (needRegister) RegisterFor<TBlock>(MyAPIGateway.TerminalControls);
            RepositionFor<TBlock>();
        }

        static readonly Dictionary<Type, int> _factoryReadyAttempts = new Dictionary<Type, int>();
        const int FactoryReadyDeferTicks = 6;

        static bool IsFactoryReady<TBlock>()
        {
            // Whitelist-safe heuristic: wait a few Update10 cycles (~600ms)
            // after the first sighting of the type. BeforeGameLogicInit
            // schedules CreateTerminalControls for BEFORE_NEXT_FRAME, so a
            // short defer covers the replication race on clients where the
            // text-surface script is instantiated before the block's factory
            // entry exists. This check never calls GetControls/CreateControl,
            // so it never creates the factory entry prematurely (which would
            // permanently suppress vanilla controls).
            Type t = typeof(TBlock);
            int attempts;
            _factoryReadyAttempts.TryGetValue(t, out attempts);
            if (attempts < FactoryReadyDeferTicks)
            {
                _factoryReadyAttempts[t] = attempts + 1;
                return false;
            }
            return true;
        }

        static readonly HashSet<Type> _registered = new HashSet<Type>();
        static readonly List<IMyTerminalControl> _paddingSliders = new List<IMyTerminalControl>();

        /// <summary>Creates a full set of controls for one block type. The game
        /// binds every control to its block type (ITerminalControl.IsVisible
        /// casts the block to it), so each block type needs its own instances.
        /// The type's list already exists here (its first block constructed),
        /// so the game's own controls are intact and ours are appended.</summary>
        static void RegisterFor<TBlock>(IMyTerminalControls helper)
        {
            if (!_registered.Add(typeof(TBlock))) return;
            try
            {
                List<IMyTerminalControl> existing;
                helper.GetControls<TBlock>(out existing);
                if (existing != null)
                {
                    for (int i = 0; i < existing.Count; i++)
                    {
                        if (existing[i].Id == "DisplayApps_TextScroll") return;
                    }
                }
            }
            catch { return; }

            AddLabel<TBlock>(helper, "DisplayApps_Title", "DISPLAYAPPS APP SETTINGS",
                AllRegions);

            AddOnOffSwitch<TBlock>(helper, "DisplayApps_TextScroll", "TextScroll",
                "Auto-scroll lists that don't fit on screen.", AllRegions, "TextScroll", false);
            AddOnOffSwitch<TBlock>(helper, "DisplayApps_SubGrids", "SubGrids",
                "Also scan blocks on subgrids (rotors/pistons/hinges) and connector-docked grids (Physical group).", AllRegions, "SubGrids", false);
            AddOnOffSwitch<TBlock>(helper, "DisplayApps_FullList", "FullList",
                "Show all item types, including ones with 0 stock.", FullListRegions, "FullList", true);

            AddSlider<TBlock>(helper, "DisplayApps_TextScale", "TextScale",
                "Multiplier for the whole layout: text, icons, bars and spacing scale and shift together.", AllRegions, "TextScale", 1f, 0.1f, 4f);
            AddPaddingSlider<TBlock>(helper, "DisplayApps_TextPaddingUp", "Padding Up",
                "Top padding in pixels.", AllRegions, 0, 16f);
            AddPaddingSlider<TBlock>(helper, "DisplayApps_TextPaddingDown", "Padding Down",
                "Bottom padding in pixels.", AllRegions, 1, 16f);
            AddPaddingSlider<TBlock>(helper, "DisplayApps_TextPaddingLeft", "Padding Left",
                "Left padding in pixels.", AllRegions, 2, 16f);
            AddPaddingSlider<TBlock>(helper, "DisplayApps_TextPaddingRight", "Padding Right",
                "Right padding in pixels.", AllRegions, 3, 16f);
            AddButton<TBlock>(helper, "DisplayApps_ResetPadding", "Reset Padding",
                "Reset all padding values back to the default of 16 px.", AllRegions, ResetPadding);
            AddTextbox<TBlock>(helper, "DisplayApps_Groups", "Groups",
                "Comma separated names of terminal block groups. When set, ONLY blocks in these groups are shown. Leave empty for the whole grid.", AllRegions, "Groups", "");
            AddTextbox<TBlock>(helper, "DisplayApps_RemoteGrid", "RemoteGrid",
                "Name of another grid to pull data from instead of the grid the display is on. Leave empty for the local grid.", AllRegions, "RemoteGrid", "");

            AddSeparator<TBlock>(helper, "DisplayApps_AppSeparator", AllRegions);

            AddOnOffSwitch<TBlock>(helper, "DisplayApps_HighlightDamaged", "HighlightDamaged",
                "Highlight the 10 most damaged blocks in the world with a solid outline.", new[] { "DamageInfo" }, "HighlightDamaged", false);
            AddOnOffSwitch<TBlock>(helper, "DisplayApps_PerfLog", "PerfLog",
                "Advanced performance stats on the Performance app (timing histograms, per-display breakdown, slow update list).", new[] { "PerfInfo" }, "PerfLog", false);
            AddStorageType<TBlock>(helper, "DisplayApps_StorageType", "StorageType",
                "Unit display for storage: Both (kg & L), kg only or L only.", StorageTypeRegions);
            AddOreIngotType<TBlock>(helper, "DisplayApps_OreIngotType", "OreIngotType",
                "Which sections the Ores & Ingots app shows: Both, Ores only or Ingots only.", new[] { "OreIngotInfo" });
        }

        static void AddOnOffSwitch<TBlock>(IMyTerminalControls helper, string id, string title, string tooltip, string[] regions, string key, bool defaultValue)
        {
            IMyTerminalControlOnOffSwitch c = helper.CreateControl<IMyTerminalControlOnOffSwitch, TBlock>(id);
            c.Title = MyStringId.GetOrCompute(title);
            c.Tooltip = MyStringId.GetOrCompute(tooltip);
            c.OnText = MyStringId.GetOrCompute("ON");
            c.OffText = MyStringId.GetOrCompute("OFF");
            c.Getter = b => ReadBool(b, key, defaultValue);
            c.Setter = (b, v) => WriteValue(b, key, v ? "true" : "false");
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        static void AddSlider<TBlock>(IMyTerminalControls helper, string id, string title, string tooltip, string[] regions, string key, float defaultValue, float min, float max)
        {
            IMyTerminalControlSlider c = helper.CreateControl<IMyTerminalControlSlider, TBlock>(id);
            c.Title = MyStringId.GetOrCompute(title);
            c.Tooltip = MyStringId.GetOrCompute(tooltip);
            c.SetLimits(min, max);
            c.Getter = b => ReadFloat(b, key, defaultValue);
            c.Setter = (b, v) => WriteValue(b, key, v.ToString("0.##", CultureInfo.InvariantCulture));
            c.Writer = (b, sb) => sb.Append(ReadFloat(b, key, defaultValue).ToString("0.0x", CultureInfo.InvariantCulture));
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        static void AddPaddingSlider<TBlock>(IMyTerminalControls helper, string id, string title, string tooltip, string[] regions, int index, float defaultValue)
        {
            IMyTerminalControlSlider c = helper.CreateControl<IMyTerminalControlSlider, TBlock>(id);
            c.Title = MyStringId.GetOrCompute(title);
            c.Tooltip = MyStringId.GetOrCompute(tooltip);
            c.SetLimits(0f, 128f);
            c.Getter = b => ReadPadding(b, index, defaultValue);
            c.Setter = (b, v) => WritePadding(b, index, v);
            c.Writer = (b, sb) => sb.Append(ReadPadding(b, index, defaultValue).ToString("0.#", CultureInfo.InvariantCulture)).Append(" px");
            c.Visible = b => AppActive(b, regions);
            _paddingSliders.Add(c);
            helper.AddControl<TBlock>(c);
        }

        static void AddButton<TBlock>(IMyTerminalControls helper, string id, string title, string tooltip, string[] regions, Action<MyTerminalBlock> action)
        {
            IMyTerminalControlButton c = helper.CreateControl<IMyTerminalControlButton, TBlock>(id);
            c.Title = MyStringId.GetOrCompute(title);
            c.Tooltip = MyStringId.GetOrCompute(tooltip);
            c.Action = action;
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        /// <summary>Writes the default [16,16,16,16] padding back to CustomData
        /// and refreshes the padding sliders so the new values show at once.</summary>
        static void ResetPadding(MyTerminalBlock block)
        {
            WriteValue(block, "TextPadding", "[16,16,16,16]");
            for (int i = 0; i < _paddingSliders.Count; i++)
            {
                try { _paddingSliders[i].UpdateVisual(); }
                catch { }
            }
        }

        static void AddTextbox<TBlock>(IMyTerminalControls helper, string id, string title, string tooltip, string[] regions, string key, string defaultValue)
        {
            IMyTerminalControlTextbox c = helper.CreateControl<IMyTerminalControlTextbox, TBlock>(id);
            c.Title = MyStringId.GetOrCompute(title);
            c.Tooltip = MyStringId.GetOrCompute(tooltip);
            c.Getter = b => new StringBuilder(ReadString(b, key, defaultValue));
            c.Setter = (b, sb) => WriteValue(b, key, sb == null ? "" : sb.ToString().Trim());
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        static readonly MyStringId _optBothKgL = MyStringId.GetOrCompute("Both (kg & L)");
        static readonly MyStringId _optKgOnly = MyStringId.GetOrCompute("kg only");
        static readonly MyStringId _optLOnly = MyStringId.GetOrCompute("L only");
        static readonly MyStringId _optBoth = MyStringId.GetOrCompute("Both");
        static readonly MyStringId _optOresOnly = MyStringId.GetOrCompute("Ores only");
        static readonly MyStringId _optIngotsOnly = MyStringId.GetOrCompute("Ingots only");

        static void AddStorageType<TBlock>(IMyTerminalControls helper, string id, string title, string tooltip, string[] regions)
        {
            IMyTerminalControlCombobox c = helper.CreateControl<IMyTerminalControlCombobox, TBlock>(id);
            c.Title = MyStringId.GetOrCompute(title);
            c.Tooltip = MyStringId.GetOrCompute(tooltip);
            c.ComboBoxContent = list =>
            {
                if (list == null) return;
                list.Clear();
                list.Add(new MyTerminalControlComboBoxItem { Key = 1L, Value = _optBothKgL });
                list.Add(new MyTerminalControlComboBoxItem { Key = 2L, Value = _optKgOnly });
                list.Add(new MyTerminalControlComboBoxItem { Key = 3L, Value = _optLOnly });
            };
            c.Getter = ReadStorageType;
            c.Setter = (b, v) => WriteValue(b, "StorageType", v.ToString());
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        static void AddOreIngotType<TBlock>(IMyTerminalControls helper, string id, string title, string tooltip, string[] regions)
        {
            IMyTerminalControlCombobox c = helper.CreateControl<IMyTerminalControlCombobox, TBlock>(id);
            c.Title = MyStringId.GetOrCompute(title);
            c.Tooltip = MyStringId.GetOrCompute(tooltip);
            c.ComboBoxContent = list =>
            {
                if (list == null) return;
                list.Clear();
                list.Add(new MyTerminalControlComboBoxItem { Key = 1L, Value = _optBoth });
                list.Add(new MyTerminalControlComboBoxItem { Key = 2L, Value = _optOresOnly });
                list.Add(new MyTerminalControlComboBoxItem { Key = 3L, Value = _optIngotsOnly });
            };
            c.Getter = ReadOreIngotType;
            c.Setter = (b, v) => WriteValue(b, "OreIngotType", v.ToString());
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        static void AddLabel<TBlock>(IMyTerminalControls helper, string id, string text, string[] regions)
        {
            IMyTerminalControlLabel c = helper.CreateControl<IMyTerminalControlLabel, TBlock>(id);
            c.Label = MyStringId.GetOrCompute(text);
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        static void AddSeparator<TBlock>(IMyTerminalControls helper, string id, string[] regions)
        {
            IMyTerminalControlSeparator c = helper.CreateControl<IMyTerminalControlSeparator, TBlock>(id);
            c.Visible = b => AppActive(b, regions);
            helper.AddControl<TBlock>(c);
        }

        /// <summary>Most recently selected app per block: the screen and app region
        /// last picked by the player (the script instance starts the moment an
        /// app is selected on a screen).</summary>
        struct SelectionRecord
        {
            public int Surface;
            public string Region;
        }

        static readonly Dictionary<long, SelectionRecord> _lastSelected = new Dictionary<long, SelectionRecord>();
        static readonly HashSet<Type> _repositioned = new HashSet<Type>();

        static readonly List<long> _deadSelections = new List<long>();

        public static void RecordAppSelection(long blockId, string region, int surfaceIndex)
        {
            if (blockId == 0 || string.IsNullOrEmpty(region)) return;
            SelectionRecord rec;
            rec.Surface = surfaceIndex;
            rec.Region = region;
            _lastSelected[blockId] = rec;
            _regionCacheFrame = -1;
            if (_lastSelected.Count > 1024)
                PruneSelections();
        }

        /// <summary>Drops records of blocks that no longer exist instead of
        /// wiping every block's remembered selection at once. Full clear only
        /// as a last resort when the world genuinely has 1024+ live entries.</summary>
        static void PruneSelections()
        {
            var entities = MyAPIGateway.Entities;
            if (entities != null)
            {
                _deadSelections.Clear();
                foreach (var kv in _lastSelected)
                    if (entities.GetEntityById(kv.Key) == null) _deadSelections.Add(kv.Key);
                for (int i = 0; i < _deadSelections.Count; i++)
                    _lastSelected.Remove(_deadSelections[i]);
                _deadSelections.Clear();
            }
            if (_lastSelected.Count > 1024)
                _lastSelected.Clear();
        }

        /// <summary>Reposition attempts per block type - after enough failed
        /// tries (another mod's controls after the anchor, or no anchor found)
        /// the type is marked done so the GetControls scan stops repeating
        /// every update for the rest of the session.</summary>
        static readonly Dictionary<Type, int> _repositionTries = new Dictionary<Type, int>();
        const int RepositionMaxTries = 20;

        /// <summary>Moves our controls right after the block's screen/app
        /// controls (the game's per-surface "Content/Font/Script" section).
        /// Only called for a block type once a block of that type actually
        /// exists - its control list is fully built by then, and calling
        /// GetControls earlier could suppress the game's own registration.</summary>
        static void RepositionFor<TBlock>()
        {
            try
            {
                Type t = typeof(TBlock);
                if (_repositioned.Contains(t)) return;
                // Guard: GetControls<T> itself creates the factory entry via
                // MyTerminalControlFactory.GetList/InitializeControls. If called
                // before AreControlsCreated, the entry is created with only base
                // controls and the game's own CreateTerminalControls early-exits
                // forever (client missing many buttons). Require factory ready.
                if (!IsFactoryReady<TBlock>()) return;
                int tries;
                _repositionTries.TryGetValue(t, out tries);
                if (tries >= RepositionMaxTries)
                {
                    _repositioned.Add(t);
                    return;
                }
                _repositionTries[t] = tries + 1;

                IMyTerminalControls helper = MyAPIGateway.TerminalControls;
                List<IMyTerminalControl> list;
                helper.GetControls<TBlock>(out list);
                if (list == null || list.Count == 0) return;

                int anchor = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    string id = list[i].Id;
                    if (id != null && id.StartsWith("DisplayApps_")) break;
                    if (IsScreenControl(id)) anchor = i;
                }
                if (anchor < 0) return;

                for (int i = anchor + 1; i < list.Count; i++)
                {
                    string id = list[i].Id;
                    if (id == null || !id.StartsWith("DisplayApps_")) return;
                }

                List<IMyTerminalControl> moved = new List<IMyTerminalControl>();
                for (int i = list.Count - 1; i > anchor; i--)
                {
                    moved.Add(list[i]);
                    helper.RemoveControl<TBlock>(list[i]);
                }
                moved.Reverse();
                for (int k = 0; k < moved.Count; k++)
                    helper.AddControl<TBlock>(moved[k]);
                _repositioned.Add(typeof(TBlock));
            }
            catch
            {
            }
        }

        static bool IsScreenControl(string id)
        {
            if (id == null) return false;
            if (id.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (id.IndexOf("Content", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (id.IndexOf("Font", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (id.IndexOf("Alignment", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (id.IndexOf("ChangeInterval", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (id.IndexOf("TextPadding", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (id.IndexOf("PreserveAspectRatio", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>App region the settings page currently applies to. Always
        /// re-checked against the actual screens on every terminal refresh so
        /// it can never show config for an app that isn't running. Priority:
        /// the screen selected in the game's own cockpit screen list, then the
        /// most recently selected app, then any single app running on the
        /// block.</summary>
        /// <summary>One-entry per-frame memo: with the settings page open every
        /// control getter and Visible callback resolves the region, ~30 times
        /// per GUI frame for the same block - compute it once per frame.</summary>
        static long _regionCacheEntity;
        static int _regionCacheFrame = -1;
        static string _regionCacheValue;

        static string CurrentRegion(MyTerminalBlock block)
        {
            if (block == null) return null;
            int frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : -1;
            if (frame >= 0 && frame == _regionCacheFrame && block.EntityId == _regionCacheEntity)
                return _regionCacheValue;
            string result = ResolveCurrentRegion(block);
            _regionCacheEntity = block.EntityId;
            _regionCacheFrame = frame;
            _regionCacheValue = result;
            return result;
        }

        static string ResolveCurrentRegion(MyTerminalBlock block)
        {
            string result = null;

            int selected = GameSelectedScreen(block);
            if (selected >= 0)
                result = SurfaceApp(block, selected);

            string recorded = null;
            int recordedSurface = -1;
            SelectionRecord rec;
            bool hasRecord = _lastSelected.TryGetValue(block.EntityId, out rec);
            if (result == null && hasRecord)
            {
                recorded = rec.Region;
                recordedSurface = rec.Surface;
                if (SurfaceRuns(block, recordedSurface, recorded))
                    result = recorded;
            }
            if (result == null)
            {
                result = SurfaceApp(block, hasRecord ? recordedSurface : -1);
                if (result == null)
                    result = FirstActiveRegion(block);
                if (result != null && !string.Equals(result, recorded, StringComparison.OrdinalIgnoreCase))
                {
                    SelectionRecord nr;
                    nr.Surface = recordedSurface;
                    nr.Region = result;
                    _lastSelected[block.EntityId] = nr;
                }
            }
            return result;
        }

        static IMyTerminalControlCombobox _screenSelector;
        static bool _screenSelectorSearched;

        /// <summary>Reads the screen currently selected in the game's own
        /// cockpit screen list ("LCD Panels"). The game stores the selection
        /// on the block's MyMultiTextPanelComponent (SelectedPanelIndex) -
        /// the same state its per-screen controls use. Falls back to scanning
        /// the control list for a screen-selecting combobox. Returns -1 when
        /// there is no selection state.</summary>
        static int GameSelectedScreen(MyTerminalBlock block)
        {
            try
            {
                var panels = block.Components.Get<Sandbox.Game.EntityComponents.MyMultiTextPanelComponent>();
                if (panels != null)
                {
                    int idx = panels.SelectedPanelIndex;
                    if (idx >= 0 && idx < 8)
                        return idx;
                    return -1;
                }
            }
            catch { }
            try
            {
                if (!(block is Sandbox.ModAPI.IMyCockpit)) return -1;
                if (!_screenSelectorSearched)
                {
                    // Same guard as EnsureFor: GetControls<IMyCockpit> creates the
                    // factory entry. Do not search until AreControlsCreated is true,
                    // otherwise we permanently suppress MyCockpit's own controls.
                    if (!IsFactoryReady<IMyCockpit>()) return -1;
                    _screenSelectorSearched = true;
                    IMyTerminalControls helper = MyAPIGateway.TerminalControls;
                    if (helper == null) return -1;
                    List<IMyTerminalControl> list;
                    helper.GetControls<IMyCockpit>(out list);
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var cb = list[i] as IMyTerminalControlCombobox;
                            if (cb == null) continue;
                            string id = list[i].Id ?? "";
                            if (id.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                id.IndexOf("Surface", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                id.IndexOf("LCD", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _screenSelector = cb;
                                break;
                            }
                        }
                    }
                }
                if (_screenSelector == null) return -1;
                var getter = _screenSelector.Getter;
                if (getter == null) return -1;
                long val = getter(block);
                if (val < 0 || val > 8) return -1;
                return (int)val;
            }
            catch { }
            return -1;
        }

        /// <summary>True when the given screen of the block runs the given region.</summary>
        static bool SurfaceRuns(MyTerminalBlock block, int index, string region)
        {
            if (index < 0 || region == null) return false;
            try
            {
                var provider = block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
                if (provider == null || index >= provider.SurfaceCount) return false;
                return ScriptMatches(provider.GetSurface(index).Script, region);
            }
            catch { }
            return false;
        }

        /// <summary>DisplayApps region running on the given screen of the block,
        /// or null when it runs none.</summary>
        static string SurfaceApp(MyTerminalBlock block, int index)
        {
            if (index < 0) return null;
            try
            {
                var provider = block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
                if (provider == null || index >= provider.SurfaceCount) return null;
                string script = provider.GetSurface(index).Script;
                if (string.IsNullOrEmpty(script)) return null;
                for (int k = 0; k < AllRegions.Length; k++)
                {
                    if (ScriptMatches(script, AllRegions[k])) return AllRegions[k];
                }
            }
            catch { }
            return null;
        }

        /// <summary>True when one of the given app regions is the app the
        /// settings page currently applies to.</summary>
        static bool AppActive(MyTerminalBlock block, string[] regions)
        {
            string region = CurrentRegion(block);
            if (region == null) return false;
            for (int k = 0; k < regions.Length; k++)
            {
                if (string.Equals(region, regions[k], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>First DisplayApps region found on any screen of the block,
        /// or null when none runs a DisplayApps app.</summary>
        static string FirstActiveRegion(MyTerminalBlock block)
        {
            try
            {
                var provider = block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
                if (provider == null) return null;
                for (int i = 0; i < provider.SurfaceCount; i++)
                {
                    string script = provider.GetSurface(i).Script;
                    if (string.IsNullOrEmpty(script)) continue;
                    for (int k = 0; k < AllRegions.Length; k++)
                    {
                        if (ScriptMatches(script, AllRegions[k])) return AllRegions[k];
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>The surface script value is the script id, but a few game
        /// versions report the display name - accept both.</summary>
        static bool ScriptMatches(string script, string region)
        {
            if (script == null) return false;
            if (string.Equals(script, region, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(script, DisplayName(region), StringComparison.OrdinalIgnoreCase);
        }

        static string DisplayName(string region)
        {
            switch (region)
            {
                case "AssemblerInfo": return "Info Assembler";
                case "ComponentsInfo": return "Info Components";
                case "DamageInfo": return "Info Damaged";
                case "DockedInfo": return "Info Docked Ships";
                case "O2H2": return "Info O2 / H2";
                case "OreIngotInfo": return "Info Ores & Ingots";
                case "PerfInfo": return "Info Performance";
                case "PowerInfo": return "Info Power";
                case "StorageInfo": return "Info Storage";
                default: return region;
            }
        }

        static string ReadValue(MyTerminalBlock block, string key)
        {
            return AppBase.ReadConfigValue(block, CurrentRegion(block), key);
        }

        static void WriteValue(MyTerminalBlock block, string key, string value)
        {
            string region = CurrentRegion(block);
            if (region == null) return;
            AppBase.WriteConfigValue(block, region, key, value);
        }

        static bool ReadBool(MyTerminalBlock block, string key, bool fallback)
        {
            string v = ReadValue(block, key);
            bool result;
            if (v != null && bool.TryParse(v, out result)) return result;
            return fallback;
        }

        static float ReadFloat(MyTerminalBlock block, string key, float fallback)
        {
            string v = ReadValue(block, key);
            float result;
            if (v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return result;
            return fallback;
        }

        static string ReadString(MyTerminalBlock block, string key, string fallback)
        {
            string v = ReadValue(block, key);
            return v == null ? fallback : v;
        }

        /// <summary>One padding value ([UP, DOWN, LEFT, RIGHT]) from the
        /// TextPadding option, or the fallback when unset or unparseable.</summary>
        static float ReadPadding(MyTerminalBlock block, int index, float fallback)
        {
            float[] pad = ParsePadding(ReadValue(block, "TextPadding"));
            return pad == null ? fallback : pad[index];
        }

        /// <summary>Updates one padding value and writes the whole
        /// "[UP, DOWN, LEFT, RIGHT]" option back to CustomData.</summary>
        static void WritePadding(MyTerminalBlock block, int index, float value)
        {
            float[] pad = ParsePadding(ReadValue(block, "TextPadding")) ?? new float[] { 16f, 16f, 16f, 16f };
            pad[index] = Math.Max(0f, Math.Min(512f, value));
            WriteValue(block, "TextPadding",
                "[" + pad[0].ToString("0.#", CultureInfo.InvariantCulture)
                + "," + pad[1].ToString("0.#", CultureInfo.InvariantCulture)
                + "," + pad[2].ToString("0.#", CultureInfo.InvariantCulture)
                + "," + pad[3].ToString("0.#", CultureInfo.InvariantCulture) + "]");
        }

        static float[] ParsePadding(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string cleaned = value.Trim();
            if (cleaned.StartsWith("[") && cleaned.EndsWith("]"))
                cleaned = cleaned.Substring(1, cleaned.Length - 2);

            string[] parts = cleaned.Split(new char[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            float[] parsed = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                float val;
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    return null;
                parsed[i] = Math.Max(0f, Math.Min(512f, val));
            }

            if (parsed.Length >= 4) return new float[] { parsed[0], parsed[1], parsed[2], parsed[3] };
            if (parsed.Length == 1) return new float[] { parsed[0], parsed[0], parsed[0], parsed[0] };
            if (parsed.Length == 2) return new float[] { parsed[0], parsed[0], parsed[1], parsed[1] };
            return null;
        }

        static long ReadStorageType(MyTerminalBlock block)
        {
            string v = ReadValue(block, "StorageType");
            if (v == null) return 1L;
            v = v.Trim();
            if (v.Equals("1", StringComparison.OrdinalIgnoreCase) || v.Equals("both", StringComparison.OrdinalIgnoreCase) || v.Equals("all", StringComparison.OrdinalIgnoreCase))
                return 1L;
            if (v.Equals("2", StringComparison.OrdinalIgnoreCase) || v.Equals("kg", StringComparison.OrdinalIgnoreCase) || v.Equals("mass", StringComparison.OrdinalIgnoreCase) || v.Equals("weight", StringComparison.OrdinalIgnoreCase))
                return 2L;
            if (v.Equals("3", StringComparison.OrdinalIgnoreCase) || v.Equals("l", StringComparison.OrdinalIgnoreCase) || v.Equals("liter", StringComparison.OrdinalIgnoreCase) || v.Equals("liters", StringComparison.OrdinalIgnoreCase) || v.Equals("vol", StringComparison.OrdinalIgnoreCase) || v.Equals("volume", StringComparison.OrdinalIgnoreCase))
                return 3L;
            int result;
            if (int.TryParse(v, out result) && result >= 1 && result <= 3) return result;
            return 1L;
        }

        static long ReadOreIngotType(MyTerminalBlock block)
        {
            string v = ReadValue(block, "OreIngotType");
            if (v == null) return 1L;
            v = v.Trim();
            if (v.Equals("1", StringComparison.OrdinalIgnoreCase) || v.Equals("both", StringComparison.OrdinalIgnoreCase) || v.Equals("all", StringComparison.OrdinalIgnoreCase))
                return 1L;
            if (v.Equals("2", StringComparison.OrdinalIgnoreCase) || v.Equals("ores", StringComparison.OrdinalIgnoreCase) || v.Equals("ore", StringComparison.OrdinalIgnoreCase))
                return 2L;
            if (v.Equals("3", StringComparison.OrdinalIgnoreCase) || v.Equals("ingots", StringComparison.OrdinalIgnoreCase) || v.Equals("ingot", StringComparison.OrdinalIgnoreCase))
                return 3L;
            int result;
            if (int.TryParse(v, out result) && result >= 1 && result <= 3) return result;
            return 1L;
        }
    }
}