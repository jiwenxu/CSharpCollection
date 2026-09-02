using System;
using System.Drawing;
using System.Windows.Forms;
using LotteryPicker.Models;

namespace LotteryPicker.Forms
{
    /// <summary>Tab2 推荐记录：每期推荐 + 中奖结果，可按彩种筛选，选中行可复制号码</summary>
    public class RecommendsTab : UserControl
    {
        private ComboBox _cboLottery;
        private Button _btnCopy;
        private DataGridView _grid;

        public RecommendsTab()
        {
            Dock = DockStyle.Fill;
            BuildUi();
            RefreshData();
        }

        private void BuildUi()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(10, 8, 10, 0) };
            _cboLottery = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            _cboLottery.Items.Add("全部彩种");
            foreach (var l in LotteryInfo.All) _cboLottery.Items.Add(l.Name);
            _cboLottery.SelectedIndex = 0;
            _cboLottery.SelectedIndexChanged += (s, e) => RefreshData();
            top.Controls.Add(_cboLottery);

            _btnCopy = new Button { Text = "复制选中号码", Width = 120, Location = new Point(140, 4) };
            _btnCopy.Click += (s, e) => CopySelected();
            top.Controls.Add(_btnCopy);

            var tip = new Label
            {
                Text = "提示：选中一行后点击按钮，或双击任意一行，即可复制该注号码",
                AutoSize = true,
                ForeColor = Color.Gray,
                Location = new Point(280, 10),
            };
            top.Controls.Add(tip);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
            };
            _grid.Columns.Add("Issue", "期号");
            _grid.Columns.Add("Lottery", "彩种");
            _grid.Columns.Add("Rule", "规则");
            _grid.Columns.Add("Numbers", "号码");
            _grid.Columns.Add("Prize", "中奖");
            _grid.Columns.Add("Created", "生成时间");
            _grid.Columns["Issue"].FillWeight = 15;
            _grid.Columns["Lottery"].FillWeight = 10;
            _grid.Columns["Rule"].FillWeight = 10;
            _grid.Columns["Numbers"].FillWeight = 55;
            _grid.Columns["Prize"].FillWeight = 12;
            _grid.Columns["Created"].FillWeight = 25;
            _grid.CellDoubleClick += (s, e) => CopySelected();

            Controls.Add(_grid);
            Controls.Add(top);
        }

        public void RefreshData()
        {
            string code = _cboLottery.SelectedIndex <= 0 ? "" : LotteryInfo.All[_cboLottery.SelectedIndex - 1].Code;
            var recs = Db.RecommendRepository.GetAll(code);

            _grid.Rows.Clear();
            foreach (var rec in recs)
            {
                string lotteryName = LotteryInfo.Get(rec.LotteryCode).Name;
                _grid.Rows.Add(rec.Issue, lotteryName, rec.RuleName,
                    Models.LotteryInfo.Format(rec.Reds.Replace(",", "  "), rec.Blues.Replace(",", "  ")),
                    rec.PrizeLevel, rec.CreatedAt);
            }
        }

        private void CopySelected()
        {
            if (_grid.SelectedRows.Count == 0) return;
            string numbers = Convert.ToString(_grid.SelectedRows[0].Cells["Numbers"].Value);
            if (string.IsNullOrEmpty(numbers)) return;
            Clipboard.SetText(numbers.Replace("  ", " "));
            MessageBox.Show(this, "号码已复制到剪贴板：\n" + numbers, "复制成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
