using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LotteryPicker.Models;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 六壬选号（小六壬掌诀掐算）：
    /// 1) 主宫：取当前时刻的农历月、日、时三数，自大安起逐宫掐算，落点即主宫（定财运基调与数字池）；
    /// 2) 变宫：取当前秒数 % 6 定宫（余 1 = 大安，余 0 = 空亡）；
    /// 3) 选号：主宫数字池优先取"主宫五行尾数"的号码，变宫池按"生我五行尾数"取，
    ///    双色球第 6 个前区号码走"相生补齐"；后区/蓝球按主宫五行尾数在蓝号范围选取。
    /// 传入 excludeHotCold = true 时为"去热冷"版本：候选池先剔除最近81期热门与冷门号码。
    /// </summary>
    public class LiuRenRule : IRuleGenerator
    {
        // ---------- 六宫定义（顺序即掐算循环序）----------
        private const int ELEM_MU = 0, ELEM_SHUI = 1, ELEM_HUO = 2, ELEM_JIN = 3, ELEM_TU = 4;

        /// <summary>每宫五行序号：大安木、留连水、速喜火、赤口金、小吉土、空亡火</summary>
        private static readonly int[] PalaceElement =
            { ELEM_MU, ELEM_SHUI, ELEM_HUO, ELEM_JIN, ELEM_TU, ELEM_HUO };

        /// <summary>五行对应尾数：木3/8、水1/6、火2/7、金4/9、土5/0</summary>
        private static readonly int[][] ElementTails =
        {
            new[] { 3, 8 },  // 木
            new[] { 1, 6 },  // 水
            new[] { 2, 7 },  // 火
            new[] { 4, 9 },  // 金
            new[] { 5, 0 }   // 土
        };

        /// <summary>"生我"关系：五行相生 木→火→土→金→水→木，此处存每个五行的生我者</summary>
        private static readonly int[] MotherOf =
            { ELEM_SHUI, ELEM_JIN, ELEM_MU, ELEM_TU, ELEM_HUO };
        // 木之生我=水、水之生我=金、火之生我=木、金之生我=土、土之生我=火

        /// <summary>每宫固定数字池（与需求描述一一对应）</summary>
        private static readonly int[][] PalacePools =
        {
            new[] { 1, 5, 7, 15, 25 },   // 大安
            new[] { 2, 6, 8, 16, 26 },   // 留连
            new[] { 3, 7, 9, 17, 27 },   // 速喜
            new[] { 4, 8, 10, 18, 28 },  // 赤口
            new[] { 5, 9, 11, 19, 29 },  // 小吉
            new[] { 6, 10, 12, 20, 30 }  // 空亡
        };

        private static readonly ChineseLunisolarCalendar _cal = new ChineseLunisolarCalendar();

        private readonly string _code;
        private readonly string _name;
        private readonly bool _excludeHotCold;

        /// <param name="code">规则编码（与 Rule 表 Code 对应）</param>
        /// <param name="name">展示名称</param>
        /// <param name="excludeHotCold">true = 候选池剔除热门/冷门号码</param>
        public LiuRenRule(string code, string name, bool excludeHotCold)
        {
            _code = code;
            _name = name;
            _excludeHotCold = excludeHotCold;
        }

        public string Code { get { return _code; } }
        public string Name { get { return _name; } }
        public string Description
        {
            get
            {
                return _excludeHotCold
                    ? "六壬掐算选号（农历月日时定主宫、秒数定变宫、五行尾数定号），候选池剔除热门与冷门号码"
                    : "六壬掐算选号（农历月日时定主宫、秒数定变宫、五行尾数定号）";
            }
        }

        public GeneratedNumbers Generate(LotteryInfo lottery, List<Draw> recentDraws)
        {
            DateTime now = DateTime.Now;

            // ---- 掐时定主宫（农历月、日、时三数自大安起掐算）----
            int mainIdx = CalcMainPalace(now);

            // ---- 秒数定变宫 ----
            int bianIdx = CalcBianPalace(now);

            int mainElem = PalaceElement[mainIdx];
            int feedElem = MotherOf[mainElem];     // 生主宫的五行
            int[] mainTails = ElementTails[mainElem];
            int[] feedTails = ElementTails[feedElem];

            var rnd = new Random(Guid.NewGuid().GetHashCode());

            // ---- 候选池（去热冷版本先剔除热门冷门）----
            var redUniverse = PoolFilter.GetPool(recentDraws, false,
                lottery.FrontMax, lottery.FrontCount, _excludeHotCold);
            var blueUniverse = PoolFilter.GetPool(recentDraws, true,
                lottery.BackMax, lottery.BackCount, _excludeHotCold);

            // ---- 前区：主宫池 3 + 变宫池 2，双色球第 6 个走"相生补齐" ----
            var used = new HashSet<int>();
            int mainWant = Math.Min(3, lottery.FrontCount);
            int bianWant = Math.Min(2, lottery.FrontCount - mainWant);
            int fillWant = lottery.FrontCount - mainWant - bianWant;

            PickFrom(PalacePools[mainIdx], mainTails, mainWant, rnd, redUniverse, used);       // 主宫：优先主宫五行尾数
            PickFrom(PalacePools[bianIdx], feedTails, bianWant, rnd, redUniverse, used);       // 变宫：优先生我五行尾数
            if (fillWant > 0)
            {
                var fillTails = feedTails.Concat(mainTails).ToArray();                         // 补齐：相生尾数
                PickFrom(redUniverse, fillTails, fillWant, rnd, redUniverse, used);
            }

            var reds = used.OrderBy(n => n).ToList();

            // ---- 后区/蓝球：主宫五行尾数优先，生我尾数辅助 ----
            var blueUsed = new HashSet<int>();
            var blueTails = mainTails.Concat(feedTails).ToArray();
            PickFrom(blueUniverse, blueTails, lottery.BackCount, rnd, blueUniverse, blueUsed);
            var blues = blueUsed.OrderBy(n => n).ToList();

            return new GeneratedNumbers { Reds = reds, Blues = blues };
        }

        // ================= 掐算 =================

        /// <summary>农历月、日、时三数掐算主宫。以落宫计 1 的推法：月从大安起数，日在月落宫起数，时在日落宫起数。</summary>
        private static int CalcMainPalace(DateTime now)
        {
            int month = _cal.GetMonth(now);
            if (month < 0) month = -month;                       // 闰月按当月处理
            int day = _cal.GetDayOfMonth(now);
            int shichen = ((now.Hour + 1) / 2) % 12 + 1;         // 时辰序号：子=1 … 亥=12

            int pos = 0;                                         // 大安 = 0
            pos = (pos + (month - 1)) % 6;                       // 月
            pos = (pos + (day - 1)) % 6;                         // 日
            pos = (pos + (shichen - 1)) % 6;                     // 时
            return pos;
        }

        /// <summary>秒数取变宫：second % 6，余 1 → 大安(0)、余 0 → 空亡(5)。</summary>
        private static int CalcBianPalace(DateTime now)
        {
            int r = now.Second % 6;
            return r == 0 ? 5 : r - 1;
        }

        // ================= 取号 =================

        /// <summary>
        /// 从 source 中取 want 个号码放入 used。
        /// 优先级：源内&池内&尾数匹配 → 源内任意 → 全域尾数匹配 → 全域兜底。
        /// </summary>
        private static void PickFrom(IEnumerable<int> source, int[] prefTails, int want,
            Random rnd, List<int> universe, HashSet<int> used)
        {
            if (want <= 0) return;
            var src = source.Distinct().Where(n => !used.Contains(n) && universe.Contains(n)).ToList();
            var picked = new List<int>();

            Take(picked, src.Where(n => prefTails.Contains(n % 10)).ToList(), want, rnd);
            if (picked.Count < want)
                Take(picked, src, want - picked.Count, rnd);
            if (picked.Count < want)
                Take(picked, universe.Where(n => !used.Contains(n) && prefTails.Contains(n % 10)).ToList(),
                    want - picked.Count, rnd);
            if (picked.Count < want)
                Take(picked, universe.Where(n => !used.Contains(n)).ToList(), want - picked.Count, rnd);

            foreach (var n in picked) used.Add(n);
        }

        /// <summary>打乱 src 后取前 want 个（不足取全部）</summary>
        private static void Take(List<int> dst, List<int> src, int want, Random rnd)
        {
            if (want <= 0 || src.Count == 0) return;
            var take = src.OrderBy(_ => rnd.Next()).Take(Math.Min(want, src.Count));
            dst.AddRange(take);
        }
    }
}
