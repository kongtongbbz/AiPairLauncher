# AiPairLauncher 自动编排模式深度优化方案

> 版本: 2.0 | 目标: 四阶段闭环 + 六角色 SubAgent 协同编排

---

## 一、架构总览

### 1.1 编排流程全景

```
用户在 GUI 撰写需求
        │
        ▼
┌─────────────────────────────────────────────┐
│  Phase 0: 需求注入                            │
│  GUI → Claude（Default Mode，不开 Plan）       │
│  Claude 收到用户需求文本                        │
└───────────────────┬─────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────┐
│  Phase 1: 项目调研                            │
│  Claude 调用 researcher + planner SubAgent    │
│  产出: {project_root}/task.md                 │
└───────────────────┬─────────────────────────┘
                    │ task.md 生成完毕
                    ▼
┌─────────────────────────────────────────────┐
│  Phase 2: 计划编排                            │
│  Claude 进入 Plan Mode                        │
│  读取 task.md → 调用 6 个 SubAgent 协同规划     │
│  产出: 结构化执行计划                           │
│  状态: PendingUserApproval                    │
└───────────────────┬─────────────────────────┘
                    │ 用户确认
                    ▼
┌─────────────────────────────────────────────┐
│  Phase 3: 任务执行                            │
│  Claude 调度 6 个 SubAgent 并行/串行执行        │
│  按 task.md 清单逐项推进                       │
│  实时更新 task.md 进度标记                      │
└───────────────────┬─────────────────────────┘
                    │ 执行完毕
                    ▼
┌─────────────────────────────────────────────┐
│  Phase 4: 复核验收                            │
│  Claude 调用 reviewer + tester SubAgent       │
│  逐项复核 task.md 完成情况                      │
│  未通过项回退 → Phase 3 局部重试               │
│  全部通过 → 标记 task.md 为 ✅ 闭环            │
└─────────────────────────────────────────────┘
```

### 1.2 六角色 SubAgent 定义

| 角色 | 代号 | 职责边界 | 活跃阶段 |
|------|------|----------|----------|
| **Planner** | `planner` | 需求分析、任务拆解、依赖排序、优先级分配 | Phase 1, 2 |
| **Researcher** | `researcher` | 扫描仓库结构、读取配置/文档、查阅外部资料 | Phase 1, 2 |
| **Coder** | `coder` | 编写/修改代码，保持最小变更原则 | Phase 3 |
| **Reviewer** | `reviewer` | 代码审查、边界检查、影响面评估、回归风险 | Phase 4 |
| **Tester** | `tester` | 编写测试、复现验证、回归检查、构建验证 | Phase 3, 4 |
| **Debugger** | `debugger` | 复现 bug、定位根因、修复验证、异常排查 | Phase 3, 4 |

---

## 二、Phase 0 — 需求注入

### 2.0 CLI 免确认原则（核心约束）

> **自动编排模式下，用户无需在 Claude CLI 或 Codex CLI 终端内进行任何手动确认。**
> 所有人工确认点仅存在于 GUI 界面中（Phase 1→2 和 Phase 2→3 的两次确认）。

为实现此约束，CLI 启动参数必须满足：

| CLI | 启动参数 | 效果 |
|-----|----------|------|
| **Claude** | `--dangerously-skip-permissions` 或项目 `.claude/settings.local.json` 中预授权所有必要权限 | Claude 执行 Read/Write/Edit/Bash/Glob/Grep 等工具时不弹出确认 |
| **Codex** | `--full-auto` | Codex 自动执行所有操作，不等待终端确认 |

**权限预授权方案**（推荐，比 skip-permissions 更安全）：

在项目 `.claude/settings.local.json` 中配置：
```json
{
  "permissions": {
    "allow": [
      "Read",
      "Write",
      "Edit",
      "Bash(*)",
      "Glob",
      "Grep",
      "Agent"
    ]
  }
}
```

或者使用 `--permission-mode plan` + 提前信任工作区的方式，确保 Plan Mode 下工具调用无需逐次确认。

### 2.1 GUI 侧行为

1. 用户在 GUI 的"任务描述"文本框中撰写需求
2. 点击"启动自动编排"按钮
3. GUI 构建 Bootstrap 提示词，发送到 Claude pane
4. Claude 启动模式: **Default Mode**（非 Plan Mode），权限已预授权
5. Codex 启动模式: **Full Auto**，全程无终端确认
6. 用户在 CLI 终端内无需任何手动操作

### 2.2 Bootstrap 提示词模板（Phase 0）

```
你现在进入 AiPair 自动编排模式 — Phase 0: 需求注入。

■ 当前工作目录: {workingDirectory}
■ 用户需求:
{taskPrompt}

■ 你的任务:
进入 Phase 1（项目调研），不要开启 Plan Mode。
先充分调研项目，然后在项目根目录生成 task.md 文件。
详细指令见下方 Phase 1 说明。
```

---

## 三、Phase 1 — 项目调研（不开 Plan Mode）

### 3.1 目标

Claude 以 Default Mode 运行，调用 `researcher` 和 `planner` SubAgent 对目标项目进行全面调研，最终在项目根目录生成 `task.md` 文件。

### 3.2 调研清单

Claude 必须按以下顺序完成调研，每项调研结果记入内部上下文：

#### 3.2.1 Researcher SubAgent 调研项

