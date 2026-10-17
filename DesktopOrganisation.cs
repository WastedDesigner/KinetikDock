using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class RoutingRule
{
    public bool Enabled { get; set; }
    public string Pattern { get; set; }
    public string Group { get; set; }
    public RoutingRule() { Enabled = true; }
    public bool Matches(ItemData item)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(Pattern)) return false;
        if (Pattern.Trim() == "<folders>") return item.IsDirectory;
        if (item.IsDirectory) return false;
        foreach (var glob in Pattern.Split(';').Where(p=>p.Trim().Length>0 && p.Length<=128))
        {
            var expression = "^" + Regex.Escape(glob.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            if (Regex.IsMatch(Path.GetFileName(item.Path),expression,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(30))) return true;
        }
        return false;
    }
    public static List<RoutingRule> Defaults()
    { return new List<RoutingRule> {
        new RoutingRule { Pattern="*.lnk;*.url;*.exe;*.appref-ms", Group="Desktop apps" },
        new RoutingRule { Pattern="*.pdf;*.doc*;*.xls*;*.ppt*;*.txt;*.md", Group="Desktop documents" },
        new RoutingRule { Pattern="*.png;*.jpg;*.jpeg;*.gif;*.webp;*.mp4;*.mp3;*.wav", Group="Desktop media" },
        new RoutingRule { Pattern="<folders>", Group="Desktop folders" }
    }; }
    public static string Destination(ItemData item, IEnumerable<RoutingRule> rules)
    { var match=rules.FirstOrDefault(r=>r.Matches(item)); return match==null ? null : match.Group; }
}

