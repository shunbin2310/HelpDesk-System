# Helpdesk System 需求说明

日期：2026-10-23

状态：需求讨论结果；供实现前审核。本文中的“已确认”来自本次讨论，“待确认”不视为已批准功能。

相关设计：[数据库设计草案](database-design.md)

## 1. 系统目标与范围

建立公司内部使用的 IT Helpdesk System。第一版只有一个 IT Support 团队，Employee 提交问题，Support Admin 分配工单，Support IT Staff 处理问题，再由提交人确认关闭。

已确认的技术方向：

| 部分 | 技术 |
| --- | --- |
| Frontend | Vue 3 + TypeScript |
| Backend | C#、.NET 10 Minimal API |
| ORM | Entity Framework Core |
| Database | Microsoft SQL Server |
| 数据库管理工具 | SQL Server Management Studio（SSMS） |
| 代码托管 | GitHub |
| CI/CD | GitHub Actions |
| 容器化 | Docker |

公司项目 `C:\Users\shun bin\source\repos\AHAM - Agent CIF 2026` 是 Solution 分层和工程组织的主要参考，包括 Api、Application、Domain、Infrastructure、Web、EF 启动迁移及 Docker 构建和部署分阶段的做法。`C:\Users\shun bin\source\repos\Project1` 用于参考 JWT、Workflow Template / Instance / History、启动迁移控制及测试等具体实现。Helpdesk 的自动化平台采用 GitHub Actions；公司项目的 Azure Pipelines 配置需要按此适配。

第一版范围为用户与角色、工单、分配、代理、Public Reply、Internal Note、附件、优先级、操作历史及工作流配置。多团队、SLA、邮件或聊天接入、Knowledge Base 等未纳入本轮已确认范围。Dashboard、通知以及服务器、镜像仓库和部署触发方式等细节尚未确认；GitHub、GitHub Actions 和 Docker 的方向已确定。

### Solution 组织建议

```text
HelpDesk-System/
├── HelpDesk.slnx
├── src/
│   ├── HelpDesk.Api/
│   ├── HelpDesk.Application/
│   ├── HelpDesk.Domain/
│   ├── HelpDesk.Infrastructure/
│   └── HelpDesk.Web/
├── tests/
│   ├── HelpDesk.UnitTests/
│   └── HelpDesk.IntegrationTests/
└── docs/
```

Api 引用 Application 和 Infrastructure；Application 引用 Domain；Infrastructure 引用 Application；Domain 不引用其他业务项目。Application 按 Auth、Users、Tickets、Delegations、Workflows 等模块组织 Dto、Interfaces、Mappings、Services。Web 为 Vue 项目，通过 HTTP 访问 Api。EF Core Migrations 放在 Infrastructure，第一版无需独立旧数据迁移项目。

以上是参考公司项目后的组织方式。当前已建立四个 .NET 项目、Vue Web、UnitTests 和 IntegrationTests，并完成阶段 1 数据基础。启动和测试方式见 [README](../README.md)。