| # | 调研项 | 方法 | 产出 |
|---|--------|------|------|
| R1 | 目录结构 | `Glob` 扫描 `**/*`，排除 `node_modules`/`bin`/`obj` | 顶层目录树 |
| R2 | 技术栈识别 | 读取 `*.csproj`/`package.json`/`pyproject.toml`/`Cargo.toml` | 语言、框架、依赖列表 |
| R3 | 现有文档 | 读取 `README.md`、`CLAUDE.md`、`CONTRIBUTING.md`、`docs/` | 项目约定和规范 |
| R4 | 配置文件 | 读取 `.env.example`、`appsettings.json`、`tsconfig.json` 等 | 环境和构建配置 |
| R5 | 测试体系 | `Glob` 查找 `*Test*`/`*test*`/`*spec*`，读取测试配置 | 测试框架、覆盖范围 |
| R6 | Git 历史 | `git log --oneline -20`、`git branch -a` | 近期变更方向、分支策略 |
| R7 | 关键入口 | `Grep` 搜索 `Main`/`Program`/`App`/`index`/`entry` | 程序入口点 |
| R8 | 需求相关代码 | 根据用户需求关键词 `Grep` 搜索 | 直接相关的文件和函数 |

#### 3.2.2 Planner SubAgent 分析项

| # | 分析项 | 输入 | 产出 |
|---|--------|------|------|
| P1 | 需求拆解 | 用户需求 + 调研结果 | 原子任务列表 |
| P2 | 依赖分析 | 任务列表 | 任务间依赖关系图 |
| P3 | 优先级排序 | 依赖图 + 风险评估 | 有序执行清单 |
| P4 | 影响面评估 | 代码调研 + 任务清单 | 每项任务涉及的文件列表 |
| P5 | 风险标注 | 全部分析 | 高风险项标记和缓解策略 |

### 3.3 task.md 文件规范

生成路径: `{project_root}/task.md`

```markdown
# Task: {用户需求的一句话标题}

> 生成时间: {ISO 8601 时间戳}
> 工作目录: {workingDirectory}
> 状态: PENDING_PLAN

## 需求原文

{用户在 GUI 中输入的完整需求文本}

## 项目概况

- **技术栈**: {语言/框架/版本}
- **入口文件**: {主入口路径}
- **测试框架**: {测试框架名}
- **构建命令**: {构建命令}
- **测试命令**: {测试命令}

## 调研发现

### 关键文件

| 文件 | 用途 | 与需求的关系 |
|------|------|-------------|
| {path} | {description} | {relation} |

### 现有约定

{从 CLAUDE.md / README / 代码风格中提取的关键约定}

### 风险点

{调研中发现的潜在风险}

## 任务清单

### 阶段 1: {阶段名称}

- [ ] **T1.1**: {任务描述}
  - 角色: {coder/tester/debugger}
  - 涉及文件: `{file1}`, `{file2}`
  - 验收标准: {具体可验证的标准}
  - 依赖: 无
  - 风险: {低/中/高} — {风险说明}

- [ ] **T1.2**: {任务描述}
  - 角色: {coder/tester/debugger}
  - 涉及文件: `{file1}`
  - 验收标准: {标准}
  - 依赖: T1.1
  - 风险: {低/中/高}

### 阶段 2: {阶段名称}

- [ ] **T2.1**: {任务描述}
  - 角色: {coder}
  - 涉及文件: `{file1}`
  - 验收标准: {标准}
  - 依赖: T1.2
  - 风险: {低/中/高}

### 阶段 N: 验证与收尾

- [ ] **TN.1**: 全量构建验证
  - 角色: tester
  - 验收标准: `{构建命令}` 成功，无错误
  - 依赖: 前序所有任务

- [ ] **TN.2**: 回归测试
  - 角色: tester
  - 验收标准: `{测试命令}` 全部通过
  - 依赖: TN.1

## 执行约束

- 最小变更原则：只修改与需求直接相关的代码
- 编码规范：遵循项目现有风格（{从调研中提取}）
- 提交粒度：每个阶段完成后可独立提交
- 危险操作：{列出需要用户确认的操作}

## 元数据

- task_count: {总任务数}
- stage_count: {总阶段数}
- estimated_complexity: {简单/中等/复杂}
- high_risk_items: {高风险项数量}
```

### 3.4 Phase 1 完成信号

task.md 生成后，Claude 输出结构化数据包通知 GUI:

```
[AIPAIR_PACKET]
role: claude
kind: stage_plan
phase: phase1_research
stage_id: 1
title: 项目调研完成
summary: <<<SUMMARY
已完成项目调研并生成 task.md，共 {N} 个任务分 {M} 个阶段。
请确认后进入 Phase 2 计划编排。
SUMMARY
scope: <<<SCOPE
task.md 已写入项目根目录
SCOPE
steps: <<<STEPS
1. 完成项目结构扫描
2. 完成技术栈识别
3. 完成需求拆解和依赖分析
4. 生成 task.md
STEPS
acceptance: <<<ACCEPTANCE
1. task.md 文件存在于项目根目录
2. 任务清单完整覆盖用户需求
3. 每项任务有明确的验收标准
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
Phase 1 为调研阶段，无需 Codex 执行。
CODEX_BRIEF
[/AIPAIR_PACKET]
```

### 3.5 GUI 侧行为

1. 解析到 `phase: phase1_research` 的 `stage_plan` → 识别为 Phase 1 完成信号
2. 在右侧面板展示 task.md 生成摘要
3. 等待用户确认 → 触发 Phase 2

---

## 四、Phase 2 — 计划编排（开启 Plan Mode）

### 4.1 目标

Claude 切换到 Plan Mode，读取 task.md，调用 6 个 SubAgent 进行协同规划，产出精细化的执行计划。

### 4.2 Phase 2 触发提示词

用户确认后，GUI 向 Claude 发送:

> 当前实现说明：
> - 继续复用现有 `AutoCollaborationCoordinator`
> - `stage_id` 保持从 `1` 开始，不再使用 `0`
> - 通过新增 `phase` 字段区分 Phase 1/2/3/4
> - App 侧对 `task.md` 只读解析和校验，不直接写回