partial class DockContext
{
    DesktopOrganiser organiser;
    Form organisationWindow;
    public List<RoutingRule> Rules()
    {
        try { return string.IsNullOrWhiteSpace(C.RulesJson) ? RoutingRule.Defaults() : (new JavaScriptSerializer().Deserialize<List<RoutingRule>>(C.RulesJson) ?? RoutingRule.Defaults()).Where(r=>r!=null && !string.IsNullOrWhiteSpace(r.Group) && (r.Pattern??"").Length<=512).Take(50).ToList(); }
        catch { return RoutingRule.Defaults(); }
    }
    void StartOrganiser() { if(organiser!=null) organiser.Dispose(); organiser=new DesktopOrganiser(this); }
    public void AddDesktopReferences(IEnumerable<ItemData> entries, List<RoutingRule> rules, string workspace)
    {
        bool changed=false;
        var known=new HashSet<string>(C.List.SelectMany(g=>g.Items).Select(i=>i.Path),StringComparer.OrdinalIgnoreCase);
        foreach(var item in entries)
        {
            string name=RoutingRule.Destination(item,rules);
            if(string.IsNullOrWhiteSpace(name) || !known.Add(item.Path)) continue;
            var g=C.List.FirstOrDefault(x=>string.IsNullOrWhiteSpace(x.PortalPath) && x.Workspace==workspace && string.Equals(x.Name,name,StringComparison.CurrentCultureIgnoreCase));
            if(g==null) { g=new GroupData { Name=name.Trim(), Workspace=workspace }; C.List.Add(g); Open(g); }
            g.Items.Add(new ItemData { Name=Target.NameFor(item.Path), Path=item.Path, IsDirectory=item.IsDirectory }); changed=true;
        }
        if(changed) { Save(); RenderAll(); }
    }
    public void ShowOrganisation()
    {
        if(organisationWindow!=null && !organisationWindow.IsDisposed) { organisationWindow.Activate(); return; }
        var f=new Form { Text="Organise desktop references", ClientSize=new Size(760,560), MinimumSize=new Size(660,490), StartPosition=FormStartPosition.CenterScreen, Font=new Font("Segoe UI",10) }; organisationWindow=f;
        var help=new Label { Dock=DockStyle.Top, Height=55, Padding=new Padding(10), Text="Rules run from top to bottom; the first match wins. Patterns use * and ?, separated by ;.\nReview matches below. Original desktop files stay in place." };
        var grid=new DataGridView { Dock=DockStyle.Top, Height=185, AutoGenerateColumns=false, AllowUserToAddRows=true, AllowUserToDeleteRows=true, RowHeadersWidth=25, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name="Enabled",HeaderText="On",FillWeight=15 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="Pattern",HeaderText="Filename patterns / <folders>",FillWeight=65 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="Group",HeaderText="Destination group",FillWeight=45 });
        foreach(var rule in Rules()) grid.Rows.Add(rule.Enabled,rule.Pattern,rule.Group);
        var preview=new ListView { Dock=DockStyle.Fill, View=View.Details, CheckBoxes=true, FullRowSelect=true }; preview.Columns.Add("Desktop item",360); preview.Columns.Add("Destination",250);
        var footer=new FlowLayoutPanel { Dock=DockStyle.Bottom, Height=100, Padding=new Padding(8), FlowDirection=FlowDirection.LeftToRight };
        var watch=new CheckBox { Text="Watch for new desktop items", Checked=C.AutoOrganise, Width=270, Height=28 };
        var status=new Label { Text="Preview to review matching references.", AutoSize=false, Width=430, Height=28 };
        var scan=new Button { Text="Preview rules", Width=145, Height=32 };
        var apply=new Button { Text="Add checked / save rules", Width=220,Height=32,Enabled=false };
        footer.Controls.AddRange(new Control[] { watch,status,scan,apply });
        f.Controls.Add(preview); f.Controls.Add(grid); f.Controls.Add(help); f.Controls.Add(footer);
        List<RoutingRule> reviewed=null; List<ItemData> scanned=null; string reviewedWorkspace=null;
        Action invalidate=()=> { reviewed=null; apply.Enabled=false; status.Text="Rules changed — preview again before applying."; };
        grid.CellValueChanged+=(s,e)=>invalidate(); grid.RowsRemoved+=(s,e)=>invalidate();
        scan.Click+=(s,e)=>
        {
            grid.EndEdit(); var rules=new List<RoutingRule>();
            foreach(DataGridViewRow row in grid.Rows)
            {
                if(row.IsNewRow) continue;
                string pattern=Convert.ToString(row.Cells["Pattern"].Value).Trim(), name=Convert.ToString(row.Cells["Group"].Value).Trim();
                if(pattern.Length==0 && name.Length==0) continue;
                if(pattern.Length==0 || pattern.Length>512 || name.Length==0 || name.Length>80) { MessageBox.Show("Each rule needs a pattern (up to 512 characters) and a destination group (up to 80 characters)."); return; }
                rules.Add(new RoutingRule { Enabled=Convert.ToBoolean(row.Cells["Enabled"].Value??true),Pattern=pattern,Group=name });
            }
            if(rules.Count>50) { MessageBox.Show("Use at most 50 rules."); return; }
            scan.Enabled=false; apply.Enabled=false; grid.Enabled=false; status.Text="Reading desktop folders…";
            reviewedWorkspace=C.Workspace;
            if(!DockWork.Queue(()=>
            {
                string error; var found=DesktopOrganiser.ReadDesktop(out error);
                DockWork.Publish(()=>
                {
                    if(f.IsDisposed) return;
                    scan.Enabled=true; grid.Enabled=true; preview.Items.Clear();
                    if(error!=null) { status.Text=error; return; }
                    scanned=found; reviewed=rules;
                    var known=new HashSet<string>(C.List.SelectMany(g=>g.Items).Select(i=>i.Path),StringComparer.OrdinalIgnoreCase);
                    foreach(var item in found)
                    {
                        string destination=RoutingRule.Destination(item,rules);
                        if(destination==null || known.Contains(item.Path)) continue;
                        var row=new ListViewItem(new[] { item.Name,destination }) { Tag=item, Checked=true, ToolTipText=item.Path }; preview.Items.Add(row);
                    }
                    status.Text=preview.Items.Count+" matching items. Destination workspace: "+reviewedWorkspace; apply.Enabled=true;
                });
            },false)) { scan.Enabled=true; grid.Enabled=true; status.Text="Busy — try Preview again."; }
        };
        apply.Click+=(s,e)=>
        {
            if(reviewed==null || scanned==null) return;
            C.RulesJson=new JavaScriptSerializer().Serialize(reviewed); C.AutomationWorkspace=reviewedWorkspace;
            AddDesktopReferences(preview.CheckedItems.Cast<ListViewItem>().Select(i=>(ItemData)i.Tag),reviewed,reviewedWorkspace);
            C.AutoOrganise=watch.Checked;
            C.DesktopSeenJson=new JavaScriptSerializer().Serialize(scanned.Select(i=>i.Path).ToArray());
            if(organiser!=null) { organiser.Dispose(); organiser=null; }
            if(C.AutoOrganise && !Program.Demo) StartOrganiser(); Save(); f.Close();
        };
        f.Show();
    }
}

