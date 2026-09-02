using System;
using System.Collections.Generic;
using System.Linq;
using LotteryPicker.Models;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 六爻规则：模拟三枚铜钱摇卦（从下往上六爻），
    /// 由本卦、变卦、动爻推演出确定性种子，再选出一注号码。
    /// 同一卦象永远生成同一注号码（可复现、可解释）。
    /// </summary>
    public class LiuYaoRule : IRuleGenerator
    {
        public string Code { get { return "liuyao"; } }
        public string Name { get { return "六爻"; } }
        public string Description { get { return "模拟铜钱摇卦，以本卦/变卦/动爻推演号码，卦定号定"; } }

        public GeneratedNumbers Generate(LotteryInfo lottery, List<Draw> recentDraws)
        {
            // 1. 摇卦：抛 6 次三枚铜钱，每枚随机字(0)/背(1)，记录每爻背面数
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            bool[] yang = new bool[6];      // 爻的阴阳，true=阳
            bool[] moving = new bool[6];    // 是否动爻
            for (int i = 0; i < 6; i++)
            {
                int backs = rnd.Next(0, 2) + rnd.Next(0, 2) + rnd.Next(0, 2);
                switch (backs)
                {
                    case 3: yang[i] = true; moving[i] = true; break;    // 三背 = 老阳（动）
                    case 0: yang[i] = false; moving[i] = true; break;   // 三字 = 老阴（动）
                    case 2: yang[i] = true; moving[i] = false; break;   // 二背一字 = 少阳
                    default: yang[i] = false; moving[i] = false; break; // 二字一背 = 少阴
                }
            }

            // 2. 定卦：本卦数、变卦数（动爻翻转）、动爻统计
            int benGua = GuaNumber(yang);
            var bianYang = (bool[])yang.Clone();
            for (int i = 0; i < 6; i++) if (moving[i]) bianYang[i] = !bianYang[i];
            int bianGua = GuaNumber(bianYang);
            int moveCount = moving.Count(m => m);
            int moveSum = 0;
            for (int i = 0; i < 6; i++) if (moving[i]) moveSum += (i + 1);

            // 3. 推数：由卦象生成确定性种子（同一卦 → 同一组号码）
            int seed = benGua * 10000 + bianGua * 100 + moveCount * 10 + moveSum;
            var gen = new Random(seed);

            // 4. 选号：注内不重复，按彩种取满前区/后区
            var reds = PickNumbers(gen, lottery.FrontMax, lottery.FrontCount);
            var blues = PickNumbers(gen, lottery.BackMax, lottery.BackCount);

            return new GeneratedNumbers { Reds = reds, Blues = blues };
        }

        /// <summary>
        /// 六爻转卦数：下三爻为低位、上三爻为高位（初爻为最低位），结果 1-64。
        /// </summary>
        private static int GuaNumber(bool[] yang)
        {
            int lower = (yang[0] ? 1 : 0) | (yang[1] ? 2 : 0) | (yang[2] ? 4 : 0);   // 0-7
            int upper = (yang[3] ? 1 : 0) | (yang[4] ? 2 : 0) | (yang[5] ? 4 : 0);   // 0-7
            return (upper * 8 + lower) + 1;                                          // 1-64
        }

        /// <summary>在 1..max 中取 count 个不重复号码（升序）</summary>
        private static List<int> PickNumbers(Random rnd, int max, int count)
        {
            var set = new HashSet<int>();
            while (set.Count < count)
            {
                set.Add(rnd.Next(1, max + 1));
            }
            return set.OrderBy(n => n).ToList();
        }
    }
}