```
进入 Phase 2: 计划编排。

■ 指令:
1. 读取项目根目录的 task.md 文件
2. 进入 Plan Mode
3. 调用以下 SubAgent 完成计划编排:

  [planner] — 审核任务拆解是否合理，调整顺序和依赖
  [researcher] — 补充调研遗漏，验证技术方案可行性
  [coder] — 评估每项任务的实现路径，标注关键代码位置
  [reviewer] — 预审影响面，标注回归风险和边界条件
  [tester] — 规划测试策略，明确每项任务的验证方法
  [debugger] — 预判潜在故障点，准备排障方案

4. 编排完成后输出结构化计划数据包，等待用户确认
```

### 4.3 六角色协同规划流程

```
                    task.md
                      │
          ┌───────────┼───────────┐
          ▼           ▼           ▼
      [planner]   [researcher]  [coder]
      审核拆解     补充调研      评估路径
          │           │           │
          └─────┬─────┘           │
                ▼                 │
          合并调研与拆解           │
                │                 │
          ┌─────┼─────────────────┘
          ▼     ▼
      [reviewer] + [tester] + [debugger]
      影响面预审   测试策略    故障预判
          │           │           │
          └─────┬─────┘───────────┘
                ▼
        精细化执行计划
        （更新 task.md 状态为 PLANNED）
```

### 4.4 SubAgent 规划输出规范

每个 SubAgent 返回结构化评估:

#### Planner 输出
```yaml
planner_review:
  task_order_changes: [{from: T1.2, to: T1.1, reason: "..."}]
  new_dependencies: [{task: T2.1, depends_on: T1.3, reason: "..."}]
  removed_tasks: [{task: T1.4, reason: "范围溢出"}]
  added_tasks: [{id: T1.5, description: "...", reason: "遗漏"}]
  parallel_groups: [[T1.1, T1.2], [T2.1]]  # 可并行的任务组
```

#### Researcher 输出
```yaml
researcher_supplement:
  missing_context: [{topic: "...", finding: "...", impact: "..."}]
  tech_constraints: ["..."]
  api_changes: [{api: "...", version: "...", breaking: true}]
```

#### Coder 输出
```yaml
coder_assessment:
  implementation_paths:
    T1.1: {approach: "...", key_files: [...], estimated_changes: 3}
    T1.2: {approach: "...", key_files: [...], estimated_changes: 1}
  reuse_opportunities: [{existing: "...", applicable_to: "T1.1"}]
```

#### Reviewer 输出
```yaml
reviewer_preaudit:
  high_risk_changes: [{task: T1.1, risk: "...", mitigation: "..."}]
  boundary_conditions: [{task: T1.2, condition: "...", test_needed: true}]
  regression_areas: ["..."]
```

#### Tester 输出
```yaml
tester_strategy:
  test_plan:
    T1.1: {type: "unit", framework: "xUnit", cases: ["..."]}
    T1.2: {type: "integration", setup: "...", cases: ["..."]}
  build_verification: {command: "...", expected: "success"}
```

#### Debugger 输出
```yaml
debugger_forecast:
  likely_failures: [{task: T1.1, scenario: "...", diagnostic: "..."}]
  environment_risks: ["..."]
  rollback_strategy: "..."
```

### 4.5 计划编排完成信号

Claude 汇总六角色输出后，更新 task.md 状态为 `PLANNED`，并输出数据包:

```
[AIPAIR_PACKET]
role: claude
kind: stage_plan
phase: phase2_planning
stage_id: 1
title: Phase 2 计划编排完成
summary: <<<SUMMARY
已完成六角色协同规划。共 {N} 个任务，{M} 个阶段。
{P} 个任务可并行执行，{H} 个高风险项已标注缓解策略。
task.md 已更新为 PLANNED 状态。
请确认后进入 Phase 3 执行。
SUMMARY
scope: <<<SCOPE
task.md 中所有任务已分配角色、排序、评估风险
SCOPE
steps: <<<STEPS
1. planner 审核并调整任务拆解
2. researcher 补充调研遗漏
3. coder 评估实现路径
4. reviewer 预审影响面
5. tester 规划测试策略
6. debugger 预判故障点
7. 汇总并更新 task.md
STEPS
acceptance: <<<ACCEPTANCE
1. task.md 状态已更新为 PLANNED
2. 每项任务有明确的实现路径和验证方法
3. 高风险项有缓解策略
4. 并行任务组已标注
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
等待用户确认后进入 Phase 3 执行阶段。
Codex 将按 task.md 清单逐阶段执行。
CODEX_BRIEF
[/AIPAIR_PACKET]
```

### 4.6 GUI 侧行为

1. 解析到 Phase 2 完成的 `stage_plan` → 展示计划摘要
2. 在"待审批计划"卡片中展示:
   - 任务总数、阶段数、并行组数
   - 高风险项列表
   - 预估复杂度
3. 用户可查看完整 task.md 内容
4. 用户确认 → 触发 Phase 3
5. 用户退回 → 发送修订提示词，回到 Phase 2 重新规划

---

## 五、Phase 3 — 任务执行

### 5.1 目标

Claude 调度 6 个 SubAgent 按 task.md 清单逐项执行，实时更新进度标记。

### 5.2 Phase 3 触发提示词

用户确认后，GUI 向 Claude 和 Codex 分别发送执行指令。

