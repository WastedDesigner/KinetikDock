// Kinetik Dock: groups of shortcuts (apps, files, folders and web links) on the desktop, in the style of Kinetik's widget.
// Each group is its own card: drop things onto it to add them, click to open, drag to rearrange or move between groups,
// double-click the title to roll it up.
// Copyright © 2026 Akila Sella Hennedige. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Kinetik Dock")]
[assembly: AssemblyProduct("Kinetik Dock")]
[assembly: AssemblyCopyright("Copyright © 2026 Akila Sella Hennedige")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

static class Program
{
    public const string Version = "1.2.0";
    // --demo: made-up groups, nothing read from or saved to this PC. "--theme Name" picks its theme.
    // "--shot file.png" draws the demo groups into a PNG and quits (for the README screenshots).
    public static bool Demo;
    public static string DemoTheme, ShotPath;
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        Demo = args.Contains("--demo");
        int ti = Array.IndexOf(args, "--theme");
        if (ti >= 0 && ti + 1 < args.Length) DemoTheme = args[ti + 1];
        int si = Array.IndexOf(args, "--shot");
        if (si >= 0 && si + 1 < args.Length) { ShotPath = Path.GetFullPath(args[si + 1]); Demo = true; }
        bool created;
        using (new Mutex(true, Demo ? @"Local\KinetikDockDemo" + (ShotPath != null ? Guid.NewGuid().ToString("N") : "") : @"Local\KinetikDock", out created))
        {
            if (!created) return; // already running
            try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware(); } catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
            Application.OleRequired();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ToolStripManager.Renderer = new DarkMenu();
            Backup.KeyName = "KinetikDock"; Backup.AppTitle = "Kinetik Dock";
            if (!Demo) Backup.OfferRestore();
            var app = new DockContext();
            using (new KinetikSuite.Shutdown("KinetikDock" + (Demo ? "Demo" : ""), app.Quit))
                Application.Run(app);
        }
        if (!Demo) Backup.AfterExit();
    }
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}

// ======================================================================= Settings
public class ItemData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public bool IsDirectory { get; set; }
    [ScriptIgnore] public long SortValue { get; set; }
    public ItemData() { Id = Guid.NewGuid().ToString("N"); }
}

public class GroupData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Cols { get; set; }
    public bool Collapsed { get; set; }
    public bool Locked { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ItemSize { get; set; }
    public int LabelMode { get; set; }
    public int AccentArgb { get; set; }
    public bool Compact { get; set; }
    public bool ListView { get; set; }
    public string Workspace { get; set; }
    public string TabSet { get; set; }
    public string PortalPath { get; set; }
    public string PortalFilter { get; set; }
    public int PortalSort { get; set; }
    public bool PortalDescending { get; set; }
    public bool ShowHidden { get; set; }
    public List<ItemData> Items { get; set; }
    public GroupData() { Id = Guid.NewGuid().ToString("N"); Name = "Shortcuts"; X = int.MinValue; Y = int.MinValue; Cols = 4; ItemSize = -1; LabelMode = -1; Workspace = "Main"; Items = new List<ItemData>(); }
}

class Settings
{
    const string KeyPath = @"Software\KinetikDock";
    public int Scale = 100, Opacity = CommonMenu.DefaultOpacity, Layer = 0, IconSize = 1; // IconSize: 0 small, 1 normal, 2 large
    public bool Locked, Snap = true, Labels = true;
    public bool Lightweight, Hotkeys = true, DoubleClickOpen;
    public string Workspace = "Main";
    public bool AutoOrganise;
    public string RulesJson = "", DesktopSeenJson = "[]", AutomationWorkspace = "Main";
    public string ThemeName = "Midnight", FontName = "";
    public string Groups = "";  // the groups as JSON

    public List<GroupData> List = new List<GroupData>(); // not saved as a field: kept in Groups

    static IEnumerable<FieldInfo> Fields
    {
        get
        {
            return typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(bool) || f.FieldType == typeof(string));
        }
    }

    public static bool Unreadable;

    public static Settings Load()
    {
        if (Program.Demo) return Demo();
        Settings s = null;
        Unreadable = true;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0) Thread.Sleep(500);
            bool ok;
            s = TryLoad(out ok);
            if (ok) { Unreadable = false; s.Opacity = CommonMenu.Migrate(s.Opacity); break; }
        }
        if (Unreadable)
            MessageBox.Show("Kinetik Dock couldn't read its saved settings, so it's showing an empty group for now.\n\n" +
                "Your settings haven't been changed, and nothing will be saved over them while this is showing. " +
                "Restart Kinetik Dock to try again" + (Backup.Exists ? ", or use right-click → Backup → Restore settings…" : "."),
                "Kinetik Dock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        try { if (s.Groups != "") s.List = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<List<GroupData>>(s.Groups) ?? new List<GroupData>(); }
        catch { s.List = new List<GroupData>(); if (s.Groups != "") Unreadable = true; }
        s.List.RemoveAll(g => g == null);
        foreach (var g in s.List)
        {
            if (g.Items == null) g.Items = new List<ItemData>();
            g.Items.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.Path));
            foreach (var item in g.Items) if (item.Name == null) item.Name = Target.NameFor(item.Path);
            g.Cols = Math.Max(1, Math.Min(8, g.Cols));
            if (g.Name == null) g.Name = "Shortcuts";
            if (string.IsNullOrEmpty(g.Id)) g.Id = Guid.NewGuid().ToString("N");
            DockLayout.Sanitize(g);
        }
        if (s.List.Count == 0) s.List.Add(new GroupData());
        return s;
    }

    static Settings Demo()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sys = Environment.SystemDirectory;
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var s = new Settings { Layer = 2, ThemeName = Program.DemoTheme ?? "Midnight" };
        var a = new GroupData { Name = "Apps", X = 200, Y = 160, Cols = 4 };
        a.Items.Add(new ItemData { Name = "Notepad", Path = Path.Combine(sys, "notepad.exe") });
        a.Items.Add(new ItemData { Name = "File Explorer", Path = Path.Combine(win, "explorer.exe") });
        a.Items.Add(new ItemData { Name = "Terminal", Path = Path.Combine(sys, "cmd.exe") });
        a.Items.Add(new ItemData { Name = "Task Manager", Path = Path.Combine(sys, "taskmgr.exe") });
        a.Items.Add(new ItemData { Name = "Calculator", Path = Path.Combine(sys, "calc.exe") });
        a.Items.Add(new ItemData { Name = "Registry Editor", Path = Path.Combine(win, "regedit.exe") });
        var f = new GroupData { Name = "Folders", X = 560, Y = 160, Cols = 3 };
        f.Items.Add(new ItemData { Name = "Documents", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) });
        f.Items.Add(new ItemData { Name = "Desktop", Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) });
        f.Items.Add(new ItemData { Name = "Pictures", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) });
        var w = new GroupData { Name = "Web", X = 560, Y = 340, Cols = 3 };
        w.Items.Add(new ItemData { Name = "GitHub", Path = "https://github.com/" });
        w.Items.Add(new ItemData { Name = "Open-Meteo", Path = "https://open-meteo.com/" });
        w.Items.Add(new ItemData { Name = "Wikipedia", Path = "https://en.wikipedia.org/" });
        s.List.AddRange(new[] { a, f, w });
        return s;
    }

    static Settings TryLoad(out bool ok)
    {
        ok = true;
        var s = new Settings();
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                if (k != null)
                    foreach (var f in Fields)
                    {
                        var v = k.GetValue(f.Name);
                        if (v == null) continue;
                        try
                        {
                            if (f.FieldType == typeof(string)) f.SetValue(s, v.ToString());
                            else if (f.FieldType == typeof(int)) f.SetValue(s, Convert.ToInt32(v));
                            else f.SetValue(s, Convert.ToInt32(v) != 0);
                        }
                        catch { ok = false; }
                    }
        }
        catch { ok = false; }
        KinetikSuite.SettingsValidation.Sanitize(s); return s;
    }

    public static string LastSaveError;
    public bool Save()
    {
        if (Program.Demo) return true;
        if (Unreadable) return false;
        KinetikSuite.SettingsValidation.Sanitize(this);
        if (KinetikSuite.RegistryFiles.CannotSave) return false;
        try
        {
            Groups = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(List);
            using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                foreach (var f in Fields)
                {
                    var v = f.GetValue(this);
                    object desired = f.FieldType == typeof(bool) ? (object)((bool)v ? 1 : 0) : v ?? "";
                    if (!object.Equals(k.GetValue(f.Name), desired)) k.SetValue(f.Name, desired);
                }
            LastSaveError = null; return true;
        }
        catch (Exception ex) { LastSaveError = ex.Message; return false; }
    }
}

// ======================================================================= What an item is, and its icon
// Shell and filesystem work never runs inside painting. Icon APIs use one STA worker.
static class DockWork
{
    static readonly System.Collections.Concurrent.BlockingCollection<Action> icons = new System.Collections.Concurrent.BlockingCollection<Action>(256);
    static readonly System.Collections.Concurrent.BlockingCollection<Action> io = new System.Collections.Concurrent.BlockingCollection<Action>(256);
    static readonly System.Collections.Concurrent.BlockingCollection<Action> launches = new System.Collections.Concurrent.BlockingCollection<Action>(32);
    static bool launchStarted;
    static Control ui;
    static Action<string> changed;
    public static void Initialize(Action<string> refresh)
    {
        changed = refresh; ui = new Control(); var handle = ui.Handle;
        var iconThread = new Thread(() => Consume(icons)) { IsBackground = true, Name = "Dock icons" };
        iconThread.SetApartmentState(ApartmentState.STA); iconThread.Start();
        for (int i = 0; i < 2; i++) new Thread(() => Consume(io)) { IsBackground = true, Name = "Dock paths" }.Start();
    }
    static void Consume(System.Collections.Concurrent.BlockingCollection<Action> queue)
    { foreach (var work in queue.GetConsumingEnumerable()) try { work(); } catch { } }
    public static bool Queue(Action work, bool icon)
    { try { return (icon ? icons : io).TryAdd(work); } catch (InvalidOperationException) { return false; } }
    public static void Launch(Action work)
    {
        if (!launchStarted)
        {
            launchStarted = true;
            var thread = new Thread(() => Consume(launches)) { IsBackground = true, Name = "Dock launcher" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        try { if (!launches.TryAdd(work)) MessageBox.Show("Several launches are still pending. Try again in a moment.", "Kinetik Dock"); } catch (InvalidOperationException) { }
    }
    public static void Publish(Action update, Action abandoned = null, string path = null)
    {
        try { ui.BeginInvoke((Action)(() => { if (ui.IsDisposed) { if (abandoned != null) abandoned(); return; } update(); if (changed != null && path != null) changed(path); })); }
        catch { if (abandoned != null) abandoned(); }
    }
    public static void Stop() { changed = null; icons.CompleteAdding(); io.CompleteAdding(); launches.CompleteAdding(); if (ui != null) ui.Dispose(); }
}

static class Target
{
    static readonly string[] Schemes = { "http:", "https:", "ftp:", "mailto:", "shell:", "ms-settings:", "steam:", "obsidian:", "spotify:" };

    public static bool IsLink(string p) { return p != null && Schemes.Any(s => p.StartsWith(s, StringComparison.OrdinalIgnoreCase)) && !p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase); }
    public static bool IsShell(string p) { return p != null && p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase); }

