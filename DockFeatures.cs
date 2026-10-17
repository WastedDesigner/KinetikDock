// Fences-style organisation and event-driven updates. No external runtime or service.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class DockLayout
{
    public static void Sanitize(GroupData g)
    {
        g.Width = Math.Max(0, Math.Min(2000, g.Width));
        g.Height = Math.Max(0, Math.Min(2000, g.Height));
        g.ItemSize = Math.Max(-1, Math.Min(2, g.ItemSize));
        g.LabelMode = Math.Max(-1, Math.Min(1, g.LabelMode));
        g.PortalSort = Math.Max(0,Math.Min(3,g.PortalSort));
        g.Workspace = string.IsNullOrWhiteSpace(g.Workspace) ? "Main" : g.Workspace.Trim();
        if (g.Items == null) g.Items = new List<ItemData>();
        g.Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Path));
        var ids = new HashSet<string>();
        foreach (var item in g.Items)
        {
            if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) { item.Id = Guid.NewGuid().ToString("N"); ids.Add(item.Id); }
            if (item.Name == null) item.Name = Target.NameFor(item.Path);
        }
    }
    public static int MaxScroll(int count, int cols, float tileHeight, float viewport)
    { return Math.Max(0, (int)Math.Ceiling(Math.Ceiling(count / (double)Math.Max(1, cols)) * tileHeight - viewport)); }
    public static bool Match(ItemData item, string query)
    { return string.IsNullOrWhiteSpace(query) || (item.Name ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || (item.Path ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0; }
    public static int NearestSnap(int value, IEnumerable<int> candidates, int reach, ref int latch)
    {
        if(latch!=int.MinValue && Math.Abs((long)value-latch)<reach+6) return latch;
        latch=int.MinValue; long distance=reach;
        foreach(int candidate in candidates) { long delta=Math.Abs((long)value-candidate); if(delta<distance) { distance=delta; latch=candidate; } }
        return latch==int.MinValue ? value : latch;
    }
}

public class DockSnapshot
{
    public int Version = 1;
    public string Workspace = "Main";
    public List<GroupData> Groups = new List<GroupData>();
}

partial class DockContext
{
    readonly System.Windows.Forms.Timer redrawTimer = new System.Windows.Forms.Timer { Interval = 16 };
    readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 450 };
    readonly HashSet<GroupForm> dirtyForms = new HashSet<GroupForm>();
    readonly Stack<string> undo = new Stack<string>(), redo = new Stack<string>();
    readonly Dictionary<string, string> activeTabs = new Dictionary<string, string>();
    string savedState;
    bool saving, closing, groupsHidden;
    public bool Peeking { get; private set; }
    public long RenderCount, SaveCount;
    HotkeyWindow hotkeys;
    SearchWindow search;
    Form settingsWindow;
    readonly Stopwatch lifetime = Stopwatch.StartNew();

    void InitializeFeatures()
    {
        if (string.IsNullOrWhiteSpace(C.Workspace)) C.Workspace = "Main";
        savedState = Capture();
        redrawTimer.Tick += (s, e) =>
        {
            redrawTimer.Stop(); var batch = dirtyForms.ToArray(); dirtyForms.Clear();
            foreach (var f in batch) if (!f.IsDisposed) f.Render();
        };
        saveTimer.Tick += (s, e) => FlushSave();
        if (!Program.Demo) hotkeys = new HotkeyWindow(this);
        if (!Program.Demo && C.AutoOrganise) StartOrganiser();
    }
    public void RequestRender(GroupForm f)
    { if (closing || f.IsDisposed) return; dirtyForms.Add(f); if (!redrawTimer.Enabled) redrawTimer.Start(); }
    void RefreshPath(string path)
    { foreach (var f in Forms) if (IsShown(f) && !f.Data.Collapsed && (f.HasVisiblePath(path) || f.HasRetryWork())) RequestRender(f); }
    string Capture()
    { return Serializer().Serialize(new DockSnapshot { Groups = C.List, Workspace = C.Workspace }); }
    static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }; }
    void QueueSave()
    {
        if (closing) return;
        if (!saving)
        {
            var state = Capture();
            if (state != savedState)
            {
                // Bound the undo log by both entry count and total text size.
                if (undo.Count >= 30 || undo.Sum(x => (long)x.Length * 2) + savedState.Length * 2L > 8 * 1024 * 1024) undo.Clear();
                undo.Push(savedState); redo.Clear(); savedState = state;
            }
        }
        saveTimer.Stop(); saveTimer.Start();
    }
    public void FlushSave()
    {
        saveTimer.Stop();
        if (!closing)
        {
            if (!C.Save() && Settings.LastSaveError != null && !Program.Demo)
                tray.ShowBalloonTip(6000, "Kinetik Dock couldn't save changes", Settings.LastSaveError + "\nExport your layout from the tray menu to keep a copy.", ToolTipIcon.Warning);
            SaveCount++;
        }
    }
    public void Undo(bool forward)
    {
        var from = forward ? redo : undo; var to = forward ? undo : redo;
        if (from.Count == 0) return;
        to.Push(Capture()); RestoreSnapshot(Serializer().Deserialize<DockSnapshot>(from.Pop()));
    }
    void RestoreSnapshot(DockSnapshot state)
    {
        saving = true;
        try
        {
            foreach (var f in Forms.ToArray()) f.Close();
            C.List = state.Groups; C.Workspace = state.Workspace; activeTabs.Clear();
            foreach (var g in C.List) { DockLayout.Sanitize(g); Open(g); }
            savedState = Capture(); QueueSave(); RenderAll();
        }
        finally { saving = false; }
    }
    public List<GroupForm> Tabs(GroupForm f)
    { return string.IsNullOrEmpty(f.Data.TabSet) ? new List<GroupForm> { f } : Forms.Where(x => x.Data.Workspace == f.Data.Workspace && x.Data.TabSet == f.Data.TabSet).ToList(); }
    public bool IsShown(GroupForm f)
    {
        if (closing || (groupsHidden && !Peeking) || f.Data.Workspace != C.Workspace) return false;
        if (string.IsNullOrEmpty(f.Data.TabSet)) return true;
        string id; var tabs = Tabs(f);
        if (!activeTabs.TryGetValue(f.Data.TabSet, out id) || !tabs.Any(t => t.Data.Id == id)) activeTabs[f.Data.TabSet] = id = tabs[0].Data.Id;
        return f.Data.Id == id;
    }
    public void SwitchTab(GroupForm f)
    {
        if (!string.IsNullOrEmpty(f.Data.TabSet)) activeTabs[f.Data.TabSet] = f.Data.Id;
        RenderAll();
    }
    public void SyncPlacement(GroupForm f)
    { foreach (var other in Tabs(f).Where(x => x != f)) { other.Data.X = f.Data.X; other.Data.Y = f.Data.Y; other.Data.Width = f.Data.Width; other.Data.Height = f.Data.Height; } }
    public void CombineTabs(GroupForm f, GroupForm with)
    {
        string set = string.IsNullOrEmpty(with.Data.TabSet) ? Guid.NewGuid().ToString("N") : with.Data.TabSet;
        with.Data.TabSet = set; f.Data.TabSet = set; f.Data.Workspace = with.Data.Workspace;
        f.Data.X = with.Data.X; f.Data.Y = with.Data.Y; f.Data.Width = with.Data.Width; f.Data.Height = with.Data.Height;
        activeTabs[set] = f.Data.Id; Save(); RenderAll();
    }
    public void SelectWorkspace(string name) { C.Workspace = name; Save(); RenderAll(); }
    public void TogglePeek()
    {
        Peeking = !Peeking;
        if (hotkeys != null) hotkeys.SetEscape(Peeking);
        RenderAll();
    }
    public void ShowSearch()
    {
        if (search != null && !search.IsDisposed) { search.Activate(); return; }
        search = new SearchWindow(this); search.Show(); search.Activate();
    }
    public void NewPortal()
    {
        using (var dlg = new FolderBrowserDialog { Description = "Choose a folder to browse on your desktop" })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            var g = new GroupData { Name = Target.NameFor(dlg.SelectedPath), PortalPath = dlg.SelectedPath, Workspace = C.Workspace, Height = 400 };
            C.List.Add(g); Open(g); Save();
        }
    }
    void ImportLayout()
    {
        using (var dlg = new OpenFileDialog { Filter = "Kinetik Dock layout (*.json)|*.json", Title = "Import groups (adds to your layout)" })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var info = new FileInfo(dlg.FileName);
                if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("Layout file is too large (maximum 4 MB).");
                var snapshot = Serializer().Deserialize<DockSnapshot>(File.ReadAllText(dlg.FileName));
                if (snapshot == null || snapshot.Version != 1 || snapshot.Groups == null || snapshot.Groups.Count > 200) throw new InvalidDataException("Unsupported layout format.");
                var tabIds = new Dictionary<string, string>();
                foreach (var g in snapshot.Groups)
                {
                    if (g == null) throw new InvalidDataException("Layout contains an invalid group.");
                    DockLayout.Sanitize(g); g.Id = Guid.NewGuid().ToString("N"); g.Cols = Math.Max(1, Math.Min(8, g.Cols));
                    if (!string.IsNullOrEmpty(g.TabSet)) { if (!tabIds.ContainsKey(g.TabSet)) tabIds[g.TabSet] = Guid.NewGuid().ToString("N"); g.TabSet = tabIds[g.TabSet]; }
                }
                foreach (var g in snapshot.Groups) { C.List.Add(g); Open(g); }
                Save(); RenderAll();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Couldn't import layout"); }
        }
    }
    void ExportLayout()
    {
        using (var dlg = new SaveFileDialog { Filter = "Kinetik Dock layout (*.json)|*.json", FileName = "KinetikDock-layout.json" })
            if (dlg.ShowDialog() == DialogResult.OK)
                try { File.WriteAllText(dlg.FileName, Capture()); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Couldn't export layout"); }
    }
    void AddFeatureMenu(ToolStripItemCollection items)
    {
        items.Add(Item("Quick access   Ctrl+Alt+Space", Peeking, TogglePeek));
        items.Add(Item("Search shortcuts…   Ctrl+Alt+F", false, ShowSearch));
        items.Add(Item(groupsHidden ? "Show groups" : "Hide groups", false, () => { groupsHidden = !groupsHidden; RenderAll(); }));
        items.Add(Item("New folder portal…", false, NewPortal));
        items.Add(Item("Organise desktop references…", false, ShowOrganisation));
        var work = new ToolStripMenuItem("Workspaces");
        foreach (var name in C.List.Select(g => g.Workspace).Concat(new[] { C.Workspace }).Distinct().OrderBy(n => n))
        { var n = name; work.DropDownItems.Add(Item(n, C.Workspace == n, () => SelectWorkspace(n))); }
        work.DropDownItems.Add(Item("New workspace…", false, () =>
        {
            var name = GroupForm.Prompt("New workspace", "Workspace name:", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            C.Workspace = name.Trim(); var g = new GroupData { Workspace = C.Workspace }; C.List.Add(g); Open(g); Save(); RenderAll();
        }));
        items.Add(work);
        var history = new ToolStripMenuItem("Layout and history");
        var un = Item("Undo layout edit", false, () => Undo(false)); un.Enabled = undo.Count > 0; history.DropDownItems.Add(un);
        var re = Item("Redo layout edit", false, () => Undo(true)); re.Enabled = redo.Count > 0; history.DropDownItems.Add(re);
        history.DropDownItems.Add(Item("Export layout / snapshot…", false, ExportLayout));
        history.DropDownItems.Add(Item("Import groups…", false, ImportLayout));
        history.DropDownItems.Add(Item("Collapse all", false, () => { foreach (var g in C.List.Where(g => g.Workspace == C.Workspace)) g.Collapsed = true; Save(); RenderAll(); }));
        history.DropDownItems.Add(Item("Expand all", false, () => { foreach (var g in C.List.Where(g => g.Workspace == C.Workspace)) g.Collapsed = false; Save(); RenderAll(); }));
        items.Add(history);
        items.Add(Item("Settings and diagnostics…", false, ShowSettings));
        items.Add(new ToolStripSeparator());
    }
    public void ShowSettings()
    {
        if (settingsWindow != null && !settingsWindow.IsDisposed) { settingsWindow.Activate(); return; }
        var f = new Form { Text = "Kinetik Dock settings", StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(520, 430), MinimumSize = new Size(450, 400), Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(27,31,44), ForeColor = Color.White };
        settingsWindow = f;
        var filter = new TextBox { Dock = DockStyle.Top, AccessibleName = "Search settings" };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        Action<string, bool, Action<bool>> check = (name, value, action) =>
        { var cb = new CheckBox { Text = name, Checked = value, Width = 460, Height = 32 }; cb.CheckedChanged += (s,e) => { action(cb.Checked); Save(); RenderAll(); }; panel.Controls.Add(cb); };
        check("Lightweight appearance (no shadows)", C.Lightweight, v => C.Lightweight = v);
        check("Double-click to open shortcuts", C.DoubleClickOpen, v => C.DoubleClickOpen = v);
        check("Global hotkeys (Ctrl+Alt+Space / Ctrl+Alt+F)", C.Hotkeys, v => { C.Hotkeys = v; if (hotkeys != null) hotkeys.Configure(); });
        check("Lock all groups", C.Locked, v => C.Locked = v);
        check("Snap groups (hold Alt to bypass)", C.Snap, v => C.Snap = v);
        check("Show shortcut labels", C.Labels, v => C.Labels = v);
        var help = new Label { Text = "Resize groups at their edges. Scroll with the wheel or scrollbar.\nFolder portals browse files without moving them.\nQuick access: press the hotkey again or Escape to dismiss.\nSearch covers saved shortcuts and loaded portal contents.", Width = 465, Height = 95 }; panel.Controls.Add(help);
        var diagnostics = new Button { Text = "Show resource diagnostics", Width = 260, Height = 34 };
        diagnostics.Click += (s,e) => { using (var p = Process.GetCurrentProcess()) MessageBox.Show("Uptime: " + lifetime.Elapsed.ToString(@"hh\:mm\:ss") + "\nPrivate memory: " + (p.PrivateMemorySize64 / 1048576) + " MB\nWorking set: " + (p.WorkingSet64 / 1048576) + " MB\nIcon cache: " + ShellIcon.Count + " entries / " + (ShellIcon.Bytes / 1024) + " KB\nGroup renders: " + RenderCount + "\nSettings saves: " + SaveCount, "Kinetik Dock diagnostics"); }; panel.Controls.Add(diagnostics);
        filter.TextChanged += (s,e) => { foreach (Control control in panel.Controls) control.Visible = control.Text.IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0; };
        f.Controls.Add(panel); f.Controls.Add(filter); f.Show();
    }
    void CloseFeatures()
    {
        if (closing) return; closing = true;
        redrawTimer.Stop(); saveTimer.Stop(); redrawTimer.Dispose(); saveTimer.Dispose();
        if (hotkeys != null) hotkeys.Dispose();
        if (organiser != null) organiser.Dispose();
        if (organisationWindow != null) organisationWindow.Close();
        if (picker != null) picker.Close();
        if (search != null) search.Close(); if (settingsWindow != null) settingsWindow.Close();
    }
}

sealed class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    readonly DockContext app;
    public HotkeyWindow(DockContext app) { this.app = app; CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); Configure(); }
    public void Configure()
    {
        UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2);
        if (app.C.Hotkeys)
        {
            bool peek = RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.Space);
            bool search = RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.F);
            if (!peek || !search) MessageBox.Show("A Kinetik Dock hotkey is already in use. Quick access and search are also available from the tray menu.", "Kinetik Dock");
        }
    }
    public void SetEscape(bool on) { UnregisterHotKey(Handle, 3); if (on) RegisterHotKey(Handle, 3, 0x4000, (uint)Keys.Escape); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312) { if ((int)m.WParam == 2) app.ShowSearch(); else if ((int)m.WParam != 3 || app.Peeking) app.TogglePeek(); }
        base.WndProc(ref m);
    }
    public void Dispose() { for (int i = 1; i <= 3; i++) UnregisterHotKey(Handle, i); DestroyHandle(); }
}