#### Claude 侧提示词
```
进入 Phase 3: 任务执行。

■ 指令:
1. 读取 task.md 中状态为 PLANNED 的任务清单
2. 按阶段顺序，调度对应角色的 SubAgent 执行:

  [coder] — 执行代码编写/修改任务
  [tester] — 执行测试编写和验证任务
  [debugger] — 处理执行中遇到的异常

3. 同一阶段内的独立任务可并行执行
4. 每完成一项任务，立即更新 task.md:
   - [ ] → [x] 标记完成
   - 在任务下方追加执行摘要
5. 遇到阻塞时标记 ⚠️ 并报告
6. 每个阶段完成后输出 execution_report
```

#### Codex 侧提示词
```
你现在是执行者 Codex。Claude 会分阶段向你发送具体的代码执行指令。
每次收到指令后:
1. 执行代码修改
2. 运行验证命令
3. 输出 execution_report 结构化数据包
```

### 5.3 执行调度规则

```
Phase 3 执行引擎
│
├─ 阶段 1 任务组
│   ├─ [coder] T1.1 (并行)──┐
│   ├─ [coder] T1.2 (并行)──┤── 阶段 1 汇总
│   └─ [tester] T1.3 (串行)─┘
│
├─ 阶段 2 任务组（依赖阶段 1 完成）
│   ├─ [coder] T2.1──┐
│   └─ [coder] T2.2──┤── 阶段 2 汇总
│                     │
│   如遇异常:         │
│   └─ [debugger] 介入修复──→ 重新验证
│
├─ ...
│
└─ 最终阶段: 验证收尾
    ├─ [tester] 全量构建验证
    └─ [tester] 回归测试
```

### 5.4 task.md 实时更新格式

执行过程中，task.md 的任务项会被实时更新:

```markdown
### 阶段 1: 核心功能实现

- [x] **T1.1**: 添加 XxxService 类
  - 角色: coder
  - 涉及文件: `Services/XxxService.cs`
  - 验收标准: 类文件存在且编译通过
  - 依赖: 无
  - 风险: 低
  - **执行结果**: ✅ 已创建，编译通过 | 执行者: coder | 耗时: ~2min

- [ ] **T1.2**: 注册依赖注入  ⚠️ 执行中
  - 角色: coder
  - 涉及文件: `App.xaml.cs`
  - 验收标准: 服务可在 ViewModel 中注入
  - 依赖: T1.1
  - 风险: 低
```

### 5.5 阶段执行完成信号

每个阶段完成后，Codex 输出:

```
[AIPAIR_PACKET]
role: codex
kind: execution_report
stage_id: {当前阶段号}
status: {success/failure/blocked}
summary: <<<SUMMARY
阶段 {N} 执行完毕，{完成数}/{总数} 任务成功。
SUMMARY
completed: <<<COMPLETED
1. T{N}.1: {完成描述}
2. T{N}.2: {完成描述}
COMPLETED
verification: <<<VERIFICATION
1. 构建验证: {通过/失败}
2. 单元测试: {通过/失败}
VERIFICATION
blockers: <<<BLOCKERS
{无 / 阻塞描述}
BLOCKERS
review_focus: <<<REVIEW_FOCUS
{需要重点复核的变更}
REVIEW_FOCUS
body: <<<BODY
{详细执行日志}
BODY
[/AIPAIR_PACKET]
```

---

## 六、Phase 4 — 复核验收

### 6.1 目标

Claude 调用 `reviewer`、`tester`、`debugger` SubAgent 对执行结果进行全面复核，逐项验证 task.md 中的完成状态。

### 6.2 Phase 4 触发条件

- Phase 3 所有阶段的 `execution_report` 收集完毕
- Claude 自动进入 Phase 4，无需用户触发

### 6.3 复核流程

```
Phase 3 执行结果
        │
        ▼
┌───────────────────────────────────────┐
│  [reviewer] 代码审查                    │
│  - 逐文件 diff 审查                     │
│  - 检查编码规范合规性                    │
│  - 检查安全漏洞（OWASP Top 10）         │
│  - 检查类型安全和空值处理                │
│  - 标注需要修改的问题                    │
└───────────────┬───────────────────────┘
                │
                ▼
┌───────────────────────────────────────┐
│  [tester] 验证测试                      │
│  - 运行构建命令                          │
│  - 运行全量测试                          │
│  - 验证每项任务的验收标准                 │
│  - 检查是否有回归                        │
└───────────────┬───────────────────────┘
                │
                ▼
┌───────────────────────────────────────┐
│  [debugger] 异常排查                    │
│  - 检查执行日志中的异常                  │
│  - 验证边界条件                          │
│  - 确认无资源泄漏                        │
└───────────────┬───────────────────────┘
                │
                ▼
┌───────────────────────────────────────┐
│  汇总复核结果                            │
│  ├─ 全部通过 → 更新 task.md 为 DONE     │
│  └─ 有未通过项 → 生成修复清单            │
│     → 回退到 Phase 3 局部重试            │
└───────────────────────────────────────┘
```

### 6.4 复核结果格式

#### 通过时

```
[AIPAIR_PACKET]
role: claude
kind: review_decision
stage_id: {最终阶段号}
decision: complete
body: <<<BODY
Phase 4 复核完成。

■ 复核摘要:
- reviewer: {通过数}/{总数} 项通过代码审查
- tester: 构建成功，{通过数}/{总数} 测试通过
- debugger: 无异常发现

■ task.md 已更新:
- 所有任务标记为 ✅
- 状态更新为 DONE
- 总计完成 {N} 个任务，{M} 个阶段

编排闭环完成。
BODY
[/AIPAIR_PACKET]
```

#### 有未通过项时

