using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ShareX;

internal static class Check
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint h, int index);
    [DllImport("user32.dll")] static extern nint SendMessage(nint h, uint msg, nint w, nint l);
    static object Field(object o,string n)=>o.GetType().GetField(n,F)!.GetValue(o)!;
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F)!.Invoke(o,a)!;
    static void Pump(){Application.DoEvents();Thread.Sleep(100);Application.DoEvents();}
    static void Assert(bool ok,string s){if(!ok)throw new Exception(s);Console.WriteLine("PASS "+s);}
    [STAThread] static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var p=typeof(MainForm).Assembly.GetType("ShareX.Program")!;
        void Set(string n,object v)=>p.GetProperty(n,F)!.SetValue(null,v);
        var root=Path.GetFullPath("test-results/widget-alttab/profile");Directory.CreateDirectory(root);
        var config=new ApplicationConfig{ShowTray=false,DisableHotkeys=true};
        Set("CustomPersonalPath",root);Set("Settings",config);Set("DefaultTaskSettings",config.DefaultTaskSettings);
        using var widget=new TodoWidgetForm(new NoteLogStore(root));
        var page=Field(widget,"todoPage");page.GetType().GetField("choosingSchedule",F)!.SetValue(page,true);
        widget.Show();Pump();
        for(int cycle=0;cycle<3;cycle++) foreach(bool expanded in new[]{false,true})
        {
            Call(widget,"SetExpanded",expanded);Pump();
            long style=GetWindowLongPtr(widget.Handle,-20).ToInt64();
            Assert((style&0x80)!=0 && (style&0x40000)==0,"tool window, no app window: "+cycle+" expanded="+expanded);
            Assert((style&0x8000000)==0 && widget.TopMost,"activation and topmost preserved");
            if(expanded){var edit=(TextBoxBase)Field(page,"todoTextBox");widget.Activate();edit.Focus();Pump();
                SendMessage(edit.Handle,0x102,(nint)'x',0);Assert(edit.Text.EndsWith("x"),"editor accepts character");edit.Clear();}
            else {Call(widget,"CompactLabel_MouseDown",widget,new MouseEventArgs(MouseButtons.Right,1,0,0,0));Pump();
                var menu=(ContextMenuStrip)Field(widget,"searchMenu");Assert(menu.Visible,"collapsed right-click menu opens");menu.Close();}
        }
        typeof(Control).GetMethod("RecreateHandle",F)!.Invoke(widget,null);Pump();
        Assert((GetWindowLongPtr(widget.Handle,-20).ToInt64()&0x80)!=0,"tool style survives handle recreation");
        Call(widget,"SetExpanded",false);widget.Text="小蟒待办隔离测试";
        if(args.Contains("--ui")){using var timer=new System.Windows.Forms.Timer{Interval=300000};timer.Tick+=(_,_)=>widget.Close();timer.Start();Application.Run(widget);}
        Console.WriteLine("COMPLETE");
    }
}