    // A name for something just added: the file name without .lnk/.exe/.url, the folder's name, the site's name.
    public static string NameFor(string p)
    {
        if (IsLink(p))
        {
            Uri u;
            if (Uri.TryCreate(p, UriKind.Absolute, out u) && !string.IsNullOrEmpty(u.Host))
            {
                var h = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
                var first = h.Split('.')[0];
                return first.Length > 0 ? char.ToUpper(first[0]) + first.Substring(1) : h;
            }
            return p;
        }
        if (IsShell(p)) return p.Substring(6);
        var t = p.TrimEnd('\\');
        if (t.Length <= 3) return t; // a drive, "C:"
        var n = System.IO.Path.GetFileName(t);
        var ext = System.IO.Path.GetExtension(n).ToLowerInvariant();
        if (ext == ".lnk" || ext == ".exe" || ext == ".url" || ext == ".appref-ms") n = System.IO.Path.GetFileNameWithoutExtension(n);
        return n;
    }

    // Whether a file or folder is still there, checked at most once a minute per path (network drives can be slow).
    static readonly Dictionary<string, KeyValuePair<DateTime, bool>> seen = new Dictionary<string, KeyValuePair<DateTime, bool>>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static readonly HashSet<string> RetryWanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static bool Exists(string p)
    {
        if (string.IsNullOrEmpty(p)) return false;
        if (IsLink(p) || IsShell(p)) return true;
        KeyValuePair<DateTime, bool> v;
        bool known = seen.TryGetValue(p, out v);
        if (known && (DateTime.UtcNow - v.Key).TotalSeconds < 60) return v.Value;
        if (pending.Add(p) && !DockWork.Queue(() =>
        {
            bool exists = File.Exists(p) || Directory.Exists(p);
            DockWork.Publish(() => { pending.Remove(p); seen[p] = new KeyValuePair<DateTime, bool>(DateTime.UtcNow, exists); if (seen.Count > 4096) seen.Remove(seen.OrderBy(k => k.Value.Key).First().Key); }, null, p);
        }, false)) { pending.Remove(p); RetryWanted.Add(p); }
        else if (pending.Contains(p)) RetryWanted.Remove(p);
        return !known || v.Value;
    }
    public static void Forget(string p) { seen.Remove(p); }

    public static void Open(string p)
    {
        DockWork.Launch(() =>
        {
        try
        {
            var psi = new ProcessStartInfo(p) { UseShellExecute = true };
            // Let the shell resolve the target; synchronous existence probes can stall on offline shares.
            if (!IsLink(p) && !IsShell(p) && Path.IsPathRooted(p)) psi.WorkingDirectory = Path.GetDirectoryName(p) ?? "";
            Process.Start(psi);
        }
        catch (Exception ex) { DockWork.Publish(() => MessageBox.Show("Couldn't open " + p + "\n\n" + ex.Message, "Kinetik Dock", MessageBoxButtons.OK, MessageBoxIcon.Warning)); }
        });
    }

    public static void Reveal(string p)
    {
        DockWork.Launch(() =>
        {
        try
        {
            Process.Start("explorer.exe", "/select,\"" + p + "\"");
        }
        catch { }
        });
    }
}

// The Windows shell's own icon for a path, at the size asked for, with its transparency (IShellItemImageFactory).
static class ShellIcon
{
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int Type, Width, Height, WidthBytes; public short Planes, BitsPixel; public IntPtr Bits; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

    static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, DateTime> fetched = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    public static readonly HashSet<string> RetryWanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, long> used = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    public static long Bytes;
    public static int Count { get { return cache.Count; } }
    static int generation;
    public static Bitmap Get(string path, int px)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var key = px + "|" + path;
        Bitmap bitmap; DateTime at;
        bool known = cache.TryGetValue(key, out bitmap);
        if (known && bitmap != null) { used[key] = Stopwatch.GetTimestamp(); return bitmap; }
        if (known && fetched.TryGetValue(key, out at) && (DateTime.UtcNow - at).TotalSeconds < 60) return null;
        int gen = generation;
        if (pending.Add(key) && !DockWork.Queue(() =>
        {
            var loaded = Load(path, px);
            DockWork.Publish(() =>
            {
                if (gen != generation) { if (loaded != null) loaded.Dispose(); return; }
                pending.Remove(key); fetched[key] = DateTime.UtcNow; cache[key] = loaded; used[key] = Stopwatch.GetTimestamp();
                if (loaded != null) Bytes += (long)loaded.Width * loaded.Height * 4;
                while (Bytes > 24 * 1024 * 1024 || cache.Count > 2048)
                {
                    var victim = used.OrderBy(k => k.Value).First().Key;
                    var old = cache[victim];
                    if (old != null) { Bytes -= (long)old.Width * old.Height * 4; old.Dispose(); }
                    cache.Remove(victim); fetched.Remove(victim); used.Remove(victim);
                }
            }, () => { if (loaded != null) loaded.Dispose(); }, path);
        }, true)) { pending.Remove(key); RetryWanted.Add(key); }
        else if (pending.Contains(key)) RetryWanted.Remove(key);
        return bitmap;
    }

    public static void Clear()
    { generation++; foreach (var b in cache.Values) if (b != null) b.Dispose(); cache.Clear(); fetched.Clear(); pending.Clear(); used.Clear(); RetryWanted.Clear(); Bytes = 0; }

    static Bitmap Load(string path, int px)
    {
        IShellItemImageFactory f = null;
        IntPtr h = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
            if (f.GetImage(new SIZE { cx = px, cy = px }, 0x4 | 0x1, out h) != 0 || h == IntPtr.Zero) return null; // ICONONLY | BIGGERSIZEOK
            var bm = new BITMAP();
            GetObject(h, Marshal.SizeOf(typeof(BITMAP)), ref bm);
            int w = bm.Width, ht = Math.Abs(bm.Height);
            var bmi = new BITMAPINFOHEADER { Size = 40, Width = w, Height = -ht, Planes = 1, BitCount = 32 };
            var bits = new byte[w * ht * 4];
            var dc = GetDC(IntPtr.Zero);
            try { GetDIBits(dc, h, 0, (uint)ht, bits, ref bmi, 0); } finally { ReleaseDC(IntPtr.Zero, dc); }
            bool anyAlpha = false;
            for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
            var bmp = new Bitmap(w, ht, PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, ht), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(bits, 0, data.Scan0, bits.Length); } finally { bmp.UnlockBits(data); }
            return bmp;
        }
        catch { return null; }
        finally
        {
            if (h != IntPtr.Zero) DeleteObject(h);
            if (f != null) Marshal.ReleaseComObject(f);
        }
    }
}

// ======================================================================= The app: a tray icon and a window per group
partial class DockContext : ApplicationContext
{
    public readonly Settings C = Settings.Load();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = KinetikSuite.MenuFocus.Attach(new ContextMenuStrip());
    public readonly List<GroupForm> Forms = new List<GroupForm>();
    ThemePicker picker;

    public DockContext()
    {
        DockWork.Initialize(RefreshPath);
        InitializeFeatures();
        try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
        tray.Text = Program.Demo ? "Kinetik Dock (demo)" : "Kinetik Dock";
        tray.ContextMenuStrip = menu;
        menu.Opening += (s, e) => { BuildMenu(menu.Items, null); };
        Backup.BeforeExit = () => tray.Visible = false;
        tray.Visible = Program.ShotPath == null;
        foreach (var g in C.List) Open(g);
        SystemEvents.DisplaySettingsChanged += OnDisplay;
        if (Program.ShotPath != null)
        {
            var t = new System.Windows.Forms.Timer { Interval = 800 };
            t.Tick += (s, e) => { t.Stop(); Shot(); };
            t.Start();
        }
    }

    void OnDisplay(object s, EventArgs e) { DockWork.Publish(() => { foreach (var f in Forms.ToList()) f.Render(); }); }

    GroupForm Open(GroupData g)
    {
        var f = new GroupForm(this, g);
        Forms.Add(f);
        f.FormClosed += (s, e) => Forms.Remove(f);
        f.Show();
        return f;
    }

