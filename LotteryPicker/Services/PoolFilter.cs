using System;
using System.Collections.Generic;
using System.Linq;
using LotteryPicker.Models;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 候选号码池过滤工具：供"去热冷"版本规则使用。
    /// 根据最近统计窗口内的开奖数据，把频次最高的若干号码（热门）
    /// 与频次最低的若干号码（冷门）从候选池剔除，其余号码照常可选。
    /// </summary>
    public static class PoolFilter
    {
        /// <summary>
        /// 得到 1..maxNum 的候选池。excludeHotCold 为 true 时剔除热门/冷门各 topN 个。
        /// isBack 指定统计后区（蓝球）还是前区（红球）。
        /// </summary>
        public static List<int> GetPool(List<Draw> recentDraws, bool isBack,
            int maxNum, int topN, bool excludeHotCold)
        {
            var all = Enumerable.Range(1, maxNum).ToList();
            if (!excludeHotCold) return all;

            var draws = (recentDraws ?? new List<Draw>()).Where(d => d != null).ToList();
            if (draws.Count == 0) return all;   // 没有历史数据时不做剔除

            var freq = new Dictionary<int, int>();
            foreach (var n in all) freq[n] = 0;
            foreach (var d in draws)
            {
                string text = isBack ? d.Blues : d.Reds;
                if (string.IsNullOrEmpty(text)) continue;
                foreach (var n in LotteryInfo.ParseNumbers(text))
                {
                    if (freq.ContainsKey(n)) freq[n]++;
                }
            }

            int cut = Math.Min(topN, all.Count);
            var hot = all.OrderByDescending(n => freq[n]).ThenBy(n => n).Take(cut);
            var cold = all.OrderBy(n => freq[n]).ThenBy(n => n).Take(cut);
            var removed = new HashSet<int>(hot.Concat(cold));
            return all.Where(n => !removed.Contains(n)).ToList();
        }
    }
}
