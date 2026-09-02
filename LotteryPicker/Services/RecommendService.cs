using System;
using System.Linq;
using LotteryPicker.Models;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 推荐与中奖检查主流程。
    /// AutoRecommend：为每个彩种"下一期待开奖期次"按激活规则生成推荐并落库（已生成则跳过）。
    /// CheckPrizes：对已开奖但未检查的推荐计算奖级。
    /// </summary>
    public static class RecommendService
    {
        /// <summary>
        /// 对每个彩种生成下一期推荐。返回新生成条数。
        /// 下一期 = 数据库最新已开奖期号 + 1；若该期已开奖（最新期号已推进）则不补生成。
        /// </summary>
        public static int AutoRecommend()
        {
            int generated = 0;
            foreach (var lottery in LotteryInfo.All)
            {
                var latest = Db.DrawRepository.GetLatest(lottery.Code);
                if (latest == null) continue; // 数据不足，等待抓取

                string nextIssue = NextIssue(latest.Issue);
                // 若最新期号已推进到下一期（说明下一期已开奖），则本期推荐机会已过，不生成
                if (latest.Issue.CompareTo(nextIssue) >= 0) continue;

                var rules = Db.RuleRepository.GetAll()
                    .Where(r => r.Enabled && r.Lotteries.Any(l => l.Code == lottery.Code))
                    .ToList();

                foreach (var rule in rules)
                {
                    if (Db.RecommendRepository.Exists(rule.Id, lottery.Code, nextIssue)) continue;

                    var gen = RuleEngine.Generate(rule.Code, lottery);
                    if (gen == null) continue;

                    Db.RecommendRepository.InsertIfAbsent(new Recommend
                    {
                        RuleId = rule.Id,
                        LotteryCode = lottery.Code,
                        Issue = nextIssue,
                        Reds = gen.RedsText,
                        Blues = gen.BluesText,
                        CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    });
                    generated++;
                }
            }
            return generated;
        }

        /// <summary>对已开奖但未检查的推荐计算奖级，返回处理条数</summary>
        public static int CheckPrizes()
        {
            int handled = 0;
            var uncheckedRecs = Db.RecommendRepository.GetUnchecked();
            foreach (var rec in uncheckedRecs)
            {
                // 找到该期次的开奖；查不到说明还未开奖
                var draw = Db.DrawRepository.GetAll(rec.LotteryCode)
                    .FirstOrDefault(d => d.Issue == rec.Issue);
                if (draw == null) continue;

                var lottery = LotteryInfo.Get(rec.LotteryCode);
                string prize = PrizeChecker.Check(lottery, draw, rec.Reds, rec.Blues);
                Db.RecommendRepository.UpdatePrize(rec.Id, prize);
                handled++;
            }
            return handled;
        }

        /// <summary>期号递增：取尾部连续数字 +1，保持位数。25001→25002，2025001→2025002</summary>
        public static string NextIssue(string issue)
        {
            if (string.IsNullOrEmpty(issue)) return "";
            int i = issue.Length;
            while (i > 0 && char.IsDigit(issue[i - 1])) i--;
            string prefix = issue.Substring(0, i);
            string numStr = issue.Substring(i);
            if (numStr.Length == 0) return "";
            int num = int.Parse(numStr) + 1;
            return prefix + num.ToString("D" + numStr.Length);
        }
    }
}
