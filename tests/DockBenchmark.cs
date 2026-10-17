using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

static class DockBenchmark
{
    [STAThread] static int Main(string[] args)
    {
        Program.Demo = true; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using(var app = new DockContext())
        {
            var settle=Stopwatch.StartNew(); while(settle.ElapsedMilliseconds<1500) { Application.DoEvents(); Thread.Sleep(10); }
            var f=app.Forms[0]; f.Data.Items.Clear();
            for(int i=0;i<1000;i++) f.Data.Items.Add(new ItemData { Name="Bookmark "+i, Path="https://example.com/"+i });
            f.Render();
            var timer=Stopwatch.StartNew(); for(int i=0;i<10;i++) f.Render(); timer.Stop();
            using(var p=Process.GetCurrentProcess())
                Console.WriteLine("{\"Version\":\""+Program.Version+"\",\"Items\":1000,\"TenRendersMs\":"+timer.ElapsedMilliseconds+",\"SurfaceWidth\":"+f.Frame.Width+",\"SurfaceHeight\":"+f.Frame.Height+",\"PrivateBytes\":"+p.PrivateMemorySize64+",\"WorkingSetBytes\":"+p.WorkingSet64+"}");
            app.Quit();
        }
        return 0;
    }
}
