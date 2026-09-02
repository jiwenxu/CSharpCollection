namespace LotteryPicker.Models
{
    /// <summary>
    /// 每期推荐记录。生成后不再更改。
    /// </summary>
    public class Recommend
    {
        public long Id { get; set; }
        public long RuleId { get; set; }
        public string RuleName { get; set; }
        public string LotteryCode { get; set; }
        public string Issue { get; set; }
        public string Reds { get; set; }
        public string Blues { get; set; }
        public string PrizeLevel { get; set; }  // 未开奖 / 未中奖 / 一等奖 ...
        public bool Checked { get; set; }       // 是否已对照开奖结果检查
        public string CreatedAt { get; set; }
    }
}
