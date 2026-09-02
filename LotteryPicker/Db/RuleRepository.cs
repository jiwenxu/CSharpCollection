using System;
using System.Collections.Generic;
using System.Data.SQLite;
using LotteryPicker.Models;

namespace LotteryPicker.Db
{
    /// <summary>
    /// 规则数据访问（含规则-彩种多对多）
    /// </summary>
    public static class RuleRepository
    {
        public static List<Rule> GetAll()
        {
            var rules = new List<Rule>();
            using (var conn = Database.Open())
            {
                using (var cmd = new SQLiteCommand("SELECT * FROM Rule ORDER BY Id", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        rules.Add(new Rule
                        {
                            Id = Convert.ToInt64(r["Id"]),
                            Name = Convert.ToString(r["Name"]),
                            Code = Convert.ToString(r["Code"]),
                            Enabled = Convert.ToInt32(r["Enabled"]) == 1,
                        });
                    }
                }

                // 加载每个规则关联的彩种
                foreach (var rule in rules)
                {
                    using (var cmd = new SQLiteCommand(
                        "SELECT l.Code, l.Name FROM RuleLottery rl " +
                        "JOIN Lottery l ON l.Id = rl.LotteryId WHERE rl.RuleId = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", rule.Id);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                rule.Lotteries.Add(new LotteryInfo
                                {
                                    Code = Convert.ToString(r["Code"]),
                                    Name = Convert.ToString(r["Name"]),
                                });
                            }
                        }
                    }
                }
            }
            return rules;
        }

        public static void UpdateEnabled(long ruleId, bool enabled)
        {
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand("UPDATE Rule SET Enabled = @e WHERE Id = @id", conn))
            {
                cmd.Parameters.AddWithValue("@e", enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("@id", ruleId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>更新规则关联的彩种（多对多，全量替换）</summary>
        public static void UpdateLotteries(long ruleId, List<string> lotteryCodes)
        {
            using (var conn = Database.Open())
            using (var tx = conn.BeginTransaction())
            {
                using (var del = new SQLiteCommand("DELETE FROM RuleLottery WHERE RuleId = @id", conn, tx))
                {
                    del.Parameters.AddWithValue("@id", ruleId);
                    del.ExecuteNonQuery();
                }
                foreach (var code in lotteryCodes)
                {
                    using (var ins = new SQLiteCommand(
                        "INSERT OR IGNORE INTO RuleLottery (RuleId, LotteryId) " +
                        "SELECT @id, l.Id FROM Lottery l WHERE l.Code = @c", conn, tx))
                    {
                        ins.Parameters.AddWithValue("@id", ruleId);
                        ins.Parameters.AddWithValue("@c", code);
                        ins.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
        }
    }
}
