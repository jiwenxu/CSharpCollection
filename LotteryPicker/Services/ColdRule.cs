namespace LotteryPicker.Services
{
    /// <summary>
    /// 冷号规则：取最近81期出现频率最低的号码；并列时81次抽样取被抽中次数最少的。
    /// </summary>
    public class ColdRule : FrequencyRuleBase
    {
        public override string Code { get { return "cold"; } }
        public override string Name { get { return "冷号"; } }
        public override string Description { get { return "取最近81期出现频率最低的号码，并列时抽样决出"; } }
        protected override bool IsHot { get { return false; } }
    }
}