    // All the groups, as they sit on screen, in one PNG.
    void Shot()
    {
        try
        {
            var rects = Forms.Select(f => f.Placed).ToList();
            var all = rects.Aggregate(Rectangle.Union);
            using (var bmp = new Bitmap(all.Width, all.Height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                foreach (var f in Forms) { var img = f.Frame; if (img != null) g.DrawImage(img, f.Placed.X - all.X, f.Placed.Y - all.Y, img.Width, img.Height); }
                bmp.Save(Program.ShotPath, ImageFormat.Png);
            }
        }
        catch { }
        Quit();
    }

    public void Save() { QueueSave(); }
    public void RenderAll() { foreach (var f in Forms.ToList()) f.Render(); }

    public void NewGroup(Point at)
    {
        var name = GroupForm.Prompt("New group", "Name of the group:", "Shortcuts");
        if (name == null) return;
        var g = new GroupData { Name = name.Trim() == "" ? "Shortcuts" : name.Trim(), X = at.X, Y = at.Y, Workspace = C.Workspace };
        C.List.Add(g);
        Save();
        Open(g);
    }

    public void DeleteGroup(GroupForm f)
    {
        if (f.Data.Items.Count > 0 && MessageBox.Show("Delete the group \"" + f.Data.Name + "\" and its " + f.Data.Items.Count + " shortcuts?\n\nOnly the shortcuts in Kinetik Dock are removed, not the files they open.",
                "Kinetik Dock", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        C.List.Remove(f.Data);
        if (C.List.Count == 0) { var g = new GroupData(); C.List.Add(g); Save(); f.Close(); Open(g); return; }
        Save();
        f.Close();
        RenderAll();
    }

    public void SetLayer(int l)
    {
        C.Layer = l; Save();
        foreach (var f in Forms.ToList()) f.ApplyLayer();
    }

    public void Quit()
    {
        FlushSave();
        CloseFeatures();
        tray.Visible = false;
        SystemEvents.DisplaySettingsChanged -= OnDisplay;
        foreach (var f in Forms.ToList()) f.Close();
        ExitThread();
    }

    ToolStripMenuItem Item(string text, bool check, Action click)
    {
        var it = new ToolStripMenuItem(text) { Checked = check };
        it.Click += (s, e) => click();
        return it;
    }

    void Changed() { Save(); RenderAll(); }

    // The look, placement, startup and backup options, shared word for word with the other Kinetik apps.
    CommonMenu Common()
    {
        return new CommonMenu
        {
            Title = "Kinetik Dock", Version = Program.Version,
            OpenTheme = () =>
            {
                if (picker != null && !picker.IsDisposed) { picker.Activate(); return; }
                picker = new ThemePicker(C.ThemeName, n => { C.ThemeName = n; Changed(); });
                picker.Show();
            },
            Scale = () => C.Scale, SetScale = v => { C.Scale = v; Changed(); },
            FontName = () => C.FontName, SetFontName = v => { C.FontName = v; Changed(); },
            Opacity = () => C.Opacity, SetOpacity = v => { C.Opacity = v; Changed(); },
            Layer = () => C.Layer, SetLayer = SetLayer,
            Locked = () => C.Locked, SetLocked = v => { C.Locked = v; Changed(); }, LockText = "Lock all groups in place",
            Snap = () => C.Snap, SetSnap = v => { C.Snap = v; Changed(); },
            RunAtStartup = () => Startup.IsOn, SetRunAtStartup = Startup.Set,
            BackUp = () => { FlushSave(); Backup.SaveNow(); }, Restore = Backup.RestoreNow, LastBackup = () => Backup.SavedText,
            Exit = Quit,
        };
    }

    // The app-wide part of the menu, shared by the tray and every group's right-click menu. group is null from the tray.
    public void BuildMenu(ToolStripItemCollection items, GroupForm group)
    {
        if (group == null)
        {
            var old = items.Cast<ToolStripItem>().ToList();
            items.Clear();
            foreach (var it in old) it.Dispose();
            items.Add(Item("New group", false, () => NewGroup(Cursor.Position)));
            var show = new ToolStripMenuItem("Groups");
            foreach (var f in Forms.ToList())
            {
                var gf = f;
                show.DropDownItems.Add(Item(f.Data.Name + "   (" + f.Data.Items.Count + ")", false, () => gf.Bring()));
            }
            items.Add(show);
            items.Add(new ToolStripSeparator());
        }

        var icons = new ToolStripMenuItem("Icons");
        AddFeatureMenu(items);
        string[] sizes = { "Small", "Normal", "Large" };
        for (int i = 0; i < 3; i++) { int v = i; icons.DropDownItems.Add(Item(sizes[i], C.IconSize == v, () => { C.IconSize = v; Changed(); })); }
        icons.DropDownItems.Add(new ToolStripSeparator());
        icons.DropDownItems.Add(Item("Show names", C.Labels, () => { C.Labels = !C.Labels; Changed(); }));
        items.Add(icons);

        Common().AddAll(items);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { CloseFeatures(); DockWork.Stop(); tray.Dispose(); menu.Dispose(); ShellIcon.Clear(); }
        base.Dispose(disposing);
    }
}

// ======================================================================= One group on the desktop
partial class GroupForm : Form
{
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    public readonly GroupData Data;
    readonly DockContext app;
    readonly DibSurface surface = new DibSurface();
    readonly ContextMenuStrip menu = KinetikSuite.MenuFocus.Attach(new ContextMenuStrip());
    int layer, winW, winH, margin;
    float u = 1;
    bool dragging, moved, dropHot;
    Point grab, downAt;
    int downTile = -1, hoverTile = -1, dropAt = -1;
    bool downOnHeader;
    readonly List<RectangleF> tiles = new List<RectangleF>();
    RectangleF header;

    const string ItemFormat = "KinetikDock.Item";

    Settings C { get { return app.C; } }
    public Bitmap Frame { get { return surface.Current; } }
    // where the window is (its frame includes the shadow margin round the card)
    public Rectangle Placed { get { return new Rectangle(renderX, renderY, winW, winH); } }

    public GroupForm(DockContext app, GroupData data)
    {
        this.app = app; Data = data;
        layer = C.Layer;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Kinetik Dock: " + data.Name;
        AllowDrop = true;
        if (!string.IsNullOrWhiteSpace(data.PortalPath)) portal = new FolderPortal(this, data.PortalPath);
        menu.Opening += (s, e) => BuildMenu();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; // LAYERED | TOOLWINDOW | NOACTIVATE
            if (layer == 2) cp.ExStyle |= 0x8;          // TOPMOST
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE → MA_NOACTIVATE
        if (m.Msg == 0x46 && layer == 0 && !app.Peeking)
        {
            var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
            if ((wp.flags & 0x4) == 0) { wp.hwndInsertAfter = new IntPtr(1); Marshal.StructureToPtr(wp, m.LParam, false); }
        }
        base.WndProc(ref m);
        if (m.Msg == 0x2E0) { currentDpi = (int)m.WParam & 0xffff; app.RequestRender(this); }
    }

    protected override void OnHandleCreated(EventArgs e) { surfaceVisible = false; appliedLayer = -1; currentDpi = 0; base.OnHandleCreated(e); Render(); }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated) CreateHandle();
        base.SetVisibleCore(false);
        if (value) BeginInvoke((Action)Render);
    }

    protected override void Dispose(bool disposing)
    {
        surface.Dispose();
        if (disposing) { if (portal != null) portal.Dispose(); if (itemMenu != null) itemMenu.Dispose(); tip.Dispose(); }
        if (disposing) { menu.Dispose(); DisposeFonts(); shadow.Dispose(); if (textLayer != null) textLayer.Dispose(); }
        base.Dispose(disposing);
    }

    public void ApplyLayer() { layer = C.Layer; RecreateHandle(); Render(); }

    public void Bring()
    {
        if (Data.Workspace != C.Workspace) app.SelectWorkspace(Data.Workspace);
        app.SwitchTab(this);
        if (Data.Collapsed) { Data.Collapsed = false; app.Save(); }
        Render();
        SetWindowPos(Handle, new IntPtr(0), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
    }

    // ---- sizes
    int IconPx { get { return (int)((ItemSize == 0 ? 24 : ItemSize == 2 ? 48 : 32) * u); } }
    float TileW { get { return (Data.Compact ? 0.84f : 1f) * (ItemSize == 0 ? 64 : ItemSize == 2 ? 92 : 76) * u; } }
    float TileH { get { return Data.ListView ? Math.Max(32 * u, IconPx + 8 * u) : IconPx + (ShowLabels ? (Data.Compact ? 38 : 50) : 18) * u; } }
    const float HeaderH = 28;

    // ---- mouse
    int TileAt(Point p) { if (!contentArea.Contains(p) || Data.Collapsed) return -1; for (int i = 0; i < tiles.Count && i < tileIndices.Count; i++) if (tiles[i].Contains(p)) return tileIndices[i]; return -1; }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && FeatureMouseDown(e.Location)) return;
        if (e.Button == MouseButtons.Left)
        {
            downAt = Cursor.Position; moved = false;
            downTile = TileAt(e.Location);
            downOnHeader = header.Contains(e.Location);
            if (downTile >= 0 && SelectionMouseDown(downTile)) return;
            if (!C.Locked && !Data.Locked && downOnHeader) { PrepareSnap(); dragging = true; Capture = true; grab = new Point(Cursor.Position.X - renderX, Cursor.Position.Y - renderY); }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (FeatureMouseMove(e.Location)) return;
        bool far = Math.Abs(Cursor.Position.X - downAt.X) + Math.Abs(Cursor.Position.Y - downAt.Y) > 4;
        if (dragging && (moved || far))
        {
            moved = true;
            var p = Snap(new Point(Cursor.Position.X - grab.X, Cursor.Position.Y - grab.Y));
            Data.X = p.X; Data.Y = p.Y;
            renderX = p.X; renderY = p.Y;
            app.SyncPlacement(this);
            SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, 0x1 | 0x4 | 0x10);
            Cursor = Cursors.SizeAll;
        }
        else if (portal == null && e.Button == MouseButtons.Left && downTile >= 0 && far && downTile < Items.Count)
        {
            // drag a shortcut: to rearrange it, move it to another group, or drop it anywhere that takes files
            int from = downTile;
            downTile = -1;
            var item = Items[from];
            var obj = new DataObject();
            var bundle = selected.Contains(item.Id) ? Items.Where(i => selected.Contains(i.Id)).ToList() : new List<ItemData> { item };
            obj.SetData(ItemFormat, Data.Id + "|" + string.Join(",", bundle.Select(i => i.Id)));
            var files = new StringCollection(); files.AddRange(bundle.Where(i => !Target.IsLink(i.Path) && !Target.IsShell(i.Path)).Select(i => i.Path).ToArray());
            if (files.Count > 0) obj.SetFileDropList(files);
            obj.SetText(string.Join(Environment.NewLine, bundle.Select(i => i.Path)));
            DoDragDrop(obj, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        }
        else if (!dragging)
        {
            int t = TileAt(e.Location);
            if (t != hoverTile) { hoverTile = t; Cursor = t >= 0 ? Cursors.Hand : Cursors.Default; app.RequestRender(this); }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hoverTile >= 0) { hoverTile = -1; app.RequestRender(this); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (FeatureMouseUp()) return;
        if (e.Button == MouseButtons.Left)
        {
            bool wasMove = dragging && moved;
            if (wasMove) app.Save();
            dragging = false;
            Capture = false;
            Cursor = Cursors.Default;
            if (!C.DoubleClickOpen && !wasMove && downTile >= 0 && downTile == TileAt(e.Location) && downTile < Items.Count) OpenItem(Items[downTile]);
            downTile = -1;
        }
        else if (e.Button == MouseButtons.Right)
        {
            int t = TileAt(e.Location);
            if (t >= 0 && t < Items.Count) ItemMenu(t).Show(Cursor.Position);
            else menu.Show(Cursor.Position);
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        int item = TileAt(e.Location);
        if (C.DoubleClickOpen && item >= 0 && item < Items.Count) { OpenItem(Items[item]); return; }
        if (e.Button == MouseButtons.Left && header.Contains(e.Location) && e.X < winW - margin - 32 * u) { Data.Collapsed = !Data.Collapsed; app.Save(); Render(); }
        base.OnMouseDoubleClick(e);
    }

    // ---- dropping things on the group
    static List<string> Paths(IDataObject d)
    {
        var list = new List<string>();
        if (d.GetDataPresent(DataFormats.FileDrop)) list.AddRange((string[])d.GetData(DataFormats.FileDrop));
        else
        {
            string url = null;
            foreach (var f in new[] { "UniformResourceLocatorW", "UniformResourceLocator" })
                if (url == null && d.GetDataPresent(f))
                    try
                    {
                        var ms = d.GetData(f) as MemoryStream;
                        if (ms != null) url = (f.EndsWith("W") ? System.Text.Encoding.Unicode : System.Text.Encoding.Default).GetString(ms.ToArray()).TrimEnd('\0');
                    }
                    catch { }
            if (url == null && d.GetDataPresent(DataFormats.UnicodeText)) url = (d.GetData(DataFormats.UnicodeText) as string ?? "").Trim();
            if (!string.IsNullOrEmpty(url))
            {
                url = url.Split('\n')[0].Trim();
                try { if (Target.IsLink(url) || Target.IsShell(url) || Path.IsPathRooted(url)) list.Add(url); } catch (ArgumentException) { }
            }
        }
        return list;
    }

    protected override void OnDragEnter(DragEventArgs e) { dragPaths = null; UpdateDrop(e); base.OnDragEnter(e); }
    protected override void OnDragOver(DragEventArgs e) { UpdateDrop(e); base.OnDragOver(e); }
    protected override void OnDragLeave(EventArgs e) { dropHot = false; dropAt = -1; Render(); base.OnDragLeave(e); }

    void UpdateDrop(DragEventArgs e)
    {
        if (portal != null) { e.Effect = DragDropEffects.None; return; }
        bool ours = e.Data.GetDataPresent(ItemFormat);
        if (dragPaths == null && !ours) dragPaths = Paths(e.Data);
        bool ok = ours || dragPaths.Count > 0;
        e.Effect = !ok ? DragDropEffects.None : ours ? ((e.KeyState & 8) != 0 ? DragDropEffects.Copy : DragDropEffects.Move) : (e.AllowedEffect & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy;
        AutoScrollDrop(PointToClient(new Point(e.X, e.Y)));
        int at = InsertAt(PointToClient(new Point(e.X, e.Y)));
        if (dropHot != ok || dropAt != at) { dropHot = ok; dropAt = at; Render(); }
    }

    // Where a drop at p goes: before the tile it's over (or after it, on its right half), else at the end.
    int InsertAt(Point p)
    {
        if (Data.Collapsed) return Items.Count;
        for (int i = 0; i < tiles.Count && i < tileIndices.Count; i++)
            if (tiles[i].Contains(p)) return tileIndices[i] + (Data.ListView ? (p.Y > tiles[i].Y + tiles[i].Height / 2 ? 1 : 0) : (p.X > tiles[i].X + tiles[i].Width / 2 ? 1 : 0));
        return Items.Count;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        if (portal != null) return;
        int at = InsertAt(PointToClient(new Point(e.X, e.Y)));
        dropHot = false; dropAt = -1;
        if (e.Data.GetDataPresent(ItemFormat))
        {
            var parts = (e.Data.GetData(ItemFormat) as string ?? "").Split('|');
            var src = app.Forms.FirstOrDefault(f => f.Data.Id == parts[0]);
            if (src != null && src.portal == null && parts.Length == 2)
            {
                MoveReferences(src, parts[1].Split(','), at, (e.KeyState & 8) != 0);
                app.Save();
                if (src != this) src.Render();
            }
        }
        else
        {
            foreach (var p in dragPaths ?? Paths(e.Data))
            {
                if (Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
                Items.Insert(Math.Min(at++, Items.Count), new ItemData { Name = Target.NameFor(p), Path = p });
                Target.Forget(p);
            }
            if (Data.Collapsed) Data.Collapsed = false;
            app.Save();
        }
        Render();
        base.OnDragDrop(e);
    }

    Point Snap(Point p)
    {
        if (!C.Snap || (ModifierKeys & Keys.Alt) != 0) { snapX = snapY = int.MinValue; return p; }
        if (!snapWorkArea.Contains(Cursor.Position)) snapWorkArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        int gap = (int)(12 * u), reach = (int)(18 * u);
        var xs = new List<int> { snapWorkArea.Left + gap - margin, snapWorkArea.Right - gap - winW + margin };
        var ys = new List<int> { snapWorkArea.Top + gap - margin, snapWorkArea.Bottom - gap - winH + margin };
        foreach (var other in snapRects)
        {
            xs.Add(other.Left - margin); xs.Add(other.Right + gap - margin); xs.Add(other.Left - gap - winW + margin);
            ys.Add(other.Top - margin); ys.Add(other.Bottom + gap - margin); ys.Add(other.Top - gap - winH + margin);
        }
        p.X = DockLayout.NearestSnap(p.X, xs, reach, ref snapX); p.Y = DockLayout.NearestSnap(p.Y, ys, reach, ref snapY);
        return p;
    }

    void EnsurePosition()
    {
        renderX = Data.X; renderY = Data.Y;
        var title = new Rectangle(Data.X == int.MinValue ? 0 : Data.X + margin, Data.Y == int.MinValue ? 0 : Data.Y + margin, Math.Max(1,winW-margin*2), Math.Max(1,(int)(HeaderH*u)));
        if (Data.X == int.MinValue || !Screen.AllScreens.Any(sc => { var visible = Rectangle.Intersect(sc.WorkingArea,title); return visible.Width >= Math.Min(80,title.Width) && visible.Height >= Math.Min(20,title.Height); }))
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int gap = (int)(12 * u), n = app.C.List.IndexOf(app.Tabs(this)[0].Data);
            renderX = wa.Left + gap - margin + (Math.Max(0, n) % 8) * (int)(24 * u);
            renderY = wa.Top + gap - margin + (Math.Max(0, n) % 8) * (int)(24 * u);
            if (Data.X == int.MinValue) { Data.X = renderX; Data.Y = renderY; }
        }
    }

    // ---- painting
    public void Render()
    {
        if (IsDisposed || !IsHandleCreated) return;
        bool shown = app.IsShown(this);
        if (portal != null) portal.SetActive(shown && !Data.Collapsed);
        if (!shown)
        {
            if (surfaceVisible) ShowWindow(Handle, 0); surfaceVisible = false;
            surface.Dispose(); shadow.Dispose(); if (textLayer != null) { textLayer.Dispose(); textLayer = null; }
            return;
        }
        app.RenderCount++;
        u = EffectiveDpi / 96f * C.Scale / 100f;
        margin = (int)(14 * u);
        float pad = 12 * u;
        int cols = Math.Max(1, Data.Cols);
        int rows = Math.Max(1, (int)Math.Ceiling(Items.Count / (double)cols));
        int maxW = Math.Max(170, Screen.FromPoint(new Point(Data.X == int.MinValue ? 0 : Data.X, Data.Y == int.MinValue ? 0 : Data.Y)).WorkingArea.Width - margin * 2);
        int cardW = Math.Min(maxW, (int)Math.Max(170 * u, Data.Width > 0 ? Data.Width * u : cols * TileW + pad * 2));
        cols = Data.ListView ? 1 : Data.Width > 0 || cardW < cols * TileW + pad * 2 ? Math.Max(1, (int)((cardW - pad * 2) / TileW)) : cols;
        layoutCols = cols;
        rows = Math.Max(1, (int)Math.Ceiling(Items.Count / (double)cols));
        int naturalH = (int)(pad + HeaderH * u + ExtraHeader + (Items.Count == 0 ? 64 * u : rows * TileH) + pad * 0.7f);
        int maxH = Math.Max(100, Screen.FromPoint(new Point(Data.X == int.MinValue ? 0 : Data.X, Data.Y == int.MinValue ? 0 : Data.Y)).WorkingArea.Height - margin * 2 - (int)(24 * u));
        int cardH = Data.Collapsed ? (int)(HeaderH * u + pad) : Math.Min(maxH, Math.Max((int)(140 * u), Data.Height > 0 ? (int)(Data.Height * u) : Math.Min((int)(520 * u), naturalH)));
        winW = cardW + margin * 2; winH = cardH + margin * 2;

        EnsurePosition();
        var bmp = surface.Canvas(winW, winH);
        using (var g = Graphics.FromImage(bmp)) PaintCard(g, cardW, cardH, pad, cols);
        surface.Push(Handle, renderX, renderY);
        if (Program.ShotPath != null) return;
        int desiredLayer = app.Peeking ? 2 : layer;
        if (!surfaceVisible || appliedLayer != desiredLayer)
        {
            ShowWindow(Handle, 8); surfaceVisible = true; appliedLayer = desiredLayer;
            SetWindowPos(Handle, new IntPtr(desiredLayer == 2 ? -1 : desiredLayer == 0 ? 1 : -2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }
    }

    Bitmap textLayer;
    readonly ShadowCache shadow = new ShadowCache();
    string familyKey, familyValue;
    string Fam
    {
        get
        {
            string key = C.FontName + "|" + Theme.IsTerm(C.ThemeName);
            if (familyKey != key) { familyKey = key; familyValue = C.FontName != "" && Draw.HasFont(C.FontName) ? C.FontName : Theme.IsTerm(C.ThemeName) ? Theme.Mono : Draw.Family; }
            return familyValue;
        }
    }
    readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    string fontKey;

    Font F(float size, FontStyle style = FontStyle.Regular)
    {
        var key = Fam + "|" + u + "|" + EffectiveDpi;
        if (key != fontKey) { DisposeFonts(); fontKey = key; }
        var k = size + "/" + (int)style;
        Font f;
        if (!fonts.TryGetValue(k, out f)) fonts[k] = f = new Font(Fam, size * u, style, GraphicsUnit.Point);
        return f;
    }

    void DisposeFonts() { KinetikSuite.Resources.DisposeValues(fonts); fontKey = null; }

    static readonly StringFormat fmtL = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
    static readonly StringFormat fmtR = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
    static readonly StringFormat fmtLabel = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
    static readonly StringFormat fmtC = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    void PaintCard(Graphics target, int cardW, int cardH, float pad, int cols)
    {
        var th = Theme.Get(C.ThemeName);
        if (Data.AccentArgb != 0) th = new Theme(th.Name, th.Bg, th.Text, th.Sub, Color.FromArgb(Data.AccentArgb), th.Light);
        target.Clear(Color.Transparent);
        target.SmoothingMode = SmoothingMode.AntiAlias;
        target.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var card = new RectangleF(margin, margin, cardW, cardH);
        float rad = 14 * u;
        if (!C.Lightweight) shadow.Paint(target, winW, winH, card + "|" + u + "|" + C.Opacity, sg =>
        {
            for (int i = 1; i <= 6; i++)
                using (var p = Draw.Round(RectangleF.Inflate(card, i * 1.6f * u, i * 1.6f * u), rad + i * 1.6f * u))
                using (var b = new SolidBrush(Color.FromArgb((int)(14 * C.Opacity / 100f / i), 0, 0, 0))) sg.FillPath(b, p);
        });
        else shadow.Dispose();
        int alpha = 255 * C.Opacity / 100;
        bool crisp = alpha >= 150;
        var g = target;
        if (crisp)
        {
            if (textLayer == null || textLayer.Width != winW || textLayer.Height != winH) { if (textLayer != null) textLayer.Dispose(); textLayer = new Bitmap(winW, winH, PixelFormat.Format32bppPArgb); }
            g = Graphics.FromImage(textLayer);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.TextContrast = 2;
        }
        else { if (textLayer != null) { textLayer.Dispose(); textLayer = null; } g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; }
        // the card must keep a little opacity everywhere, or clicks and drops on it would fall through to the desktop
        try { PaintContent(g, th, card, rad, crisp ? 255 : Math.Max(alpha, 2), pad, cols); }
        finally
        {
            if (crisp)
            {
                g.Dispose();
                if (alpha >= 255) target.DrawImageUnscaled(textLayer, 0, 0);
                else
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetColorMatrix(new ColorMatrix { Matrix33 = alpha / 255f });
                        target.DrawImage(textLayer, new Rectangle(0, 0, textLayer.Width, textLayer.Height), 0, 0, textLayer.Width, textLayer.Height, GraphicsUnit.Pixel, ia);
                    }
            }
        }
    }

    void PaintContent(Graphics g, Theme th, RectangleF card, float rad, int alpha, float pad, int cols)
    {
        tiles.Clear(); tileIndices.Clear(); scrollTrack = RectangleF.Empty; scrollThumb = RectangleF.Empty;
        using (var p = Draw.Round(card, rad))
        {
            using (var b = new SolidBrush(Color.FromArgb(alpha, th.Bg))) g.FillPath(b, p);
            using (var pen = new Pen(dropHot ? th.Accent : th.Line, dropHot ? 2 * u : 1)) g.DrawPath(pen, p);
        }
        float x = card.X + pad, w = card.Width - pad * 2, y = card.Y + pad * 0.6f;
        header = new RectangleF(card.X, card.Y, card.Width, HeaderH * u + pad * 0.6f);
        using (var text = new SolidBrush(th.Text))
        using (var sub = new SolidBrush(th.Sub))
        {
            // title, with a small chevron that shows it rolls up, and the count
            string count = Items.Count.ToString();
            var cw = g.MeasureString(count, F(8.5f), 200, fmtR).Width;
            g.DrawString(count, F(8.5f), sub, new RectangleF(x + w - cw - 14 * u, y, cw, HeaderH * u), fmtR);
            float cx = x + w - 6 * u, cy = y + HeaderH * u / 2, s = 3.5f * u;
            using (var pen = new Pen(th.Sub, 1.5f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                if (Data.Collapsed) g.DrawLines(pen, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
                else g.DrawLines(pen, new[] { new PointF(cx - s, cy + s / 2), new PointF(cx, cy - s / 2), new PointF(cx + s, cy + s / 2) });
            g.DrawString(Data.Name, F(10, FontStyle.Bold), text, new RectangleF(x + 2 * u, y, w - cw - 24 * u, HeaderH * u), fmtL);
            if (Data.AccentArgb != 0) using (var accent = new SolidBrush(th.Accent)) g.FillRectangle(accent, x - 5 * u, y + 7 * u, 2 * u, 14 * u);
            if (Data.Collapsed) return;
            y += HeaderH * u;
            y = PaintFeatureHeaders(g, th, x, y, w);
            using (var pen = new Pen(th.Line, 1)) g.DrawLine(pen, x, y, x + w, y);

            contentArea = new RectangleF(x, y, w, Math.Max(0, card.Bottom - pad * 0.7f - y));
            scrollY = Math.Max(0, Math.Min(scrollY, DockLayout.MaxScroll(Items.Count, cols, TileH, contentArea.Height)));
            if (Items.Count == 0)
            {
                g.DrawString(portal != null ? (string.IsNullOrEmpty(portal.Status) ? "This folder is empty" : portal.Status) : "Drop apps, files, folders or links here", F(8.5f), sub, new RectangleF(x, y, w, 64 * u), fmtC);
                PaintScrollAndGrip(g, th, card);
                return;
            }

            int px = IconPx;
            float tw = Data.ListView ? w - 12 * u : TileW, tH = TileH, gx = Data.ListView ? x : x + (w - cols * tw) / 2;
            var clip = g.Save(); g.SetClip(contentArea);
            int first = Math.Max(0, (int)(scrollY / tH) * cols);
            int last = Math.Min(Items.Count, (int)Math.Ceiling((scrollY + contentArea.Height) / tH) * cols);
            tileIndices.Clear();
            for (int i = first; i < last; i++)
            {
                var it = Items[i];
                var r = new RectangleF(gx + (i % cols) * tw, y + (i / cols) * tH - scrollY, tw, tH);
                tiles.Add(r); tileIndices.Add(i);
                var cell = RectangleF.Inflate(r, -3 * u, -3 * u);
                if (selected.Contains(it.Id) || i == hoverTile && !dropHot)
                    using (var p = Draw.Round(cell, 8 * u)) using (var b = new SolidBrush(selected.Contains(it.Id) ? Color.FromArgb(65, th.Accent) : Color.FromArgb(th.Light ? 18 : 22, th.Text))) g.FillPath(b, p);
                if (keyboardMode && focusedItem == i)
                    using (var pen = new Pen(th.Accent, 1) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
                bool there = Target.Exists(it.Path);
                var ir = new RectangleF(Data.ListView ? r.X + 4 * u : r.X + (tw - px) / 2, r.Y + (Data.ListView ? 4 : 7) * u, px, px);
                Bitmap icon = Target.IsLink(it.Path) ? null : ShellIcon.Get(it.Path, px);
                if (icon != null)
                {
                    var im = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    if (there) g.DrawImage(icon, ir);
                    else using (var ia = new ImageAttributes()) { ia.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f }); g.DrawImage(icon, Rectangle.Round(ir), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia); }
                    g.InterpolationMode = im;
                }
                else LetterTile(g, ir, it, th);
                if (ShowLabels || Data.ListView)
                {
                    var lr = Data.ListView ? new RectangleF(ir.Right + 8 * u, r.Y, Math.Max(1, tw - px - 18 * u), tH) : new RectangleF(r.X + 1 * u, ir.Bottom + 5 * u, tw - 2 * u, 38 * u);
                    using (var b = new SolidBrush(there ? th.Text : Color.FromArgb(150, th.Sub)))
                        g.DrawString(it.Name, F(8.5f), b, lr, Data.ListView ? fmtL : fmtLabel);
                }
            }
            // where a drop will land
            if (dropHot && dropAt >= 0)
            {
                int i = dropAt;
                float lx = gx + (i % cols) * tw, ly = y + (i / cols) * tH - scrollY;
                using (var pen = new Pen(th.Accent, 2.5f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, lx, ly + 6 * u, lx, ly + tH - 6 * u);
            }
            g.Restore(clip);
            PaintScrollAndGrip(g, th, card);
        }
    }

    // A web link (or anything without an icon): a rounded square in a colour picked from its name, with its first letter.
    static readonly Color[] Palette =
    {
        Color.FromArgb(77, 166, 255), Color.FromArgb(240, 88, 88), Color.FromArgb(40, 190, 200), Color.FromArgb(255, 170, 60),
        Color.FromArgb(180, 135, 255), Color.FromArgb(108, 201, 124), Color.FromArgb(255, 120, 190), Color.FromArgb(240, 200, 70),
    };

    void LetterTile(Graphics g, RectangleF r, ItemData it, Theme th)
    {
        int h = 0;
        foreach (var ch in (it.Name ?? "").ToLowerInvariant()) { h = h * 31 + ch; h ^= h >> 7; }
        var col = Palette[(h & 0x7fffffff) % Palette.Length];
        var box = RectangleF.Inflate(r, -r.Width * 0.06f, -r.Width * 0.06f);
        using (var p = Draw.Round(box, box.Width * 0.26f)) using (var b = new SolidBrush(col)) g.FillPath(b, p);
        var letter = string.IsNullOrEmpty(it.Name) ? "?" : it.Name.Substring(0, 1).ToUpperInvariant();
        using (var b = new SolidBrush(Color.White))
            g.DrawString(letter, F(Math.Max(8, box.Height * 0.36f / u), FontStyle.Bold), b, box, fmtC);
        if (Target.IsLink(it.Path))
        {
            // a small link badge in the corner
            float d = box.Width * 0.38f;
            var c = new RectangleF(box.Right - d * 0.8f, box.Bottom - d * 0.8f, d, d);
            using (var b = new SolidBrush(th.Bg)) g.FillEllipse(b, c);
            using (var pen = new Pen(th.Text, Math.Max(1, d * 0.1f)))
            {
                var ring = RectangleF.Inflate(c, -d * 0.2f, -d * 0.2f);
                g.DrawEllipse(pen, ring);
                g.DrawLine(pen, ring.X, ring.Y + ring.Height / 2, ring.Right, ring.Y + ring.Height / 2);
                g.DrawEllipse(pen, ring.X + ring.Width * 0.28f, ring.Y, ring.Width * 0.44f, ring.Height);
            }
        }
    }

    // ---- menus
    ToolStripMenuItem Item(string text, bool check, Action click)
    {
        var it = new ToolStripMenuItem(text) { Checked = check };
        it.Click += (s, e) => click();
        return it;
    }

    ContextMenuStrip itemMenu;

    ContextMenuStrip ItemMenu(int index)
    {
        if (itemMenu != null) itemMenu.Dispose();
        itemMenu = KinetikSuite.MenuFocus.Attach(new ContextMenuStrip());
        var it = Items[index];
        AddSelectionMenu(itemMenu.Items, it);
        if (portal != null)
        {
            itemMenu.Items.Add(Item(it.IsDirectory ? "Browse folder" : "Open", false, () => OpenItem(it)));
            itemMenu.Items.Add(Item("Show in File Explorer", false, () => Target.Reveal(it.Path)));
            return itemMenu;
        }
        itemMenu.Items.Add(Item("Open", false, () => Target.Open(it.Path)));
        if (!Target.IsLink(it.Path) && !Target.IsShell(it.Path)) itemMenu.Items.Add(Item("Show in File Explorer", false, () => Target.Reveal(it.Path)));
        itemMenu.Items.Add(Item("Rename…", false, () =>
        {
            var n = Prompt("Rename", "Name shown in the group:", it.Name);
            if (n == null) return;
            it.Name = n.Trim() == "" ? Target.NameFor(it.Path) : n.Trim();
            app.Save(); Render();
        }));
        itemMenu.Items.Add(Item("Change what it opens…", false, () =>
        {
            var n = Prompt("Change target", "File, folder or web address:", it.Path);
            if (n == null || n.Trim() == "") return;
            it.Path = n.Trim(); Target.Forget(it.Path);
            app.Save(); Render();
        }));
        var others = app.Forms.Where(f => f != this && f.portal == null).ToList();
        if (others.Count > 0)
        {
            var move = new ToolStripMenuItem("Move to");
            foreach (var f in others)
            {
                var to = f;
                move.DropDownItems.Add(Item(f.Data.Name, false, () => { Items.Remove(it); to.Items.Add(it); app.Save(); Render(); to.Render(); }));
            }
            itemMenu.Items.Add(move);
        }
        itemMenu.Items.Add(new ToolStripSeparator());
        itemMenu.Items.Add(Item("Remove from this group", false, () => { Items.Remove(it); app.Save(); Render(); }));
        return itemMenu;
    }

    void BuildMenu()
    {
        var old = menu.Items.Cast<ToolStripItem>().ToList();
        menu.Items.Clear();
        foreach (var it in old) it.Dispose();
        menu.Items.Add(new ToolStripMenuItem(Data.Name) { Enabled = false });
        BuildGroupFeatureMenu(menu.Items);
        var add = new ToolStripMenuItem("Add");
        add.DropDownItems.Add(Item("Apps or files…", false, AddFiles));
        add.DropDownItems.Add(Item("A folder…", false, AddFolder));
        add.DropDownItems.Add(Item("A web link…", false, () =>
        {
            var url = Prompt("Add a web link", "Web address:", "https://");
            if (url == null) return;
            url = url.Trim();
            if (!Target.IsLink(url) && !Target.IsShell(url)) url = "https://" + url;
            Items.Add(new ItemData { Name = Target.NameFor(url), Path = url });
            app.Save(); Render();
        }));
        menu.Items.Add(add);
        add.Enabled = portal == null;
        menu.Items.Add(Item("Rename group…", false, () =>
        {
            var n = Prompt("Rename group", "Name of the group:", Data.Name);
            if (n == null || n.Trim() == "") return;
            Data.Name = n.Trim(); app.Save(); Render();
        }));
        var cols = new ToolStripMenuItem("Columns");
        for (int i = 1; i <= 8; i++) { int v = i; cols.DropDownItems.Add(Item(v.ToString(), Data.Cols == v, () => { Data.Cols = v; app.Save(); Render(); })); }
        menu.Items.Add(cols);
        menu.Items.Add(Item(Data.Collapsed ? "Unroll" : "Roll up", false, () => { Data.Collapsed = !Data.Collapsed; app.Save(); Render(); }));
        menu.Items.Add(Item("Sort by name", false, () => { Items.Sort((a,b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name)); app.Save(); Render(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("New group", false, () => app.NewGroup(new Point(renderX + (int)(30 * u), renderY + (int)(30 * u)))));
        menu.Items.Add(Item("Delete this group…", false, () => app.DeleteGroup(this)));
        menu.Items.Add(new ToolStripSeparator());
        app.BuildMenu(menu.Items, this);
    }

    void AddFiles()
    {
        using (var dlg = new OpenFileDialog { Title = "Add to " + Data.Name, Multiselect = true, DereferenceLinks = false, Filter = "Apps and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*" })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            foreach (var p in dlg.FileNames)
                if (!Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) Items.Add(new ItemData { Name = Target.NameFor(p), Path = p });
            app.Save(); Render();
        }
    }

    void AddFolder()
    {
        using (var dlg = new FolderBrowserDialog { Description = "Add a folder to " + Data.Name })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            Items.Add(new ItemData { Name = Target.NameFor(dlg.SelectedPath), Path = dlg.SelectedPath });
            app.Save(); Render();
        }
    }

    public static string Prompt(string title, string label, string value)
    {
        using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(400, 110), BackColor = Color.FromArgb(27, 31, 44), ForeColor = Color.FromArgb(232, 236, 244), Font = new Font("Segoe UI", 9.5f), TopMost = true })
        {
            var lb = new Label { Text = label, Left = 12, Top = 12, AutoSize = true };
            var tb = new TextBox { Text = value, Left = 12, Top = 36, Width = 376, BackColor = Color.FromArgb(40, 47, 66), ForeColor = f.ForeColor, BorderStyle = BorderStyle.FixedSingle };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 232, Top = 72, Width = 75, FlatStyle = FlatStyle.Flat };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 313, Top = 72, Width = 75, FlatStyle = FlatStyle.Flat };
            f.Controls.AddRange(new Control[] { lb, tb, ok, cancel });
            f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
        }
    }
}

// ======================================================================= Shared with the other Kinetik apps
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string Name { get { return Backup.AppTitle; } }
    public static string Args = ""; // added after the exe path, e.g. " --startup"

    public static bool IsOn
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null; }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"" + Args);
                else k.DeleteValue(Name, false);
        }
        catch { }
    }
}

