using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LotteryPicker.Models;

namespace LotteryPicker.Forms
{
    /// <summary>Tab1 开奖数据：最新开奖展示 + 历史列表 + 彩种筛选</summary>
    public class DrawsTab : UserControl
    {
        private ComboBox _cboLottery;
        private Label _lblLatest;
        private DataGridView _grid;

        public DrawsTab()
        {
            Dock = DockStyle.Fill;
            BuildUi();
        }

        private void BuildUi()
        {
            // 顶部筛选区
            var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(10, 8, 10, 0) };
            _cboLottery = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120,
            };
            _cboLottery.Items.Add("全部彩种");
            foreach (var l in LotteryInfo.All) _cboLottery.Items.Add(l.Name);
            _cboLottery.SelectedIndex = 0;
            _cboLottery.SelectedIndexChanged += (s, e) => RefreshData();
            top.Controls.Add(_cboLottery);

            _lblLatest = new Label
            {
                Text = "最新开奖：--",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 30, 30),
                Location = new Point(150, 12),
            };
            top.Controls.Add(_lblLatest);

            // 数据表格
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
            _grid.Columns.Add("Date", "开奖日期");
            _grid.Columns.Add("Numbers", "号码");
            _grid.Columns["Issue"].FillWeight = 25;
            _grid.Columns["Date"].FillWeight = 30;
            _grid.Columns["Numbers"].FillWeight = 100;

            Controls.Add(_grid);
            Controls.Add(top);
        }

        public void RefreshData()
        {
            string code = _cboLottery.SelectedIndex <= 0 ? "" : LotteryInfo.All[_cboLottery.SelectedIndex - 1].Code;

            _grid.Rows.Clear();
            foreach (var lottery in LotteryInfo.All)
            {
                if (!string.IsNullOrEmpty(code) && lottery.Code != code) continue;

                var draws = Db.DrawRepository.GetAll(lottery.Code);
                var latest = draws.FirstOrDefault();
                if (latest != null && (string.IsNullOrEmpty(code) || lottery.Code == code))
                {
                    _lblLatest.Text = "最新开奖：" + lottery.Name + " " + latest.Issue + " 期  " +
                                      Models.LotteryInfo.Format(latest.Reds.Replace(",", " "), latest.Blues.Replace(",", " "));
                }

                foreach (var d in draws)
                {
                    _grid.Rows.Add(d.Issue, d.DrawDate,
                        Models.LotteryInfo.Format(d.Reds.Replace(",", "  "), d.Blues.Replace(",", "  ")));
                }
            }
        }
    }
}