sealed class SearchWindow : Form
{
    sealed class Result { public ItemData Item; public GroupForm Group; public override string ToString() { return Item.Name + "   —   " + Group.Data.Workspace + " / " + Group.Data.Name; } }
    readonly DockContext app;
    readonly TextBox query = new TextBox { Dock = DockStyle.Top, AccessibleName = "Search shortcuts" };
    readonly ListBox results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Matching shortcuts" };
    public SearchWindow(DockContext app)
    {
        this.app = app; Text = "Search Kinetik Dock"; TopMost = true; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(620, 370); Font = new Font("Segoe UI", 11);
        Controls.Add(results); Controls.Add(query); KeyPreview = true;
        query.TextChanged += (s,e) => UpdateResults(); results.DoubleClick += (s,e) => Launch();
        KeyDown += (s,e) =>
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { Launch(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down && query.Focused && results.Items.Count > 0) { results.Focus(); e.SuppressKeyPress = true; }
        };
        Shown += (s,e) => query.Focus(); UpdateResults();
    }
    void UpdateResults()
    {
        results.BeginUpdate(); results.Items.Clear();
        foreach (var f in app.Forms)
            foreach (var item in f.Items.Where(i => DockLayout.Match(i, query.Text)).Take(300 - results.Items.Count)) results.Items.Add(new Result { Item = item, Group = f });
        if (results.Items.Count > 0) results.SelectedIndex = 0;
        results.EndUpdate();
    }
    void Launch()
    {
        var result = results.SelectedItem as Result;
        if (result == null) return;
        if (app.Peeking) app.TogglePeek(); Close(); Target.Open(result.Item.Path);
    }
}