// ======================================================================= Themes (same set as Kinetik and Kinetik Calendar)
class Theme
{
    public readonly string Name;
    public readonly Color Bg, Text, Sub, Accent;
    public readonly bool Light;

    public Theme(string name, Color bg, Color text, Color sub, Color accent, bool light)
    {
        Name = name; Bg = bg; Text = text; Sub = sub; Accent = accent; Light = light;
    }

    public Color Line { get { return Term ? Color.FromArgb(110, Sub) : Light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(28, 255, 255, 255); } }

    static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    // The terminal themes (the same green, amber and yellow as Kinetik's dashboard and Kinetik Screen) draw in a
    // monospace font unless a font is picked, and their divider lines glow in the theme's colour.
    public bool Term { get { return IsTerm(Name); } }
    public static bool IsTerm(string name) { return name == "Terminal" || name == "Amber terminal" || name == "Yellow terminal"; }
    static string mono;
    public static string Mono
    {
        get
        {
            if (mono == null)
            {
                mono = "Consolas";
                try { using (var f = new Font("Cascadia Mono", 10f)) if (f.Name == "Cascadia Mono") mono = "Cascadia Mono"; } catch { }
            }
            return mono;
        }
    }

    public static readonly Theme[] All =
    {
        new Theme("Midnight", C(22, 22, 26), C(242, 242, 246), C(168, 168, 180), C(240, 88, 88), false),
        new Theme("Navy", C(22, 27, 40), C(236, 240, 248), C(138, 150, 176), C(77, 166, 255), false),
        new Theme("Graphite", C(40, 40, 44), C(236, 236, 236), C(160, 160, 166), C(255, 159, 10), false),
        new Theme("Nord", C(46, 52, 64), C(236, 239, 244), C(152, 162, 182), C(136, 192, 208), false),
        new Theme("Dracula", C(40, 42, 54), C(248, 248, 242), C(150, 156, 192), C(255, 121, 198), false),
        new Theme("Solarized", C(0, 43, 54), C(238, 232, 213), C(131, 148, 150), C(203, 75, 22), false),
        new Theme("Forest", C(22, 33, 27), C(228, 240, 231), C(138, 166, 148), C(108, 201, 124), false),
        new Theme("Ocean", C(12, 29, 45), C(228, 242, 250), C(128, 160, 186), C(56, 198, 232), false),
        new Theme("Sunset", C(40, 23, 35), C(250, 236, 240), C(192, 150, 172), C(255, 138, 92), false),
        new Theme("Mono", C(10, 10, 10), C(245, 245, 245), C(140, 140, 140), C(245, 245, 245), false),
        new Theme("Terminal", C(3, 10, 5), C(90, 255, 120), C(46, 170, 80), C(205, 255, 215), false),
        new Theme("Amber terminal", C(12, 7, 0), C(255, 182, 40), C(185, 118, 22), C(255, 232, 180), false),
        new Theme("Yellow terminal", C(0, 0, 0), C(255, 226, 0), C(200, 178, 0), C(255, 255, 190), false),
        new Theme("Light", C(246, 246, 248), C(24, 24, 28), C(96, 96, 106), C(230, 70, 70), true),
        new Theme("Paper", C(250, 246, 236), C(52, 42, 32), C(132, 116, 96), C(204, 92, 52), true),
        new Theme("Frost", C(236, 242, 250), C(20, 30, 48), C(92, 106, 132), C(40, 120, 230), true),
    };

