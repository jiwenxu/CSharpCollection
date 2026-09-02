using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LotteryPicker.Models;

namespace LotteryPicker.Forms
{
    /// <summary>Tab3 规则管理：启用开关 + 关联彩种勾选（多对多），修改即时保存</summary>
    public class RulesTab : UserControl
    {
        private Panel _listPanel;
        private bool _loading;

        public RulesTab()
        {
            Dock = DockStyle.Fill;
            BuildUi();
            RefreshData();
        }

        private void BuildUi()
        {
            var tip = new Label
            {
                Text = "勾选启用后，每期开奖前将自动为该规则关联的彩种生成推荐号码；修改即时保存。",
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = Color.Gray,
                Padding = new Padding(10, 6, 0, 0),
            };

            _listPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            Controls.Add(_listPanel);
            Controls.Add(tip);
        }

        public void RefreshData()
        {
            _loading = true;
            _listPanel.Controls.Clear();
            var rules = Db.RuleRepository.GetAll();
            int y = 10;
            foreach (var rule in rules)
            {
                var box = BuildRuleRow(rule, y);
                _listPanel.Controls.Add(box);
                y += box.Height + 6;
            }
            _listPanel.Height = y + 10;
            _loading = false;
        }

        private Panel BuildRuleRow(Rule rule, int y)
        {
            var row = new Panel
            {
                Location = new Point(10, y),
                Width = _listPanel.Width - 40,
                Height = 78,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            };

            var chkEnabled = new CheckBox
            {
                Text = rule.Name,
                Checked = rule.Enabled,
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(10, 8),
                AutoSize = true,
                Tag = rule.Id,
            };
            chkEnabled.CheckedChanged += (s, e) =>
                OnEnabledChanged((long)((CheckBox)s).Tag, ((CheckBox)s).Checked);
            row.Controls.Add(chkEnabled);

            var lbl = new Label
            {
                Text = "适用彩种：",
                AutoSize = true,
                ForeColor = Color.Gray,
                Location = new Point(10, 40),
            };
            row.Controls.Add(lbl);

            int x = 90;
            foreach (var lottery in LotteryInfo.All)
            {
                bool checkedNow = rule.Lotteries.Exists(l => l.Code == lottery.Code);
                var chk = new CheckBox
                {
                    Text = lottery.Name,
                    Checked = checkedNow,
                    AutoSize = true,
                    Location = new Point(x, 38),
                    Tag = new RuleLotteryTag { RuleId = rule.Id, LotteryCode = lottery.Code },
                };
                chk.CheckedChanged += (s, e) =>
                    OnLotteryChanged((RuleLotteryTag)((CheckBox)s).Tag, ((CheckBox)s).Checked);
                row.Controls.Add(chk);
                x += 90;
            }

            return row;
        }

        private class RuleLotteryTag
        {
            public long RuleId;
            public string LotteryCode;
        }

        private void OnEnabledChanged(long ruleId, bool enabled)
        {
            if (_loading) return;
            Db.RuleRepository.UpdateEnabled(ruleId, enabled);
        }

        private void OnLotteryChanged(RuleLotteryTag tag, bool checkedNow)
        {
            if (_loading) return;
            var rules = Db.RuleRepository.GetAll();
            var rule = rules.Find(r => r.Id == tag.RuleId);
            if (rule == null) return;

            var codes = new List<string>();
            foreach (var l in rule.Lotteries) codes.Add(l.Code);
            if (checkedNow && !codes.Contains(tag.LotteryCode)) codes.Add(tag.LotteryCode);
            if (!checkedNow) codes.Remove(tag.LotteryCode);
            Db.RuleRepository.UpdateLotteries(tag.RuleId, codes);
        }
    }
}
