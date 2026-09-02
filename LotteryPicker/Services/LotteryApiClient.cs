using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using LotteryPicker.Models;
using Newtonsoft.Json.Linq;

namespace LotteryPicker.Services
{
    /// <summary>
    /// 从官网接口抓取开奖数据。
    /// 大乐透：体彩官方 webapi.sporttery.cn（gameNo=85）
    /// 双色球：福彩官方 www.cwl.gov.cn（name=ssq）
    /// </summary>
    public class LotteryApiClient
    {
        private static readonly HttpClient _http = new HttpClient();

        static LotteryApiClient()
        {
            _http.Timeout = TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Referer", "https://static.sporttery.cn/");
            _http.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        }

        /// <summary>抓取大乐透最近 count 期</summary>
        public List<Draw> FetchDlt(int count = 100)
        {
            string url = "https://webapi.sporttery.cn/gateway/lottery/getHistoryPageListV1.qry?" +
                         "gameNo=85&provinceId=0&pageSize=" + count + "&isVerify=1&pageNo=1";
            string json = _http.GetStringAsync(url).Result;
            var root = JObject.Parse(json);
            var arr = root["value"]?["list"] as JArray ?? new JArray();
            var list = new List<Draw>();
            foreach (var item in arr)
            {
                string issue = (string)item["lotteryDrawNum"];
                string date = (string)item["lotteryDrawTime"];
                string result = (string)item["lotteryDrawResult"];
                if (string.IsNullOrEmpty(issue) || string.IsNullOrEmpty(result)) continue;

                // 结果形如 "01 05 08 12 33 02 07"（空格或逗号分隔）
                var nums = Regex.Split(result.Trim(), @"[\s,]+")
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (nums.Count < 7) continue;

                list.Add(new Draw
                {
                    LotteryCode = "dlt",
                    Issue = issue,
                    DrawDate = date,
                    Reds = string.Join(",", nums.Take(5)),
                    Blues = string.Join(",", nums.Skip(5).Take(2)),
                });
            }
            return list.OrderBy(x => x.Issue).ToList();
        }

        /// <summary>抓取双色球最近 count 期</summary>
        public List<Draw> FetchSsq(int count = 100)
        {
            string url = "https://www.cwl.gov.cn/cwl_admin/front/cwlkj/search/kjxx/findDrawNotice?" +
                         "name=ssq&issueCount=" + count;
            string json = _http.GetStringAsync(url).Result;
            var root = JObject.Parse(json);
            var arr = root["result"] as JArray ?? new JArray();
            var list = new List<Draw>();
            foreach (var item in arr)
            {
                string issue = (string)item["code"];
                string date = (string)item["date"];
                string reds = (string)item["red"];
                string blue = (string)item["blue"];
                if (string.IsNullOrEmpty(issue) || string.IsNullOrEmpty(reds)) continue;

                list.Add(new Draw
                {
                    LotteryCode = "ssq",
                    Issue = issue,
                    DrawDate = date,
                    Reds = string.Join(",", Regex.Split(reds.Trim(), @"[\s,]+").Where(x => x != "")),
                    Blues = string.Join(",", Regex.Split((blue ?? "").Trim(), @"[\s,]+").Where(x => x != "")),
                });
            }
            return list.OrderBy(x => x.Issue).ToList();
        }

        /// <summary>
        /// 抓取并合并入库，返回本次新增期数。
        /// 增量策略：库中已有数据时只拉最近 20 期（覆盖最新 3 周以上），首次运行时拉 100 期。
        /// 两个官方站点请求之间留间隔，避免被当作攻击封 IP。
        /// </summary>
        public int UpdateAll()
        {
            bool hasData = Db.DrawRepository.Count("dlt") + Db.DrawRepository.Count("ssq") > 0;
            int count = hasData ? 20 : 100;

            int added = SaveList(FetchDlt(count));
            System.Threading.Thread.Sleep(800); // 请求间隔，防封
            added += SaveList(FetchSsq(count));

            Db.Database.SetConfig("last_update_time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return added;
        }

        private static int SaveList(List<Draw> draws)
        {
            int added = 0;
            foreach (var d in draws)
            {
                if (Db.DrawRepository.InsertIfAbsent(d)) added++;
            }
            return added;
        }
    }
}