```
[AIPAIR_PACKET]
role: claude
kind: review_decision
stage_id: {当前阶段号}
decision: retry_stage
title: 复核发现问题，需要局部修复
summary: <<<SUMMARY
复核发现 {N} 个问题需要修复。
SUMMARY
steps: <<<STEPS
1. [coder] 修复 T1.2: {问题描述}
2. [tester] 重新验证 T1.2 的验收标准
3. [reviewer] 确认修复无副作用
STEPS
acceptance: <<<ACCEPTANCE
1. 所有标记的问题已修复
2. 构建和测试全量通过
3. 无新增回归
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
{具体的修复指令}
CODEX_BRIEF
body: <<<BODY
复核详情:
{逐项问题列表}
BODY
[/AIPAIR_PACKET]
```

### 6.5 task.md 最终状态

复核全部通过后，task.md 更新为:

```markdown
# Task: {标题}

> 生成时间: {生成时间}
> 完成时间: {完成时间}
> 工作目录: {workingDirectory}
> 状态: ✅ DONE

## 执行摘要

| 指标 | 值 |
|------|-----|
| 总任务数 | {N} |
| 成功完成 | {N} |
| 重试次数 | {R} |
| 总阶段数 | {M} |
| 复核通过 | ✅ |

## 任务清单

### 阶段 1: ...
- [x] **T1.1**: ... ✅
- [x] **T1.2**: ... ✅

### 阶段 2: ...
- [x] **T2.1**: ... ✅

### 最终阶段: 验证与收尾
- [x] **TN.1**: 全量构建验证 ✅
- [x] **TN.2**: 回归测试 ✅

## 复核报告

### Reviewer
{代码审查结论}

### Tester
{测试验证结论}

### Debugger
{异常排查结论}
```

---

## 七、GUI 状态机扩展

### 7.1 新增状态

在现有 `AutomationStageStatus` 基础上扩展:

```csharp
enum AutomationStageStatus
{
    // === 现有状态 ===
    Idle,
    BootstrappingClaude,
    WaitingForClaudePlan,
    PendingUserApproval,
    WaitingForCodexReport,
    WaitingForClaudeReview,
    Completed,
    PausedOnError,
    Stopped,

    // === Phase 扩展状态 ===
    Phase1_Researching,           // Phase 1: 调研中
    Phase1_GeneratingTaskMd,      // Phase 1: 生成 task.md
    Phase1_WaitingConfirm,        // Phase 1: 等待用户确认 task.md
    Phase2_Planning,              // Phase 2: 六角色协同规划中
    Phase2_WaitingConfirm,        // Phase 2: 等待用户确认计划
    Phase3_Executing,             // Phase 3: 执行中
    Phase3_StageComplete,         // Phase 3: 当前阶段完成
    Phase4_Reviewing,             // Phase 4: 复核中
    Phase4_PartialRetry,          // Phase 4: 局部重试
}
```

### 7.2 状态转换矩阵

```
Idle
  → Phase1_Researching          (用户点击启动)

Phase1_Researching
  → Phase1_GeneratingTaskMd     (调研完成)
  → PausedOnError               (调研失败)

Phase1_GeneratingTaskMd
  → Phase1_WaitingConfirm       (task.md 生成完毕)
  → PausedOnError               (生成失败)

Phase1_WaitingConfirm
  → Phase2_Planning             (用户确认)
  → Phase1_Researching          (用户退回，重新调研)
  → Stopped                     (用户取消)

Phase2_Planning
  → Phase2_WaitingConfirm       (规划完成)
  → PausedOnError               (规划失败)

Phase2_WaitingConfirm
  → Phase3_Executing            (用户确认)
  → Phase2_Planning             (用户退回，重新规划)
  → Stopped                     (用户取消)

Phase3_Executing
  → Phase3_StageComplete        (当前阶段完成)
  → PausedOnError               (执行异常)

Phase3_StageComplete
  → Phase3_Executing            (进入下一阶段)
  → Phase4_Reviewing            (所有阶段完成)

Phase4_Reviewing
  → Completed                   (全部通过)
  → Phase4_PartialRetry         (有未通过项)
  → PausedOnError               (复核异常)

Phase4_PartialRetry
  → Phase3_Executing            (重新执行未通过项)
  → PausedOnError               (重试异常)

Completed
  → Idle                        (闭环完成，可启动新任务)
```

---

## 八、提示词工程优化

### 8.1 Phase 1 调研提示词（完整版）

```
你现在进入 AiPair 自动编排模式 — Phase 1: 项目调研。

■ 身份: 你是 Claude，项目调研的领导者。
■ 模式: Default Mode（不要开启 Plan Mode）
■ 当前工作目录: {workingDirectory}
■ 用户需求:
{taskPrompt}

■ 你的任务:
调用 researcher 和 planner SubAgent 完成项目调研，然后在项目根目录生成 task.md。

■ 调研步骤（必须全部完成）:

[researcher] 调研阶段:
1. 扫描项目目录结构（排除 node_modules/bin/obj/.git）
2. 识别技术栈（读取项目配置文件）
3. 读取 README.md、CLAUDE.md 等文档
4. 扫描测试体系
5. 查看最近 20 条 git log
6. 搜索与用户需求直接相关的代码

[planner] 分析阶段:
7. 将用户需求拆解为原子任务
8. 分析任务间依赖关系
9. 评估每项任务的影响面和风险
10. 生成有序执行清单

■ 最终产出:
在 {workingDirectory}/task.md 写入完整的任务清单。
格式严格遵循 task.md 规范（见编排优化文档）。

■ 完成后:
输出 stage_id=0 的 stage_plan 数据包，通知 GUI 调研完成。

■ 禁止事项:
- 不要开启 Plan Mode
- 不要修改任何项目代码
- 不要执行任何构建或测试命令
- 只做调研和文件生成
```

### 8.2 Phase 2 规划提示词（完整版）

