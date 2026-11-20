using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Purify
{
    internal sealed class MainWindow : Form
    {
        private static readonly Color Navy = Color.FromArgb(23, 39, 65);
        private static readonly Color Ink = Color.FromArgb(24, 39, 62);
        private static readonly Color Muted = Color.FromArgb(75, 92, 115);
        private static readonly Color Blue = Color.FromArgb(30, 82, 187);
        private static readonly Color Green = Color.FromArgb(26, 108, 79);
        private static readonly Color Amber = Color.FromArgb(140, 79, 15);
        private static readonly Color Red = Color.FromArgb(166, 42, 53);
        private static readonly Color Canvas = Color.FromArgb(237, 242, 250);
        private static readonly Color Field = Color.FromArgb(246, 248, 252);
        private readonly TextBox installPath = new TextBox { AccessibleName = "百度网盘安装目录" };
        private readonly Label compatibility = new Label { AccessibleName = "兼容性结果" };
        private readonly Label count = new Label { AccessibleName = "已选项目数" };
        private readonly Label operation = new Label { AccessibleName = "操作结果" };
        private readonly CheckBox videoPreview = new CheckBox {
            Text = "在网盘内预览视频（取消后下载再打开）", Checked = true, AutoSize = true, Margin = new Padding(0, 0, 0, 8)
        };
        private readonly ListView options = new ListView {
            View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, AccessibleName = "可移除项目", Dock = DockStyle.Fill
        };
        private readonly TextBox details = new TextBox {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "操作日志"
        };
        private readonly Button browse = MakeButton("浏览…");
        private readonly Button check = MakeButton("检查兼容性");
        private readonly Button apply = MakeButton("应用方案", true);
        private readonly Button restore = MakeButton("还原客户端");
        private readonly LinkLabel selectAll = MakeLink("全选");
        private readonly LinkLabel clearAll = MakeLink("全不选");
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Right, Width = 92, Visible = false };
        private ClientDirectory verifiedClient;
        private Scheme verifiedScheme;
        private bool canRestore;
        private bool busy;
        private bool updatingOptions;

        internal MainWindow()
        {
            Text = "百度网盘纯净补丁";
            Font = new Font("Microsoft YaHei UI", 9.5f);
            BackColor = Canvas;
            ForeColor = Ink;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(830, 730);
            MinimumSize = new Size(640, 690);

            var layout = Table();
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var header = Table();
            header.BackColor = Navy;
            header.Padding = new Padding(24, 15, 24, 16);
            header.Controls.Add(new Label {
                Text = Text, ForeColor = Color.White, Font = new Font(Font.FontFamily, 21, FontStyle.Bold), AutoSize = true
            }, 0, 0);
            header.Controls.Add(new Label {
                Text = "检查已安装的客户端，选择要移入备份的项目。",
                ForeColor = Color.FromArgb(206, 219, 241), AutoSize = true, Margin = new Padding(3, 5, 0, 0)
            }, 0, 1);
            layout.Controls.Add(header, 0, 0);

            var body = Table();
            body.Padding = new Padding(22, 16, 22, 16);
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(ClientCard(), 0, 0);
            body.Controls.Add(OptionsCard(), 0, 1);
            body.Controls.Add(ActivityCard(), 0, 2);
            layout.Controls.Add(body, 0, 1);
            Controls.Add(layout);

            installPath.TextChanged += (sender, e) => ResetClient();
            browse.Click += ChooseDirectory;
            check.Click += async (sender, e) => await CheckClient();
            apply.Click += async (sender, e) => await Execute(false);
            restore.Click += async (sender, e) => await Execute(true);
            selectAll.LinkClicked += (sender, e) => SetSelection(true);
            clearAll.LinkClicked += (sender, e) => SetSelection(false);
            options.ItemChecked += (sender, e) => { if (!updatingOptions) UpdateCount(); };
            options.SizeChanged += (sender, e) => ResizeColumns();
            FormClosing += (sender, e) => { if (busy) e.Cancel = true; };
            Shown += DetectClient;
            ResetClient();
        }

        private TableLayoutPanel ClientCard()
        {
            var card = Card(true);
            card.Controls.Add(Heading("客户端目录"), 0, 0);
            var row = Table(3);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            var border = new Panel {
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(9, 0, 9, 0),
                BackColor = Field, BorderStyle = BorderStyle.FixedSingle
            };
            installPath.BorderStyle = BorderStyle.None;
            installPath.BackColor = Field;
            installPath.ForeColor = Ink;
            installPath.Dock = DockStyle.Fill;
            // A single-line text box keeps its preferred height, so center it in the taller input frame.
            border.Resize += (sender, e) => {
                int top = Math.Max(0, (border.ClientSize.Height - installPath.PreferredHeight) / 2);
                border.Padding = new Padding(9, top, 9, 0);
            };
            border.Controls.Add(installPath);
            row.Controls.Add(border, 0, 0);
            row.Controls.Add(browse, 1, 0);
            row.Controls.Add(check, 2, 0);
            card.Controls.Add(row, 0, 1);
            FormatCaption(compatibility, new Padding(0, 8, 0, 0));
            card.Controls.Add(compatibility, 0, 2);
            return card;
        }

        private TableLayoutPanel OptionsCard()
        {
            var card = Card(true);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var row = Table(3);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(Heading("可移除项目"), 0, 0);
            row.Controls.Add(selectAll, 1, 0);
            row.Controls.Add(clearAll, 2, 0);
            card.Controls.Add(row, 0, 1);

            options.Columns.Add("项目", 235);
            options.Columns.Add("文件位置", 460);
            options.Margin = Padding.Empty;
            options.ForeColor = Ink;
            options.BorderStyle = BorderStyle.FixedSingle;
            card.Controls.Add(videoPreview, 0, 0);
            card.Controls.Add(options, 0, 2);
            FormatCaption(count, new Padding(0, 8, 0, 0));
            card.Controls.Add(count, 0, 3);
            return card;
        }

        private TableLayoutPanel ActivityCard()
        {
            var card = Card(false);
            var row = Table(2);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            var status = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            FormatCaption(operation, Padding.Empty);
            operation.AutoEllipsis = true;
            operation.AutoSize = false;
            operation.TextAlign = ContentAlignment.MiddleLeft;
            operation.Dock = DockStyle.Fill;
            status.Controls.Add(operation);
            status.Controls.Add(progress);
            row.Controls.Add(status, 0, 0);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            actions.Controls.Add(restore);
            actions.Controls.Add(apply);
            row.Controls.Add(actions, 1, 0);
            card.Controls.Add(row, 0, 0);
            var label = Heading("操作日志");
            label.Margin = new Padding(0, 10, 0, 6);
            card.Controls.Add(label, 0, 1);
            details.BackColor = Field;
            details.ForeColor = Ink;
            details.Margin = Padding.Empty;
            details.Height = 83;
            card.Controls.Add(details, 0, 2);
            return card;
        }

        private async void DetectClient(object sender, EventArgs e)
        {
            try
            {
                var detected = ClientDirectory.Detect();
                if (detected == null) return;
                installPath.Text = detected.Root;
                await CheckClient();
            }
            catch (Exception ex)
            {
                Program.Log(ex.ToString());
                compatibility.Text = "未能自动识别安装目录，请手动选择。";
            }
        }

        private async void ChooseDirectory(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog {
                Description = "选择包含 BaiduNetdisk.exe 的目录", ShowNewFolderButton = false, SelectedPath = installPath.Text
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                installPath.Text = dialog.SelectedPath;
                await CheckClient();
            }
        }

        private async Task CheckClient()
        {
            ResetClient();
            string path = installPath.Text.Trim().Trim('"');
            string version = null;
            compatibility.Text = "正在检查客户端和可用方案…";
            SetBusy(true);
            try
            {
                var client = new ClientDirectory(path);
                version = client.Version;
                var state = await Task.Run(() => new ClientState(client));
                verifiedClient = client;
                canRestore = state.Record != null;
                var scheme = await Task.Run(() => new SchemeSource().Resolve(state.OriginalHash));
                if (scheme == null)
                {
                    SetStatus(compatibility, version + "  ·  暂无匹配方案", Amber);
                    Report("方案索引中没有该构建。原版 core.asar SHA-256 为 " + state.OriginalHash);
                    return;
                }
                var components = await Task.Run(() => state.InspectComponents(scheme));
                PopulateOptions(components, canRestore && state.Record.registryBackedUp);
                verifiedScheme = scheme;
                videoPreview.Checked = !canRestore || state.Record.downloadVideos != true;
                videoPreview.Visible = scheme.VideoDownloadRules != null;
                SetStatus(compatibility, version + (scheme.IsLocal ? "  ·  已匹配本地内置方案" : "  ·  已匹配 GitHub 签名方案"), Green);
                Report("已校验方案 " + scheme.Entry.name + "，可以选择要移除的项目。");
            }
            catch (Exception ex)
            {
                Program.Log(ex.ToString());
                SetStatus(compatibility, (version == null ? "" : version + "  ·  ") + ex.Message, Red);
                Report(ex.Message);
            }
            finally { SetBusy(false); }
        }

        private void PopulateOptions(IEnumerable<KeyValuePair<string, bool>> components, bool openWithRemoved)
        {
            updatingOptions = true;
            options.BeginUpdate();
            try
            {
                foreach (var component in components)
                {
                    string name = Path.GetFileName(component.Key);
                    string label;
                    var item = new ListViewItem(Names.TryGetValue(name, out label) ? label : name) { Tag = component.Key };
                    item.SubItems.Add(component.Key).ForeColor = Muted;
                    item.UseItemStyleForSubItems = false;
                    options.Items.Add(item);
                    item.Checked = component.Value;
                }
                var windows = new ListViewItem("Windows「打开方式」");
                windows.UseItemStyleForSubItems = false;
                windows.SubItems.Add("当前用户的图片查看器和播放器注册").ForeColor = Muted;
                options.Items.Add(windows);
                windows.Checked = openWithRemoved;
            }
            finally
            {
                options.EndUpdate();
                updatingOptions = false;
                UpdateCount();
            }
        }

        private async Task Execute(bool restoring)
        {
            var client = verifiedClient;
            var scheme = verifiedScheme;
            var checkedItems = options.CheckedItems.Cast<ListViewItem>().ToList();
            var selections = checkedItems.Select(item => item.Tag as string).Where(path => path != null).ToList();
            bool removeOpenWith = checkedItems.Any(item => item.Tag == null);
            bool downloadVideos = scheme != null && scheme.VideoDownloadRules != null && !videoPreview.Checked;
            if (!restoring && ApplyService.HasVideoPreviewConflict(scheme, selections, downloadVideos))
            {
                MessageBox.Show(this, ApplyService.VideoPreviewConflict, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string action = restoring ? "还原客户端" : "应用方案";
            string summary = restoring ? "" : "\n已勾选 " + checkedItems.Count + " 个移除项目。";
            if (MessageBox.Show(this, "操作时会关闭该目录下运行的百度网盘。请先完成上传和下载。" + summary + "\n\n继续" + action + "？",
                action, MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return;

            SetStatus(operation, restoring ? "正在还原…" : "正在应用…", Blue);
            SetBusy(true);
            IProgress<string> feedback = new Progress<string>(Report);
            try
            {
                bool complete = await Task.Run(() =>
                {
                    var service = new ApplyService(message =>
                    {
                        Program.Log(message);
                        feedback.Report(message);
                    }, true);
                    return restoring ? service.Restore(client) : service.Apply(client, scheme, selections, removeOpenWith, downloadVideos);
                });
                if (complete && restoring)
                {
                    SetSelection(false);
                    videoPreview.Checked = true;
                }
                SetStatus(operation, complete ? (restoring ? "客户端已还原" : "方案已应用") : "部分操作未完成，请查看日志",
                    complete ? Green : Amber);
            }
            catch (Exception ex)
            {
                Program.Log(ex.ToString());
                Report(ex.Message);
                SetStatus(operation, "操作中止，请查看日志", Red);
                MessageBox.Show(this, ex.Message + "\n\n日志位于 " + Program.LogFile, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                canRestore = File.Exists(client.Manifest);
                SetBusy(false);
            }
        }

        private void ResetClient()
        {
            verifiedClient = null;
            verifiedScheme = null;
            canRestore = false;
            videoPreview.Visible = false;
            options.Items.Clear();
            UpdateCount();
            SetStatus(compatibility, "选择安装目录后检查兼容性。", Muted);
            SetStatus(operation, "应用方案会安装核心补丁。", Muted);
            UpdateActions();
        }

        private void UpdateCount()
        {
            count.Text = options.Items.Count == 0
                ? "匹配方案后显示可选项目。"
                : "已选 " + options.CheckedItems.Count + " / " + options.Items.Count + "  ·  取消勾选并应用，可放回已备份的项目。";
        }

        private void SetSelection(bool selected)
        {
            updatingOptions = true;
            options.BeginUpdate();
            try { foreach (ListViewItem item in options.Items) item.Checked = selected; }
            finally
            {
                options.EndUpdate();
                updatingOptions = false;
                UpdateCount();
            }
        }

        private void Report(string message)
        {
            details.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            installPath.ReadOnly = value;
            progress.Visible = value;
            progress.Style = value ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            UpdateActions();
        }

        private void UpdateActions()
        {
            browse.Enabled = !busy;
            check.Enabled = !busy && !string.IsNullOrWhiteSpace(installPath.Text);
            apply.Enabled = !busy && verifiedScheme != null;
            restore.Enabled = !busy && canRestore;
            options.Enabled = selectAll.Enabled = clearAll.Enabled = apply.Enabled;
            videoPreview.Enabled = apply.Enabled;
        }

        private void ResizeColumns()
        {
            int width = options.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
            if (width <= 0) return;
            options.Columns[0].Width = width * 34 / 100;
            options.Columns[1].Width = width - options.Columns[0].Width;
        }

        private static void SetStatus(Label label, string message, Color color)
        {
            label.Text = message;
            label.ForeColor = color;
        }

        private static TableLayoutPanel Table(int columns = 1)
        {
            var table = new TableLayoutPanel {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty, ColumnCount = columns
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static TableLayoutPanel Card(bool spaced)
        {
            var card = Table();
            card.BackColor = Color.White;
            card.Padding = new Padding(16, 12, 16, 12);
            card.Margin = new Padding(0, 0, 0, spaced ? 12 : 0);
            return card;
        }

        private Label Heading(string text)
        {
            return new Label {
                Text = text, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 8)
            };
        }

        private static void FormatCaption(Label label, Padding margin)
        {
            label.AutoSize = true;
            label.ForeColor = Muted;
            label.Margin = margin;
        }

        private static Button MakeButton(string text, bool primary = false)
        {
            var button = new Button {
                Text = text, AutoSize = true, MinimumSize = new Size(86, 35), FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, primary ? 0 : 7, 0), Padding = new Padding(8, 1, 8, 1),
                UseVisualStyleBackColor = false, BackColor = primary ? Blue : Field, ForeColor = primary ? Color.White : Ink
            };
            button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(191, 204, 222);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(22, 64, 151) : Canvas;
            button.EnabledChanged += (sender, e) => {
                button.BackColor = primary && button.Enabled ? Blue : Field;
                button.ForeColor = primary && button.Enabled ? Color.White : Ink;
                button.FlatAppearance.BorderColor = primary && button.Enabled ? Blue : Color.FromArgb(191, 204, 222);
            };
            return button;
        }

        private static LinkLabel MakeLink(string text)
        {
            return new LinkLabel { Text = text, AutoSize = true, LinkColor = Blue, Margin = new Padding(10, 2, 0, 0) };
        }

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "ImageViewer", "图片查看器" }, { "AiEngine", "AI 引擎" }, { "SpeechSDK", "语音组件" },
            { "VastPlayer", "内置视频播放器" }, { "BaiduNetdiskPlayerLaunch.exe", "视频播放器启动程序" },
            { "localplayer.dll", "本地播放库" }, { "vastplayer.dll", "视频播放库" }, { "videoipc.dll", "播放进程通信库" },
            { "syncengine.dll", "同步引擎" }, { "entsyncengine.dll", "企业同步引擎" },
            { "video_player.asar", "视频播放界面" }, { "5bc5a1fc2a45d4cb1415284e9905af6b", "视频播放器资源" },
            { "YunOfficeAddin.dll", "Office 插件（32 位）" }, { "YunOfficeAddin64.dll", "Office 插件（64 位）" },
            { "netdisk-chrome-ext.crx", "Chrome 扩展" }, { "BugReport.exe", "故障报告程序" },
            { "HelpUtility.exe", "帮助程序" }
        };
    }
}
