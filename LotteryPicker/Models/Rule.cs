using System.Collections.Generic;

namespace LotteryPicker.Models
{
    /// <summary>
    /// 推荐规则。规则由代码实现（IRuleGenerator），界面自动列出。
    /// </summary>
    public class Rule
    {
        public long Id { get; set; }
        public string Name { get; set; }        // 热号 / 冷号
        public string Code { get; set; }        // hot / cold，与规则实现对应
        public bool Enabled { get; set; }       // 界面开关

        public List<LotteryInfo> Lotteries { get; set; } = new List<LotteryInfo>();
    }
}