```
你现在进入 AiPair 自动编排模式 — Phase 2: 计划编排。

■ 身份: 你是 Claude，编排的领导者。
■ 模式: Plan Mode
■ 当前工作目录: {workingDirectory}

■ 你的任务:
1. 读取 {workingDirectory}/task.md
2. 调用 6 个 SubAgent 协同规划

■ SubAgent 调度:

[planner] — 审核任务拆解，调整顺序和依赖，识别可并行任务组
[researcher] — 补充技术调研，验证方案可行性
[coder] — 评估每项任务的实现路径，标注关键代码修改点
[reviewer] — 预审每项变更的影响面和回归风险
[tester] — 为每项任务规划验证方法
[debugger] — 预判潜在故障场景和排障策略

■ 编排原则:
- 同一阶段内无依赖的任务标记为可并行
- 高风险任务必须有缓解策略
- 每项任务必须有可自动验证的验收标准
- 优先级: 核心功能 > 辅助功能 > 测试 > 文档

■ 完成后:
1. 更新 task.md 状态为 PLANNED
2. 输出 stage_plan 数据包，等待用户确认

■ 数据包格式:
严格遵循 [AIPAIR_PACKET] 格式规范。
```

### 8.3 Phase 3 执行提示词（完整版）

```
你现在进入 AiPair 自动编排模式 — Phase 3: 任务执行。

■ 身份: 你是 Claude，执行调度的领导者。
■ 当前工作目录: {workingDirectory}

■ 你的任务:
按 task.md 清单逐阶段调度 SubAgent 执行。

■ 执行规则:
1. 按阶段顺序执行，同阶段内可并行
2. 每项任务交给对应角色的 SubAgent:
   - [coder]: 代码编写/修改
   - [tester]: 测试编写和运行
   - [debugger]: 遇到异常时介入
3. 每完成一项任务，立即更新 task.md:
   - 将 [ ] 改为 [x]
   - 追加执行结果摘要
4. 遇到阻塞时标记 ⚠️ 并报告
5. 每个阶段完成后通过 Codex 输出 execution_report

■ 最小变更原则:
- 只修改与当前任务直接相关的文件
- 不做范围外的"顺手优化"
- 不引入新依赖除非任务明确要求

■ 执行失败处理:
- 构建失败: [debugger] 定位原因 → [coder] 修复 → [tester] 重新验证
- 测试失败: [debugger] 分析失败原因 → [coder] 修复 → [tester] 重跑
- 依赖冲突: 暂停当前任务，标记 ⚠️，报告详情等待人工介入
```

### 8.4 Phase 4 复核提示词（完整版）

```
你现在进入 AiPair 自动编排模式 — Phase 4: 复核验收。

■ 身份: 你是 Claude，复核验收的领导者。
■ 当前工作目录: {workingDirectory}

■ 你的任务:
调用 reviewer、tester、debugger SubAgent 逐项复核执行结果。

■ 复核步骤:

[reviewer] 代码审查:
1. 逐文件审查所有变更（git diff）
2. 检查编码规范合规性
3. 检查安全性（注入、XSS、敏感信息泄露）
4. 检查类型安全和空值处理
5. 检查边界条件和错误处理

[tester] 验证测试:
6. 运行构建命令
7. 运行全量测试
8. 逐项验证 task.md 中每项任务的验收标准
9. 检查是否有回归（对比测试结果变化）

[debugger] 异常排查:
10. 检查执行过程中的所有异常日志
11. 验证边界条件处理
12. 确认无资源泄漏或性能退化

■ 复核通过条件:
- 所有变更通过代码审查
- 构建成功
- 全量测试通过
- 每项任务的验收标准已验证
- 无新增安全漏洞
- 无资源泄漏

■ 复核未通过时:
1. 生成具体的修复清单
2. 输出 retry_stage review_decision
3. 系统自动回退到 Phase 3 局部重试

■ 复核通过时:
1. 更新 task.md:
   - 所有任务标记 ✅
   - 状态更新为 DONE
   - 追加复核报告
2. 输出 complete review_decision
3. 编排闭环完成
```

---

## 九、数据包协议扩展

### 9.1 新增字段

在现有 `[AIPAIR_PACKET]` 协议基础上，扩展以下字段:

| 字段 | 类型 | 适用包类型 | 说明 |
|------|------|-----------|------|
| `phase` | 标量 | 所有 | 当前阶段: `1`/`2`/`3`/`4` |
| `subagent` | 标量 | 所有 | 执行该包的 SubAgent 角色 |
| `task_ref` | 标量 | execution_report | 关联的 task.md 任务 ID（如 `T1.2`）|
| `parallel_group` | 标量 | stage_plan | 可并行执行的任务组标识 |
| `retry_count` | 标量 | execution_report | 当前任务的重试次数 |
| `task_progress` | 多行 | execution_report | task.md 更新摘要 |

### 9.2 扩展包示例

```
[AIPAIR_PACKET]
role: claude
kind: stage_plan
phase: 3
stage_id: 2
subagent: coder
task_ref: T2.1
parallel_group: group_2a
title: 实现 XxxService.ProcessAsync 方法
summary: <<<SUMMARY
根据 task.md T2.1 的要求，实现异步处理方法。
SUMMARY
scope: <<<SCOPE
Services/XxxService.cs
SCOPE
steps: <<<STEPS
1. 在 XxxService 中添加 ProcessAsync 方法
2. 实现核心业务逻辑
3. 添加日志记录
STEPS
acceptance: <<<ACCEPTANCE
1. 方法签名符合接口定义
2. 编译通过
3. 单元测试覆盖核心路径
ACCEPTANCE
codex_brief: <<<CODEX_BRIEF
在 Services/XxxService.cs 中实现 ProcessAsync 方法。
参考 task.md T2.1 的验收标准。
CODEX_BRIEF
[/AIPAIR_PACKET]
```

