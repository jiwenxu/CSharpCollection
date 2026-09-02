using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using LotteryPicker.Forms;

namespace LotteryPicker
{
    public class MainForm : Form
    {
        private TabControl _tabs;
        private Button _btnUpdate;
        private Label _lblStatus;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;

        private DrawsTab _drawsTab;
        private RecommendsTab _recommendsTab;
        private RulesTab _rulesTab;
        private QuickGenerateTab _quickTab;

        private Services.LotteryApiClient _api = new Services.LotteryApiClient();

        public MainForm()
        {
            Text = "彩票选号助手";
            Width = 980;
            Height = 640;
            MinimumSize = new System.Drawing.Size(800, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

            // 任务栏/标题栏图标：从程序自身提取嵌入的 lottery.ico
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { /* 图标缺失时使用系统默认 */ }

            // 先初始化数据库（Tab 界面构建时会读取数据）
            Db.Database.Initialize();
            BuildUi();
            Shown += async (s, e) => await RunStartup(true);
        }

        private void BuildUi()
        {
            // 顶部工具栏
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 6) };
            _btnUpdate = new Button
            {
                Text = "更新数据",
                Width = 96,
                Height = 30,
                Dock = DockStyle.Left,
            };
            _btnUpdate.Click += async (s, e) => { await RunUpdate(); };
            toolbar.Controls.Add(_btnUpdate);

            _lblStatus = new Label
            {
                Text = "就绪",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                ForeColor = System.Drawing.Color.Gray,
            };
            toolbar.Controls.Add(_lblStatus);

            // 底部状态栏
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("数据库未初始化");
            _statusStrip.Items.Add(_statusLabel);

            // Tab页
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _drawsTab = new DrawsTab();
            _recommendsTab = new RecommendsTab();
            _rulesTab = new RulesTab();
            _quickTab = new QuickGenerateTab();
            _tabs.TabPages.Add(new TabPage("开奖数据") { Controls = { _drawsTab } });
            _tabs.TabPages.Add(new TabPage("推荐记录") { Controls = { _recommendsTab } });
            _tabs.TabPages.Add(new TabPage("规则管理") { Controls = { _rulesTab } });
            _tabs.TabPages.Add(new TabPage("临时生成") { Controls = { _quickTab } });

            Controls.Add(_tabs);
            Controls.Add(toolbar);
            Controls.Add(_statusStrip);
        }

        /// <summary>
        /// 更新数据：更新开奖 → 检查中奖 → 自动推荐 → 刷新界面。
        /// isStartup=true 表示程序启动触发，受节流保护（10 分钟内已更新则跳过，防频繁请求被封 IP）。
        /// </summary>
        private async Task RunStartup(bool isStartup = false)
        {
            if (isStartup && Db.Database.WithinUpdateThrottle())
            {
                _lblStatus.Text = "数据较新（10 分钟内已更新），本次跳过自动更新";
                RefreshAllTabs();
                SetBusy("就绪");
                return;
            }

            SetBusy("正在更新开奖数据...");
            try
            {
                var result = await Task.Run(() =>
                {
                    int added = _api.UpdateAll();
                    int checkedPrizes = Services.RecommendService.CheckPrizes();
                    int generated = Services.RecommendService.AutoRecommend();
                    return new { added, checkedPrizes, generated };
                });

                _lblStatus.Text = "更新完成：新增 " + result.added + " 期开奖，检查 " + result.checkedPrizes +
                                  " 条推荐，生成 " + result.generated + " 条推荐";
                RefreshAllTabs();
                SetBusy("就绪");
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "更新失败：" + ex.Message;
                SetBusy("就绪");
                RefreshAllTabs();
                MessageBox.Show(this, "数据更新失败：" + ex.Message + "\n请检查网络后重试。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>手动更新按钮</summary>
        private async Task RunUpdate()
        {
            _btnUpdate.Enabled = false;
            await RunStartup();
            _btnUpdate.Enabled = true;
        }

        private void SetBusy(string status)
        {
            _statusLabel.Text = status;
            if (_lblStatus.Text == "就绪" || _lblStatus.Text.StartsWith("更新"))
                _lblStatus.Text = status;
        }

        private void RefreshAllTabs()
        {
            _drawsTab.RefreshData();
            _recommendsTab.RefreshData();
            _rulesTab.RefreshData();
            _quickTab.RefreshData();
        }
    }
}