    public static Theme Get(string name)
    {
        if (name == "Auto")
        {
            bool light = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    light = k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1;
            }
            catch { }
            return Get(light ? "Light" : "Midnight");
        }
        foreach (var t in All) if (t.Name == name) return t;
        return All[0];
    }
}

// ======================================================================= Drawing helpers
static class Draw
{
    public static string Family = HasFont("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    public static readonly string[] FontChoices = { "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Calibri", "Aptos", "Inter", "Cascadia Code", "Consolas", "Verdana", "Georgia" };

    public static bool HasFont(string name)
    {
        try { using (var f = new FontFamily(name)) return f.Name == name; } catch { return false; }
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// A premultiplied-alpha bitmap backed by a DIB, pushed to a layered window with UpdateLayeredWindow (as in Kinetik).
sealed class DibSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
    {
        public int Size, Width, Height; public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLEND b, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    IntPtr memDC, dib, oldObj;
    Bitmap bmp;
    int w, h;

    public Bitmap Canvas(int width, int height)
    {
        if (dib != IntPtr.Zero && width == w && height == h) return bmp;
        Free();
        memDC = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFOHEADER { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        IntPtr bits;
        dib = CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        oldObj = SelectObject(memDC, dib);
        bmp = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        w = width; h = height;
        return bmp;
    }

    public void Push(IntPtr hwnd, int x, int y)
    {
        var size = new SIZE { CX = w, CY = h };
        var src = new POINT();
        var pos = new POINT { X = x, Y = y };
        var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
        UpdateLayeredWindow(hwnd, IntPtr.Zero, ref pos, ref size, memDC, ref src, 0, ref blend, 2);
    }

    void Free()
    {
        if (bmp != null) { bmp.Dispose(); bmp = null; }
        if (memDC != IntPtr.Zero) { SelectObject(memDC, oldObj); DeleteDC(memDC); memDC = IntPtr.Zero; }
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
    }

    // The last frame, and the same as a PNG (for --shot screenshots).
    public Bitmap Current { get { return bmp; } }
    public void Save(string path) { if (bmp != null) bmp.Save(path, ImageFormat.Png); }

    public void Dispose() { Free(); }
}

// A card's soft shadow: stacked, slightly larger translucent shapes. Those anti-aliased fills over the whole card cost
// more than the rest of a frame put together, so they're drawn once into a bitmap and copied while nothing about them changes.
sealed class ShadowCache : IDisposable
{
    Bitmap bmp; string key;

    // Lays the shadow into the (cleared) w × h target. look sums up everything draw depends on.
    public void Paint(Graphics target, int w, int h, string look, Action<Graphics> draw)
    {
        var k = w + "x" + h + "|" + look + "|" + (int)target.SmoothingMode + (int)target.PixelOffsetMode;
        if (k != key || bmp == null)
        {
            if (bmp == null || bmp.Width != w || bmp.Height != h) { if (bmp != null) bmp.Dispose(); bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb); }
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = target.SmoothingMode; g.PixelOffsetMode = target.PixelOffsetMode;
                draw(g);
            }
            key = k;
        }
        bmp.SetResolution(target.DpiX, target.DpiY); // so it's copied 1:1
        var mode = target.CompositingMode;
        target.CompositingMode = CompositingMode.SourceCopy;
        target.DrawImageUnscaled(bmp, 0, 0);
        target.CompositingMode = mode;
    }

    public void Dispose() { if (bmp != null) bmp.Dispose(); bmp = null; key = null; }
}

class DarkColors : ProfessionalColorTable
{
    static readonly Color Bg = Color.FromArgb(27, 31, 44), Hi = Color.FromArgb(40, 47, 66), Edge = Color.FromArgb(52, 60, 82);
    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color ImageMarginGradientBegin { get { return Bg; } }
    public override Color ImageMarginGradientMiddle { get { return Bg; } }
    public override Color ImageMarginGradientEnd { get { return Bg; } }
    public override Color MenuBorder { get { return Edge; } }
    public override Color MenuItemBorder { get { return Hi; } }
    public override Color MenuItemSelected { get { return Hi; } }
    public override Color MenuItemSelectedGradientBegin { get { return Hi; } }
    public override Color MenuItemSelectedGradientEnd { get { return Hi; } }
    public override Color MenuItemPressedGradientBegin { get { return Hi; } }
    public override Color MenuItemPressedGradientEnd { get { return Hi; } }
    public override Color SeparatorDark { get { return Edge; } }
    public override Color SeparatorLight { get { return Edge; } }
    public override Color CheckBackground { get { return Color.FromArgb(52, 96, 160); } }
    public override Color CheckSelectedBackground { get { return Color.FromArgb(62, 110, 180); } }
    public override Color CheckPressedBackground { get { return Color.FromArgb(62, 110, 180); } }
}

class DarkMenu : ToolStripProfessionalRenderer
{
    public DarkMenu() : base(new DarkColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Color.FromArgb(232, 236, 244) : Color.FromArgb(110, 118, 140);
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(180, 188, 205);
        base.OnRenderArrow(e);
    }
}

// A grid of theme tiles, the same picker as the other Kinetik apps.
class ThemePicker : Form
{
    public static readonly Color Page = Color.FromArgb(20, 23, 33), Card = Color.FromArgb(27, 31, 44), CardHi = Color.FromArgb(36, 41, 58), Accent = Color.FromArgb(77, 166, 255);
    public static readonly Color TextCol = Color.FromArgb(232, 236, 244), SubCol = Color.FromArgb(150, 158, 180);

    public ThemePicker(string current, Action<string> chosen)
    {
        Text = Backup.AppTitle + " theme";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = Page;
        float k = DeviceDpi / 96f;
        var flow = new FlowLayoutPanel { Left = (int)(16 * k), Top = (int)(16 * k), Width = (int)(3 * 118 * k), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new Size((int)(3 * 118 * k), 0), WrapContents = true, BackColor = Page };
        foreach (var name in new[] { "Auto" }.Concat(Theme.All.Select(t => t.Name)))
        {
            var tile = new ThemeTile(name, name == current, k);
            tile.Click += (s, e) =>
            {
                foreach (ThemeTile o in flow.Controls) o.SetSelected(o == tile);
                chosen(tile.ThemeName);
            };
            flow.Controls.Add(tile);
        }
        Controls.Add(flow);
        ClientSize = new Size(flow.Right + (int)(8 * k), flow.Bottom + (int)(8 * k));
    }
}

class ThemeTile : Control
{
    public readonly string ThemeName;
    bool selected, hot;
    float sel;
    readonly float k;
    readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 15 };
    static readonly Color[] Bars = { Color.FromArgb(77, 166, 255), Color.FromArgb(56, 198, 180), Color.FromArgb(240, 88, 88) };

    public ThemeTile(string name, bool isSelected, float scale)
    {
        ThemeName = name; selected = isSelected; sel = isSelected ? 1 : 0; k = scale;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(I(110), I(84));
        Margin = new Padding(0, 0, I(8), I(8));
        Cursor = Cursors.Hand;
        BackColor = ThemePicker.Page;
        t.Tick += (s, e) =>
        {
            float target = selected ? 1 : 0;
            sel += (target - sel) * 0.3f;
            if (Math.Abs(target - sel) < 0.02f) { sel = target; t.Stop(); }
            Invalidate();
        };
    }

    int I(float v) { return (int)Math.Round(v * k); }
    public void SetSelected(bool v) { if (selected == v) return; selected = v; t.Start(); }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }

