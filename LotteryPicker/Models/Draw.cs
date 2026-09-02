namespace LotteryPicker.Models
{
    /// <summary>
    /// 开奖记录（一期）
    /// </summary>
    public class Draw
    {
        public long Id { get; set; }
        public string LotteryCode { get; set; }
        public string Issue { get; set; }       // 期号，如 25001 / 2025001
        public string DrawDate { get; set; }    // 开奖日期 yyyy-MM-dd
        public string Reds { get; set; }        // 前区/红球，逗号分隔 "01,05,08,12,33"
        public string Blues { get; set; }       // 后区/蓝球，逗号分隔 "02,07"
    }
}
