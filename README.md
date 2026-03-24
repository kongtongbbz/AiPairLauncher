# AiPairLauncher

> 一个面向 Windows 的双 AI 协作启动器：左侧 `Claude Code`，右侧 `Codex CLI`。

![AiPairLauncher Preview](assets/AiPairLauncher-preview.png)

AiPairLauncher 是一个桌面程序，用来把日常需要反复手动完成的这几件事收敛成一次点击：

- 打开专用终端窗口
- 固定左 Claude / 右 Codex 的双栏布局
- 记录最近一次会话信息
- 在两个 CLI 之间发送上下文
- 切换下一次启动时使用的工作模式

它基于 WezTerm 做窗口和 pane 管理，但不会去接管或污染你的全局 WezTerm 配置。项目目标不是“再造一个终端”，而是把双 AI 协作工作流做成一个可安装、可分发、可恢复的 Windows 工作台。

## 下载

可以直接从 GitHub Releases 下载最新版本：

- [Releases 页面](https://github.com/kongtongbbz/AiPairLauncher/releases)
- `AiPairLauncher.msi`：适合直接双击安装
- `AiPairLauncher-win-x64.zip`：适合手动解压、脚本安装或分发整套目录

## 功能亮点

- 一键启动双栏终端：左侧 `Claude Code`，右侧 `Codex CLI`
- 使用应用自带的 WezTerm 配置，不修改用户全局配置
- 自动记录最近一次会话，减少重复输入和手动恢复
- 支持从左到右、从右到左发送终端上下文
- GUI 内可切换 Claude / Codex 启动模式
- 自带启动前自检脚本，便于安装前排查环境问题
- 自带无副作用安装测试脚本，便于验证分发链路
- 发布产物为 Windows 自包含目录，目标机器无需安装 .NET SDK 或 .NET Runtime

## 适用场景

- 同时使用 `Claude Code` 和 `Codex CLI` 的开发者
- 想把双 AI 协作流程固定成标准工作台的个人用户
- 需要把这套工作台复制到其他 Windows 电脑上的团队或实验环境

## 平台与依赖

当前支持：

- Windows 10
- Windows 11

目标机器需要提前安装并可直接调用以下命令：

- [WezTerm](https://wezfurlong.org/wezterm/)
- `claude`
- `codex`

如果你是从源码构建，还需要：

- .NET 8 SDK

## 快速开始

### 方式 A：使用已经打包好的发布目录

如果你拿到的是别人已经构建好的发布包，目录结构通常如下：

```text
win-x64/
├─ AiPairLauncher.msi
├─ AiPairLauncher-win-x64.zip
├─ app/
├─ install/
├─ LICENSE
└─ README.md
```

推荐直接双击：

```text
AiPairLauncher.msi
```

如果你更希望保留现有脚本安装方式，也可以执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install\install.ps1
```

安装完成后会：

- 复制程序到 `%LOCALAPPDATA%\AiPairLauncher`
- 创建桌面快捷方式 `AiPairLauncher.lnk`
- 自动使用应用自带图标

之后直接双击桌面快捷方式即可启动。

### 方式 B：从源码构建并安装

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

发布完成后会生成：

```text
artifacts/
└─ win-x64/
   ├─ AiPairLauncher.msi
   ├─ AiPairLauncher-win-x64.zip
   ├─ app/
   ├─ install/
   ├─ LICENSE
   └─ README.md
```

然后你可以任选一种安装方式：

直接双击 MSI：

```text
artifacts\win-x64\AiPairLauncher.msi
```

或者继续使用脚本安装：

```powershell
powershell -ExecutionPolicy Bypass -File .\artifacts\win-x64\install\install.ps1
```

## 日常使用

启动后，你可以在 GUI 中完成这些操作：

- 选择工作目录
- 设置工作区名称
- 选择 `Claude 模式`
- 选择 `Codex 模式`
- 设置右侧 pane 宽度
- 启动新的 Ai Pair 会话
- 查看最近一次会话信息
- 在左右 pane 之间发送上下文

## 模式说明

### Claude 模式

- `Default Mode`
- `Plan Mode`

### Codex 模式

- `Standard`
- `Full Auto`
- `Never Ask`

这些模式只影响“下一次启动的新会话”，不会强制改动已经运行中的终端。

## 自检与安装验证

### 启动前自检

如果想在不弹出任何终端窗口的前提下检查安装结果和依赖环境，可以执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install\preflight-check.ps1
```

它会检查：

- `app\AiPairLauncher.App.exe` 是否存在
- `app\config\app.wezterm.lua` 是否存在
- `launch.cmd` / `preflight-check.ps1` 是否存在
- `wezterm`、`claude`、`codex` 是否可解析
- CLI 版本是否可正常读取

如果已经安装到本机，也可以直接执行：

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\AiPairLauncher\preflight-check.ps1"
```

### 无副作用安装测试

如果你只想验证安装链路，不希望创建桌面快捷方式或污染正式安装目录，可以执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-install.ps1
```

这个脚本会：

- 安装到 `%TEMP%\AiPairLauncher-smoke`
- 跳过桌面快捷方式创建
- 检查关键文件是否完整

## 分发到其他 Windows 电脑

如果你要把这个程序发给其他 Windows 用户，通常只需要提供 `artifacts\win-x64` 目录即可。目标机器上不需要安装 .NET SDK，也不需要额外配置 WezTerm 布局文件。

如果希望对方直接双击安装，优先提供：

- `artifacts\win-x64\AiPairLauncher.msi`
- `artifacts\win-x64\AiPairLauncher-win-x64.zip`

目标机器仍然需要自行准备：

- WezTerm
- Claude Code CLI
- Codex CLI

建议对方安装后先运行一次 `preflight-check.ps1`，确认依赖命令能够被正确解析。

## 设计原则

- 独立 WezTerm GUI 窗口，而不是复用用户已有 mux 状态
- 固定左 Claude / 右 Codex 的协作布局
- 尽量避免对用户现有终端环境造成副作用
- 用最少输入完成会话恢复和上下文转发

## 仓库结构

```text
AiPairLauncher/
├─ AiPairLauncher.App/        # WPF 桌面程序
├─ AiPairLauncher.Setup/      # WiX MSI 安装项目
├─ assets/                    # 图标和预览资源
├─ config/                    # 专用 WezTerm 配置
├─ install/                   # 安装、自检、启动包装器
├─ scripts/                   # 构建、发布、冒烟测试脚本
└─ artifacts/                 # 发布后的可分发目录（生成产物）
```

## 开发说明

### 本地构建

```powershell
dotnet build .\AiPairLauncher.sln
```

### 本地发布

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

该命令会同时生成：

- 目录版发布包
- `AiPairLauncher.msi`
- `AiPairLauncher-win-x64.zip`

如果只想单独重建 MSI，可以执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-msi.ps1
```

### GitHub Actions 自动发布

仓库已经包含基于 tag 的自动发布工作流。推送符合 `vMAJOR.MINOR.PATCH` 格式的 tag 后，会自动：

- 在 Windows runner 上执行 `scripts\publish.ps1`
- 生成 `AiPairLauncher.msi`
- 生成 `AiPairLauncher-win-x64.zip`
- 创建或更新对应的 GitHub Release
- 上传这两个附件到 Release

示例：

```powershell
git tag v1.0.1
git push origin v1.0.1
```

如果你的 `dotnet` 不在 `PATH` 中，也可以直接使用完整路径，例如：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\AiPairLauncher.sln
```

## 常见问题

### 1. 双击桌面快捷方式启动失败

先执行：

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\AiPairLauncher\preflight-check.ps1"
```

如果自检通过但仍有问题，通常可以通过重新发布并重新安装解决：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
powershell -ExecutionPolicy Bypass -File .\artifacts\win-x64\install\install.ps1
```

### 2. GUI 中依赖状态显示为缺失

先点击“刷新依赖”。如果仍然异常，运行 `preflight-check.ps1` 查看 CLI 是否真的可解析。

### 3. 桌面图标没有立即刷新

Windows 有时会缓存快捷方式图标，可以尝试：

- 在桌面空白处按 `F5`
- 重新运行一次安装脚本

## License

本项目采用 `MIT` 许可证，详见根目录 [LICENSE](LICENSE)。
