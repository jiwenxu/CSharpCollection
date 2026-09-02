using System;
using System.Collections.Generic;
using System.Data.SQLite;
using LotteryPicker.Models;

namespace LotteryPicker.Db
{
    /// <summary>
    /// 推荐记录数据访问
    /// </summary>
    public static class RecommendRepository
    {
        /// <summary>插入推荐记录；若 (RuleId, LotteryCode, Issue) 已存在则忽略</summary>
        public static bool InsertIfAbsent(Recommend rec)
        {
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "INSERT OR IGNORE INTO Recommend (RuleId, LotteryCode, Issue, Reds, Blues, PrizeLevel, Checked, CreatedAt) " +
                "VALUES (@rid, @c, @i, @r, @b, '未开奖', 0, @t)", conn))
            {
                cmd.Parameters.AddWithValue("@rid", rec.RuleId);
                cmd.Parameters.AddWithValue("@c", rec.LotteryCode);
                cmd.Parameters.AddWithValue("@i", rec.Issue);
                cmd.Parameters.AddWithValue("@r", rec.Reds);
                cmd.Parameters.AddWithValue("@b", rec.Blues);
                cmd.Parameters.AddWithValue("@t", rec.CreatedAt);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>查询某彩种某期次是否已生成某规则的推荐</summary>
        public static bool Exists(long ruleId, string code, string issue)
        {
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM Recommend WHERE RuleId = @rid AND LotteryCode = @c AND Issue = @i", conn))
            {
                cmd.Parameters.AddWithValue("@rid", ruleId);
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@i", issue);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        /// <summary>未检查中奖的记录</summary>
        public static List<Recommend> GetUnchecked()
        {
            var list = new List<Recommend>();
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "SELECT rc.*, r.Name AS RuleName FROM Recommend rc " +
                "LEFT JOIN Rule r ON r.Id = rc.RuleId WHERE rc.Checked = 0 ORDER BY rc.Issue", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) list.Add(ReadRec(r));
            }
            return list;
        }

        /// <summary>全部推荐记录（新在前），code 为空则不过滤彩种</summary>
        public static List<Recommend> GetAll(string code)
        {
            var list = new List<Recommend>();
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "SELECT rc.*, r.Name AS RuleName FROM Recommend rc " +
                "LEFT JOIN Rule r ON r.Id = rc.RuleId " +
                "WHERE (@c = '' OR rc.LotteryCode = @c) ORDER BY rc.Issue DESC, rc.Id", conn))
            {
                cmd.Parameters.AddWithValue("@c", string.IsNullOrEmpty(code) ? "" : code);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(ReadRec(r));
                }
            }
            return list;
        }

        /// <summary>更新中奖结果</summary>
        public static void UpdatePrize(long id, string prizeLevel)
        {
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand("UPDATE Recommend SET PrizeLevel = @p, Checked = 1 WHERE Id = @id", conn))
            {
                cmd.Parameters.AddWithValue("@p", prizeLevel);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        private static Recommend ReadRec(SQLiteDataReader r)
        {
            return new Recommend
            {
                Id = Convert.ToInt64(r["Id"]),
                RuleId = Convert.ToInt64(r["RuleId"]),
                RuleName = r["RuleName"] == DBNull.Value ? "" : Convert.ToString(r["RuleName"]),
                LotteryCode = Convert.ToString(r["LotteryCode"]),
                Issue = Convert.ToString(r["Issue"]),
                Reds = Convert.ToString(r["Reds"]),
                Blues = Convert.ToString(r["Blues"]),
                PrizeLevel = Convert.ToString(r["PrizeLevel"]),
                Checked = Convert.ToInt32(r["Checked"]) == 1,
                CreatedAt = r["CreatedAt"] == DBNull.Value ? "" : Convert.ToString(r["CreatedAt"]),
            };
        }
    }
}