    static void Fill(Graphics g, RectangleF r, float rad, Color c) { using (var p = Draw.Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new RectangleF(1, 1, Width - 3, Height - 3);
        Fill(g, outer, I(10), hot ? ThemePicker.CardHi : ThemePicker.Card);
        if (sel > 0) using (var p = Draw.Round(outer, I(10))) using (var pen = new Pen(Color.FromArgb((int)(255 * sel), ThemePicker.Accent), 2)) g.DrawPath(pen, p);
        var inner = new RectangleF(I(8), I(8), Width - I(16), Height - I(34));
        if (ThemeName == "Auto")
        {
            Mini(g, inner, Theme.Get("Midnight"));
            var st = g.Save();
            g.SetClip(new RectangleF(inner.X + inner.Width / 2, inner.Y, inner.Width, inner.Height));
            Mini(g, inner, Theme.Get("Light"));
            g.Restore(st);
        }
        else Mini(g, inner, Theme.Get(ThemeName));
        using (var f = new Font(Draw.Family, 9f))
            TextRenderer.DrawText(g, ThemeName == "Auto" ? "Match Windows" : ThemeName, f, new Rectangle(0, Height - I(26), Width, I(20)), selected ? ThemePicker.TextCol : ThemePicker.SubCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    void Mini(Graphics g, RectangleF r, Theme th)
    {
        Fill(g, r, I(6), th.Bg);
        float x = r.X + I(7), w = r.Width - I(14), y = r.Y + I(7);
        using (var b = new SolidBrush(th.Accent)) g.FillEllipse(b, x, y, I(7), I(7));
        Fill(g, new RectangleF(x + I(11), y + I(1), w * 0.45f, I(4)), I(2), th.Text);
        for (int i = 0; i < 3; i++)
        {
            float yy = y + I(13) + i * I(9);
            Fill(g, new RectangleF(x, yy, I(3), I(6)), 1.5f * k, Bars[i]);
            Fill(g, new RectangleF(x + I(7), yy + I(1), w * (0.62f - i * 0.12f), I(4)), I(2), Color.FromArgb(150, th.Sub));
            Fill(g, new RectangleF(x + w - I(14), yy + I(1), I(14), I(4)), I(2), Color.FromArgb(200, th.Text));
        }
    }
}

// ======================================================================= Settings backup
// Copies the app's settings (HKCU\Software\<KeyName>) to "<KeyName>-settings.reg" next to the exe, so they survive a
// Windows reinstall when the app folder is kept (on OneDrive, a USB stick…). It's a normal .reg export, so
// double-clicking the file restores the settings as well. A restore runs after the app has closed, so nothing the app
// saves on the way out can overwrite it, and then the app starts again with the restored settings.
// Same code in every Kinetik app.
static class Backup
{
    public static string KeyName, AppTitle;
    public static Action BeforeExit; // e.g. hide the tray icon, as the app's own Exit does
    static bool restoreAfterExit;

    public static string FilePath
    {
        get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), KeyName + "-settings.reg"); }
    }
    public static bool Exists { get { return System.IO.File.Exists(FilePath); } }
    public static string SavedText
    {
        get { return Exists ? System.IO.File.GetLastWriteTime(FilePath).ToString("d MMM yyyy, h:mm tt") : "never"; }
    }

