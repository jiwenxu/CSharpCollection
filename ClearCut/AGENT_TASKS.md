# Agent 执行任务表

目标：在不改变现有功能的前提下，提升可读性、可维护性，并做小幅性能优化。

## P0（先做）

- [x] 集中依赖创建，避免多处 `new FfmpegService()/new CommandService()`
- [x] 收敛 `MainWindowViewModel` 的重复 UI 通知逻辑
- [x] 统一临时目录来源，清理与预览路径对齐
- [x] 明确 `MainWindow` 中定时器生命周期（启动/停止）

## P1（随后）

- [x] 移除 UI 层调试 `Console.WriteLine` 噪音
- [x] 非 Windows 平台减少重复 `chmod` 调用
- [x] 保持命令参数构建可读性（不改参数语义）

## 收尾

- [ ] 本地编译验证
- [ ] 关键流程回归检查（加载、压缩、提取、播放控制、退出清理）

