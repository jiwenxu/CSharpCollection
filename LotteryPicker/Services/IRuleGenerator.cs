using System.Collections.Generic;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 一次生成结果：前区/红球 + 后区/蓝球
    /// </summary>
    public class GeneratedNumbers
    {
        public List<int> Reds { get; set; }
        public List<int> Blues { get; set; }

        public string RedsText
        {
            get { return Models.LotteryInfo.JoinNumbers(Reds); }
        }

        public string BluesText
        {
            get { return Models.LotteryInfo.JoinNumbers(Blues); }
        }
    }

    /// <summary>
    /// 推荐规则接口。新增规则时实现此接口并在 RuleEngine 中注册，界面自动列出。
    /// </summary>
    public interface IRuleGenerator
    {
        string Code { get; }    // 与 Rule 表的 Code 对应
        string Name { get; }    // 展示名，如 热号
        string Description { get; }

        /// <summary>根据最近 stats 期开奖数据生成一注完整号码</summary>
        GeneratedNumbers Generate(LotteryPicker.Models.LotteryInfo lottery,
            List<LotteryPicker.Models.Draw> recentDraws);
    }
}
