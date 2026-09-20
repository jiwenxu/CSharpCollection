using System;
using System.Drawing;
using System.Windows.Forms;

namespace LotteryPicker.Forms
{
    /// <summary>
    /// 通用分页条：每页条数 + 首页/上一页/下一页/末页 + 页码跳转。
    /// 使用方通过 Reset/SetTotal 传入总条数，并在 PageChanged 事件中渲染当前页数据。
    /// </summary>
    public class PagerBar : Panel
    {
        private ComboBox _cboSize;
        private Button _btnFirst, _btnPrev, _btnNext, _btnLast;
        private Label _lblInfo, _lblTotal;
        private TextBox _txtJump;
        private int _total;
        private bool _loading = true;

        /// <summary>翻页或改变每页条数时触发</summary>
        public event EventHandler PageChanged;

        /// <summary>当前页码（从 1 开始）</summary>
        public int CurrentPage { get; private set; }

        /// <summary>每页条数</summary>
        public int PageSize
        {
            get { return _cboSize.SelectedItem == null ? 50 : Convert.ToInt32(_cboSize.SelectedItem); }
        }

        /// <summary>总页数（无数据时记为 1 页）</summary>
        public int TotalPages
        {
            get { return _total <= 0 ? 1 : (_total + PageSize - 1) / PageSize; }
        }

        public PagerBar()
        {
            Dock = DockStyle.Bottom;
            Height = 40;
            Padding = new Padding(10, 6, 10, 6);
            CurrentPage = 1;

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoScroll = false,
            };

            flow.Controls.Add(MakeLabel("每页", 0, 8));

            _cboSize = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 64,
                Margin = new Padding(2, 3, 14, 0),
            };
            _cboSize.Items.AddRange(new object[] { 20, 50, 100, 200 });
            _cboSize.SelectedIndex = 1;
            _cboSize.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                CurrentPage = 1;
                RaiseChanged();
            };
            flow.Controls.Add(_cboSize);

            _btnFirst = MakeButton("首页", 0, delegate { GoTo(1); });
            _btnPrev = MakeButton("上一页", 0, delegate { GoTo(CurrentPage - 1); });
            _btnNext = MakeButton("下一页", 0, delegate { GoTo(CurrentPage + 1); });
            _btnLast = MakeButton("末页", 0, delegate { GoTo(TotalPages); });

            _lblInfo = MakeLabel("第 1 / 1 页", 10, 9);
            _lblInfo.Margin = new Padding(10, 9, 10, 0);

            flow.Controls.Add(_btnFirst);
            flow.Controls.Add(_btnPrev);
            flow.Controls.Add(_lblInfo);
            flow.Controls.Add(_btnNext);
            flow.Controls.Add(_btnLast);

            flow.Controls.Add(MakeLabel("跳至", 16, 9));
            _txtJump = new TextBox { Width = 52, Margin = new Padding(2, 3, 2, 0) };
            _txtJump.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                int page;
                if (int.TryParse(_txtJump.Text.Trim(), out page)) GoTo(page);
                _txtJump.Text = "";
            };
            flow.Controls.Add(_txtJump);
            flow.Controls.Add(MakeLabel("页", 2, 9));

            _lblTotal = MakeLabel("共 0 条", 16, 9);

            Controls.Add(flow);

            _loading = false;
            UpdateState();
        }

        private static Label MakeLabel(string text, int leftMargin, int topMargin)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(leftMargin, topMargin, 0, 0),
            };
        }

        private static Button MakeButton(string text, int leftMargin, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Width = 64,
                Height = 26,
                Margin = new Padding(leftMargin, 1, 4, 0),
            };
            btn.Click += onClick;
            return btn;
        }

        /// <summary>设置总条数并回到第 1 页（查询条件变化时调用）</summary>
        public void Reset(int total)
        {
            CurrentPage = 1;
            _total = Math.Max(0, total);
            RaiseChanged();
        }

        /// <summary>更新总条数，尽量保持当前页码（数据刷新时调用）</summary>
        public void SetTotal(int total)
        {
            _total = Math.Max(0, total);
            RaiseChanged();
        }

        private void GoTo(int page)
        {
            int p = Math.Max(1, Math.Min(TotalPages, page));
            if (p == CurrentPage) return;
            CurrentPage = p;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = 1;
            UpdateState();

            var handler = PageChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void UpdateState()
        {
            _lblInfo.Text = "第 " + CurrentPage + " / " + TotalPages + " 页";
            _lblTotal.Text = "共 " + _total + " 条";
            _btnFirst.Enabled = _btnPrev.Enabled = CurrentPage > 1;
            _btnNext.Enabled = _btnLast.Enabled = CurrentPage < TotalPages;
        }
    }
}
