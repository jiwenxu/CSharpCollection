# 彩票选号助手（LotteryPicker）

基于 **WinForms + SQLite** 的彩票开奖查询与选号推荐工具，支持大乐透、双色球两个彩种。

## 功能特性

- **开奖数据抓取**：从体彩、福彩官方网站接口抓取开奖号码，自动存储到本地 SQLite 数据库
- **启动自动更新**：打开程序自动更新数据；手动点击"更新数据"按钮可随时刷新
- **智能推荐规则**：
  - **热号**：统计最近 81 期出现频率最高的号码，频率相同时对并列号码做 81 次随机抽取，取出现次数最多的
  - **冷号**：统计最近 81 期出现频率最低的号码，频率相同时做 81 次随机抽取，取出现次数最少的
  - **六爻**：随机摇卦生成本卦/变卦/动爻，以卦象作随机种子确定性选号
- **开奖前推荐落库**：每期开奖前按激活的规则自动生成推荐并记录，一旦生成不再更改，可点击复制
- **自动中奖检查**：更新开奖数据时自动核对推荐号码，精确到几等奖
- **临时生成**：按规则随时生成号码，可复制但不写入数据库
- **规则可扩展**：通过代码实现 `IRuleGenerator` 接口即可添加新规则

## 技术栈

- 语言：C#（.NET Framework 4.8，WinForms）
- 数据库：SQLite（`System.Data.SQLite.Core`）
- JSON：Newtonsoft.Json

## 运行方式

- 直接运行 `bin\Debug\net48\LotteryPicker.exe`（首次运行自动抓取 100 期开奖数据）
- 或用 Visual Studio 打开 `LotteryPicker.csproj` 编译运行

## 界面说明（四个标签页）

| 标签页 | 功能 |
|--------|------|
| 开奖数据 | 查看两个彩种的开奖历史（可按彩种筛选，最新一期加粗显示） |
| 推荐记录 | 查看已生成的推荐号码与中奖情况，双击行或点按钮复制号码 |
| 规则管理 | 启用/停用推荐规则，并设置每条规则适用的彩种 |
| 临时生成 | 按规则即时生成号码（不落库），可复制 |

## 项目结构

```
LotteryPicker/
├── LotteryPicker.csproj      # 项目文件
├── Program.cs                # 入口
├── MainForm.cs               # 主窗体（4 个 Tab）
├── lottery.ico               # 程序图标
├── Db/                       # 数据库层
│   ├── Database.cs           # 建表、种子数据、连接管理
│   ├── DrawRepository.cs     # 开奖数据访问
│   ├── RuleRepository.cs     # 规则数据访问
│   └── RecommendRepository.cs# 推荐记录数据访问
├── Models/                   # 实体模型
│   ├── LotteryInfo.cs        # 彩种注册表（大乐透/双色球）
│   ├── Draw.cs / Rule.cs / Recommend.cs
├── Services/                 # 业务服务
│   ├── LotteryApiClient.cs   # 官网接口抓取（增量 + 请求间隔 + 节流）
│   ├── RecommendService.cs   # 自动推荐 + 中奖检查
│   ├── PrizeChecker.cs       # 奖级计算
│   ├── IRuleGenerator.cs     # 规则接口（扩展点）
│   ├── FrequencyRuleBase.cs  # 热号/冷号公共算法
│   ├── HotRule.cs / ColdRule.cs / LiuYaoRule.cs
│   └── RuleEngine.cs         # 规则注册表
└── Forms/                    # 标签页界面
    ├── DrawsTab.cs / RecommendsTab.cs / RulesTab.cs / QuickGenerateTab.cs
```

## 数据库表结构

- `Lottery`：彩种（dlt 大乐透 / ssq 双色球）
- `Draw`：开奖记录（`UNIQUE(LotteryCode, Issue)`）
- `Rule`：推荐规则（`UNIQUE(Code)`）
- `RuleLottery`：规则与彩种的多对多关联
- `Recommend`：推荐记录（`UNIQUE(RuleId, LotteryCode, Issue)`，包含奖级与检查标记）
- `AppConfig`：键值配置（记录最近一次成功更新时间）

数据库文件 `lottery.db` 位于程序运行目录（exe 同目录），首次启动自动创建。

## 数据更新策略

- **增量拉取**：库中已有数据时只拉最近 20 期，首次运行时拉 100 期（满足不少于 81 期）
- **请求间隔**：两个官方站点请求之间间隔 800ms，避免被封 IP
- **更新节流**：程序启动自动更新时，若距上次成功更新不足 10 分钟则跳过；手动更新不受限

## 如何新增一条推荐规则

1. 新建规则类，实现 `IRuleGenerator` 接口（`Code` / `Name` / `Generate()`）
2. 在 `RuleEngine` 构造函数中注册该规则实例
3. 在 `Database` 的种子数据中插入对应的 `Rule` 记录及 `RuleLottery` 彩种关联
4. 重新编译即可，规则管理页会自动出现新规则开关

## 数据来源

- 大乐透：中国体彩网官方接口（`webapi.sporttery.cn`）
- 双色球：中国福彩网官方接口（`www.cwl.gov.cn`）

## 免责声明

本工具仅供个人学习与娱乐使用，开奖结果以官方公布为准，请理性购彩。
