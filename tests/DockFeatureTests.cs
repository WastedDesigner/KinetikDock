using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class DockFeatureTests
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
    static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(o); }
    static object Call(object o, string name, params object[] args) { return o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(o,args); }
    static void Pump(int ms) { var timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); } }
    static void Until(Func<bool> condition, string name) { var timer = Stopwatch.StartNew(); while (!condition() && timer.ElapsedMilliseconds < 8000) Pump(20); Check(condition(), name); }
    static GroupForm Open(DockContext app, GroupData data) { app.C.List.Add(data); return (GroupForm)Call(app,"Open",data); }
    [STAThread] static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "kinetik-dock-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        DockContext app = null;
        try
        {
            Program.Demo = true; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            app = new DockContext(); Pump(400);
            foreach (var f in app.Forms.ToArray()) f.Close(); app.C.List.Clear();
            var legacy = new JavaScriptSerializer().Deserialize<GroupData>("{\"Name\":\"Legacy\",\"Cols\":4,\"Items\":[{\"Name\":\"Old\",\"Path\":\"https://example.com\"}]}");
            DockLayout.Sanitize(legacy);
            Check(legacy.ItemSize == -1 && legacy.LabelMode == -1 && legacy.Workspace == "Main", "old layouts inherit global appearance and Main workspace");
            Check(!string.IsNullOrEmpty(legacy.Items[0].Id), "legacy shortcuts acquire stable IDs");
            legacy.Items.Add(new ItemData { Id = legacy.Items[0].Id, Path = "https://example.net" }); DockLayout.Sanitize(legacy);
            Check(legacy.Items.Select(i=>i.Id).Distinct().Count() == 2, "duplicate item IDs repaired");
            Check(DockLayout.MaxScroll(1000,4,80,400) == 19600, "scroll extent accounts for all rows");
            Check(DockLayout.Match(legacy.Items[0], "EXAMPLE") && !DockLayout.Match(legacy.Items[0], "unrelated"), "search matches names and paths case insensitively");
            int latch=int.MinValue;
            Check(DockLayout.NearestSnap(97,new[] { 100,90 },18,ref latch)==100,"snapping picks the nearest candidate rather than the last candidate");
            Check(DockLayout.NearestSnap(92,new[] { 100,90 },18,ref latch)==100,"snap hysteresis prevents jitter between adjacent edges");
            Check(DockLayout.NearestSnap(130,new[] { 100,90 },18,ref latch)==130,"snapping releases beyond its hysteresis distance");
            var group = new GroupData { Name = "Large", X = 100, Y = 100, Width = 340, Height = 320 };
            for (int i=0;i<1000;i++) group.Items.Add(new ItemData { Name = "Link " + i, Path = "https://example.com/" + i });
            var large = Open(app,group); var other = Open(app,new GroupData { Name = "Other", X = 600, Y=100 }); Pump(200);
            large.Render();
            Check(large.Frame.Height < 700 && large.Frame.Width < 800, "1,000 items use a bounded viewport surface");
            var visible = (List<int>)Field(large,"tileIndices");
            Check(visible.Count < 60 && visible.Count > 0, "only visible rows have drawing and hit-test entries");
            Call(large,"OnMouseWheel",new MouseEventArgs(MouseButtons.None,0,50,100,-1200));
            Until(()=>((List<int>)Field(large,"tileIndices"))[0] > 0, "wheel scroll changes visible item range");
            var scroll = (int)Field(large,"scrollY");
            var rects = (List<RectangleF>)Field(large,"tiles"); var indices = (List<int>)Field(large,"tileIndices");
            int hit = (int)Call(large,"TileAt",Point.Round(new PointF(rects[0].X+8, Math.Max(rects[0].Y+8, ((RectangleF)Field(large,"contentArea")).Y+2))));
            Check(hit == indices[0], "scrolled hit testing returns the original item index");
            long before = app.RenderCount; Call(app,"RefreshPath",group.Items[indices[0]].Path); Pump(50);
            Check(app.RenderCount - before == 1, "a target update redraws only its visible group");
            before = app.RenderCount; for(int i=0;i<30;i++) app.RequestRender(large); Pump(50);
            Check(app.RenderCount - before == 1, "render bursts coalesce into one group render");
            before = app.RenderCount; Call(app,"RefreshPath",group.Items[999].Path); Pump(50);
            Check(app.RenderCount == before, "offscreen target update causes no redraw");
            Check(large.AccessibilityObject.GetChildCount()==1000,"accessibility exposes shortcut names beyond the visible rows");
            Check(large.AccessibilityObject.GetChild(indices[0]).Name==group.Items[indices[0]].Name,"accessible names match the correct scrolled item");
            Check(large.AccessibilityObject.GetChild(999).Bounds.IsEmpty,"offscreen accessible items have no visible bounds");
            var references=new GroupData { Name="Moves" };
            foreach(string name in new[] { "A","B","C","D" }) references.Items.Add(new ItemData { Name=name,Path="https://example.com/"+name });
            var moveForm=Open(app,references); var moving=references.Items.Skip(1).Take(2).Select(i=>i.Id).ToArray();
            moveForm.MoveReferences(moveForm,moving,4,false);
            Check(string.Join("",references.Items.Select(i=>i.Name))=="ADBC","multi-item reorder preserves relative order and adjusts insertion index");
            other.MoveReferences(moveForm,moving,0,true);
            Check(other.Items.Count==2 && references.Items.Count==4,"copying multiple references leaves the originals in place");
            Check(!other.Items.Any(i=>moving.Contains(i.Id)),"copied references receive fresh stable IDs");
            other.MoveReferences(moveForm,moving,other.Items.Count,false);
            Check(string.Join("",references.Items.Select(i=>i.Name))=="AD" && other.Items.Count==4,"cross-group move removes exactly the selected references");
            var rules=RoutingRule.Defaults();
            Check(RoutingRule.Destination(new ItemData { Path=@"C:\Desktop\report.PDF" },rules)=="Desktop documents","desktop rules match file extensions without case sensitivity");
            Check(RoutingRule.Destination(new ItemData { Path=@"C:\Desktop\project",IsDirectory=true },rules)=="Desktop folders","desktop rules identify folders separately");
            Check(RoutingRule.Destination(new ItemData { Path=@"C:\Desktop\unknown.bin" },rules)==null,"unmatched desktop items are left alone");
            rules.Insert(0,new RoutingRule { Pattern="report.*",Group="Reports" });
            Check(RoutingRule.Destination(new ItemData { Path=@"C:\Desktop\report.pdf" },rules)=="Reports","the first matching routing rule wins");
            var imports=new[] { new ItemData { Path=Path.Combine(root,"report.pdf"),Name="Report" } };
            app.AddDesktopReferences(imports,rules,"Main"); app.AddDesktopReferences(imports,rules,"Main");
            Check(app.C.List.Where(g=>g.Name=="Reports").SelectMany(g=>g.Items).Count()==1,"desktop reference imports do not duplicate existing references");
            Check(!File.Exists(imports[0].Path),"reference organisation does not create or relocate target files");
            int intendedX=group.X, intendedY=group.Y; group.X=50000; group.Y=50000; large.Render();
            Check(group.X==50000 && group.Y==50000 && large.Placed.X<50000,"offscreen recovery keeps the saved monitor placement intact");
            group.X=intendedX; group.Y=intendedY; large.Render();
            group.Collapsed = true; large.Render(); Check(large.Frame.Height < 130, "collapse retains only the title surface");
            group.Collapsed = false; large.Render(); Check((int)Field(large,"scrollY") == scroll, "roll-up preserves scroll position");
            app.CombineTabs(large,other); Check(app.IsShown(large) && !app.IsShown(other), "tab combination shows one active group");
            app.SwitchTab(other); Check(!app.IsShown(large) && app.IsShown(other), "switching tabs hides the previous group");
            Check(large.Frame==null,"inactive tabs release their drawing surfaces");
            app.SelectWorkspace("Second"); Check(!app.IsShown(other) && !app.IsShown(large), "workspace hides groups from other workspaces");
            app.SelectWorkspace("Main"); app.TogglePeek(); Check(app.Peeking, "quick access raises current workspace groups"); app.TogglePeek(); Check(!app.Peeking, "quick access restores normal layer");
            group.Name="Changed"; app.Save(); app.Undo(false);
            Check(app.C.List.Any(g=>g.Name=="Large"), "undo restores the previous layout"); app.Undo(true);
            Check(app.C.List.Any(g=>g.Name=="Changed"), "redo reapplies layout changes");
            File.WriteAllText(Path.Combine(root,"a.txt"),"a"); Directory.CreateDirectory(Path.Combine(root,"sub"));
            var portalForm = Open(app,new GroupData { Name="Portal", PortalPath=root, X=100,Y=450,Height=250 });
            var portal = (FolderPortal)Field(portalForm,"portal");
            Until(()=>portal.Items.Count==2,"portal enumerates folder contents asynchronously");
            Check(portal.Items[0].IsDirectory,"portal sorts folders before files");
            File.WriteAllText(Path.Combine(root,"b.txt"),"b"); Until(()=>portal.Items.Count==3,"portal watcher refreshes added files");
            portalForm.Data.Collapsed=true; portalForm.Render();
            Check(Field(portal,"watcher")==null,"collapsed portal releases its watcher");
            portalForm.Data.Collapsed=false; portalForm.Render(); Until(()=>Field(portal,"watcher")!=null,"expanded portal reconnects its watcher");
            portal.Navigate(Path.Combine(root,"does-not-exist")); Until(()=>portal.Status.Contains("unavailable"),"missing folder produces a recoverable status");
            portal.Navigate(root); Until(()=>portal.Items.Count==3 && portal.Status=="","portal recovers after reconnecting");
            portalForm.Data.PortalFilter="*.png"; portal.Refresh(); Until(()=>portal.Status=="" && portal.Items.Count==1,"portal filters files while retaining folders for navigation");
            portalForm.Data.PortalFilter=null; portal.Refresh(); Until(()=>portal.Items.Count==3,"clearing a portal filter restores its files");
            portal.Navigate(Path.Combine(root,"sub")); Until(()=>portal.Items.Count==0 && portal.Status=="","portal navigates into a subfolder");
            portal.GoBack(); Until(()=>portal.CurrentPath==root && portal.Items.Count==3,"portal history restores the previous folder");
            portal.GoForward(); Until(()=>portal.CurrentPath.EndsWith("sub") && portal.Items.Count==0,"portal forward history restores the subfolder");
            portal.Navigate(Path.Combine(root,"does-not-exist")); portal.Navigate(root);
            Until(()=>portal.CurrentPath==root && portal.Items.Count==3 && portal.Status=="","rapid navigation discards stale folder results");
            app.SelectWorkspace("Second"); Check(Field(portal,"watcher")==null,"inactive workspace releases portal watchers");
            var serial = new JavaScriptSerializer().Serialize(app.C.List); var restored = new JavaScriptSerializer().Deserialize<List<GroupData>>(serial);
            Check(restored.Any(g=>g.PortalPath==root) && restored.Any(g=>g.TabSet!=null),"portals and tabs survive layout serialization");
            Check(ShellIcon.Bytes <= 24*1024*1024 && ShellIcon.Count<=2048,"icon cache stays within its configured bounds");
            Pump(500); long saves=app.SaveCount; for(int i=0;i<20;i++) app.Save(); Pump(600);
            Check(app.SaveCount-saves==1,"settings changes debounce to a single save");
            before=app.RenderCount; Pump(300);
            Check(app.RenderCount==before,"idle groups do not run a continuous render loop");
            if(args.Length>0) Showcase(app,root,args[0]);
            Console.WriteLine("RESULT: " + checks + " Dock checks passed."); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if(app!=null) { app.Quit(); app.Dispose(); } Directory.Delete(root,true); }
    }
    static void Showcase(DockContext app,string root,string output)
    {
        foreach(var f in app.Forms.ToArray()) f.Close(); app.C.List.Clear(); app.C.Workspace="Main";
        var design=Open(app,new GroupData { Name="Creative workspace",X=100,Y=100,Width=340,Height=260,AccentArgb=Color.FromArgb(88,180,244).ToArgb() });
        foreach(string name in new[] { "Figma","Adobe","References","Inspiration","Brand library","Research","Moodboard","Portfolio","Typography","Archive","Projects","Resources" }) design.Data.Items.Add(new ItemData { Name=name,Path="https://example.com/"+name });
        var development=Open(app,new GroupData { Name="Development",X=100,Y=100,Width=340,Height=260 });
        development.Data.Items.Add(new ItemData { Name="GitHub",Path="https://github.com" });
        app.CombineTabs(development,design); app.SwitchTab(design);
        var folder=Path.Combine(root,"Demo project"); Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.Combine(folder,"Assets"));
        File.WriteAllText(Path.Combine(folder,"Brand notes.md"),"Demo fixture"); File.WriteAllText(Path.Combine(folder,"Launch checklist.txt"),"Demo fixture"); File.WriteAllText(Path.Combine(folder,"Website brief.txt"),"Demo fixture");
        var portal=Open(app,new GroupData { Name="Project files",PortalPath=folder,X=500,Y=100,Width=340,Height=260,ListView=true,AccentArgb=Color.FromArgb(108,201,124).ToArgb() });
        Until(()=>portal.Items.Count==4,"showcase portal loaded");
        var quick=Open(app,new GroupData { Name="Quick links",X=100,Y=410,Width=340,Height=180,Compact=true,ItemSize=0,AccentArgb=Color.FromArgb(255,170,60).ToArgb() });
        foreach(string name in new[] { "Mail","Calendar","Notes","Tasks","Files" }) quick.Data.Items.Add(new ItemData { Name=name,Path="https://example.com/"+name });
        app.RenderAll(); Pump(500); app.RenderAll();
        var forms=app.Forms.Where(app.IsShown).ToArray(); var bounds=forms.Select(f=>f.Placed).Aggregate(Rectangle.Union);
        using(var bmp=new Bitmap(bounds.Width,bounds.Height)) using(var g=Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(12,14,20));
            foreach(var f in forms) if(f.Frame!=null) g.DrawImageUnscaled(f.Frame,f.Placed.X-bounds.X,f.Placed.Y-bounds.Y);
            bmp.Save(output,System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