    static bool HasSettings
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\" + KeyName)) return k != null; }
            catch { return true; }
        }
    }

    static bool Reg(string args) { return KinetikSuite.RegistryFiles.Run(args); }

    static bool FileIsOurs()
    {
        try
        {
            string own = "[HKEY_CURRENT_USER\\Software\\" + KeyName;
            bool any = false;
            foreach (var line in System.IO.File.ReadAllLines(FilePath))
            {
                var l = line.Trim();
                if (!l.StartsWith("[")) continue;
                if (!l.StartsWith(own + "]", StringComparison.OrdinalIgnoreCase) && !l.StartsWith(own + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                any = true;
            }
            return any;
        }
        catch { return false; }
    }

    static bool Import()
    {
        try { return FileIsOurs() && KinetikSuite.RegistryFiles.Replace(FilePath, new[] { KeyName }); }
        catch (Exception ex) { Say(ex.Message, MessageBoxIcon.Warning); return false; }
    }

    static void Say(string text, MessageBoxIcon icon) { MessageBox.Show(text, AppTitle, MessageBoxButtons.OK, icon); }

    // At launch: no settings in the registry (a fresh Windows) but a backup beside the exe, so offer it.
    public static void OfferRestore()
    {
        if (HasSettings || !Exists) return;
        if (MessageBox.Show("There are saved " + AppTitle + " settings in this folder, from " + SavedText + ".\n\nRestore them?",
                AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!Import()) Say("Couldn't restore the settings from\n" + FilePath, MessageBoxIcon.Warning);
    }

    public static void SaveNow()
    {
        if (Reg("export \"HKCU\\Software\\" + KeyName + "\" \"" + FilePath + "\" /y"))
            Say("Settings backed up to\n" + FilePath + "\n\nKeep this file with the app. After reinstalling Windows, " + AppTitle + " offers to restore it the first time it starts.", MessageBoxIcon.Information);
        else
            Say("Couldn't save the backup to\n" + FilePath + "\n\nCheck that the folder isn't read-only.", MessageBoxIcon.Warning);
    }

    public static void RestoreNow()
    {
        if (!Exists) { Say("There's no backup yet. Use \"Back up settings\" first.\n\nIt would be saved as\n" + FilePath, MessageBoxIcon.Information); return; }
        if (!FileIsOurs()) { Say("The backup file isn't a " + AppTitle + " settings file, so it wasn't used.", MessageBoxIcon.Warning); return; }
        if (MessageBox.Show("Replace the current settings with the backup from " + SavedText + "?\n\n" + AppTitle + " will restart.",
                AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        restoreAfterExit = true;
        if (BeforeExit != null) try { BeforeExit(); } catch { }
        Application.Exit();
    }

    // Called from Main once the app has closed and released its single-instance lock.
    public static void AfterExit()
    {
        if (!restoreAfterExit) return;
        if (!Import()) Say("Couldn't restore the settings from\n" + FilePath, MessageBoxIcon.Warning);
        try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
    }
}

// ======================================================================= The right-click menu every Kinetik widget shares
// The look, placement, startup and backup options, in the same order and the same words in every Kinetik app, so they
// feel like one set. Each app adds its own items above. Same code in every Kinetik app.
#pragma warning disable 0649 // the hooks an app has no use for stay null
class CommonMenu
{
    public static readonly int[] Scales = { 80, 90, 100, 115, 125, 150 };
    public static readonly int[] Opacities = { 100, 95, 80, 65, 50, 30, 0 };
    public const int DefaultOpacity = 95;
    public static readonly string[] Fonts = { "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Calibri", "Aptos", "Inter", "Cascadia Code", "Consolas", "Verdana", "Georgia" };

    // Settings saved before the shared default changed from 92% to 95% move with it; any other choice is kept.
    public static int Migrate(int opacity) { return opacity == 92 ? DefaultOpacity : opacity; }

    public string Title, Version;
    public Action OpenTheme;                                        // opens the theme picker (or the page with it)
    public Func<int> Scale; public Action<int> SetScale;
    public int[] Widths; public Func<int> CardWidth; public Action<int> SetCardWidth; // optional: Narrow, Normal, Wide
    public Func<string> FontName; public Action<string> SetFontName;   // optional; "" = the default font
    public Func<int> Opacity; public Action<int> SetOpacity;
    public Func<int> Layer; public Action<int> SetLayer;
    public Func<bool> Locked; public Action<bool> SetLocked;
    public Func<bool> ClickThrough; public Action<bool> SetClickThrough; // optional
    public Func<bool> Snap; public Action<bool> SetSnap;
    public string LockText = "Lock position";
    public Action OpenSettings;                                     // optional: the app's Settings window
    public Func<bool> RunAtStartup; public Action<bool> SetRunAtStartup;   // optional
    public Action BackUp, Restore; public Func<string> LastBackup; // optional
    public Action Exit;                                         // optional

    static ToolStripMenuItem Item(string text, bool check, Action click)
    {
        var it = new ToolStripMenuItem(text) { Checked = check };
        it.Click += (s, e) => click();
        return it;
    }

    static bool HasFont(string name)
    {
        try { using (var f = new FontFamily(name)) return f.Name == name; } catch { return false; }
    }

    // Appearance and placement: Theme, Size, Font, Background opacity, Placement.
    public void AddLook(ToolStripItemCollection items)
    {
        if (OpenTheme != null) items.Add(Item("Theme…", false, OpenTheme));

        var size = new ToolStripMenuItem("Size");
        foreach (var s in Scales) { int v = s; size.DropDownItems.Add(Item(v + "%", Scale() == v, () => SetScale(v))); }
        if (Widths != null)
        {
            size.DropDownItems.Add(new ToolStripSeparator());
            string[] names = { "Narrow", "Normal", "Wide" };
            for (int i = 0; i < Widths.Length && i < 3; i++) { int v = Widths[i]; size.DropDownItems.Add(Item(names[i], CardWidth() == v, () => SetCardWidth(v))); }
        }
        items.Add(size);

        if (FontName != null)
        {
            var font = new ToolStripMenuItem("Font");
            font.DropDownItems.Add(Item("Default", FontName() == "", () => SetFontName("")));
            foreach (var n in Fonts.Where(HasFont)) { string v = n; font.DropDownItems.Add(Item(v, FontName() == v, () => SetFontName(v))); }
            if (FontName() != "" && !Fonts.Contains(FontName())) font.DropDownItems.Add(Item(FontName(), true, () => { }));
            items.Add(font);
        }

        var opacity = new ToolStripMenuItem("Background opacity");
        foreach (var o in Opacities) { int v = o; opacity.DropDownItems.Add(Item(v + "%", Opacity() == v, () => SetOpacity(v))); }
        if (!Opacities.Contains(Opacity())) opacity.DropDownItems.Add(Item(Opacity() + "%", true, () => { }));
        items.Add(opacity);

        var place = new ToolStripMenuItem("Placement");
        string[] layers = { "On the desktop", "Normal window", "Always on top" };
        for (int i = 0; i < 3; i++) { int v = i; place.DropDownItems.Add(Item(layers[i], Layer() == v, () => SetLayer(v))); }
        place.DropDownItems.Add(new ToolStripSeparator());
        place.DropDownItems.Add(Item(LockText, Locked(), () => SetLocked(!Locked())));
        if (ClickThrough != null) place.DropDownItems.Add(Item("Click-through when locked", ClickThrough(), () => SetClickThrough(!ClickThrough())));
        place.DropDownItems.Add(Item("Snap to screen edges", Snap(), () => SetSnap(!Snap())));
        items.Add(place);
    }

    // The rest: Settings…, Run at startup, Backup, then the version and Exit.
    public void AddEnd(ToolStripItemCollection items)
    {
        items.Add(new ToolStripSeparator());
        if (OpenSettings != null) items.Add(Item("Settings…", false, OpenSettings));
        if (RunAtStartup != null) items.Add(Item("Run at startup", RunAtStartup(), () => SetRunAtStartup(!RunAtStartup())));
        if (BackUp != null)
        {
            var backup = new ToolStripMenuItem("Backup");
            backup.DropDownItems.Add(Item("Back up settings", false, BackUp));
            backup.DropDownItems.Add(Item("Restore settings…", false, Restore));
            backup.DropDownItems.Add(new ToolStripMenuItem("Last backup: " + LastBackup()) { Enabled = false });
            items.Add(backup);
        }
        items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem(Title + " " + Version) { Enabled = false });
        if (Exit != null) items.Add(Item("Exit", false, Exit));
    }

    public void AddAll(ToolStripItemCollection items) { AddLook(items); AddEnd(items); }
}
#pragma warning restore 0649
