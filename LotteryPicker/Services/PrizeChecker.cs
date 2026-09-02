using System.Collections.Generic;
using System.Linq;
using LotteryPicker.Models;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 中奖检查：按官方奖级规则计算推荐号码的中奖等级。
    /// 大乐透 1-9 等奖；双色球 1-6 等奖。
    /// </summary>
    public static class PrizeChecker
    {
        /// <summary>返回奖级名称，未中奖返回"未中奖"</summary>
        public static string Check(LotteryInfo lottery, Draw draw, string myReds, string myBlues)
        {
            var drawReds = new HashSet<int>(LotteryInfo.ParseNumbers(draw.Reds));
            var drawBlues = new HashSet<int>(LotteryInfo.ParseNumbers(draw.Blues));

            int hitR = LotteryInfo.ParseNumbers(myReds).Count(drawReds.Contains);
            int hitB = LotteryInfo.ParseNumbers(myBlues).Count(drawBlues.Contains);

            if (lottery.Code == "dlt") return CheckDlt(hitR, hitB);
            if (lottery.Code == "ssq") return CheckSsq(hitR, hitB);
            return "未中奖";
        }

        private static string CheckDlt(int r, int b)
        {
            if (r == 5 && b == 2) return "一等奖";
            if (r == 5 && b == 1) return "二等奖";
            if (r == 5 && b == 0) return "三等奖";
            if (r == 4 && b == 2) return "四等奖";
            if (r == 4 && b == 1) return "五等奖";
            if (r == 3 && b == 2) return "六等奖";
            if (r == 4 && b == 0) return "七等奖";
            if ((r == 3 && b == 1) || (r == 2 && b == 2)) return "八等奖";
            if ((r == 3 && b == 0) || (r == 1 && b == 2) || (r == 2 && b == 1) || (r == 0 && b == 2)) return "九等奖";
            return "未中奖";
        }

        private static string CheckSsq(int r, int b)
        {
            if (r == 6 && b == 1) return "一等奖";
            if (r == 6 && b == 0) return "二等奖";
            if (r == 5 && b == 1) return "三等奖";
            if ((r == 5 && b == 0) || (r == 4 && b == 1)) return "四等奖";
            if ((r == 4 && b == 0) || (r == 3 && b == 1)) return "五等奖";
            if ((r == 2 && b == 1) || (r == 1 && b == 1) || (r == 0 && b == 1)) return "六等奖";
            return "未中奖";
        }
    }
}
