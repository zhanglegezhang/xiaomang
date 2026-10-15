using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;
using ShareX.Properties;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private sealed class TodoPageComponent : WidgetPageComponent
        {

            internal readonly Panel inputPanel;
            internal readonly Label titleLabel;
            internal readonly Button toggleCompletedButton;
            internal readonly TextBox todoTextBox;
            internal readonly CheckBox reminderCheckBox;
            internal readonly DateTimePicker reminderPicker;
            internal readonly ComboBox reminderHour;
            internal readonly ComboBox reminderMinute;
            internal readonly Label timeSeparator;
            internal readonly Panel schedulePanel;
            internal readonly Label validationLabel;
            internal bool choosingSchedule;
            internal readonly Button addButton;
            internal readonly VirtualClipboardPanel todoList;
            internal readonly SlimListScrollBar todoSlimScrollBar;
            internal readonly Label emptyLabel;
            internal readonly Font todoItemFont;
            internal readonly Font todoCompletedFont;
            internal readonly Font todoReminderFont;
            internal readonly Font voiceDurationFont;
            internal readonly List<Panel> todoRowPool = new List<Panel>();
            internal readonly Dictionary<Guid, VirtualRowMetric> todoMetricCache = new Dictionary<Guid, VirtualRowMetric>();
            internal TodoItem[] todoViewItems = Array.Empty<TodoItem>();
            internal int[] todoRowOffsets = Array.Empty<int>();
            internal int[] todoRowHeights = Array.Empty<int>();
            internal int todoVirtualRowWidth;
            internal bool updatingTodoViewport;
            internal bool showCompleted;

            internal void TodoTextBox_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && e.Control)
                {
                    AddTodo();
                    e.SuppressKeyPress = true;
                }
            }

            internal void AddTodo()
            {
                string text = todoTextBox.Text.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    todoTextBox.Focus();
                    return;
                }

                DateTime? reminder = reminderCheckBox.Checked ? SelectedReminderTime() : null;
                if (reminder.HasValue && reminder.Value <= DateTime.Now)
                {
                    validationLabel.Visible = true;
                    Owner.LayoutContent();
                    return;
                }
                TodoItem item = new TodoItem
                {
                    Text = text,
                    ReminderAt = reminder
                };
                Owner.Settings.Items.Add(item);
                Owner.WriteNoteLog(NoteLogKind.Todo, "新增", item.Text, item.CreatedAt);

                todoTextBox.Clear();
                reminderCheckBox.Checked = false;
                ResetReminderTime();
                Owner.SaveAndRender();
                todoList.AutoScrollPosition = Point.Empty;
                UpdateTodoViewport();
                todoTextBox.Focus();
            }

            internal ComboBox CreateTimeCombo(int count, string name)
            {
                ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name, MaxDropDownItems = 12, IntegralHeight = false };
                combo.Items.AddRange(Enumerable.Range(0, count).Select(i => (object)i.ToString("00")).ToArray());
                combo.DropDown += (s, e) => { choosingSchedule = true; Owner.collapseTimer.Stop(); };
                combo.DropDownClosed += (s, e) => { choosingSchedule = false; };
                return combo;
            }

            internal DateTime SelectedReminderTime() => DateTime.SpecifyKind(reminderPicker.Value.Date
                .AddHours(reminderHour.SelectedIndex).AddMinutes(reminderMinute.SelectedIndex), DateTimeKind.Local);

            internal void ReminderSelectionChanged(object sender, EventArgs e)
            {
                if (validationLabel.Visible && reminderCheckBox.Checked && SelectedReminderTime() > DateTime.Now)
                {
                    validationLabel.Visible = false;
                    Owner.LayoutContent();
                }
            }

            internal void ResetReminderTime()
            {
                DateTime next = DateTime.Today.AddDays(1).AddHours(10);
                reminderPicker.Value = next.Date;
                reminderHour.SelectedIndex = next.Hour;
                reminderMinute.SelectedIndex = next.Minute;
            }

            internal void RenderItems()
            {
                todoViewItems = Owner.Settings.Items.Where(x => showCompleted || !x.IsCompleted)
                    .OrderBy(x => x.IsCompleted).ThenByDescending(x => x.CreatedAt).ToArray();
                HashSet<Guid> currentIds = todoViewItems.Select(x => x.Id).ToHashSet();
                foreach (Guid staleId in todoMetricCache.Keys.Where(x => !currentIds.Contains(x)).ToArray())
                {
                    todoMetricCache.Remove(staleId);
                }
                toggleCompletedButton.Text = showCompleted ? "隐藏已办" : "展示已办";
                Owner.UpdateCompactText();
                // Keep data and the compact badge current, but measure rows only at the
                // expanded viewport width. Hidden pages are refreshed on their next visit.
                if (!Owner.expanded || Owner.Settings.ActiveModule != Module) return;
                RebuildTodoMetrics(todoViewItems, todoList.ClientSize);

                if (todoViewItems.Length == 0)
                {
                    foreach (Panel row in todoRowPool) row.Visible = false;
                    emptyLabel.Text = showCompleted ? "暂无待办" : "没有未办事项";
                    emptyLabel.Bounds = new Rectangle(0, 0, Math.Max(Owner.D(80), todoList.ClientSize.Width - (todoList.HasVerticalScroll ? Owner.D(12) : 0)), Owner.D(54));
                    if (!todoList.Controls.Contains(emptyLabel)) todoList.Controls.Add(emptyLabel);
                    emptyLabel.Visible = true;
                }
                else
                {
                    emptyLabel.Visible = false;
                    UpdateTodoViewport();
                }
            }

            internal void RebuildTodoMetrics(TodoItem[] items, Size viewportSize)
            {
                if (items.Length == 0 || viewportSize.Width <= 0 || viewportSize.Height <= 0)
                {
                    todoRowOffsets = Array.Empty<int>();
                    todoRowHeights = Array.Empty<int>();
                    todoVirtualRowWidth = Math.Max(Owner.D(120), viewportSize.Width);
                    todoList.AutoScrollMinSize = Size.Empty;
                    Owner.LayoutSlimScrollBar(todoList, todoSlimScrollBar);
                    return;
                }

                int minimumRowHeight = Math.Min(todoItemFont.Height, todoCompletedFont.Height) + Owner.D(4) + Owner.D(14);
                bool mustScroll = (long)items.Length * minimumRowHeight + (long)(items.Length - 1) * Owner.D(7) > viewportSize.Height;
                int rowWidth = Math.Max(Owner.D(120), viewportSize.Width - (mustScroll ? Owner.D(12) : 0));
                CalculateTodoMetrics(items, rowWidth, out int[] offsets, out int[] heights, out int totalHeight);
                if (!mustScroll && totalHeight > viewportSize.Height)
                {
                    rowWidth = Math.Max(Owner.D(120), viewportSize.Width - Owner.D(12));
                    CalculateTodoMetrics(items, rowWidth, out offsets, out heights, out totalHeight);
                }
                todoVirtualRowWidth = rowWidth;
                todoRowOffsets = offsets;
                todoRowHeights = heights;
                todoList.AutoScrollMinSize = new Size(0, Math.Max(0, totalHeight));
                Owner.LayoutSlimScrollBar(todoList, todoSlimScrollBar);
            }

            internal void CalculateTodoMetrics(TodoItem[] items, int rowWidth, out int[] offsets, out int[] heights, out int totalHeight)
            {
                WidgetListLayout.Calculate(items.Length, Owner.D(7), i =>
                {
                    TodoItem item = items[i];
                    bool hasVoice = !string.IsNullOrWhiteSpace(item.VoiceAudioRelativePath);
                    int textWidth = Math.Max(Owner.D(50), rowWidth - Owner.D(28) - Owner.D(8) - (hasVoice ? Owner.D(62) : 0));
                    Font font = item.IsCompleted ? todoCompletedFont : todoItemFont;
                    todoMetricCache.TryGetValue(item.Id, out VirtualRowMetric previous);
                    bool Matches(VirtualRowMetric candidate) => candidate != null && candidate.Text == item.Text &&
                        candidate.State == item.IsCompleted && candidate.HasVoice == hasVoice && candidate.EditorWidth == textWidth &&
                        candidate.MaximumEditorHeight == (item.ReminderAt.HasValue ? 1 : 0);
                    VirtualRowMetric metric = previous;
                    if (!Matches(metric) && Matches(previous?.Alternate))
                    {
                        metric = previous.Alternate;
                        previous.Alternate = null;
                        metric.Alternate = previous;
                        todoMetricCache[item.Id] = metric;
                    }
                    if (!Matches(metric))
                    {
                        Size measured = TextRenderer.MeasureText(string.IsNullOrEmpty(item.Text) ? " " : item.Text, font,
                            new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                        int editorHeight = Math.Max(font.Height + Owner.D(4), measured.Height + Owner.D(4));
                        int reminderHeight = item.ReminderAt.HasValue ? todoReminderFont.Height + Owner.D(5) : 0;
                        if (previous != null) previous.Alternate = null;
                        metric = new VirtualRowMetric
                        {
                            Alternate = previous,
                            Id = item.Id,
                            Text = item.Text,
                            State = item.IsCompleted,
                            HasVoice = hasVoice,
                            EditorWidth = textWidth,
                            MaximumEditorHeight = item.ReminderAt.HasValue ? 1 : 0,
                            EditorHeight = editorHeight,
                            RowHeight = Owner.D(14) + editorHeight + reminderHeight
                        };
                        todoMetricCache[item.Id] = metric;
                    }
                    return metric.RowHeight;
                }, out offsets, out heights, out totalHeight);
            }

            internal void UpdateTodoViewport()
            {
                if (Owner.changingExpansion || !Owner.expanded || Owner.Settings.ActiveModule != "Todo") return;
                if (updatingTodoViewport || todoViewItems.Length == 0 || todoList.ClientSize.Height <= 0) return;
                updatingTodoViewport = true;
                try
                {
                    int scrollY = Math.Max(0, -todoList.AutoScrollPosition.Y);
                    TodoWidgetForm.GetVisibleRange(todoRowOffsets, todoRowHeights, scrollY, todoList.ClientSize.Height, Owner.D(80), out int start, out int end);
                    int needed = Math.Max(0, end - start);
                    while (todoRowPool.Count < needed)
                    {
                        Panel row = CreateTodoRow();
                        todoRowPool.Add(row);
                        todoList.Controls.Add(row);
                    }

                    HashSet<Panel> used = new HashSet<Panel>();
                    for (int index = start; index < end; index++)
                    {
                        TodoItem item = todoViewItems[index];
                        Panel row = todoRowPool.FirstOrDefault(x => !used.Contains(x) && x.Tag is Guid id && id == item.Id) ??
                            todoRowPool.First(x => !used.Contains(x));
                        used.Add(row);
                        BindTodoRow(row, item);
                        row.Bounds = new Rectangle(0, todoRowOffsets[index] - scrollY, todoVirtualRowWidth, todoRowHeights[index]);
                        LayoutTodoRow(row, item);
                        row.Visible = true;
                    }
                    WidgetListLayout.HideUnusedRows(todoRowPool, used);
                }
                finally
                {
                    updatingTodoViewport = false;
                }
            }



            internal Panel CreateTodoRow()
            {
                Panel row = new FavoriteRowPanel { BackColor = TodoWidgetForm.WidgetRowColor, Margin = Padding.Empty };
                CheckBox completedCheckBox = new CheckBox { BackColor = Color.Transparent };
                completedCheckBox.CheckedChanged += (sender, e) =>
                {
                    if (updatingTodoViewport || row.Tag is not Guid id) return;
                    TodoItem item = Owner.Settings.Items.FirstOrDefault(x => x.Id == id);
                    if (item == null || item.IsCompleted == completedCheckBox.Checked) return;
                    item.IsCompleted = completedCheckBox.Checked;
                    item.CompletedAt = item.IsCompleted ? DateTime.Now : null;
                    Owner.WriteNoteLog(NoteLogKind.Todo, item.IsCompleted ? "标记已办" : "恢复未办", item.Text);
                    Owner.reminderService.Remove(item.Id);
                    Owner.SaveAndRender();
                };
                TextBox editor = new TextBox
                {
                    ReadOnly = true,
                    ShortcutsEnabled = true,
                    Multiline = true,
                    WordWrap = true,
                    ScrollBars = ScrollBars.None,
                    BorderStyle = BorderStyle.None,
                    BackColor = row.BackColor,
                    TextAlign = HorizontalAlignment.Left,
                    Cursor = Cursors.IBeam,
                    AccessibleName = "待办内容，可选择并复制"
                };
                Label reminderLabel = new Label { Name = "ReminderLabel", BackColor = Color.Transparent };
                RoundedIconButton voiceButton = new RoundedIconButton
                {
                    Name = "VoicePlaybackButton",
                    BackColor = TodoWidgetForm.WidgetRowColor,
                    ForeColor = ShareXResources.Theme.TextColor,
                    SoftBorderColor = Color.FromArgb(82, 82, 82),
                    CornerRadius = 4,
                    Image = Owner.sharedSpeakerIcon,
                    AccessibleName = "播放录音",
                    TabStop = false
                };
                voiceButton.Click += (sender, e) =>
                {
                    if (row.Tag is Guid id) Owner.ToggleVoicePlayback(id);
                };
                Label voiceDurationLabel = new Label
                {
                    Name = "VoiceDurationLabel",
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(190, ShareXResources.Theme.TextColor),
                    Font = voiceDurationFont,
                    TextAlign = ContentAlignment.MiddleRight,
                    AutoEllipsis = false
                };
                row.Controls.Add(completedCheckBox);
                row.Controls.Add(editor);
                row.Controls.Add(reminderLabel);
                row.Controls.Add(voiceButton);
                row.Controls.Add(voiceDurationLabel);
                Owner.AttachHoverEvents(row);
                Owner.AttachListWheel(row, todoList);
                Owner.AttachListWheel(voiceButton, todoList);
                Owner.AttachListWheel(voiceDurationLabel, todoList);
                return row;
            }

            internal void BindTodoRow(Panel row, TodoItem item)
            {
                CheckBox checkBox = row.Controls.OfType<CheckBox>().Single();
                TextBox editor = row.Controls.OfType<TextBox>().Single();
                Label reminderLabel = row.Controls.OfType<Label>().Single(x => x.Name == "ReminderLabel");
                Button voiceButton = row.Controls.OfType<Button>().Single(x => x.Name == "VoicePlaybackButton");
                Label voiceDurationLabel = row.Controls.OfType<Label>().Single(x => x.Name == "VoiceDurationLabel");
                bool hasVoice = !string.IsNullOrWhiteSpace(item.VoiceAudioRelativePath);
                row.Tag = item.Id;
                checkBox.Checked = item.IsCompleted;
                editor.Text = item.Text;
                editor.ReadOnly = true;
                editor.Font = item.IsCompleted ? todoCompletedFont : todoItemFont;
                editor.ForeColor = item.IsCompleted ? Color.Gray : ShareXResources.Theme.TextColor;
                reminderLabel.Visible = item.ReminderAt.HasValue;
                reminderLabel.Text = item.ReminderAt.HasValue ? "提醒：" + item.ReminderAt.Value.ToString("MM-dd HH:mm") : "";
                reminderLabel.Font = todoReminderFont;
                reminderLabel.ForeColor = Color.FromArgb(190, ShareXResources.Theme.TextColor);
                voiceButton.Visible = hasVoice;
                voiceButton.Image = Owner.playingVoiceItemId == item.Id ? Owner.sharedStopIcon : Owner.sharedSpeakerIcon;
                voiceButton.AccessibleName = Owner.playingVoiceItemId == item.Id ? "停止播放录音" : "播放录音";
                voiceDurationLabel.Visible = hasVoice;
                voiceDurationLabel.Text = hasVoice ? Math.Clamp(item.VoiceDurationSeconds, 1, 60) + "s" : "";
                voiceDurationLabel.ForeColor = item.IsCompleted ? Color.Gray : Color.FromArgb(190, ShareXResources.Theme.TextColor);
            }

            internal void LayoutTodoRow(Panel row, TodoItem item)
            {
                int textLeft = Owner.D(28);
                bool hasVoice = !string.IsNullOrWhiteSpace(item.VoiceAudioRelativePath);
                int textWidth = Math.Max(Owner.D(50), row.ClientSize.Width - textLeft - Owner.D(8) - (hasVoice ? Owner.D(62) : 0));
                VirtualRowMetric metric = todoMetricCache[item.Id];
                CheckBox checkBox = row.Controls.OfType<CheckBox>().Single();
                TextBox editor = row.Controls.OfType<TextBox>().Single();
                Label reminderLabel = row.Controls.OfType<Label>().Single(x => x.Name == "ReminderLabel");
                Button voiceButton = row.Controls.OfType<Button>().Single(x => x.Name == "VoicePlaybackButton");
                Label voiceDurationLabel = row.Controls.OfType<Label>().Single(x => x.Name == "VoiceDurationLabel");
                checkBox.Bounds = new Rectangle(Owner.D(5), Owner.D(8), Owner.D(20), Owner.D(20));
                editor.Bounds = new Rectangle(textLeft, Owner.D(7), textWidth, metric.EditorHeight);
                reminderLabel.Bounds = new Rectangle(textLeft, editor.Bottom, textWidth, todoReminderFont.Height + Owner.D(5));
                int durationWidth = TextRenderer.MeasureText(voiceDurationLabel.Text, voiceDurationLabel.Font).Width;
                voiceDurationLabel.Bounds = new Rectangle(row.ClientSize.Width - Owner.D(8) - durationWidth, Owner.D(7), durationWidth, Owner.D(22));
                voiceButton.Bounds = new Rectangle(voiceDurationLabel.Left - Owner.D(3) - Owner.D(22), voiceDurationLabel.Top, Owner.D(22), Owner.D(22));
            }

            /* The original eager row builder is retained here only as historical context for the
               virtualized implementation above.
            internal Control CreateTodoRowLegacy(TodoItem item)
            {
                int rowWidth = Math.Max(100, todoList.ClientSize.Width - (todoList.HasVerticalScroll ? D(12) : 0));
                int textLeft = D(28);
                int textWidth = Math.Max(50, rowWidth - textLeft - D(8));
                Font textFont = new Font(Font.FontFamily, 8.5f, item.IsCompleted ? FontStyle.Strikeout : FontStyle.Regular);
                Size measuredTextSize = TextRenderer.MeasureText(item.Text, textFont, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                int textHeight = Math.Max(TextRenderer.MeasureText("待办", textFont).Height + 4, measuredTextSize.Height + 4);
                int reminderHeight = item.ReminderAt.HasValue ? TextRenderer.MeasureText("提醒", Font).Height + 5 : 0;
                int rowHeight = 14 + textHeight + reminderHeight;

                Panel row = new Panel
                {
                    Tag = item.Id,
                    Size = new Size(rowWidth, rowHeight),
                    Margin = new Padding(0, 0, 0, 7),
                    BackColor = WidgetRowColor
                };

                CheckBox completedCheckBox = new CheckBox
                {
                    Location = new Point(D(5), 8),
                    Size = new Size(D(20), D(20)),
                    Checked = item.IsCompleted,
                    BackColor = Color.Transparent
                };
                completedCheckBox.CheckedChanged += (sender, e) =>
                {
                    item.IsCompleted = completedCheckBox.Checked;
                    item.CompletedAt = item.IsCompleted ? DateTime.Now : null;
                    WriteNoteLog(NoteLogKind.Todo, item.IsCompleted ? "标记已办" : "恢复未办", item.Text);
                    reminderService.Remove(item.Id);
                    SaveAndRender();
                };
                row.Controls.Add(completedCheckBox);

                TextBox textLabel = new TextBox
                {
                    Location = new Point(textLeft, 7),
                    Size = new Size(textWidth, textHeight),
                    Text = item.Text,
                    ReadOnly = true,
                    ShortcutsEnabled = true,
                    Multiline = true,
                    WordWrap = true,
                    ScrollBars = ScrollBars.None,
                    BorderStyle = BorderStyle.None,
                    ForeColor = item.IsCompleted ? Color.Gray : ShareXResources.Theme.TextColor,
                    Font = textFont,
                    BackColor = row.BackColor,
                    TextAlign = HorizontalAlignment.Left,
                    Cursor = Cursors.IBeam
                };
                textLabel.AccessibleName = "待办内容，可选择并复制";
                row.Controls.Add(textLabel);

                if (item.ReminderAt.HasValue)
                {
                    Label reminderLabel = new Label
                    {
                        Location = new Point(textLeft, 7 + textHeight),
                        Size = new Size(textWidth, reminderHeight),
                        Text = "提醒：" + item.ReminderAt.Value.ToString("MM-dd HH:mm"),
                        ForeColor = Color.FromArgb(190, ShareXResources.Theme.TextColor),
                        Font = new Font(Font.FontFamily, 7.2f),
                        BackColor = Color.Transparent
                    };
                    row.Controls.Add(reminderLabel);
                }

                AttachHoverEvents(row);
                AttachListWheel(row, todoList);
                row.MouseEnter += Widget_MouseEnter;
                row.MouseLeave += Widget_MouseLeave;
                return row;
            }
            */


            internal readonly PictureBox todoTitleLogo;

            internal Panel contentPanel => this;

            internal void LayoutTodoPanel()
            {
                int padding = Owner.D(8), gap = Owner.D(5);
                int contentWidth = Math.Max(100, contentPanel.ClientSize.Width - padding * 2);
                int buttonWidth = TextRenderer.MeasureText("隐藏已办", toggleCompletedButton.Font).Width + Owner.D(12);
                int todoEditorHeight = todoTextBox.Font.Height * 4 + Owner.D(4);
                int textHeight = todoEditorHeight + Owner.D(4);
                int titleWidth = TextRenderer.MeasureText(titleLabel.Text, titleLabel.Font).Width + Owner.D(8);
                Owner.LayoutPageTitle(todoTitleLogo, titleLabel, titleWidth);
                int titleTop = titleLabel.Top, titleHeight = titleLabel.Height;
                toggleCompletedButton.Bounds = new Rectangle(padding + contentWidth - buttonWidth, titleTop, buttonWidth, titleHeight);
                int checkWidth = reminderCheckBox.GetPreferredSize(Size.Empty).Width + Owner.D(4);
                reminderCheckBox.Bounds = new Rectangle(padding, titleLabel.Bottom + gap, checkWidth, Owner.D(22));

                int inputTop = reminderCheckBox.Bottom + gap;
                if (reminderCheckBox.Checked)
                {
                    int scheduleLeft = reminderCheckBox.Right + gap;
                    int scheduleWidth = Math.Max(Owner.D(1), padding + contentWidth - scheduleLeft);
                    int separatorWidth = Owner.D(8);
                    int comboWidth = TextRenderer.MeasureText("00", reminderHour.Font).Width + Owner.D(12);
                    int dateWidth = Math.Min(TextRenderer.MeasureText("2026-09-12", reminderPicker.Font).Width + Owner.D(18),
                        scheduleWidth - comboWidth * 2 - separatorWidth - gap);
                    dateWidth = Math.Max(Owner.D(64), dateWidth);
                    int scheduleHeight = Math.Max(reminderPicker.PreferredHeight, reminderHour.PreferredHeight);
                    int scheduleTop = reminderCheckBox.Top + Math.Max(0, (reminderCheckBox.Height - scheduleHeight) / 2);
                    schedulePanel.Bounds = new Rectangle(scheduleLeft, scheduleTop, scheduleWidth, scheduleHeight);
                    reminderPicker.Bounds = new Rectangle(0, 0, dateWidth, reminderPicker.PreferredHeight);
                    reminderHour.Bounds = new Rectangle(dateWidth + gap, 0, comboWidth, scheduleHeight);
                    timeSeparator.Bounds = new Rectangle(reminderHour.Right, 0, separatorWidth, scheduleHeight);
                    reminderMinute.Bounds = new Rectangle(timeSeparator.Right, 0, comboWidth, scheduleHeight);
                    inputTop = Math.Max(reminderCheckBox.Bottom, schedulePanel.Bottom) + gap;
                }
                inputPanel.Bounds = new Rectangle(padding, inputTop, contentWidth, textHeight);
                todoTextBox.Bounds = new Rectangle(Owner.D(2), Owner.D(2), inputPanel.ClientSize.Width - Owner.D(4), todoEditorHeight);
                int addWidth = Math.Max(Owner.D(68), TextRenderer.MeasureText(addButton.Text, addButton.Font).Width + Owner.D(18));
                int addHeight = Math.Max(Owner.D(25), addButton.Font.Height + Owner.D(8));
                addButton.Bounds = new Rectangle(inputPanel.ClientSize.Width - addWidth - Owner.D(6),
                    inputPanel.ClientSize.Height - addHeight - Owner.D(6), addWidth, addHeight);
                validationLabel.Bounds = new Rectangle(Owner.D(2), inputPanel.ClientSize.Height - addHeight - Owner.D(6),
                    Math.Max(1, addButton.Left - Owner.D(4)), addHeight);
                todoTextBox.SendToBack();
                validationLabel.BringToFront();
                addButton.BringToFront();
                int listTop = inputPanel.Bottom + gap;
                todoList.Bounds = new Rectangle(padding, listTop, contentWidth, Math.Max(20, contentPanel.ClientSize.Height - listTop - padding));
                Owner.LayoutSlimScrollBar(todoList, todoSlimScrollBar);
                emptyLabel.Width = Math.Max(50, todoList.ClientSize.Width - (todoList.HasVerticalScroll ? Owner.D(12) : 0));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    todoItemFont.Dispose();
                    todoCompletedFont.Dispose();
                    todoReminderFont.Dispose();
                    voiceDurationFont.Dispose();
                }
                base.Dispose(disposing);
            }

            internal TodoPageComponent(TodoWidgetForm owner) : base(owner, "Todo")
            {
                Padding = new Padding(12);
                todoItemFont = new Font(Font.FontFamily, 8.5f);
                todoCompletedFont = new Font(Font.FontFamily, 8.5f, FontStyle.Strikeout);
                todoReminderFont = new Font(Font.FontFamily, 7.2f);
                voiceDurationFont = new Font(Font.FontFamily, 7.5f);
                titleLabel = new Label
                {
                    Location = new Point(12, 12),
                    Size = new Size(115, 28),
                    Text = "待办事项",
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                titleLabel.Cursor = Cursors.SizeNS;
                titleLabel.MouseDown += Owner.TitleLabel_MouseDown;
                titleLabel.MouseMove += Owner.TitleLabel_MouseMove;
                titleLabel.MouseUp += Owner.TitleLabel_MouseUp;
                contentPanel.Controls.Add(titleLabel);
                todoTitleLogo = Owner.CreateTitleLogo(contentPanel);

                toggleCompletedButton = Owner.CreateButton("展示已办", new Point(244, 12), new Size(112, 28));
                toggleCompletedButton.BackColor = TodoWidgetForm.WidgetBackgroundColor;
                toggleCompletedButton.ForeColor = TodoWidgetForm.HalfWhiteColor;
                toggleCompletedButton.FlatAppearance.BorderSize = 0;
                toggleCompletedButton.Click += (sender, e) =>
                {
                    showCompleted = !showCompleted;
                    RenderItems();
                };
                contentPanel.Controls.Add(toggleCompletedButton);

                inputPanel = new RoundedBorderPanel
                {
                    BorderStyle = BorderStyle.None,
                    BackColor = TodoWidgetForm.WidgetInputColor,
                    BorderColor = TodoWidgetForm.WidgetBorderColor,
                    CornerRadius = 7
                };
                contentPanel.Controls.Add(inputPanel);
                todoTextBox = new TextBox
                {
                    Location = new Point(12, 52),
                    Size = new Size(344, 62),
                    Font = new Font(Font.FontFamily, 9f),
                    PlaceholderText = "添加待办内容",
                    Multiline = true,
                    AcceptsReturn = true,
                    ScrollBars = ScrollBars.None,
                    BackColor = TodoWidgetForm.WidgetInputColor,
                    ForeColor = ShareXResources.Theme.TextColor,
                    BorderStyle = BorderStyle.None
                };
                todoTextBox.KeyDown += TodoTextBox_KeyDown;
                inputPanel.Controls.Add(todoTextBox);

                reminderCheckBox = new CheckBox
                {
                    Location = new Point(12, 122),
                    Size = new Size(70, 25),
                    Text = "提醒",
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = Color.Transparent
                };
                reminderCheckBox.CheckedChanged += (sender, e) =>
                {
                    schedulePanel.Visible = reminderCheckBox.Checked;
                    validationLabel.Visible = false;
                    Owner.LayoutContent();
                };
                contentPanel.Controls.Add(reminderCheckBox);

                schedulePanel = new Panel { Visible = false, BackColor = TodoWidgetForm.WidgetInputColor };
                contentPanel.Controls.Add(schedulePanel);

                reminderPicker = new DateTimePicker
                {
                    Location = new Point(84, 121),
                    Size = new Size(177, 26),
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = "yyyy-MM-dd",
                    ShowUpDown = false,
                    Value = DateTime.Now.AddHours(1),
                    AccessibleName = "提醒日期"
                };
                schedulePanel.Controls.Add(reminderPicker);
                reminderPicker.DropDown += (s, e) => { choosingSchedule = true; Owner.collapseTimer.Stop(); };
                reminderPicker.CloseUp += (s, e) => { choosingSchedule = false; };
                reminderHour = CreateTimeCombo(24, "提醒小时");
                reminderMinute = CreateTimeCombo(60, "提醒分钟");
                timeSeparator = new Label { Text = ":", TextAlign = ContentAlignment.MiddleCenter, ForeColor = ShareXResources.Theme.TextColor };
                schedulePanel.Controls.Add(reminderHour);
                schedulePanel.Controls.Add(timeSeparator);
                schedulePanel.Controls.Add(reminderMinute);
                ResetReminderTime();
                validationLabel = new Label { Visible = false, ForeColor = Color.Orange, Text = "请选择未来的提醒时间", AutoSize = false };
                inputPanel.Controls.Add(validationLabel);
                reminderPicker.ValueChanged += ReminderSelectionChanged;
                reminderHour.SelectedIndexChanged += ReminderSelectionChanged;
                reminderMinute.SelectedIndexChanged += ReminderSelectionChanged;

                addButton = Owner.CreateCompactAddButton("添加待办内容");
                addButton.Click += (sender, e) => AddTodo();
                inputPanel.Controls.Add(addButton);

                todoList = Owner.CreateVirtualClipboardList();
                todoList.Location = new Point(12, 160);
                todoList.Size = new Size(344, 256);
                todoList.ViewportChanged += (sender, e) => UpdateTodoViewport();
                /*todoList = new DarkFlowLayoutPanel
                {
                    Location = new Point(12, 160),
                    Size = new Size(344, 256),
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = WidgetBackgroundColor
                };*/
                contentPanel.Controls.Add(todoList);
                todoSlimScrollBar = new SlimListScrollBar(todoList, Owner.D(12));
                todoList.Controls.Add(todoSlimScrollBar);

                emptyLabel = new Label
                {
                    AutoSize = false,
                    Size = new Size(315, 50),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = Color.Transparent,
                    Margin = Padding.Empty
                };

                todoTextBox.Leave += (sender, e) => Owner.ScheduleCollapseIfPointerOutside();
            }

            internal override void ArrangePage() => LayoutTodoPanel();
            internal override void RefreshPage() { RenderItems(); }
        }
    }
}
