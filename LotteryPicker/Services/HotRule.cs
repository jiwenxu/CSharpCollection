namespace LotteryPicker.Services
{
    /// <summary>
    /// 热号规则：取最近81期出现频率最高的号码；并列时81次抽样取被抽中次数最多的。
    /// </summary>
    public class HotRule : FrequencyRuleBase
    {
        public override string Code { get { return "hot"; } }
        public override string Name { get { return "热号"; } }
        public override string Description { get { return "取最近81期出现频率最高的号码，并列时抽样决出"; } }
        protected override bool IsHot { get { return true; } }
    }
}