Migration 管理参考公司项目 API 启动时查询并应用待执行 Migration 的流程，并结合 Project1 的开关、目标数据库校验及模型检查：开发时生成并提交文件，通过模型检查发现漏写的 Migration；开启启动迁移后，由 Api 的 Program.cs 调用迁移服务，检查并应用目标数据库尚未执行的 Migration，成功后才继续初始化和启动服务。Helpdesk 默认关闭、部署显式开启；具体设计见[实施计划第 15 节](implementation-plan.md#15-ef-core-migration-与启动更新)。阶段 1 已实现并验证启动迁移；GitHub Actions 的 CI 配置仍留在部署阶段。

## 2. 用户与角色

每个用户账号只分配一个角色，采用 Email + Password 登录。Email 唯一，姓名用于显示。账号由 System Admin 创建、分配角色及停用，不开放自行注册；密码以 Hash 保存。

已确认采用 ASP.NET Core Identity 管理账号，并参考 Project1 使用 JWT Bearer 提供 API 身份凭证，具体适配见[实施计划](implementation-plan.md)。每个账号只能关联一个角色；Identity 的标准角色关联结构需要加上单角色约束。阶段 1 已实现 Identity 模型及内部账号存储，尚未接入登录和 JWT API；令牌存储、有效期、退出和会话失效策略仍需在认证阶段确定。

| 能力 | Employee | Support IT Staff | Support Admin | System Admin |
| --- | --- | --- | --- | --- |
| 提交 Ticket | 是 | 否 | 否 | 否；遵守仅 Employee 提交的明确限制 |
| 查看 Ticket | 仅自己提交的 | 仅当前分配给自己的 | 所有 Ticket | 所有 Ticket |
| 分配、重新分配 | 否 | 否 | 是 | 是 |
| Start Processing、Resolve | 否 | 仅当前负责的 Ticket | 不因管理角色自动获得这些动作 | 可管理覆盖执行当前可用动作 |
| Close、Reopen | 仅自己提交、且状态允许的 Ticket | 否 | 不因管理角色自动获得这些动作 | 可管理覆盖执行当前可用动作 |
| 调整优先级 | 否 | 否 | 是 | 是 |
| Public Reply | 自己的 Ticket | 当前负责的 Ticket | 所有 Ticket | 所有 Ticket |
| Internal Note | 不可查看或添加 | 当前负责的 Ticket | 所有 Ticket | 所有 Ticket |
| 设置 Delegation | 否 | 否 | 是 | 是 |
| 管理账号和角色 | 否 | 否 | 否 | 是 |
| 管理、发布 Workflow Template | 否 | 否 | 否 | 是 |

System Admin 的全部业务权限仍受合法流程约束，例如不能从 Open 直接 Close，也不能 Reopen 已 Closed 的 Ticket。仅 Employee 可以提交工单，是对通用管理员权限的明确限制。

Support IT Staff 不设用于提交问题的 My Tickets 入口。Ticket 转派后，新负责人可查看完整 IT 处理历史，原负责人不再可查看或处理；权限以当前负责人为准，而不是以过去是否处理过为准。权限应在列表、搜索、详情、回复、附件和操作接口中一致执行。

## 3. 提交 Ticket

| 字段 | 规则 |
| --- | --- |
| Title | 必填，问题标题 |
| Description | 必填，问题描述 |
| Category | 必填：Hardware、Software、Network、Account、Other |
| Attachments | 可选，截图或相关文件 |

提交人及提交时间由系统记录，不能由浏览器指定其他提交人。新工单进入 Open，默认优先级为 Medium，初始未分配。

优先级为 Low、Medium、High、Critical，由 Support Admin / System Admin 调整。普通 Employee 和 Support IT Staff 不调整优先级。

## 4. 工单流程

只有四个状态：Open、In Progress、Resolved、Closed。没有 Pending，也没有独立的 Reopened 状态。

| 当前状态 | 动作 | 正常操作人 | 结果 |
| --- | --- | --- | --- |
| 尚未创建 | Submit Ticket | Employee | 建立 Ticket 和流程实例，进入 Open |
| Open | Assign / Reassign | Support Admin / System Admin | 更新负责人，保持 Open |
| Open | Start Processing | 当前负责的 Support IT Staff | In Progress |
| In Progress | Resolve | 当前负责的 Support IT Staff | Resolved |
| Resolved | Close | 提交该 Ticket 的 Employee | Closed |
| Resolved | Reopen | 提交该 Ticket 的 Employee | In Progress |

System Admin 可按第 2 节执行管理覆盖，但不能跳过状态规则。Staff 在尚未分配的工单上不能 Start Processing，也不能主动领取工单。

业务流程：

```text
Employee 提交 → Open
                  ↓ Support Admin 分配，保持 Open
               IT Staff 开始处理
                  ↓
              In Progress → Resolve → Resolved → Close → Closed
                   ↑                       │
                   └────── Reopen ──────────┘
```

- Resolved 表示已提供解决方案，等待 Employee 确认，流程仍在运行。
- Employee 认为问题未解决时，可 Reopen，回到 In Progress，由当前负责人继续处理。
- Closed 是流程终点，不能 Reopen；问题再次发生时，Employee 提交新 Ticket。
- Reopen 原因、Resolve 处理说明是否必须填写，尚待确认；可在 Action Template 中表达备注要求。
- 每次流程操作记录前后状态、操作人、时间及操作说明。

## 5. Assignment

Support Admin / System Admin 使用 Ticket Assignment 页面分配和重新分配 Ticket。分配只改变负责人，不改变工单状态，也不会因为新负责人接手就自动回到 Open。

负责人只能是可用的 Support IT Staff。每次分配记录原负责人、新负责人、操作人、时间，以及适用的说明。

对已 Closed 工单不再进行日常转派，是数据库草案的建议约束；是否允许管理员作特殊数据更正，需另行确认。Employee 修改已提交内容、撤回或取消工单的能力尚未确认，不默认加入第一版。

## 6. Delegation

### 6.1 设置与生效

Delegation 由 Support Admin / System Admin 设置，指定原负责人 A、代理人 B、代理开始及结束时间。第一版为同一个 IT Support 团队内的 Staff 之间交接。

生效时：

- 将 A 的所有未完成 Ticket 转派给 B；这里的未完成只包括 Open 和 In Progress。
- 工单状态保持不变，B 获得访问及处理权限，A 失去相应权限。
- A 原有的 Resolved、Closed Ticket 不因代理开始而批量转交。
- 代理期间，新分配给 A 的 Ticket 自动分配给 B，并记录它原本应分配给 A。
- 每张因代理而转交的 Ticket 都要单独记录，供到期转回使用。

### 6.2 到期转回

代理到期时，只考虑本次代理交给 B、且未被后续人工重新安排的 Ticket：

| 到期时状态 | 处理 |
| --- | --- |
| Open | 转回 A，保持 Open |
| In Progress | 转回 A，保持 In Progress |
| Resolved | 转回 A，保持 Resolved |
| Closed | 保留原记录，不再转派 |

“开始交接”和“到期转回”的范围不同：开始时只交接 Open / In Progress；交接后被 B 处理为 Resolved 的 Ticket，到期也要转回 A。

例如 A → B 代理期间，Ticket 001 一直由 B 负责，到期自动转回 A；Ticket 002 后来被 Support Admin 手动转派给 C，则保留 C 的安排，不自动转回 A。B 原本自己的 Ticket 也不会被转给 A。

已转回 A 的 Resolved Ticket 被 Employee Reopen 后，进入 In Progress，由 A 继续处理。所有转交、转回和人工重新分配保留历史。

### 6.3 尚需确定的代理边界

代理重叠、A → B → C 链式代理、提前结束、取消、代理期间人员停用，以及任务调度延迟的处理尚未确认。为保持第一版简单，数据库设计建议先限制重叠和链式代理；这属于待审核建议，不是已确认规则。

## 7. 沟通、附件与历史

| 类型 | 可见范围 |
| --- | --- |
| Public Reply | 提交人、当前负责 Staff、Support Admin、System Admin |
| Internal Note | 当前负责 Staff、Support Admin、System Admin；Employee 不可见 |

回复和备注记录作者及时间。新负责人能查看前任的完整处理记录；Employee 仍只能看到对员工公开的内容。

附件须遵守所属 Ticket 及消息的权限。数据库草案建议附件继承所属消息的 Public / Internal 可见性，避免内部备注附件泄露给 Employee。

历史分工：

- TicketHistories：负责人、优先级、代理相关变化。
- WorkflowHistory：Start Processing、Resolve、Close、Reopen 等步骤变化。

界面可以合并展示时间线，但同一次状态变化不需要在两处重复存储。Employee 的时间线不得暴露 Internal Note 内容或其附件。

附件类型、大小、数量、存储位置，以及消息或附件能否编辑、删除，尚待确认。

## 8. Workflow Template 与 Instance

采用与 Project1 对应的结构：

| 定义 | 每张 Ticket 的副本 |
| --- | --- |
| WorkflowProcessTemplate | WorkflowProcessInstance |
| WorkflowStepTemplate | WorkflowStepInstance |
| WorkflowActionTemplate | WorkflowActionInstance |
| WorkflowActionerTemplate | WorkflowActionerInstance |

另设 WorkflowHistory 记录实际操作。ActionInstance 是动作定义的副本，不是某次点击记录。

Employee 创建 Ticket 时，以当前已发布且启用的模板创建独立流程实例，复制 Steps、Actions、Actioners，并记录模板版本。发布新版本只影响之后创建的 Ticket，旧 Ticket 保留原版本。

第一版预置四个步骤和 Start Processing、Resolve、Close、Reopen 动作。Closed 是唯一终点；Resolved 不是终点。必须允许 Resolved → In Progress 的循环。

Actioner 至少需表达：

- Requester：该 Ticket 的提交人。
- CurrentAssignee：该 Ticket 当前负责的 Support IT Staff，随分配及代理动态变化。
- Role / User：参考 Project1 保留的配置能力，具体开放范围见下文。

不能只给 Start Processing / Resolve 配置一个 Staff 角色就让全部 Staff 执行。后端还必须验证当前负责人；浏览器隐藏按钮不能代替后端授权。

只有 System Admin 可以管理和发布模板。可编辑步骤、动作、角色和备注要求的具体范围仍需确认；模板配置始终不能绕过工单可见性、Closed 终点及仅提交人确认等业务约束，System Admin 管理覆盖除外。

## 9. 第一版验收场景

1. Employee 创建必填字段完整的 Ticket，进入 Open / Medium，提交身份来自当前登录用户。
2. Staff 无法列出或访问其他 Staff 的 Ticket，也不能领取未分配 Ticket。
3. Support Admin 分配后保持 Open；只有负责人或 System Admin 可执行 Start Processing。
4. IT Staff Resolve 后，Employee 可以 Close，或 Reopen 至 In Progress。
5. Closed Ticket 无法 Reopen，Employee 必须新建 Ticket。
6. Employee 无法读取 Internal Note 或其内部附件；转派后旧 Staff 失去访问权限。
7. 代理开始交接全部 Open / In Progress，期间新分给 A 的 Ticket 自动路由给 B。
8. 代理结束将本次代理的 Open / In Progress / Resolved 转回 A；Closed 和后续人工转派的 Ticket 保留。
9. B 原有的 Ticket 不受 A 的代理结束影响，重复执行到期处理不重复转派。
10. 发布新模板版本后，已有 Ticket 的流程副本保持不变；非法动作被后端拒绝。
11. 每个账号只有一个角色，普通用户不能管理模板或创建其他账号。

以上是后续业务实现的验收目标。阶段 1 已通过数据库约束、账号存储和启动迁移测试，但这些完整业务场景仍待后续 API 及页面实现后验收。

## 10. 实现前仍需决定的事项

- Identity 账号初始化、初始密码交付、修改和重置密码；JWT 的令牌存储、有效期、刷新、退出及停用后的会话处理。
- 附件限制、存储方式和消息修改策略。
- 代理边界与调度方式，尤其是人员停用和重叠安排。
- 工作流配置页的可编辑范围，以及哪些动作必须填写说明。
- 工单编号格式、字段长度，以及模板和数据库的部署初始化方式。
- UI 组件库、Dashboard、通知，以及 GitHub Actions 触发方式、镜像仓库、服务器和 SQL Server 部署位置等细节。

这些事项不妨碍审核当前数据库草案，但涉及的实现不得把草案建议当作额外已批准业务功能。
