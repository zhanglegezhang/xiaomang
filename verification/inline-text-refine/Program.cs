using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using ShareX.ScreenCaptureLib;

static class Check
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
 static int checks;static object Get(object o,string n)=>o.GetType().GetProperty(n,F)?.GetValue(o)??o.GetType().GetField(n,F)!.GetValue(o)!;
 static object Call(object o,string n,params object[] args){var type=o.GetType();while(type!=null){var m=type.GetMethods(F|BindingFlags.DeclaredOnly).FirstOrDefault(m=>m.Name==n&&m.GetParameters().Length==args.Length);if(m!=null)return m.Invoke(o,args)!;type=type.BaseType;}throw new Exception(n);}
 static void Assert(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Pump(){Application.DoEvents();Application.DoEvents();}
 static void Set(object o,string n,object v){var t=o.GetType();while(t!=null){var p=t.GetProperty(n,F|BindingFlags.DeclaredOnly);if(p!=null){p.SetValue(o,v);return;}var f=t.GetField(n,F|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(o,v);return;}t=t.BaseType;}throw new Exception(n);}
 static object Field(object o,string n){var t=o.GetType();while(t!=null){var f=t.GetField(n,F|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(o)!;t=t.BaseType;}throw new Exception(n);}
 static RichTextBox Editor(object manager)=>(RichTextBox)Field(manager,"inlineText");
 static List<BaseShape> Shapes(object manager)=>(List<BaseShape>)Get(manager,"Shapes");
 static TextDrawingShape Add(object manager,ShapeType type){Set(manager,"CurrentTool",type);var s=(TextDrawingShape)Call(manager,"CreateShape",type);Call(manager,"AddShape",s);return s;}
 static void Finish(object manager,bool commit){Call(manager,"FinishInlineText",commit);Pump();}
 static bool Editing(object manager)=>(bool)Get(manager,"IsEditingText");
 static void Key(object editor,Keys key){var msg=Message.Create(((Control)editor).Handle,0x100,IntPtr.Zero,IntPtr.Zero);Call(editor,"ProcessCmdKey",msg,key);Pump();}
 [STAThread] static int Main(string[] args){try{Run(args);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static void Run(string[] args)
 {
  Application.SetHighDpiMode(args.Contains("--96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
  var output=Path.GetFullPath(args.FirstOrDefault(a=>a.StartsWith("--out="))?[6..]??"build/inline-text-20261022/checks");Directory.CreateDirectory(output);
  var options=new RegionCaptureOptions{DetectWindows=false,DetectControls=false,RememberMenuState=false,ShowEditorPanTip=false,ImageEditorStartMode=ImageEditorStartMode.Normal};
  var backdrop=new Bitmap(1200,800);using(var g=Graphics.FromImage(backdrop)){g.Clear(Color.FromArgb(33,76,108));using var p=new Pen(Color.FromArgb(133,176,198),2);for(int x=0;x<1200;x+=20)g.DrawLine(p,x,0,x,800);for(int y=0;y<800;y+=20)g.DrawLine(p,0,y,1200,y);}
  using var form=new RegionCaptureForm(args.Contains("--capture")?RegionCaptureMode.Annotation:RegionCaptureMode.Editor,options,backdrop,true);var manager=Get(form,"ShapeManager");
  form.Shown-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),form,typeof(RegionCaptureForm).GetMethod("RegionCaptureForm_Shown",F)!);
  form.TopMost=false;form.Show();Pump();Call(form,"Pause");Set(form,"ZoomFactor",1f);form.ClientSize=new Size(1200,850);
  var originalPreview=form.KeyPreview;var samples=new List<double>();
  foreach(var type in new[]{ShapeType.DrawingTextOutline,ShapeType.DrawingSpeechBalloon,ShapeType.DrawingTextBackground}){
   var s=Add(manager,type);var sw=Stopwatch.StartNew();s.OnCreating();Pump();sw.Stop();samples.Add(sw.Elapsed.TotalMilliseconds);
   Assert(Editing(manager),"new shape edits inline "+type);
   Assert(s.TextOptions.Font=="Microsoft YaHei"&&s.TextOptions.Size==27&&s.TextOptions.SizeInPixels,"new default YaHei 20pt "+type);
   Assert(Application.OpenForms.Cast<Form>().All(f=>f.GetType().Name!="TextDrawingInputBox"),"no modal text input");
   var editor=Editor(manager);Assert(editor.Focused,"input gets focus");
   var charMessage=Message.Create(editor.Handle,0x0102,new IntPtr('A'),IntPtr.Zero);Call(editor,"WndProc",charMessage);
   SendKeys.SendWait("{ENTER}");Pump();
   Assert(editor.Text.StartsWith("A")&&(editor.Text.Contains('\n')||editor.Text.Contains('\r'))&&Editing(manager),"native characters and Enter newline stay in editor");
   Assert(Math.Abs(editor.Font.SizeInPoints*editor.DeviceDpi/72-27)<1,"editor physical pixel font at "+editor.DeviceDpi);
   editor.Text="中文 MIT University\n第二行测试";Pump();
   if(s is SpeechBalloonDrawingShape growing){editor.Text=string.Join("\n",Enumerable.Repeat("气泡增长测试",8));Pump();Assert(growing.TailVisible,"new balloon tail stays outside growing text");editor.Text="中文 MIT University\n第二行测试";Pump();}
   Key(editor,Keys.Control|Keys.A);Assert(editor.SelectionLength==editor.TextLength,"Ctrl+A selects text");
   editor.Select(editor.TextLength,0);editor.SelectedText=" ABC 123";Pump();
   int count=Shapes(manager).Count;Call(manager,"form_KeyDown",form,new KeyEventArgs(Keys.Delete));
   Call(form,"RegionCaptureForm_KeyDown",form,new KeyEventArgs(Keys.Enter));Assert(Editing(manager)&&!form.IsDisposed&&Shapes(manager).Count==count,"editing guards Delete and screenshot Enter");
   if(type==ShapeType.DrawingSpeechBalloon){
    var frame=(Control)Field(manager,"inlineTextFrame");var before=s.Rectangle;var tail=((SpeechBalloonDrawingShape)s).TailPosition;
    Call(frame,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,2,2,0));Call(frame,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,32,22,0));Call(frame,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,32,22,0));Pump();
    Assert(s.Rectangle.X==before.X+30&&s.Rectangle.Y==before.Y+20,"editing border drags shape");
    Assert(((SpeechBalloonDrawingShape)s).TailPosition==new PointF(tail.X+30,tail.Y+20),"balloon tail follows move");
   }
   Finish(manager,true);Assert(!Editing(manager)&&s.Text.Contains("中文")&&s.Text.Contains("123"),"commit multiline mixed text");
   Assert(form.KeyPreview==originalPreview,"keyboard preview restored");
   var text=s.Text;var bounds=s.Rectangle;s.OnDoubleClicked();Pump();Editor(manager).Text="要取消的更改";Key(Editor(manager),Keys.Escape);
   Assert(!Editing(manager)&&s.Text==text&&s.Rectangle==bounds,"Esc restores old text and bounds");
   s.OnDoubleClicked();Pump();Editor(manager).Text="修改后 中文 English 42";Key(Editor(manager),Keys.Control|Keys.Enter);
   Assert(s.Text=="修改后 中文 English 42"&&!Editing(manager),"Ctrl+Enter commits");
   var history=Field(manager,"history");Call(history,"Undo");Assert(((TextDrawingShape)Shapes(manager).Last()).Text==text,"one undo restores edit");
   Call(history,"Redo");s=(TextDrawingShape)Shapes(manager).Last();Assert(s.Text=="修改后 中文 English 42","redo reapplies edit");
   s.Rectangle=new RectangleF(300,200,s.Rectangle.Width,s.Rectangle.Height);Set(manager,"CurrentShape",s);
   Call(manager,"form_MouseDoubleClick",form,new MouseEventArgs(MouseButtons.Left,2,310,210,0));Pump();Assert(Editing(manager),"double click enters editing");
   Editor(manager).Text="最后未提交的文字";using(var image=(Bitmap)Call(manager,"RenderOutputImage",new Bitmap(1200,850),PointF.Empty)){Assert(!Editing(manager)&&s.Text=="最后未提交的文字","output commits pending text");image.Save(Path.Combine(output,type+"-output.png"));}
   s.OnDoubleClicked();Pump();Editor(manager).Text="";Finish(manager,true);Assert(!Shapes(manager).Contains(s),"empty edit removes annotation");
  }
  var cancelled=Add(manager,ShapeType.DrawingTextOutline);cancelled.OnCreating();Pump();Key(Editor(manager),Keys.Escape);Assert(!Shapes(manager).Contains(cancelled),"new empty cancel removes shape");
  var remembered=Add(manager,ShapeType.DrawingTextOutline);remembered.OnCreating();Pump();Editor(manager).Text="字体记忆";Finish(manager,true);remembered.TextOptions.Size=28;remembered.OnConfigSave();
  var next=Add(manager,ShapeType.DrawingTextOutline);next.OnCreating();Pump();Assert(next.TextOptions.Size==28,"next shape remembers changed size");Finish(manager,false);
  var legacy=Add(manager,ShapeType.DrawingTextBackground);legacy.Text="legacy";legacy.TextOptions.Size=18;legacy.TextOptions.SizeInPixels=false;legacy.TextOptions.InlineDefaultsApplied=false;legacy.Rectangle=new RectangleF(200,180,180,80);legacy.OnDoubleClicked();Pump();Finish(manager,false);Assert(legacy.TextOptions.Size==18&&!legacy.TextOptions.SizeInPixels,"legacy point size retained");
  var ime=Add(manager,ShapeType.DrawingTextOutline);ime.OnCreating();Pump();var imeEditor=Editor(manager);var start=Message.Create(imeEditor.Handle,0x010D,IntPtr.Zero,IntPtr.Zero);Call(imeEditor,"WndProc",start);Key(imeEditor,Keys.Control|Keys.Enter);Assert(Editing(manager),"IME composition does not submit");var end=Message.Create(imeEditor.Handle,0x010E,IntPtr.Zero,IntPtr.Zero);Call(imeEditor,"WndProc",end);Finish(manager,false);
  Call(manager,"CreateToolbar");Pump();var tools=(ToolStripDropDownButton)Field(manager,"inlineTextTools");Set(manager,"CurrentTool",ShapeType.DrawingTextOutline);Call(manager,"UpdateMenu");Assert(tools.Visible,"text styles available in toolbar");
  var chosen=(TextDrawingShape)Shapes(manager).Last();Set(manager,"CurrentShape",chosen);var old=chosen.TextOptions.Size;Call(manager,"PreviewInlineTextSize",5);Call(manager,"CommitInlineTextSize");Assert(chosen.TextOptions.Size==32,"toolbar changes size");Call(Field(manager,"history"),"Undo");Assert(((TextDrawingShape)Shapes(manager).Last()).TextOptions.Size==old,"toolbar size undo");
  var strip=(ToolStrip)Field(manager,"tsMain");var slider=(Control)Field(manager,"inlineSizeSlider");
  Assert(tools.Name=="capture-text-size"&&!tools.ShowDropDownArrow&&tools.DropDownItems.Count==1&&tools.DropDownItems[0] is ToolStripControlHost,"size only flyout has no font or color options");
  var sizes=(int[])manager.GetType().GetField("TextSizePoints",F)!.GetValue(null)!;Assert(sizes.SequenceEqual(new[]{12,14,16,18,20,24,28,32,36,48}),"ten agreed point sizes");
  Set(manager,"CurrentShape",null!);
  foreach(var type in new[]{ShapeType.DrawingTextOutline,ShapeType.DrawingSpeechBalloon}){Set(manager,"CurrentTool",type);Call(manager,"UpdateMenu");var anchor=strip.Items.Cast<ToolStripItem>().First(i=>i.Tag is ShapeType st&&st==type);Assert(strip.Items.IndexOf(tools)==strip.Items.IndexOf(anchor)+1,"size icon immediately follows "+type);}
  Set(manager,"CurrentTool",ShapeType.DrawingRectangle);Call(manager,"UpdateMenu");Assert(!tools.Visible,"size icon hidden for non text tool");
  var sizing=Add(manager,ShapeType.DrawingTextOutline);sizing.OnCreating();Pump();Editor(manager).Text="字号测试";Finish(manager,true);Set(manager,"CurrentShape",sizing);Call(manager,"UpdateMenu");
  int initialSize=sizing.TextOptions.Size;for(int i=0;i<10;i++)Call(manager,"PreviewInlineTextSize",i);Call(manager,"CommitInlineTextSize");Assert(sizing.TextOptions.Size==64,"slider previews all ten sizes");Call(Field(manager,"history"),"Undo");Assert(((TextDrawingShape)Shapes(manager).Last()).TextOptions.Size==initialSize,"entire slider drag undoes in one operation");
  Call(Field(manager,"history"),"Redo");Set(manager,"CurrentShape",Shapes(manager).Last());Call(manager,"UpdateMenu");var sizeBeforeWheel=((TextDrawingShape)Shapes(manager).Last()).TextOptions.Size;Call(slider,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,20,20,120));Assert(((TextDrawingShape)Shapes(manager).Last()).TextOptions.Size==sizeBeforeWheel,"wheel cannot change font size");
  tools.ShowDropDown();Pump();Assert(slider.Width>=232*slider.DeviceDpi/96&&slider.Height>=82*slider.DeviceDpi/96,"slider scales with DPI");Assert(tools.DropDown.ClientRectangle.Contains(((ToolStripControlHost)tools.DropDownItems[0]).Bounds),"size flyout does not clip hosted slider");
  using(var shot=new Bitmap(tools.DropDown.Width,tools.DropDown.Height)){tools.DropDown.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(output,"font-size-slider.png"));}tools.HideDropDown();Pump();
  var clear=Add(manager,ShapeType.DrawingTextOutline);clear.Rectangle=new RectangleF(250,220,400,160);clear.Text="OLD TEXT SHOULD NOT SHOW";Call(manager,"BeginInlineText",clear,false);Pump();var ce=Editor(manager);var cf=(Control)Field(manager,"inlineTextFrame");
  Assert(ce.BackColor==Color.Transparent&&cf.BackColor==Color.Transparent,"input and frame backgrounds transparent");Assert(clear.TextOptions.AlignmentVertical==StringAlignment.Center,"text vertically centered");
  using(var suppressed=new Bitmap(1200,800)){using(var g=Graphics.FromImage(suppressed))clear.OnDraw(g);Assert(suppressed.GetPixel(260,230).A==0&&Enumerable.Range(0,suppressed.Width).All(x=>Enumerable.Range(220,160).All(y=>suppressed.GetPixel(x,y).A==0)),"original annotation glyphs suppressed while editing");}
  ce.Text="第一行 English\n第二行 MIT";Pump();Assert(ce.Height>=Math.Ceiling(ce.Font.GetHeight(form.DeviceDpi))*2,"multiline editor has full line height");Assert(Math.Abs(ce.Top+ce.Height/2d-cf.Height/2d)<=1,"multiline input block vertically centered");
  ce.Text="";Pump();using(var blank1=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(blank1,new Rectangle(Point.Empty,blank1.Size));ce.Text="测试重影 ABC 123";Pump();ce.Text="";Pump();using var blank2=new Bitmap(form.Width,form.Height);form.DrawToBitmap(blank2,new Rectangle(Point.Empty,blank2.Size));int mismatch=0;var origin=form.PointToScreen(cf.Location);var clientOffset=form.PointToScreen(Point.Empty)-new Size(form.Location);for(int y=ce.Top+8;y<Math.Min(cf.Height-8,ce.Bottom-8);y++)for(int x=80;x<Math.Min(cf.Width-12,300);x++){int sx=cf.Left+x+clientOffset.X,sy=cf.Top+y+clientOffset.Y;if(blank1.GetPixel(sx,sy)!=blank2.GetPixel(sx,sy))mismatch++;}Assert(mismatch==0,"transparent editor erases text without residual glyphs");blank2.Save(Path.Combine(output,"transparent-erased.png"));}
  Finish(manager,false);
  using(var icon=new Bitmap(120,120)){using(var g=Graphics.FromImage(icon)){g.Clear(Color.FromArgb(34,43,51));g.ScaleTransform(4,4);g.TranslateTransform(3,3);var renderer=manager.GetType().Assembly.GetType("ShareX.ScreenCaptureLib.CaptureToolbarRenderer")!;renderer.GetMethod("DrawTextSizeIcon",F)!.Invoke(null,new object[]{g,Color.FromArgb(224,233,240)});}icon.Save(Path.Combine(output,"text-size-icon.png"));}
  foreach(float zoom in new[]{.5f,1f,2f}){
   Set(form,"ZoomFactor",zoom);var s=Add(manager,ShapeType.DrawingTextOutline);s.Rectangle=new RectangleF(200,160,180,60);s.Text="缩放 English";Call(manager,"BeginInlineText",s,false);Pump();var editor=Editor(manager);Assert(Math.Abs(editor.Font.SizeInPoints*editor.DeviceDpi/72-s.TextOptions.Size*zoom)<1,"editor tracks canvas zoom "+zoom);var frame=(Control)Field(manager,"inlineTextFrame");Assert(Math.Abs(frame.Left-(s.Rectangle.X*zoom-4))<=1,"editor position tracks zoom "+zoom);Finish(manager,true);
  }
  Set(form,"ZoomFactor",1f);
  foreach(var screen in Screen.AllScreens){
   var s=Add(manager,ShapeType.DrawingTextOutline);s.OnCreating();Pump();Editor(manager).Text="跨屏 中文 English";
   form.Location=new Point(screen.WorkingArea.Left+40,screen.WorkingArea.Top+40);Pump();
   Assert(Editing(manager),"cross screen retains active edit "+form.DeviceDpi);
   var ed=Editor(manager);Assert(Math.Abs(ed.Font.SizeInPoints*ed.DeviceDpi/72-s.TextOptions.Size)<1,"cross screen editor pixel size "+form.DeviceDpi);
   Finish(manager,true);
  }
  var input=Get(form,"InputManager");
  void Pointer(Point current,Point previous){var currentState=new MouseState();object a=currentState;Set(a,"ClientPosition",current);object b=new MouseState();Set(b,"ClientPosition",previous);Set(input,"mouseState",a);Set(input,"oldMouseState",b);}
  var draggable=Add(manager,ShapeType.DrawingTextOutline);draggable.Text="拖动测试";draggable.Rectangle=new RectangleF(300,250,180,60);Set(manager,"CurrentTool",ShapeType.DrawingRectangle);Set(manager,"NodesVisible",false);
  var cursor=Cursor.Position;try{Cursor.Position=form.PointToScreen(new Point(320,270));Call(manager,"StartRegionSelection");}finally{Cursor.Position=cursor;}
  Assert((bool)Get(manager,"IsMoving"),"press text with another tool starts move");Pointer(new Point(350,290),new Point(320,270));draggable.OnUpdate();Set(manager,"IsMoving",false);Assert(draggable.Rectangle.X==330&&draggable.Rectangle.Y==270,"non editing drag moves text");
  var region=Call(manager,"CreateShape",ShapeType.RegionRectangle);Set(region,"Rectangle",new RectangleF(20,20,800,600));Call(manager,"AddShape",region);Set(manager,"CurrentShape",draggable);
  Call(manager,"form_MouseDoubleClick",form,new MouseEventArgs(MouseButtons.Left,2,340,280,0));Pump();Assert(Editing(manager)&&!(bool)Get(form,"CopyCaptureRequested"),"double click text does not finish valid region");Finish(manager,true);
  var timerShape=Add(manager,ShapeType.DrawingTextOutline);timerShape.OnCreating();Pump();Assert(!(bool)Get(manager,"HasActiveInput"),"idle edit does not request continuous capture frames");Finish(manager,false);
  Shapes(manager).Clear();var preview=Add(manager,ShapeType.DrawingSpeechBalloon);preview.OnCreating();Pump();preview.TextOptions.Size=27;Call(manager,"RefreshInlineTextFont");preview.Move(220-preview.Rectangle.X,160-preview.Rectangle.Y);Editor(manager).Text="在画布直接输入\n微软雅黑 20pt";Pump();using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(output,"inline-preview.png"));}Finish(manager,true);
  File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Checks=checks,Passed=true,Dpi=form.DeviceDpi,OpenMilliseconds=samples,Scope="Isolated native capture editor; IME message lifecycle simulated, actual candidate input requires user check"},new JsonSerializerOptions{WriteIndented=true}));
  Call(form,"CloseWindow",RegionResult.Close);Console.WriteLine("PASS "+checks+" checks");
 }
}
