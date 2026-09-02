using System;
using System.Collections.Generic;
using System.Data.SQLite;
using LotteryPicker.Models;

namespace LotteryPicker.Db
{
    /// <summary>
    /// 开奖记录数据访问
    /// </summary>
    public static class DrawRepository
    {
        /// <summary>插入或忽略已存在期次，返回是否新增</summary>
        public static bool InsertIfAbsent(Draw draw)
        {
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "INSERT OR IGNORE INTO Draw (LotteryCode, Issue, DrawDate, Reds, Blues) VALUES (@c, @i, @d, @r, @b)", conn))
            {
                cmd.Parameters.AddWithValue("@c", draw.LotteryCode);
                cmd.Parameters.AddWithValue("@i", draw.Issue);
                cmd.Parameters.AddWithValue("@d", draw.DrawDate);
                cmd.Parameters.AddWithValue("@r", draw.Reds);
                cmd.Parameters.AddWithValue("@b", draw.Blues);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>获取某彩种最新一期（按期号排序取最大）</summary>
        public static Draw GetLatest(string code)
        {
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "SELECT * FROM Draw WHERE LotteryCode = @c ORDER BY Issue DESC LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@c", code);
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read()) return ReadDraw(r);
                }
            }
            return null;
        }

        /// <summary>获取某彩种最近 count 期（按期号升序）</summary>
        public static List<Draw> GetRecent(string code, int count)
        {
            var list = new List<Draw>();
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "SELECT * FROM (SELECT * FROM Draw WHERE LotteryCode = @c ORDER BY Issue DESC LIMIT @n) ORDER BY Issue ASC", conn))
            {
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@n", count);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(ReadDraw(r));
                }
            }
            return list;
        }

        /// <summary>获取某彩种全部开奖（按期号降序，新在前）</summary>
        public static List<Draw> GetAll(string code)
        {
            var list = new List<Draw>();
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand(
                "SELECT * FROM Draw WHERE LotteryCode = @c ORDER BY Issue DESC", conn))
            {
                cmd.Parameters.AddWithValue("@c", code);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(ReadDraw(r));
                }
            }
            return list;
        }

        public static int Count(string code)
        {
            using (var conn = Database.Open())
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Draw WHERE LotteryCode = @c", conn))
            {
                cmd.Parameters.AddWithValue("@c", code);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static Draw ReadDraw(SQLiteDataReader r)
        {
            return new Draw
            {
                Id = Convert.ToInt64(r["Id"]),
                LotteryCode = Convert.ToString(r["LotteryCode"]),
                Issue = Convert.ToString(r["Issue"]),
                DrawDate = r["DrawDate"] == DBNull.Value ? "" : Convert.ToString(r["DrawDate"]),
                Reds = Convert.ToString(r["Reds"]),
                Blues = Convert.ToString(r["Blues"]),
            };
        }
    }
}
