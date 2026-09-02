using System;
using System.Collections.Generic;
using System.Linq;

namespace LotteryPicker.Models
{
    /// <summary>
    /// 彩种配置。新增彩种时在此注册，界面自动支持。
    /// </summary>
    public class LotteryInfo
    {
        public string Code { get; set; }        // dlt / ssq
        public string Name { get; set; }
        public int FrontCount { get; set; }     // 前区/红球个数
        public int FrontMax { get; set; }       // 前区/红球最大值
        public int BackCount { get; set; }      // 后区/蓝球个数
        public int BackMax { get; set; }        // 后区/蓝球最大值

        public static readonly List<LotteryInfo> All = new List<LotteryInfo>
        {
            new LotteryInfo { Code = "dlt", Name = "大乐透", FrontCount = 5, FrontMax = 35, BackCount = 2, BackMax = 12 },
            new LotteryInfo { Code = "ssq", Name = "双色球", FrontCount = 6, FrontMax = 33, BackCount = 1, BackMax = 16 },
        };

        public static LotteryInfo Get(string code)
        {
            return All.FirstOrDefault(x => x.Code == code) ?? All[0];
        }

        /// <summary>
        /// 号码格式化为统一展示/复制文本，如 "01 05 08 12 33 + 02 07"
        /// </summary>
        public static string Format(string reds, string blues)
        {
            return reds + " + " + blues;
        }

        /// <summary>
        /// 解析 "01,05,08,12,33" 为 int 列表
        /// </summary>
        public static List<int> ParseNumbers(string text, char sep = ',')
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<int>();
            return text.Split(sep)
                .Select(s => int.Parse(s.Trim()))
                .ToList();
        }

        /// <summary>
        /// 把 int 列表格式化为两位数字符串 "01,05,08,12,33"
        /// </summary>
        public static string JoinNumbers(IEnumerable<int> nums, char sep = ',')
        {
            return string.Join(sep.ToString(), nums.Select(n => n.ToString("D2")));
        }
    }
}
