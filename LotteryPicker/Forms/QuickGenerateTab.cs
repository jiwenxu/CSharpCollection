using System;
using System.Drawing;
using System.Windows.Forms;
using LotteryPicker.Models;

namespace LotteryPicker.Forms
{
    /// <summary>Tab4 临时生成：按规则生成号码，可复制，不落库</summary>
    public class QuickGenerateTab : UserControl
    {
        private ComboBox _cboRule;
        private ComboBox _cboLottery;
        private Button _btnGenerate;
        private Label _lblResult;
        private Button _btnCopy;

        private string _lastNumbers = "";

        public QuickGenerateTab()
        {
            Dock = DockStyle.Fill;
            BuildUi();
            RefreshData();
        }

        private void BuildUi()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            var lblRule = new Label { Text = "规则：", AutoSize = true, Location = new Point(20, 24) };
            _cboRule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Location = new Point(70, 20) };

            var lblLottery = new Label { Text = "彩种：", AutoSize = true, Location = new Point(250, 24) };
            _cboLottery = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Location = new Point(300, 20) };

            _btnGenerate = new Button { Text = "生成号码", Width = 100, Height = 30, Location = new Point(440, 18) };
            _btnGenerate.Click += (s, e) => Generate();

            _lblResult = new Label
            {
                Text = "点击\"生成号码\"获取推荐（不保存到数据库）",
                AutoSize = false,
                Width = 600,
                Height = 80,
                Font = new Font("Consolas", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 90, 180),
                Location = new Point(20, 80),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _btnCopy = new Button { Text = "复制号码", Width = 100, Height = 30, Location = new Point(20, 180) };
            _btnCopy.Click += (s, e) => CopyNumbers();

            var tip = new Label
            {
                Text = "说明：临时生成的号码仅用于查看/复制，不会写入推荐记录；\n推荐记录仅在\"每期开奖前\"由激活的规则自动生成。",
                AutoSize = true,
                ForeColor = Color.Gray,
                Location = new Point(20, 230),
            };

            panel.Controls.Add(lblRule);
            panel.Controls.Add(_cboRule);
            panel.Controls.Add(lblLottery);
            panel.Controls.Add(_cboLottery);
            panel.Controls.Add(_btnGenerate);
            panel.Controls.Add(_lblResult);
            panel.Controls.Add(_btnCopy);
            panel.Controls.Add(tip);

            Controls.Add(panel);
        }

        public void RefreshData()
        {
            string prevRule = _cboRule.SelectedItem as string;
            string prevLottery = _cboLottery.SelectedItem as string;

            _cboRule.Items.Clear();
            foreach (var gen in Services.RuleEngine.All) _cboRule.Items.Add(gen.Name);
            if (prevRule != null && _cboRule.Items.Contains(prevRule)) _cboRule.SelectedItem = prevRule;
            else if (_cboRule.Items.Count > 0) _cboRule.SelectedIndex = 0;

            _cboLottery.Items.Clear();
            foreach (var l in LotteryInfo.All) _cboLottery.Items.Add(l.Name);
            if (prevLottery != null && _cboLottery.Items.Contains(prevLottery)) _cboLottery.SelectedItem = prevLottery;
            else if (_cboLottery.Items.Count > 0) _cboLottery.SelectedIndex = 0;
        }

        private void Generate()
        {
            if (_cboRule.SelectedIndex < 0 || _cboLottery.SelectedIndex < 0) return;

            var gen = Services.RuleEngine.All[_cboRule.SelectedIndex];
            var lottery = LotteryInfo.All[_cboLottery.SelectedIndex];
            var recent = Db.DrawRepository.GetRecent(lottery.Code, Services.FrequencyRuleBase.STAT_WINDOW);
            var result = gen.Generate(lottery, recent);

            _lastNumbers = Models.LotteryInfo.Format(result.RedsText.Replace(",", " "), result.BluesText.Replace(",", " "));
            _lblResult.Text = _lastNumbers;
        }

        private void CopyNumbers()
        {
            if (string.IsNullOrEmpty(_lastNumbers)) return;
            Clipboard.SetText(_lastNumbers);
            MessageBox.Show(this, "号码已复制到剪贴板：\n" + _lastNumbers, "复制成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
