using System;
using System.Data.SQLite;
using System.IO;

namespace LotteryPicker.Db
{
    /// <summary>
    /// 数据库初始化与连接管理（SQLite）
    /// </summary>
    public static class Database
    {
        public static string DbPath { get; private set; }

        public static string ConnStr
        {
            get { return "Data Source=" + DbPath + ";Version=3;"; }
        }

        public static void Initialize()
        {
            DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lottery.db");
            using (var conn = Open())
            {
                CreateTables(conn);
                SeedData(conn);
            }
        }

        public static SQLiteConnection Open()
        {
            var conn = new SQLiteConnection(ConnStr);
            conn.Open();
            return conn;
        }

        private static void CreateTables(SQLiteConnection conn)
        {
            string sql = @"
CREATE TABLE IF NOT EXISTS Lottery (
    Id      INTEGER PRIMARY KEY AUTOINCREMENT,
    Code    TEXT NOT NULL UNIQUE,
    Name    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Draw (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    LotteryCode TEXT NOT NULL,
    Issue       TEXT NOT NULL,
    DrawDate    TEXT,
    Reds        TEXT NOT NULL,
    Blues       TEXT NOT NULL,
    UNIQUE (LotteryCode, Issue)
);

CREATE TABLE IF NOT EXISTS Rule (
    Id      INTEGER PRIMARY KEY AUTOINCREMENT,
    Name    TEXT NOT NULL,
    Code    TEXT NOT NULL UNIQUE,
    Enabled INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS RuleLottery (
    RuleId    INTEGER NOT NULL,
    LotteryId INTEGER NOT NULL,
    PRIMARY KEY (RuleId, LotteryId)
);

CREATE TABLE IF NOT EXISTS Recommend (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    RuleId      INTEGER NOT NULL,
    LotteryCode TEXT NOT NULL,
    Issue       TEXT NOT NULL,
    Reds        TEXT NOT NULL,
    Blues       TEXT NOT NULL,
    PrizeLevel  TEXT NOT NULL DEFAULT '未开奖',
    Checked     INTEGER NOT NULL DEFAULT 0,
    CreatedAt   TEXT,
    UNIQUE (RuleId, LotteryCode, Issue)
);

CREATE TABLE IF NOT EXISTS AppConfig (
    Key   TEXT PRIMARY KEY,
    Value TEXT
);
";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void SeedData(SQLiteConnection conn)
        {
            // 彩种
            InsertIgnore(conn, "INSERT OR IGNORE INTO Lottery (Code, Name) VALUES ('dlt', '大乐透')");
            InsertIgnore(conn, "INSERT OR IGNORE INTO Lottery (Code, Name) VALUES ('ssq', '双色球')");

            // 规则（与 RuleEngine 中注册的规则一一对应）
            InsertIgnore(conn, "INSERT OR IGNORE INTO Rule (Name, Code, Enabled) VALUES ('热号', 'hot', 1)");
            InsertIgnore(conn, "INSERT OR IGNORE INTO Rule (Name, Code, Enabled) VALUES ('冷号', 'cold', 1)");
            InsertIgnore(conn, "INSERT OR IGNORE INTO Rule (Name, Code, Enabled) VALUES ('六爻', 'liuyao', 1)");

            // 规则-彩种 关联（默认热号/冷号/六爻均适用于两个彩种）
            using (var cmd = new SQLiteCommand(
                "INSERT OR IGNORE INTO RuleLottery (RuleId, LotteryId) " +
                "SELECT r.Id, l.Id FROM Rule r, Lottery l WHERE r.Code = 'hot' AND l.Code IN ('dlt','ssq')", conn))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SQLiteCommand(
                "INSERT OR IGNORE INTO RuleLottery (RuleId, LotteryId) " +
                "SELECT r.Id, l.Id FROM Rule r, Lottery l WHERE r.Code = 'cold' AND l.Code IN ('dlt','ssq')", conn))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SQLiteCommand(
                "INSERT OR IGNORE INTO RuleLottery (RuleId, LotteryId) " +
                "SELECT r.Id, l.Id FROM Rule r, Lottery l WHERE r.Code = 'liuyao' AND l.Code IN ('dlt','ssq')", conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void InsertIgnore(SQLiteConnection conn, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>读取配置项，不存在返回 null</summary>
        public static string GetConfig(string key)
        {
            using (var conn = Open())
            using (var cmd = new SQLiteCommand("SELECT Value FROM AppConfig WHERE Key = @k", conn))
            {
                cmd.Parameters.AddWithValue("@k", key);
                var v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? null : Convert.ToString(v);
            }
        }

        /// <summary>写入配置项</summary>
        public static void SetConfig(string key, string value)
        {
            using (var conn = Open())
            using (var cmd = new SQLiteCommand(
                "INSERT OR REPLACE INTO AppConfig (Key, Value) VALUES (@k, @v)", conn))
            {
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", value);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>启动自动更新节流：距上次成功更新未超过阈值时返回 true（跳过更新）</summary>
        public static bool WithinUpdateThrottle()
        {
            string last = GetConfig("last_update_time");
            if (string.IsNullOrEmpty(last)) return false;
            DateTime t;
            if (!DateTime.TryParse(last, out t)) return false;
            return (DateTime.Now - t).TotalMinutes < 10;
        }
    }
}