sealed class DesktopOrganiser : IDisposable
{
    readonly DockContext app;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=700 };
    readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
    HashSet<string> seen;
    bool disposed, scanning, again;
    public DesktopOrganiser(DockContext app)
    {
        this.app=app;
        try { seen=new HashSet<string>(new JavaScriptSerializer().Deserialize<string[]>(app.C.DesktopSeenJson)??new string[0],StringComparer.OrdinalIgnoreCase); } catch { seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        timer.Tick+=(s,e)=> { timer.Stop(); Scan(); };
        DockWork.Queue(()=>
        {
            var created=new List<FileSystemWatcher>();
            foreach(var folder in DesktopFolders())
            {
                try
                {
                    var watcher=new FileSystemWatcher(folder) { NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName, IncludeSubdirectories=false };
                    FileSystemEventHandler notify=(s,e)=>Schedule(); watcher.Created+=notify; watcher.Deleted+=notify; watcher.Renamed+=(s,e)=>Schedule(); watcher.Error+=(s,e)=>Schedule(); watcher.EnableRaisingEvents=true; created.Add(watcher);
                } catch { }
            }
            DockWork.Publish(()=> { if(disposed) { foreach(var w in created) w.Dispose(); return; } watchers.AddRange(created); Scan(); },()=> { foreach(var w in created) w.Dispose(); });
        },false);
    }
    public static IEnumerable<string> DesktopFolders()
    { return new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }.Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase); }
    public static List<ItemData> ReadDesktop(out string error)
    {
        error=null; var items=new List<ItemData>();
        try
        {
            foreach(var folder in DesktopFolders())
                foreach(var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if((entry.Attributes&(FileAttributes.Hidden|FileAttributes.System))!=0) continue;
                    if(items.Count>=2000) { error="Desktop scan limited to 2,000 items. Use a folder portal for larger folders."; return items; }
                    items.Add(new ItemData { Name=entry.Name,Path=entry.FullName,IsDirectory=(entry.Attributes&FileAttributes.Directory)!=0 });
                }
        }
        catch { error="Could not read a desktop folder. Check access and try again."; }
        return items;
    }
    void Schedule() { DockWork.Publish(()=> { if(!disposed) { timer.Stop(); timer.Start(); } }); }
    void Scan()
    {
        if(disposed) return;
        if(scanning) { again=true; return; } scanning=true;
        if(!DockWork.Queue(()=>
        {
            string error; var items=ReadDesktop(out error);
            DockWork.Publish(()=>
            {
                if(disposed) return; scanning=false;
                if(error==null)
                {
                    app.AddDesktopReferences(items.Where(i=>!seen.Contains(i.Path)),app.Rules(),app.C.AutomationWorkspace);
                    var now=new HashSet<string>(items.Select(i=>i.Path),StringComparer.OrdinalIgnoreCase);
                    if(!seen.SetEquals(now)) { seen=now; app.C.DesktopSeenJson=new JavaScriptSerializer().Serialize(now.ToArray()); app.Save(); }
                }
                if(again) { again=false; timer.Start(); }
            });
        },false)) scanning=false;
    }
    public void Dispose() { disposed=true; timer.Dispose(); foreach(var watcher in watchers) watcher.Dispose(); watchers.Clear(); }
}
