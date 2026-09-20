namespace LotteryPicker.Services
{
    /// <summary>
    /// 六爻(去热冷)：与六爻同一套摇卦算法，但选号前先把最近统计窗口内的
    /// 热门号码与冷门号码从候选池中剔除，再从剩余号码中按卦象取号。
    /// </summary>
    public class LiuYaoCfRule : LiuYaoRule
    {
        public override string Code { get { return "liuyao_cf"; } }
        public override string Name { get { return "六爻(去热冷)"; } }
        public override string Description { get { return "摇卦选号，候选池剔除热门与冷门号码"; } }

        protected override bool ExcludeHotCold { get { return true; } }
    }
}