// Folder I/O never runs on the UI thread. Watch only the active portal and coalesce changes.
sealed class FolderPortal : IDisposable
{
    readonly GroupForm owner;
    readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 300 };
    FileSystemWatcher watcher;
    volatile int generation;
    bool active, loading, again, disposed;
    public string CurrentPath, Status = "Loading…";
    public List<ItemData> Items = new List<ItemData>();
    readonly Stack<string> back = new Stack<string>(), forward = new Stack<string>();
    public bool CanBack { get { return back.Count > 0; } }
    public bool CanForward { get { return forward.Count > 0; } }
    public FolderPortal(GroupForm owner, string path)
    { this.owner = owner; CurrentPath = path; debounce.Tick += (s,e) => { debounce.Stop(); Refresh(); }; }
    public void SetActive(bool value)
    {
        if (active == value || disposed) return; active = value;
        if (value) Refresh();
        else { generation++; loading = false; if (watcher != null) { watcher.Dispose(); watcher = null; } debounce.Stop(); }
    }
    public void Navigate(string path)
    {
        if (!string.Equals(path,CurrentPath,StringComparison.OrdinalIgnoreCase)) { back.Push(CurrentPath); forward.Clear(); }
        NavigateCore(path);
    }
    public void GoBack() { if(back.Count>0) { forward.Push(CurrentPath); NavigateCore(back.Pop()); } }
    public void GoForward() { if(forward.Count>0) { back.Push(CurrentPath); NavigateCore(forward.Pop()); } }
    void NavigateCore(string path)
    {
        generation++; loading = false; again = false;
        if (watcher != null) { watcher.Dispose(); watcher = null; }
        CurrentPath = path; Items = new List<ItemData>(); Refresh();
    }
    public void Refresh()
    {
        if (!active || disposed) return;
        if (loading) { again = true; return; }
        loading = true; again = false; Status = "Loading…";
        int gen = ++generation; string path = CurrentPath;
        int sort = owner.Data.PortalSort; bool descending = owner.Data.PortalDescending, hidden = owner.Data.ShowHidden;
        string filter = owner.Data.PortalFilter;
        owner.RefreshSoon();
        if (!DockWork.Queue(() =>
        {
            var list = new List<ItemData>(); string status = ""; FileSystemWatcher candidate = null;
            try
            {
                try
                {
                    candidate = new FileSystemWatcher(path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite, IncludeSubdirectories = false };
                    FileSystemEventHandler changed = (s,e) => Schedule(gen);
                    candidate.Created += changed; candidate.Deleted += changed; candidate.Changed += changed;
                    candidate.Renamed += (s,e) => Schedule(gen); candidate.Error += (s,e) => Schedule(gen);
                    candidate.EnableRaisingEvents = true;
                }
                catch { if(candidate!=null) candidate.Dispose(); candidate=null; status="Live updates unavailable — refresh from menu"; }
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    if (gen != generation || disposed) { if(candidate!=null) candidate.Dispose(); return; }
                    if (!hidden && (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    var item = new ItemData { Path = entry.FullName, Name = entry.Name, IsDirectory = (entry.Attributes & FileAttributes.Directory) != 0 };
                    if (!item.IsDirectory && !string.IsNullOrWhiteSpace(filter) && !new RoutingRule { Pattern = filter }.Matches(item)) continue;
                    if(sort==2) item.SortValue = entry.LastWriteTimeUtc.Ticks;
                    if(sort==3 && entry is FileInfo) item.SortValue = ((FileInfo)entry).Length;
                    list.Add(item);
                    if (list.Count >= 5000) { status = "Showing first 5,000 items — open Explorer for all"; break; }
                }
                IOrderedEnumerable<ItemData> ordered = list.OrderByDescending(i => i.IsDirectory);
                if (sort == 1) ordered = descending ? ordered.ThenByDescending(i=>Path.GetExtension(i.Path),StringComparer.CurrentCultureIgnoreCase) : ordered.ThenBy(i=>Path.GetExtension(i.Path),StringComparer.CurrentCultureIgnoreCase);
                if (sort >= 2) ordered = descending ? ordered.ThenByDescending(i=>i.SortValue) : ordered.ThenBy(i=>i.SortValue);
                list = (descending && sort == 0 ? ordered.ThenByDescending(i=>i.Name,StringComparer.CurrentCultureIgnoreCase) : ordered.ThenBy(i=>i.Name,StringComparer.CurrentCultureIgnoreCase)).ToList();
            }
            catch (Exception ex) { status = ex is UnauthorizedAccessException ? "Access denied — open menu to retry" : "Folder unavailable — open menu to retry"; }
            DockWork.Publish(() =>
            {
                if (disposed || !active || gen != generation) { if (candidate != null) candidate.Dispose(); return; }
                if (watcher != null) watcher.Dispose(); watcher = candidate;
                Items = list; Status = status; loading = false; owner.RefreshSoon();
                if (again) { again = false; debounce.Start(); }
            }, () => { if (candidate != null) candidate.Dispose(); });
        }, false)) { loading = false; Status = "Busy — retry from the group menu"; owner.RefreshSoon(); }
    }
    void Schedule(int gen)
    { DockWork.Publish(() => { if (!disposed && active && gen == generation) { debounce.Stop(); debounce.Start(); } }); }
    public void Dispose() { disposed = true; generation++; debounce.Dispose(); if (watcher != null) watcher.Dispose(); }
}

