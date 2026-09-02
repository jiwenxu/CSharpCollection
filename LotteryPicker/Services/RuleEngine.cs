using System.Collections.Generic;
using System.Linq;
using LotteryPicker.Models;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 规则注册表。新增规则时：实现 IRuleGenerator 并在此 Register 中添加，
    /// 数据库种子数据中同步插入对应 Rule 记录。
    /// </summary>
    public static class RuleEngine
    {
        private static readonly List<IRuleGenerator> _generators = new List<IRuleGenerator>
        {
            new HotRule(),
            new ColdRule(),
            new LiuYaoRule(),
        };

        public static List<IRuleGenerator> All { get { return _generators; } }

        public static IRuleGenerator Get(string code)
        {
            return _generators.FirstOrDefault(g => g.Code == code);
        }

        /// <summary>根据规则与彩种生成一注号码</summary>
        public static GeneratedNumbers Generate(string ruleCode, LotteryInfo lottery)
        {
            var gen = Get(ruleCode);
            if (gen == null) return null;
            var recent = Db.DrawRepository.GetRecent(lottery.Code, FrequencyRuleBase.STAT_WINDOW);
            return gen.Generate(lottery, recent);
        }
    }
}
