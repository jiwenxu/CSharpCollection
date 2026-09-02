using System;
using System.Collections.Generic;
using System.Linq;
using LotteryPicker.Models;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 频率类规则公共实现（热号/冷号共用统计与选号算法）。
    /// 统计最近 STAT_WINDOW 期号码出现频率，按频率取名额；
    /// 仅当名额落在并列档位时，对该档做81次随机抽样按被抽中次数排序决出。
    /// </summary>
    public abstract class FrequencyRuleBase : IRuleGenerator
    {
        /// <summary>统计窗口期数</summary>
        public const int STAT_WINDOW = 81;

        /// <summary>并列时随机抽取次数</summary>
        public const int SAMPLE_TIMES = 81;

        public abstract string Code { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }

        /// <summary>取频率最高（热号）还是最低（冷号）</summary>
        protected abstract bool IsHot { get; }

        public GeneratedNumbers Generate(LotteryInfo lottery, List<Draw> recentDraws)
        {
            // 只取最近 STAT_WINDOW 期
            var window = recentDraws.OrderByDescending(d => d.Issue).Take(STAT_WINDOW).ToList();

            var frontFreq = CountFrequency(window, false, lottery.FrontMax);
            var backFreq = CountFrequency(window, true, lottery.BackMax);

            var reds = SelectTopByFrequency(frontFreq, lottery.FrontMax, lottery.FrontCount, IsHot);
            var blues = SelectTopByFrequency(backFreq, lottery.BackMax, lottery.BackCount, IsHot);

            return new GeneratedNumbers { Reds = reds, Blues = blues };
        }

        /// <summary>统计每个号码在窗口内出现次数</summary>
        private static Dictionary<int, int> CountFrequency(List<Draw> window, bool isBack, int maxNum)
        {
            var freq = Enumerable.Range(1, maxNum).ToDictionary(n => n, n => 0);
            foreach (var draw in window)
            {
                string text = isBack ? draw.Blues : draw.Reds;
                foreach (var n in LotteryInfo.ParseNumbers(text))
                {
                    if (freq.ContainsKey(n)) freq[n]++;
                }
            }
            return freq;
        }

        /// <summary>
        /// 按频率选取 count 个号码。
        /// 排序方向：热号频率降序，冷号频率升序；
        /// 仅当名额边界落在并列档时，对并列档做81次抽样排序。
        /// </summary>
        protected static List<int> SelectTopByFrequency(Dictionary<int, int> freq, int maxNum,
            int count, bool isHot)
        {
            var all = Enumerable.Range(1, maxNum).ToList();
            // 稳定排序：频率按方向排序，同频率保持编号升序
            List<int> ordered;
            if (isHot)
                ordered = all.OrderByDescending(n => freq[n]).ThenBy(n => n).ToList();
            else
                ordered = all.OrderBy(n => freq[n]).ThenBy(n => n).ToList();

            // 按频率分组为档位（保持已排好的顺序）
            var groups = new List<List<int>>();
            foreach (var n in ordered)
            {
                if (groups.Count == 0 || freq[groups[groups.Count - 1][0]] != freq[n])
                    groups.Add(new List<int>());
                groups[groups.Count - 1].Add(n);
            }

            var selected = new List<int>();
            foreach (var group in groups)
            {
                int remaining = count - selected.Count;
                if (remaining <= 0) break;

                if (group.Count <= remaining)
                {
                    selected.AddRange(group);
                }
                else
                {
                    // 名额边界落在本并列档内：81次抽样决出档内排名，取前 remaining 个
                    var ranked = SampleRank(group, isHot);
                    selected.AddRange(ranked.Take(remaining));
                    break;
                }
            }

            return selected.OrderBy(n => n).ToList();
        }

        /// <summary>
        /// 对并列档做 SAMPLE_TIMES 次随机抽取，按被抽中次数排序。
        /// 热号取被抽中次数多的优先；冷号取少的优先。
        /// </summary>
        private static List<int> SampleRank(List<int> tied, bool isHot)
        {
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            var counts = tied.ToDictionary(n => n, n => 0);
            for (int i = 0; i < SAMPLE_TIMES; i++)
            {
                counts[tied[rnd.Next(tied.Count)]]++;
            }

            IEnumerable<int> ranked;
            if (isHot)
                ranked = tied.OrderByDescending(n => counts[n]).ThenBy(n => rnd.Next());
            else
                ranked = tied.OrderBy(n => counts[n]).ThenBy(n => rnd.Next());
            return ranked.ToList();
        }
    }
}
