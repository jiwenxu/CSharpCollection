# 微信防错发提醒助手（WeChat Send Guard）

在 PC 微信聊天窗口上叠加一层**独立、透明、鼠标穿透**的提醒层。当当前会话命中你维护的**高风险会话名单**时，用红框 + 可选 GIF 悬浮动画作强提醒，降低"手快发错人"的概率。

**核心约束**：不注入微信进程、不修改微信文件、不读取聊天内容、不联网上传。

---

## 1. 工作原理（简述）

1. **定位**：只读枚举窗口，按进程名关键字（`WeChat.exe` / `Weixin.exe`）+ 类名白名单定位微信主窗口（`Qt51514QWindowIcon`）与其渲染子窗口（`MMUIRenderSubWindowHW`）。
2. **识别**：对渲染子窗口**顶部标题区**做局部截图，按 `dpi/96` 归一化后放大 2 倍，送 **PaddleOCR PP-OCRv5 mobile**（Paddle Inference + oneDNN，CPU）识别，得到当前会话名。
3. **判定**：会话名与名单匹配（包含 / 完全相等 / 正则）→ 命中则显示覆盖层。
4. **同步**：覆盖层全程按**物理像素**定位（`GetWindowRect` + `SetWindowPos`），DPI 逐窗口采样（`GetDpiForWindow`），随微信移动 / 最大化实时同步。
5. **失败策略**：识别不确定（未找到 / 最小化 / 非前台 / 隐藏 / 识别为空）一律判为"无法判定"并隐藏覆盖层，**绝不显示"安全"提示**（漏报优先于误报）。

---

## 2. 系统要求

| 项 | 要求 |
|----|------|
| 操作系统 | Windows 10 1809（`10.0.17763`）及以上 / Windows 11，x64 |
| 微信 | PC 微信 4.x（实测 4.1.13.65）；**3.9.x 未实测** |
| 运行库 | **需预装 .NET 10 桌面运行时**（.NET Desktop Runtime x64） |
| 其它 | OCR 模型随程序分发（`models\paddle-ppocrv5-mobile\`），**无需系统 OCR 语言包** |

---

## 3. 快速开始

1. 把**整个发布目录**（`WeChatSendGuard.exe` 连同同目录的 `models\`、`resource\` 与 Paddle/OpenCV 原生 `.dll`）放到任意位置（建议单独建目录；程序会在**同目录**生成 `config.json` 与 `logs\`）。**不要只拷贝 exe** —— OCR 模型与原生库是旁挂文件。
2. 双击运行。首次启动会自动生成 `config.json`，托盘出现图标，并弹出「状态」窗口。
3. 点「**设置…**」→ 在「**风险名单**」中加入你的高风险会话名（例如某个群名），点「**保存并应用**」。
4. 打开微信并切到该会话（保持微信在前台），即会出现红框提醒。

> 提醒**仅在微信处于前台、且会话名识别命中时**才显示。

---

## 4. 日常使用

**托盘图标右键菜单**

| 菜单项 | 作用 |
|--------|------|
| 显示状态窗口 | 打开状态窗口 |
| 启用提醒 | 总开关（勾选 = 启用）；**仅本次运行有效**，不影响 `config.json` |
| 设置… | 打开配置界面 |
| 重载配置 | 重新读取 `config.json`（手工改文件后用这个生效） |
| 打开配置目录 | 在资源管理器中打开程序目录 |
| 退出 | 真正结束进程 |

- **关闭状态窗口** ≠ 退出：程序会收进托盘继续运行（首次会弹一次气泡提示）。
- 状态窗口实时显示：微信窗口状态、渲染区（物理矩形）、DPI、识别文本、风险判定、覆盖层状态、识别耗时。

**提醒方式**

- **红框**：贴合微信渲染区，颜色/粗细/是否显示文字标签可配。
- **GIF 悬浮窗**：默认使用 `resource\cat-run.gif`（相对程序的路径），相对微信渲染区左上角按 `dpi/96` 偏移（默认 `750, 450`），尺寸同样按 DPI 缩放；`gif.file` 留空则使用内置循环动画。

---

## 5. 配置

两种方式，内容等价：

- **设置界面（推荐）**：托盘「设置…」或状态窗口「设置…」。在 `GuardConfig` 副本上编辑，「保存并应用」写回 `config.json` 并**热重载**，「取消」丢弃改动。
- **直接编辑 `config.json`**：改完用托盘「重载配置」生效。字段说明见 [用户手册.md](./用户手册.md) 第 4 节；`ocr` 段的微调方法见 `WeChatSendGuard.App/高级配置说明.md`（随程序输出）。

---

## 6. 已知限制与边界

- **会话名识别是弱信号**：主题、DPI、微信 UI 版本任一变化都可能导致识别率下降；**超长会话名被微信用省略号截断时，全串匹配必然失败（漏报）**。
- **不提供"群 / 私"类型判别**：视觉通道无法提供该能力，风险与否完全由你维护的名单决定。
- **识别较慢**：单次 OCR 约 600 ms（PaddleOCR CPU 推理的固有开销），故默认识别间隔为 800 ms、去抖 2 次 → 切换会话后提醒最长可能延迟约 1.6 s 才出现。这是为换取 100% 识别准确率而接受的代价（详见 [Spike-05验证设计.md](./Spike-05验证设计.md) ⑨-补）。
- **不显示"安全"确认**：覆盖层只在命中时出现；未出现 = 未命中或无法判定，二者视觉上不做区分（设计如此，避免错误安全感）。
- **未实测项**：跨屏**混合 DPI**、微信 3.9.x、微信进程真实崩溃路径（详见 [Spike-05验证设计.md](./Spike-05验证设计.md) 12.5 / 12.7）。
- 不拦截发送、不读取聊天内容、不监听键盘。

---

## 7. 目录结构

```text
WeChatSendGuard/
├─ WeChatSendGuard.App/            WPF 主程序（含 Properties/PublishProfiles 发布配置）
├─ WeChatWindowInspector/          窗口侦察工具（技术验证用，可独立运行）
├─ models/                         OCR 模型（paddle-ppocrv5-mobile\{det,rec}，两项目共用）
├─ resource/                       示例 GIF 与图标（cat-run.gif / daily.gif / rabbit.gif / wcsg.ico）
├─ README.md                       本文件
├─ 用户手册.md                      面向使用者的操作说明
├─ 需求文档v1.1.md                  需求与开发计划（含任务进度与测试结果）
├─ 需求审议v1.1.md                  需求评审意见
└─ Spike-05验证设计.md              识别通道验证设计与实测结论（含负向测试矩阵）
```

---

## 8. 构建与发布

> 下面的命令请在 **`WeChatSendGuard.App` 项目目录**下执行（发布配置文件 `Properties\PublishProfiles\win-x64.pubxml` 属于该项目）。

```powershell
# 开发构建
dotnet build -c Release