partial class GroupForm
{
    int snapX=int.MinValue, snapY=int.MinValue;
    Rectangle snapWorkArea;
    Rectangle[] snapRects=new Rectangle[0];
    void PrepareSnap()
    {
        snapX=snapY=int.MinValue; snapWorkArea=Screen.FromPoint(Cursor.Position).WorkingArea;
        snapRects=app.Forms.Where(f=>f!=this && app.IsShown(f) && !app.Tabs(this).Contains(f)).Select(f=>new Rectangle(f.Placed.X+f.margin,f.Placed.Y+f.margin,Math.Max(1,f.Placed.Width-f.margin*2),Math.Max(1,f.Placed.Height-f.margin*2))).ToArray();
    }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr handle);
    int currentDpi, renderX, renderY;
    int EffectiveDpi { get { if (currentDpi > 0) return currentDpi; try { return IsHandleCreated ? Math.Max(96,(int)GetDpiForWindow(Handle)) : DeviceDpi; } catch (EntryPointNotFoundException) { return DeviceDpi; } } }
    FolderPortal portal;
    public List<ItemData> Items { get { return portal == null ? Data.Items : portal.Items; } }
    int ItemSize { get { return Data.ItemSize < 0 ? C.IconSize : Data.ItemSize; } }
    bool ShowLabels { get { return Data.LabelMode < 0 ? C.Labels : Data.LabelMode == 1; } }
    readonly List<int> tileIndices = new List<int>();
    readonly List<KeyValuePair<RectangleF, GroupForm>> tabButtons = new List<KeyValuePair<RectangleF, GroupForm>>();
    RectangleF contentArea, scrollTrack, scrollThumb, backButton, homeButton, explorerButton, tabsOverflow;
    int scrollY, layoutCols = 1, resizeEdges, appliedLayer = -1;
    bool surfaceVisible, consumedMouse, scrolling;
    Point resizeStart;
    Rectangle resizeOriginal;
    List<string> dragPaths;
    float ExtraHeader { get { return (app.Tabs(this).Count > 1 ? 27 * u : 0) + (portal != null ? 29 * u : 0); } }

    public void RefreshSoon() { app.RequestRender(this); }
    public bool HasVisiblePath(string path)
    { return tileIndices.Any(i => i < Items.Count && string.Equals(Items[i].Path, path, StringComparison.OrdinalIgnoreCase)); }
    public bool HasRetryWork()
    { return tileIndices.Any(i => i < Items.Count && (Target.RetryWanted.Contains(Items[i].Path) || ShellIcon.RetryWanted.Contains(IconPx + "|" + Items[i].Path))); }
    void OpenItem(ItemData item)
    { if (portal != null && item.IsDirectory) { scrollY = 0; portal.Navigate(item.Path); } else Target.Open(item.Path); }

    int ResizeAt(Point p)
    {
        if (C.Locked || Data.Locked || Data.Collapsed) return 0;
        int edge = (int)(6 * u), bits = 0;
        if (p.X < margin - edge || p.Y < margin - edge || p.X > winW - margin + edge || p.Y > winH - margin + edge) return 0;
        if (Math.Abs(p.X - margin) <= edge) bits |= 1;
        if (Math.Abs(p.X - (winW - margin)) <= edge) bits |= 2;
        if (Math.Abs(p.Y - margin) <= edge) bits |= 4;
        if (Math.Abs(p.Y - (winH - margin)) <= edge) bits |= 8;
        return bits;
    }
    bool FeatureMouseDown(Point p)
    {
        consumedMouse = false; downTile = -1;
        resizeEdges = ResizeAt(p);
        if (resizeEdges != 0)
        {
            Capture = true; resizeStart = Cursor.Position; resizeOriginal = new Rectangle(renderX, renderY, winW - margin * 2, winH - margin * 2); consumedMouse = true; return true;
        }
        foreach (var tab in tabButtons)
            if (!Data.Collapsed && tab.Key.Contains(p)) { app.SwitchTab(tab.Value); consumedMouse = true; return true; }
        if (!Data.Collapsed && tabsOverflow.Contains(p))
        {
            var popup=KinetikSuite.MenuFocus.Attach(new ContextMenuStrip());
            foreach(var tab in app.Tabs(this)) { var target=tab; popup.Items.Add(Item(tab.Data.Name,tab==this,()=>app.SwitchTab(target))); }
            popup.Closed+=(s,e)=>popup.Dispose(); popup.Show(PointToScreen(p)); consumedMouse=true; return true;
        }
        if (portal != null && !Data.Collapsed)
        {
            if (backButton.Contains(p)) { PortalUp(); consumedMouse = true; return true; }
            if (homeButton.Contains(p)) { scrollY = 0; portal.Navigate(Data.PortalPath); consumedMouse = true; return true; }
            if (explorerButton.Contains(p)) { Target.Open(portal.CurrentPath); consumedMouse = true; return true; }
        }
        if (!Data.Collapsed && scrollTrack.Contains(p)) { scrolling = true; Capture = true; ScrollToPointer(p); consumedMouse = true; return true; }
        if (header.Contains(p) && p.X >= winW - margin - 32 * u)
        { Data.Collapsed = !Data.Collapsed; app.Save(); Render(); consumedMouse = true; return true; }
        return false;
    }
    bool FeatureMouseMove(Point p)
    {
        UpdateTooltip(p);
        if (resizeEdges != 0)
        {
            var delta = new Point(Cursor.Position.X - resizeStart.X, Cursor.Position.Y - resizeStart.Y);
            int width = resizeOriginal.Width + ((resizeEdges & 1) != 0 ? -delta.X : (resizeEdges & 2) != 0 ? delta.X : 0);
            int height = resizeOriginal.Height + ((resizeEdges & 4) != 0 ? -delta.Y : (resizeEdges & 8) != 0 ? delta.Y : 0);
            width = Math.Max((int)(170 * u), width); height = Math.Max((int)(140 * u), height);
            Data.Width = (int)(width / u); Data.Height = (int)(height / u);
            if ((resizeEdges & 1) != 0) Data.X = resizeOriginal.X + resizeOriginal.Width - width;
            if ((resizeEdges & 4) != 0) Data.Y = resizeOriginal.Y + resizeOriginal.Height - height;
            app.SyncPlacement(this); RefreshSoon(); return true;
        }
        if (scrolling) { ScrollToPointer(p); return true; }
        if (dragging || downTile >= 0) return false;
        int edges = ResizeAt(p);
        if (edges != 0)
        {
            Cursor = (edges == 9 || edges == 6) ? Cursors.SizeNESW : (edges == 5 || edges == 10) ? Cursors.SizeNWSE : (edges & 3) != 0 ? Cursors.SizeWE : Cursors.SizeNS;
            return true;
        }
        if (TileAt(p) < 0) Cursor = Cursors.Default;
        return false;
    }
    bool FeatureMouseUp()
    {
        if (!consumedMouse) return false;
        if (resizeEdges != 0) app.Save();
        resizeEdges = 0; scrolling = false; consumedMouse = false; Capture = false; Cursor = Cursors.Default; return true;
    }
    void ScrollToPointer(Point p)
    {
        int max = DockLayout.MaxScroll(Items.Count, layoutCols, TileH, contentArea.Height);
        scrollY = (int)(max * Math.Max(0, Math.Min(1, (p.Y - scrollTrack.Top - scrollThumb.Height / 2) / Math.Max(1, scrollTrack.Height - scrollThumb.Height))));
        hoverTile = -1; RefreshSoon();
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        scrollY = Math.Max(0, Math.Min(DockLayout.MaxScroll(Items.Count, layoutCols, TileH, contentArea.Height), scrollY - (int)(e.Delta / 120f * TileH * 2)));
        hoverTile = -1; RefreshSoon(); base.OnMouseWheel(e);
    }
    void PortalUp()
    {
        try { var parent = Path.GetDirectoryName(portal.CurrentPath.TrimEnd(Path.DirectorySeparatorChar)); if (!string.IsNullOrEmpty(parent)) { scrollY = 0; portal.Navigate(parent); } }
        catch (ArgumentException) { }
    }
    float PaintFeatureHeaders(Graphics g, Theme th, float x, float y, float width)
    {
        tabButtons.Clear(); tabsOverflow=RectangleF.Empty;
        var tabs = app.Tabs(this);
        if (tabs.Count > 1)
        {
            int slots=Math.Max(2,(int)(width/(70*u)));
            var shown=tabs.Take(tabs.Count>slots ? slots-1 : slots).ToList();
            if(!shown.Contains(this)) shown[shown.Count-1]=this;
            bool overflow=tabs.Count>shown.Count;
            float tabWidth = (width-(overflow?26*u:0)) / shown.Count;
            for (int i = 0; i < shown.Count; i++)
            {
                var rect = new RectangleF(x + i * tabWidth, y, tabWidth, 26 * u);
                tabButtons.Add(new KeyValuePair<RectangleF, GroupForm>(rect, shown[i]));
                using (var b = new SolidBrush(shown[i] == this ? th.Accent : th.Sub))
                {
                    g.DrawString(shown[i].Data.Name, F(8), b, RectangleF.Inflate(rect, -4 * u, 0), fmtL);
                    if (shown[i] == this) g.FillRectangle(b, rect.X, rect.Bottom - 2 * u, rect.Width, 2 * u);
                }
            }
            if(overflow) { tabsOverflow=new RectangleF(x+width-26*u,y,26*u,26*u); using(var b=new SolidBrush(th.Sub)) g.DrawString("…",F(11),b,tabsOverflow,fmtC); }
            y += 27 * u;
        }
        if (portal != null)
        {
            backButton = new RectangleF(x, y, 25 * u, 28 * u); homeButton = new RectangleF(x + 26 * u, y, 25 * u, 28 * u); explorerButton = new RectangleF(x + 54 * u, y, Math.Max(1, width - 54 * u), 28 * u);
            using (var b = new SolidBrush(th.Sub))
            {
                g.DrawString("↑", F(11), b, backButton, fmtC); g.DrawString("⌂", F(11), b, homeButton, fmtC);
                string label = string.IsNullOrEmpty(portal.Status) ? Target.NameFor(portal.CurrentPath) : portal.Status;
                g.DrawString(label, F(8), b, explorerButton, fmtL);
            }
            y += 29 * u;
        }
        return y;
    }
    void PaintScrollAndGrip(Graphics g, Theme th, RectangleF card)
    {
        int max = DockLayout.MaxScroll(Items.Count, layoutCols, TileH, contentArea.Height);
        scrollTrack = RectangleF.Empty; scrollThumb = RectangleF.Empty;
        using (var b = new SolidBrush(th.Sub))
        {
            if (max > 0)
            {
                scrollTrack = new RectangleF(card.Right - 10 * u, contentArea.Top + 3 * u, 7 * u, Math.Max(1, contentArea.Height - 6 * u));
                float height = Math.Max(22 * u, scrollTrack.Height * contentArea.Height / (contentArea.Height + max));
                height = Math.Min(height, scrollTrack.Height);
                scrollThumb = new RectangleF(scrollTrack.X + 2 * u, scrollTrack.Y + (scrollTrack.Height - height) * scrollY / max, 3 * u, height);
                g.FillRectangle(b, scrollThumb);
            }
        }
        if (!C.Locked && !Data.Locked)
            using (var p = new Pen(th.Sub, 1))
                for (int i = 0; i < 3; i++) { float d = (4 + i * 3) * u; g.DrawLine(p, card.Right - d, card.Bottom - 3 * u, card.Right - 3 * u, card.Bottom - d); }
    }
    void BuildGroupFeatureMenu(ToolStripItemCollection items)
    {
        items.Add(Item("Select items with keyboard…", false, () => { keyboardMode = true; if (Items.Count > 0) { focusedItem = 0; selected.Clear(); selected.Add(Items[0].Id); } Activate(); Focus(); Render(); }));
        items.Add(Item("Lock this group", Data.Locked, () => { Data.Locked = !Data.Locked; app.Save(); Render(); }));
        var view = new ToolStripMenuItem("Group appearance");
        view.DropDownItems.Add(Item("Accent colour…", Data.AccentArgb != 0, () => { using (var dlg = new ColorDialog { Color = Data.AccentArgb == 0 ? Theme.Get(C.ThemeName).Accent : Color.FromArgb(Data.AccentArgb), FullOpen = true }) if (dlg.ShowDialog() == DialogResult.OK) { Data.AccentArgb = dlg.Color.ToArgb(); app.Save(); Render(); } }));
        if (Data.AccentArgb != 0) view.DropDownItems.Add(Item("Reset accent colour", false, () => { Data.AccentArgb = 0; app.Save(); Render(); }));
        view.DropDownItems.Add(Item("Compact spacing", Data.Compact, () => { Data.Compact = !Data.Compact; app.Save(); Render(); }));
        view.DropDownItems.Add(Item("List view", Data.ListView, () => { Data.ListView = !Data.ListView; app.Save(); Render(); }));
        view.DropDownItems.Add(Item("Automatic size", Data.Width == 0 && Data.Height == 0, () => { Data.Width = Data.Height = 0; app.SyncPlacement(this); app.Save(); Render(); }));
        var icons = new ToolStripMenuItem("Icon size");
        string[] names = { "Use global size", "Small", "Normal", "Large" };
        for (int i = -1; i <= 2; i++) { int size = i; icons.DropDownItems.Add(Item(names[i + 1], Data.ItemSize == i, () => { Data.ItemSize = size; app.Save(); Render(); })); }
        view.DropDownItems.Add(icons);
        var labels = new ToolStripMenuItem("Labels");
        for (int i = -1; i <= 1; i++) { int mode = i; labels.DropDownItems.Add(Item(i == -1 ? "Use global labels" : i == 0 ? "Hide names" : "Show names", Data.LabelMode == i, () => { Data.LabelMode = mode; app.Save(); Render(); })); }
        view.DropDownItems.Add(labels); items.Add(view);
        var tabs = new ToolStripMenuItem("Tabs");
        foreach (var f in app.Forms.Where(f => f != this && f.Data.Workspace == Data.Workspace && (string.IsNullOrEmpty(Data.TabSet) || f.Data.TabSet != Data.TabSet)))
        { var with = f; tabs.DropDownItems.Add(Item("Combine with " + f.Data.Name, false, () => app.CombineTabs(this, with))); }
        if (!string.IsNullOrEmpty(Data.TabSet)) tabs.DropDownItems.Add(Item("Detach this tab", false, () => { Data.TabSet = null; Data.X += 30; Data.Y += 30; app.Save(); app.RenderAll(); }));
        if (tabs.DropDownItems.Count > 0) items.Add(tabs);
        var workspace = new ToolStripMenuItem("Move to workspace");
        foreach (var name in app.C.List.Select(g => g.Workspace).Distinct())
        { string n = name; workspace.DropDownItems.Add(Item(n, n == Data.Workspace, () => { Data.Workspace = n; Data.TabSet = null; app.Save(); app.RenderAll(); })); }
        items.Add(workspace);
        if (portal != null)
        {
            var back = Item("Back",false,portal.GoBack); back.Enabled=portal.CanBack; items.Add(back);
            var forward = Item("Forward",false,portal.GoForward); forward.Enabled=portal.CanForward; items.Add(forward);
            var folderView = new ToolStripMenuItem("Folder view");
            folderView.DropDownItems.Add(Item("Filter files…", !string.IsNullOrWhiteSpace(Data.PortalFilter), () => { var filter=Prompt("Folder filter","Filename patterns, e.g. *.pdf;*.docx (blank for all):",Data.PortalFilter??""); if(filter!=null && filter.Length<=512) { Data.PortalFilter=filter.Trim(); app.Save(); portal.Refresh(); } }));
            string[] sorts={"Name","Type","Date modified","Size"};
            for(int i=0;i<sorts.Length;i++) { int mode=i; folderView.DropDownItems.Add(Item("Sort by "+sorts[i],Data.PortalSort==i,()=> { Data.PortalSort=mode; app.Save(); portal.Refresh(); })); }
            folderView.DropDownItems.Add(Item("Descending",Data.PortalDescending,()=> { Data.PortalDescending=!Data.PortalDescending; app.Save(); portal.Refresh(); }));
            folderView.DropDownItems.Add(Item("Show hidden items",Data.ShowHidden,()=> { Data.ShowHidden=!Data.ShowHidden; app.Save(); portal.Refresh(); }));
            items.Add(folderView);
            items.Add(Item("Refresh folder", false, portal.Refresh));
            items.Add(Item("Open current folder in Explorer", false, () => Target.Open(portal.CurrentPath)));
            items.Add(Item("Return to home folder", false, () => { scrollY = 0; portal.Navigate(Data.PortalPath); }));
        }
        items.Add(new ToolStripSeparator());
    }

    readonly HashSet<string> selected = new HashSet<string>();
    int anchor = -1, focusedItem = -1;
    bool keyboardMode;
    long lastDragScroll;
    bool SelectionMouseDown(int index)
    {
        focusedItem = index;
        if ((ModifierKeys & Keys.Shift) != 0 && anchor >= 0)
        {
            selected.Clear(); for (int i = Math.Min(anchor,index); i <= Math.Max(anchor,index) && i < Items.Count; i++) selected.Add(Items[i].Id);
        }
        else if ((ModifierKeys & Keys.Control) != 0)
        { if (!selected.Add(Items[index].Id)) selected.Remove(Items[index].Id); anchor = index; }
        else { if (!selected.Contains(Items[index].Id)) selected.Clear(); anchor = index; return false; }
        downTile = -1; consumedMouse = true; RefreshSoon(); return true;
    }
    void AutoScrollDrop(Point point)
    {
        if (Data.Collapsed || Stopwatch.GetTimestamp() - lastDragScroll < Stopwatch.Frequency / 8) return;
        int direction = point.Y < contentArea.Top + 20 * u ? -1 : point.Y > contentArea.Bottom - 20 * u ? 1 : 0;
        if (direction == 0) return; lastDragScroll = Stopwatch.GetTimestamp();
        scrollY = Math.Max(0,Math.Min(DockLayout.MaxScroll(Items.Count,layoutCols,TileH,contentArea.Height), scrollY + (int)(direction * TileH)));
        RefreshSoon();
    }
    public void MoveReferences(GroupForm source, string[] ids, int at, bool copy)
    {
        if (source.portal != null || portal != null) return;
        var wanted = new HashSet<string>(ids);
        var bundle = source.Items.Where(i => wanted.Contains(i.Id)).ToList();
        at = Math.Max(0,Math.Min(at,Items.Count));
        if (!copy)
        {
            if (source == this) at -= Items.Take(at).Count(i=>wanted.Contains(i.Id));
            source.Items.RemoveAll(i=>wanted.Contains(i.Id)); source.selected.Clear();
        }
        selected.Clear();
        foreach (var item in bundle)
        {
            var inserted = copy ? new ItemData { Name=item.Name, Path=item.Path, IsDirectory=item.IsDirectory } : item;
            Items.Insert(Math.Min(at++,Items.Count),inserted); selected.Add(inserted.Id);
        }
        RefreshSoon(); source.RefreshSoon();
    }
    void AddSelectionMenu(ToolStripItemCollection items, ItemData item)
    {
        items.Add(Item("Copy path", false, () => Clipboard.SetText(item.Path)));
        if (portal == null && selected.Count > 0)
        {
            items.Add(Item("Remove selected references (" + selected.Count + ")",false,RemoveSelected));
            var move = new ToolStripMenuItem("Move selected to");
            foreach (var target in app.Forms.Where(f=>f!=this && f.portal==null))
            { var to=target; move.DropDownItems.Add(Item(to.Data.Name,false,()=> { to.MoveReferences(this,selected.ToArray(),to.Items.Count,false); app.Save(); })); }
            if(move.DropDownItems.Count>0) items.Add(move);
        }
    }
    void RemoveSelected()
    { if(portal!=null) return; Items.RemoveAll(i=>selected.Contains(i.Id)); selected.Clear(); app.Save(); Render(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!keyboardMode) return base.ProcessCmdKey(ref msg,keyData);
        Keys key = keyData & Keys.KeyCode;
        if(key==Keys.Escape) { selected.Clear(); keyboardMode=false; RefreshSoon(); return true; }
        if(keyData==(Keys.Control|Keys.Z)) { app.Undo(false); return true; }
        if(keyData==(Keys.Control|Keys.Y)) { app.Undo(true); return true; }
        if(keyData==(Keys.Control|Keys.A)) { selected.Clear(); foreach(var item in Items) selected.Add(item.Id); RefreshSoon(); return true; }
        if(key==Keys.Delete && portal==null) { RemoveSelected(); return true; }
        if(key==Keys.Enter && focusedItem>=0 && focusedItem<Items.Count) { OpenItem(Items[focusedItem]); return true; }
        if(key==Keys.F2 && portal==null && focusedItem>=0 && focusedItem<Items.Count)
        {
            var item=Items[focusedItem]; var name=Prompt("Rename shortcut","Name shown in the group:",item.Name);
            if(!string.IsNullOrWhiteSpace(name)) { item.Name=name.Trim(); app.Save(); Render(); } return true;
        }
        int step=key==Keys.Left?-1:key==Keys.Right?1:key==Keys.Up?-layoutCols:key==Keys.Down?layoutCols:0;
        if(step!=0 && Items.Count>0)
        {
            focusedItem=Math.Max(0,Math.Min(Items.Count-1,focusedItem+step));
            if((keyData&Keys.Shift)==0) { selected.Clear(); anchor=focusedItem; }
            selected.Add(Items[focusedItem].Id);
            float top=(focusedItem/layoutCols)*TileH;
            if(top<scrollY) scrollY=(int)top;
            if(top+TileH>scrollY+contentArea.Height) scrollY=(int)(top+TileH-contentArea.Height);
            RefreshSoon(); return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }

    readonly ToolTip tip = new ToolTip { InitialDelay=650, ReshowDelay=150, AutoPopDelay=7000, ShowAlways=true };
    string tooltipText;
    void UpdateTooltip(Point p)
    {
        int item=TileAt(p);
        string text=item>=0 && item<Items.Count ? Items[item].Name+"\n"+Items[item].Path : header.Contains(p) ? Data.Name+"\nDouble-click title to roll up. Drag title to move; drag edges to resize." : portal!=null && explorerButton.Contains(p) ? portal.CurrentPath+"\nClick to open in Explorer" : "";
        if(text!=tooltipText) { tooltipText=text; tip.SetToolTip(this,text); }
    }
    protected override AccessibleObject CreateAccessibilityInstance() { return new DockAccessible(this); }
    sealed class DockAccessible : ControlAccessibleObject
    {
        readonly GroupForm owner;
        public DockAccessible(GroupForm owner) : base(owner) { this.owner=owner; }
        public override string Name { get { return owner.Data.Name; } set { } }
        public override AccessibleRole Role { get { return AccessibleRole.List; } }
        public override int GetChildCount() { return owner.Data.Collapsed ? 0 : owner.Items.Count; }
        public override AccessibleObject GetChild(int index) { return index>=0 && index<GetChildCount() ? new ItemAccessible(owner,index,this) : null; }
    }
    sealed class ItemAccessible : AccessibleObject
    {
        readonly GroupForm owner; readonly int index; readonly AccessibleObject parent;
        public ItemAccessible(GroupForm owner,int index,AccessibleObject parent) { this.owner=owner; this.index=index; this.parent=parent; }
        ItemData Item { get { return index<owner.Items.Count ? owner.Items[index] : null; } }
        public override string Name { get { return Item==null ? "" : Item.Name; } set { } }
        public override string Description { get { return Item==null ? "" : Item.Path; } }
        public override string DefaultAction { get { return Item!=null && Item.IsDirectory && owner.portal!=null ? "Browse folder" : "Open"; } }
        public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
        public override AccessibleObject Parent { get { return parent; } }
        public override Rectangle Bounds
        {
            get
            {
                int visible=owner.tileIndices.IndexOf(index);
                if(visible<0 || visible>=owner.tiles.Count || owner.Data.Collapsed || !owner.app.IsShown(owner)) return Rectangle.Empty;
                var rect=Rectangle.Round(RectangleF.Intersect(owner.tiles[visible],owner.contentArea)); rect.Offset(owner.renderX,owner.renderY); return rect;
            }
        }
        public override AccessibleStates State { get { return AccessibleStates.Selectable|AccessibleStates.Focusable|(Bounds.IsEmpty?AccessibleStates.Offscreen:AccessibleStates.None)|(Item!=null && owner.selected.Contains(Item.Id)?AccessibleStates.Selected:AccessibleStates.None)|(owner.keyboardMode && owner.focusedItem==index?AccessibleStates.Focused:AccessibleStates.None); } }
        public override void DoDefaultAction() { var item=Item; if(item!=null && !owner.IsDisposed) owner.BeginInvoke((Action)(()=>owner.OpenItem(item))); }
        public override void Select(AccessibleSelection flags)
        {
            var item=Item; if(item==null) return;
            owner.keyboardMode=true; owner.focusedItem=index; owner.selected.Clear(); owner.selected.Add(item.Id); owner.RefreshSoon();
        }
    }
}