---

## 十、保护机制与容错

### 10.1 阶段级保护

| 保护项 | 默认值 | 说明 |
|--------|--------|------|
| Phase 1 调研超时 | 300s | 调研无输出超时 |
| Phase 2 规划超时 | 600s | 规划无输出超时 |
| Phase 3 单阶段超时 | 600s | 单个执行阶段超时 |
| Phase 4 复核超时 | 300s | 复核无输出超时 |
| 最大重试次数/任务 | 2 | 单个任务最多重试 |
| 最大总重试次数 | 6 | 整个编排最多重试 |
| Phase 4→3 回退次数 | 2 | 复核失败最多回退 |

### 10.2 异常处理矩阵

| 异常场景 | 处理策略 | 状态转换 |
|----------|----------|----------|
| task.md 生成失败 | 重试 1 次，仍失败则暂停 | → PausedOnError |
| task.md 格式异常 | 通知 Claude 重新生成 | → Phase1_GeneratingTaskMd |
| 规划 SubAgent 超时 | 跳过超时角色，继续规划 | 保持 Phase2_Planning |
| 执行中构建失败 | [debugger] 介入 → [coder] 修复 | 保持 Phase3_Executing |
| 执行中测试失败 | [debugger] 分析 → [coder] 修复 | 保持 Phase3_Executing |
| 复核发现严重问题 | 生成修复清单，回退 Phase 3 | → Phase4_PartialRetry |
| Phase 4→3 回退超限 | 暂停，等待人工介入 | → PausedOnError |
| WezTerm 断连 | 尝试重连 3 次 | → PausedOnError |
| 数据包解析失败 | 记录错误，等待下一次轮询 | 保持当前状态 |

### 10.3 人工接管入口

以下节点始终保留人工接管能力:

1. **Phase 1 → Phase 2**: task.md 生成后必须用户确认
2. **Phase 2 → Phase 3**: 计划编排后必须用户确认
3. **任何 PausedOnError**: 用户可查看错误详情并决定继续/停止
4. **Phase 4 回退超限**: 用户决定是否继续修复或标记为部分完成
5. **任何时刻**: 用户可点击"停止编排"强制终止

> **注意**：上述所有人工接管点均在 GUI 中操作，用户无需切换到 CLI 终端。
> Claude CLI 和 Codex CLI 全程自动执行，不会弹出任何终端内确认提示。

---

## 十一、实现优先级

### 11.1 第一阶段: 核心链路（MVP）

- [x] **O1**: 实现 Phase 1 调研提示词和 task.md 生成
- [x] **O2**: 实现 task.md 解析器（读取任务清单和状态）
- [x] **O3**: 实现 Phase 2 规划提示词（简化版，先跑通 planner + coder 两个角色）
- [x] **O4**: 实现 Phase 3 执行调度（按阶段串行）
- [x] **O5**: 实现 Phase 4 基础复核（构建+测试通过即完成）
- [ ] **O6**: 实现 task.md 状态更新（完成标记和闭环标记）
- [ ] **O7**: GUI 状态机扩展（新增 Phase 状态和展示）

### 11.2 第二阶段: 完整能力

- [x] **O8**: 实现六角色完整协同规划
- [ ] **O9**: 实现阶段内并行执行
- [ ] **O10**: 实现 Phase 4 → Phase 3 局部回退
- [x] **O11**: 实现数据包协议扩展字段
- [x] **O12**: GUI 展示 task.md 实时进度视图
- [ ] **O13**: 保护阈值可配置化

### 11.3 第三阶段: 体验优化

- [ ] **O14**: task.md 变更历史可视化
- [ ] **O15**: SubAgent 执行并行度自适应
- [ ] **O16**: 编排过程回放
- [ ] **O17**: 编排模板（常见需求类型的 task.md 预设）

---

## 十二、与现有架构的兼容性

### 12.1 复用清单

| 现有组件 | 复用方式 | 需要修改 |
|----------|----------|----------|
| `AutoCollaborationCoordinator` | 扩展状态机 | 新增 Phase 状态和转换逻辑 |
| `AutomationPromptFactory` | 新增 Phase 提示词方法 | 添加 4 个新的 Build 方法 |
| `AgentPacketParser` | 扩展字段解析 | 支持 `phase`/`subagent`/`task_ref` 字段 |
| `AgentPacket` | 扩展模型 | 添加新字段属性 |
| `AutomationEnums` | 扩展枚举 | 添加 Phase 状态枚举值 |
| `WezTermService` | 扩展启动参数 | Claude 添加权限预授权参数，Codex 强制 `--full-auto` |
| `SessionStore` | 扩展快照 | 保存 Phase 状态和 task.md 路径 |
| `MainWindowViewModel` | 扩展绑定 | 新增 Phase 状态展示属性 |

### 12.2 CLI 启动参数变更

自动编排模式下，`WezTermService.StartAiPairAsync` 中 CLI 启动命令需调整：

```csharp
// Claude: 自动编排模式下使用预授权或跳过权限
// 方案 A（推荐）: 项目级预授权 + plan mode
BuildClaudeInteractiveShellCommand:
  & claude --permission-mode plan
  // 前提: .claude/settings.local.json 已配置 allow 规则

// 方案 B（快速但风险高）: 跳过权限检查
  & claude --dangerously-skip-permissions

// Codex: 自动编排模式下强制 full-auto
BuildCodexInteractiveShellCommand:
  & codex --full-auto
```