# 框架依赖单文件发布（目标机需预装 .NET 10 桌面运行时）
# ⚠ 先删除旧的发布目录再发布：
#   dotnet publish 只做增量复制，不会修补"已经残缺"的发布目录，也不会清理旧文件；
#   若直接对残缺目录重发布，MSBuild 会按旧缓存跳过复制，导致 models\ 为空、缺原生 dll。
Remove-Item -Recurse -Force .\bin\Release\publish\win-x64 -ErrorAction SilentlyContinue
dotnet publish -c Release -p:PublishProfile=win-x64
```

发布完成后请核对 `bin\Release\publish\win-x64\` 下确实含 `models\paddle-ppocrv5-mobile\{det,rec}`、`resource\` 与 Paddle/OpenCV 原生 `.dll`——**缺任一都不能运行（仅 exe 无法运行）**。

产物：`WeChatSendGuard.App\bin\Release\publish\win-x64\WeChatSendGuard.exe` —— 框架依赖发布；托管程序集打进单文件，**Paddle / OpenCV 原生库（`.dll`）与 `models\`、`resource\` 作为旁挂文件保留在同目录**。整个输出目录**实测约 414 MB**（原生库合计约 366 MB、`models\` 约 20 MB、单文件 `WeChatSendGuard.exe` 约 27 MB；发布 RID 为 `win-x64`，**仅含 x64 一套原生库，不再携带 x86**）。

说明：

- 目标框架 `net10.0-windows10.0.19041.0`（LTS，支持至 2028-11-14），`SupportedOSPlatformVersion=10.0.17763.0`。
- **WPF 不支持 `PublishTrimmed`**。
- **为什么不用自包含（self-contained）**：实测同一份代码的私有工作集为 —— 框架依赖单文件 34.3 MB、自包含散开文件夹 33.8 MB、单文件自包含（不压缩）52.8 MB、单文件自包含 + 压缩 **140.3 MB**（工作集 322 MB）。`EnableCompressionInSingleFile` 会让运行时常驻私有解压后的全部程序集，直接击穿 5.1 的 100 MB 内存目标，故选框架依赖发布（详见 `需求文档v1.1.md` 9.3）。
- 若目标机无法预装 .NET 10，可临时改为自包含（代价如上）：
  `dotnet publish -c Release -p:SelfContained=true -p:RuntimeIdentifier=win-x64 -p:PublishSingleFile=true`
- **不再依赖 `Windows.Media.Ocr` 与系统中文语言包**；OCR 由 `PaddleOCR PP-OCRv5 mobile`（随程序分发的 `models\` 目录）承担，故**不可启用 `InvariantGlobalization`** 的要求已不适用。

---

## 9. 隐私与安全

- 不注入微信进程（无 `WriteProcessMemory` / `SetWindowsHookEx` / `CreateRemoteThread`）。
- 不修改微信文件、不读取聊天内容（仅对屏幕**顶部标题条**做 OCR）。
- 不联网，无任何网络请求。
