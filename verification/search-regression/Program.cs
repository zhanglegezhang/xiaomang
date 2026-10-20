using System.Collections;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

static class Check
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Assembly assembly;
    static int count;
    static Type T(string name) => assembly.GetType("ShareX." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F).GetValue(o);
    static void Set(object o, string name, object value) { var field = o.GetType().GetField(name, F); if (field != null) field.SetValue(o, value); else o.GetType().GetProperty(name, F).SetValue(o, value); }
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static void Assert(bool pass, string text) { if (!pass) throw new Exception(text); Console.WriteLine("PASS " + text); count++; }
    static void Pump() => Application.DoEvents();
    static void Until(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { if (clock.ElapsedMilliseconds > 12000) throw new TimeoutException("Search did not finish"); Pump(); Thread.Sleep(3); } Pump(); }
    static object Hit(string source, string title, string text, string path = null) => Activator.CreateInstance(T("SearchHit"), source, title, text, Guid.NewGuid(), path);
    static Array Hits(params object[] hits) { var result = Array.CreateInstance(T("SearchHit"), hits.Length); for (int i = 0; i < hits.Length; i++) result.SetValue(hits[i], i); return result; }
    static void Key(Control control, Keys key) => Call(control, "OnKeyDown", new KeyEventArgs(key));
    static void Mouse(Control c, string method, Point p, MouseButtons button = MouseButtons.Left) => Call(c, method, new MouseEventArgs(button, 1, p.X, p.Y, 0));

    [STAThread] static int Main(string[] args)
    {
        try
        {
            string runtime = Path.GetFullPath(args[0]), report = Path.GetFullPath(args[1]); Directory.CreateDirectory(report);
            string dependencies = args.Contains("--daily-dependencies") ? Path.GetFullPath("tools/ShareX/app") : runtime;
            AssemblyLoadContext.Default.Resolving += (_, name) => { string path = Path.Combine(dependencies, name.Name + ".dll"); return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null; };
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime, "ShareX.dll"));
            Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles(); Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            object config = Activator.CreateInstance(T("ApplicationConfig"));
            T("Program").GetProperty("Settings", F).SetValue(null, config);
            T("Program").GetProperty("DefaultTaskSettings", F).SetValue(null, Get(config, "DefaultTaskSettings"));
            T("Program").GetProperty("CustomPersonalPath", F).SetValue(null, Path.Combine(report, "isolated-personal"));
            string notes = Path.Combine((string)T("LocalDiaryManager").GetProperty("DataFolder", F).GetValue(null), "notes");
            Directory.CreateDirectory(Path.Combine(notes, "工作", "年度计划"));
            File.WriteAllText(Path.Combine(notes, "工作", "年度计划", "第4季度计划.md"), "# 第4季度计划\n完成第 4 版方案，整理项目记录。\n![截图](diary-asset:fixture-4.png)");
            foreach (string title in new[] { "项目复盘", "会议纪要", "阅读摘记" }) File.WriteAllText(Path.Combine(notes, title + ".md"), $"{title}：第 4 阶段需要核对。\n正文 alpha");
            object todo = Get(config, "TodoSettings");
            var tasks = (IList)Get(todo, "Items");
            foreach (string text in new[] { "整理第4版设计稿", "核对4项功能", "更新项目说明第4部分" }) { object item = Activator.CreateInstance(T("TodoItem")); Set(item, "Text", text); tasks.Add(item); }
            var favorites = (IList)Get(todo, "FavoriteItems"); object favorite = Activator.CreateInstance(T("FavoriteTextItem")); Set(favorite, "Text", "常用开发资料第4版"); favorites.Add(favorite);
            var clips = (IList)Get(todo, "ClipboardItems"); object clip = Activator.CreateInstance(T("ClipboardTextItem")); Set(clip, "Text", "剪切板测试4"); clips.Add(clip);
            using var form = (Form)Activator.CreateInstance(T("UnifiedSearchForm"), true);
            form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); Pump();
            var input = (TextBox)Get(form, "query"); var clipboard = (CheckBox)Get(form, "clipboard"); var view = (Control)Get(form, "results"); var status = (Label)Get(form, "status");
            int D(int value) => (int)Math.Round(value * form.DeviceDpi / 96d);
            Assert(!clipboard.Checked, "clipboard history remains disabled by default");
            Assert((int)Get(view, "HitCount") == 0, "empty query opens without searching personal data");
            var clock = Stopwatch.StartNew(); input.Text = "4"; Until(() => status.Text.StartsWith("显示 "));
            double firstSearch = clock.Elapsed.TotalMilliseconds;
            Assert((int)Get(view, "HitCount") == 8, "real async search retains notes, tasks and favorites");
            Assert(status.Text.Contains("最多 300 条") && ((Label)Get(form, "elapsed")).Text.EndsWith("毫秒"), "count, result limit and measured time remain visible");
            var rows = (IList)Get(view, "rows");
            Assert(rows.Cast<object>().Where(r => Get(r, "Hit") == null).Sum(r => (int)Get(r, "Count")) == 8, "group count badges describe the visible results");
            Assert(rows.Cast<object>().Any(r => (string)Get(r, "Title") == "第4季度计划.md" && ((string)Get(r, "Detail")).Contains("年度计划")), "local filenames and paths displayed on separate text levels");
            string plain = (string)T("UnifiedSearchResultsView").GetMethod("PlainSummary", F).Invoke(null, new object[] { "## **第4版** | ![截图](diary-asset:private-id) [资料](https://example.com)" });
            Assert(plain.Contains("第4版") && plain.Contains("[图片]") && !plain.Contains("diary-asset") && !plain.Contains("**") && !plain.Contains("|"), "preview summaries remove raw Markdown image addresses and table pipes");
            plain = (string)T("UnifiedSearchResultsView").GetMethod("PlainSummary", F).Invoke(null, new object[] { "第4版 ![截图](diary-asset:truncated-id" });
            Assert(plain == "第4版 [图片]", "truncated image links do not leak asset addresses into the summary");
            Assert(((string)Get(rows.Cast<object>().First(r => Get(r, "Hit") != null), "Snippet")).Length <= 180, "summary text remains bounded");
            var inputPanel = (Control)Get(form, "input");
            foreach (int width in new[] { 520, 950, 1200 })
            {
                form.Width = D(width); Pump();
                Assert(inputPanel.Right < clipboard.Left && clipboard.Right <= form.ClientSize.Width && view.Width == form.ClientSize.Width, $"{width}px logical window fills both columns without search-option overlap");
                var footer = (Control)Get(form, "footer"); Assert(view.Bottom <= footer.Top && view.Top >= ((Control)Get(form, "top")).Bottom, $"{width}px window keeps header, results and status separated");
            }
            form.Size = new Size(D(950), D(620)); Pump();
            Key(input, Keys.Down);
            Assert(view.Focused && Get(view, "SelectedHit") != null, "Down from query focuses the first selectable result");
            clipboard.Checked = true; Until(() => status.Text.StartsWith("显示 "));
            Assert((int)Get(view, "HitCount") == 9, "optional clipboard history joins the existing result sources");
            clipboard.Checked = false; input.Text = "alpha"; input.Text = "4"; Until(() => status.Text.StartsWith("显示 "));
            Assert((int)Get(view, "HitCount") == 8 && (string)Get(view, "term") == "4", "fast typing and cancellation cannot show an older query");
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(report, "search-ui.png"), ImageFormat.Png); }
            Call(Get(inputPanel, "clear"), "PerformClick"); Pump();
            Assert(input.Text.Length == 0 && (int)Get(view, "HitCount") == 0 && ((Label)Get(form, "elapsed")).Text.Length == 0, "clear button resets query, results and timing");
            input.Text = "no-such-fixture-234567"; Until(() => status.Text.StartsWith("显示 "));
            Assert((int)Get(view, "HitCount") == 0 && ((string)Get(view, "EmptyMessage")).Contains("没有找到"), "no matches show a readable empty state");
            input.Text = "4"; form.Close(); Pump();
            Assert(form.IsDisposed, "closing with scheduled search does not retain the window");

            using var host = new Form { Opacity = 0, ShowInTaskbar = false, ClientSize = new Size(D(850), D(450)) };
            var list = (Control)Activator.CreateInstance(T("UnifiedSearchResultsView"), true); list.Dock = DockStyle.Fill; list.Font = new Font("Microsoft YaHei UI", 9f); host.Controls.Add(list); host.Show(); Pump();
            object[] data = Enumerable.Range(0, 300).Select(i => Hit(i < 100 ? "本地笔记" : i < 200 ? "待办" : "手动收藏", i < 100 ? $"工作/较长文件夹名称/第4季度项目资料_{i}_长标题用于省略显示.md" : $"测试第4项 {i}", "第4版用于验证滚动及匹配高亮，完整正文仍由原入口打开。", $"工作/第4季度_{i}.md")).ToArray();
            var engine = T("UnifiedSearchEngine").GetMethod("Search", F);
            object engineResult = engine.Invoke(null, new object[] { Hits(data), notes, "第4", CancellationToken.None });
            Assert(((Array)Get(engineResult, "Item1")).Length == 300, "search engine still enforces the 300-result limit");
            engineResult = engine.Invoke(null, new object[] { Hits(data), notes, "ALPHA", CancellationToken.None });
            Assert(((Array)Get(engineResult, "Item1")).Length == 3, "note-body matching remains case insensitive");
            bool cancelled = false; try { engine.Invoke(null, new object[] { Hits(data), notes, "4", new CancellationToken(true) }); } catch (TargetInvocationException error) when (error.InnerException is OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "cancelled searches stop before rendering stale data");
            clock.Restart(); Call(list, "SetHits", Hits(data), "4"); Pump(); double bindMs = clock.Elapsed.TotalMilliseconds;
            Assert((int)Get(list, "HitCount") == 300 && list.Controls.Count == 0, "300 results use one painted control, not hundreds of child controls");
            Assert((int)Get(list, "contentHeight") < D(300 * 49 + 3 * 30) * .8 && Math.Abs(list.Font.SizeInPoints - 9f) < .01,
                "result layout reduces vertical space by over 20 percent while retaining font size");
            int invoked = 0;
            list.GetType().GetEvent("ResultInvoked", F).GetAddMethod(true).Invoke(list, new object[] { new EventHandler((_, _) => invoked++) });
            Call(list, "FocusFirst"); Key(list, Keys.Down); Assert(ReferenceEquals(Get(list, "SelectedHit"), data[1]), "Down selects the next hit");
            Key(list, Keys.Up); Assert(ReferenceEquals(Get(list, "SelectedHit"), data[0]), "Up selects the previous hit");
            Key(list, Keys.End); Assert(ReferenceEquals(Get(list, "SelectedHit"), data[^1]) && (int)Get(list, "scrollOffset") > 0, "End reaches the last result and scrolls it into view");
            Key(list, Keys.PageUp); Assert(!ReferenceEquals(Get(list, "SelectedHit"), data[^1]), "PageUp advances by the visible result count");
            Key(list, Keys.Home); Assert(ReferenceEquals(Get(list, "SelectedHit"), data[0]), "Home returns to the first result");
            Key(list, Keys.Enter); Assert(invoked == 1, "Enter invokes exactly the selected opening target");
            Mouse(list, "OnMouseDown", new Point(D(70), D(45))); Assert(invoked == 1, "group headers do not invoke an unrelated result");
            Mouse(list, "OnMouseDown", new Point(D(70), D(65))); Assert(invoked == 1, "mouse press selects without opening before release");
            Mouse(list, "OnMouseUp", new Point(D(70), D(65))); Assert(invoked == 2 && ReferenceEquals(Get(list, "SelectedHit"), data[0]), "left click invokes the clicked result after release");
            var scrollTimes = new List<double>();
            using var frame = new Bitmap(list.Width, list.Height);
            for (int i = 0; i < 20; i++) { clock.Restart(); Call(list, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120)); list.DrawToBitmap(frame, new Rectangle(Point.Empty, frame.Size)); scrollTimes.Add(clock.Elapsed.TotalMilliseconds); }
            Assert((int)Get(list, "scrollOffset") > 0, "mouse wheel scrolls the rendered result list");
            var thumb = (Rectangle)Get(list, "ScrollThumb");
            Mouse(list, "OnMouseDown", new Point(thumb.Left + thumb.Width / 2, thumb.Top + thumb.Height / 2));
            Mouse(list, "OnMouseMove", new Point(thumb.Left + thumb.Width / 2, list.Height - D(12)));
            Mouse(list, "OnMouseUp", new Point(thumb.Left + thumb.Width / 2, list.Height - D(12)));
            Assert((int)Get(list, "scrollOffset") == (int)Get(list, "MaximumScroll"), "thin scrollbar thumb can be dragged to the bottom");
            Assert(invoked == 2, "scrollbar interaction never opens a result");
            host.Width = D(520); Pump(); Assert((int)Get(list, "scrollOffset") <= (int)Get(list, "MaximumScroll"), "resize clamps the scroll offset");
            Call(list, "SetHits", Hits(), "4"); Assert(Get(list, "SelectedHit") == null && (int)Get(list, "scrollOffset") == 0, "new empty results remove stale selection and scroll offset");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(runtime, "ShareX.dll"))));
            File.WriteAllText(Path.Combine(report, "result.json"), JsonSerializer.Serialize(new { Checks = count, Dpi = form.DeviceDpi, Hash = hash, FirstAsyncSearchIncludingDebounceMs = firstSearch, Bind300Ms = bindMs, MedianScrollRenderMs = scrollTimes.OrderBy(x => x).ElementAt(scrollTimes.Count / 2), MaxScrollRenderMs = scrollTimes.Max(), Scope = "Compiled product with isolated fictional data, real asynchronous search and offscreen control rendering; no live private data or low-end-PC benchmark" }, new JsonSerializerOptions { WriteIndented = true }));
            host.Close();
            T("Program").GetProperty("MainForm", F).SetValue(null, null);
            Console.WriteLine($"RESULT {count} checks passed; DPI {form.DeviceDpi}; bind300 {bindMs:F1}ms"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