> **关键约束**: 自动编排模式启动时，GUI 必须确保两个 CLI 都以免确认方式运行。
> 如果用户选择了自动编排，GUI 应忽略用户手动选择的 Claude/Codex 模式，
> 强制使用上述免确认参数。

### 12.2 新增组件

| 组件 | 职责 |
|------|------|
| `TaskMdParser` | 解析 task.md 文件，提取任务清单和状态 |
| `TaskMdSnapshot` | 提供 task.md 的轻量摘要，供 GUI 和恢复流程使用 |
| `AutomationPhase` | 用 `phase` 字段表达宏观阶段，不打破现有 `stage_id` 规则 |
| `AutoCollaborationCoordinator` | 继续承担 Phase 0→1→2→3→4 的宏观流转，不新增独立 `PhaseCoordinator` |

---

## 附录 A: task.md 完整示例

```markdown
# Task: 为 AiPairLauncher 添加深色主题持久化功能

> 生成时间: 2026-03-28T10:30:00+08:00
> 工作目录: D:\MCP\探索\AiPairLauncher
> 状态: PENDING_PLAN

## 需求原文

希望应用的深色主题选择能在关闭后保留，下次启动自动恢复上次的主题设置。

## 项目概况

- **技术栈**: C# / .NET 8.0 / WPF
- **入口文件**: AiPairLauncher.App/App.xaml.cs
- **测试框架**: xUnit (AiPairLauncher.Tests)
- **构建命令**: dotnet build AiPairLauncher.sln
- **测试命令**: dotnet test AiPairLauncher.Tests

## 调研发现

### 关键文件

| 文件 | 用途 | 与需求的关系 |
|------|------|-------------|
| ViewModels/MainWindowViewModel.cs | 主 ViewModel | 包含主题切换逻辑 |
| Services/SessionStore.cs | SQLite 持久化 | 可复用其存储机制 |
| App.xaml | 主题资源定义 | 包含三套主题资源 |

### 现有约定

- 主题切换已在 ViewModel 中实现，但未持久化
- SessionStore 使用 SQLite，可扩展存储用户设置

### 风险点

- 首次启动无持久化记录时需要默认值兜底

## 任务清单

### 阶段 1: 持久化层

- [ ] **T1.1**: 在 SessionStore 中添加用户设置读写方法
  - 角色: coder
  - 涉及文件: `Services/SessionStore.cs`
  - 验收标准: 可读写 theme 键值对
  - 依赖: 无
  - 风险: 低

### 阶段 2: ViewModel 集成

- [ ] **T2.1**: 在 MainWindowViewModel 中接入持久化
  - 角色: coder
  - 涉及文件: `ViewModels/MainWindowViewModel.cs`
  - 验收标准: 切换主题时自动保存，启动时自动恢复
  - 依赖: T1.1
  - 风险: 低

### 阶段 3: 验证与收尾

- [ ] **T3.1**: 全量构建验证
  - 角色: tester
  - 验收标准: dotnet build 成功
  - 依赖: T2.1

- [ ] **T3.2**: 回归测试
  - 角色: tester
  - 验收标准: dotnet test 全部通过
  - 依赖: T3.1

## 执行约束

- 最小变更原则：只动 SessionStore 和 MainWindowViewModel
- 编码规范：遵循现有 C# 风格
- 提交粒度：每个阶段一次提交
- 危险操作：无

## 元数据

- task_count: 4
- stage_count: 3
- estimated_complexity: 简单
- high_risk_items: 0
```

---

## 附录 B: GUI 状态展示建议

### Phase 1 展示

```
┌──────────────────────────────────┐
│ 🔍 Phase 1: 项目调研             │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ 状态: 调研中...                   │
│                                  │
│ ✅ 目录结构扫描                    │
│ ✅ 技术栈识别                      │
│ ⏳ 需求相关代码搜索               │
│ ○ 任务拆解                        │
│ ○ 生成 task.md                   │
└──────────────────────────────────┘
```

### Phase 2 展示

```
┌──────────────────────────────────┐
│ 📋 Phase 2: 计划编排             │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ 状态: 六角色协同规划中...          │
│                                  │
│ ✅ [planner] 任务审核              │
│ ✅ [researcher] 补充调研           │
│ ⏳ [coder] 评估实现路径           │
│ ⏳ [reviewer] 影响面预审           │
│ ○ [tester] 测试策略               │
│ ○ [debugger] 故障预判             │
│                                  │
│ 任务: 4 个 | 阶段: 3 个           │
│ 高风险: 0 个 | 可并行: 2 组       │
│                                  │
│ [确认执行]  [退回修改]  [停止]     │
└──────────────────────────────────┘
```

### Phase 3 展示

```
┌──────────────────────────────────┐
│ ⚙️ Phase 3: 任务执行              │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ 进度: 2/4 任务完成 (50%)          │
│ 当前阶段: 2/3                     │
│                                  │
│ ✅ T1.1 添加用户设置读写方法       │
│ ⏳ T2.1 ViewModel 集成持久化      │
│ ○ T3.1 全量构建验证               │
│ ○ T3.2 回归测试                   │
│                                  │
│ 当前执行者: [coder]               │
│ ━━━━━━━━━━━━━━━━━━━━ 50%        │
└──────────────────────────────────┘
```

### Phase 4 展示

```
┌──────────────────────────────────┐
│ 🔎 Phase 4: 复核验收             │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ 状态: 复核中...                   │
│                                  │
│ ✅ [reviewer] 代码审查通过         │
│ ✅ [tester] 构建成功               │
│ ⏳ [tester] 测试验证中             │
│ ○ [debugger] 异常排查             │
│                                  │
│ 回退次数: 0/2                     │
└──────────────────────────────────┘
```
