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

第一版范围为用户与角色、工单、分配、代理、提交附件、优先级、操作历史及工作流配置。多团队、SLA、邮件或聊天接入、Knowledge Base 等未纳入本轮已确认范围。Dashboard、通知以及服务器、镜像仓库和部署触发方式等细节尚未确认；GitHub、GitHub Actions 和 Docker 的方向已确定。

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

Migration 管理参考公司项目 API 启动时查询并应用待执行 Migration 的流程，并结合 Project1 的目标数据库校验及模型检查：开发时生成并提交文件，通过模型检查发现漏写的 Migration；每次启动 API 时，由 Api 的 Program.cs 调用迁移服务，检查并应用目标数据库尚未执行的 Migration，成功后才继续初始化和启动服务。Helpdesk 不再提供启动迁移开关；具体设计见[实施计划第 15 节](implementation-plan.md#15-ef-core-migration-与启动更新)。阶段 1 已实现并验证启动迁移；GitHub Actions CI 已配置模型检查和独立 SQL 测试，远端首次执行需提交并 Push 后确认，详见[CI 说明](ci.md)。

## 2. 用户与角色

每个用户账号只分配一个角色，采用 Email + Password 登录。Email 唯一，姓名用于显示。账号由 System Admin 创建、分配角色及停用，不开放自行注册；密码以 Hash 保存。

首个 System Admin 使用一次性本地 `bootstrap-admin` 命令创建，Email、姓名和隐藏输入的密码由用户在交互式 Development 终端输入；不在代码中预置固定密码。阶段 2 已实现该命令，操作方式见 README；当前没有保留默认账号或测试账号。

阶段 2 已实现 JWT（60 分钟）、sessionStorage、当前用户查询、角色授权、System Admin 用户管理和用户修改密码。停用、角色改变或密码改变使旧 Token 失效；最后一个可用 System Admin 不能被停用或降级。密码至少 10 位且包含大小写、数字、符号，连续错误 5 次锁定 15 分钟。退出清理浏览器会话，暂不加入 Refresh Token、逐 Token 撤销和邮件找回。

采用 ASP.NET Core Identity 管理账号，并参考 Project1 使用 JWT Bearer 提供 API 身份凭证，具体适配见[实施计划](implementation-plan.md)。每个账号只能关联一个角色，数据库唯一索引及账号服务共同保障；阶段 2 已接入登录和 JWT API，会话策略按上述设置执行。

| 能力 | Employee | Support IT Staff | Support Admin | System Admin |
| --- | --- | --- | --- | --- |
| 提交 Ticket | 是 | 否 | 否 | 否；遵守仅 Employee 提交的明确限制 |
| 查看 Ticket | 仅自己提交的 | 仅当前分配给自己的 | 所有 Ticket | 所有 Ticket |
| 分配、重新分配 | 否 | 否 | 是 | 是 |
| Start Processing、Resolve | 否 | 仅当前负责的 Ticket | 不因管理角色自动获得这些动作 | 可管理覆盖执行当前可用动作 |
| Close、Reopen | 仅自己提交、且状态允许的 Ticket | 否 | 不因管理角色自动获得这些动作 | 可管理覆盖执行当前可用动作 |
| 调整优先级 | 否 | 否 | 是 | 是 |
| 设置 Delegation | 否 | 否 | 是 | 是 |
| 管理账号和角色 | 否 | 否 | 否 | 是 |
| 管理、发布 Workflow Template | 否 | 否 | 否 | 是 |

System Admin 的全部业务权限仍受合法流程约束，例如不能从 Open 直接 Close，也不能 Reopen 已 Closed 的 Ticket。仅 Employee 可以提交工单，是对通用管理员权限的明确限制。

Support IT Staff 不设用于提交问题的 My Tickets 入口。Ticket 转派后，新负责人可查看完整 IT 处理历史，原负责人不再可查看或处理；权限以当前负责人为准，而不是以过去是否处理过为准。权限应在列表、搜索、详情、附件和操作接口中一致执行。

## 3. 提交 Ticket

| 字段 | 规则 |
| --- | --- |
| Title | 必填，问题标题，最多 200 字符 |
| Description | 必填，问题描述，最多 4000 字符 |
| Category | 必填：Hardware、Software、Network、Account、Other |
| Attachments | 可选，截图或相关文件 |

提交人及提交时间由系统记录，不能由浏览器指定其他提交人。新工单进入 Open，默认优先级为 Medium，初始未分配。

已确认编号格式 `HD-20261024-000001`：创建日期按马来西亚时间，后缀使用全局递增 ID，至少 6 位，不按天重置，允许跳号。阶段 4 已实现 Ticket 表单、列表、详情、状态筛选及分页（25 条），并与流程实例、START 历史在同一事务创建；失败完整回滚。

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
- Reopen 原因、Resolve 处理说明必须填写，最多 4000 字符；也适用于已有实例，模板不能放宽此业务规则。其他动作遵循模板说明要求。
- 每次流程操作记录前后状态、操作人、时间及操作说明。

## 5. Assignment

Support Admin / System Admin 使用 Ticket Assignment 页面分配和重新分配 Ticket。分配只改变负责人，不改变工单状态，也不会因为新负责人接手就自动回到 Open。

阶段 4 已实现 Assignments 入口及详情页管理表单，只允许选择启用的 Support IT Staff；分配、转派和优先级变更写入 TicketHistory，同值保存不重复记历史。使用 Ticket RowVersion 拒绝过期编辑；流程动作使用独立的流程 RowVersion。转派后原 Staff 的列表、详情和操作接口均拒绝访问。

负责人只能是可用的 Support IT Staff。每次分配记录原负责人、新负责人、操作人、时间，以及适用的说明。

对已 Closed 工单不再进行日常转派，是数据库草案的建议约束；是否允许管理员作特殊数据更正，需另行确认。Employee 修改已提交内容、撤回或取消工单的能力尚未确认，不默认加入第一版。

## 6. Delegation

### 6.1 设置与生效

Delegation 由 Support Admin / System Admin 设置，指定原负责人 A、代理人 B、代理开始及结束日期，不需要选择时间或 Start now。日期按马来西亚 UTC+8，开始不能早于今天；今天立即生效，未来日期在当天 00:00 生效。结束日期不能早于开始日期，允许同一天，并包含结束当天，次日 00:00 到期。前端与 API 均校验日期，旧安排的时间保持原样。第一版为同一个 IT Support 团队内的 Staff 之间交接。

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

### 6.3 第一版代理边界（2026-10-25 已确认）

同一时间每名 Staff 只能参与一条代理，禁止重叠及 A → B → C 链式代理；不提供修改或删除；尚未开始可 Cancel，有效期间可 End early。相邻时间段允许，但未完成的上一次归还会阻止下一次交接。

Source / Target 必须为启用的 IT Staff。交接前账号异常时暂停交接并提示 Admin；到期 A 无法处理时暂留 B，恢复有效 Staff 身份后自动重试。Admin 也可手动转派其他 Staff，此类安排受 AssignmentVersion 保护。

后台在 API 启动和每 30 秒检查，API 停机时不运行，重启后恢复。若整段代理期已过且从未交接，不追溯交接 A 的现有工单。界面列表、工单明细分页显示，提供 Refresh 和 Needs attention 提示。

### 6.4 取消与提前结束

Support Admin / System Admin 可取消尚未开始的安排（Cancelled，不交接工单），或在有效期间提前结束（停止路由并按到期规则归还）。保留原计划结束时间、停止时间和操作人，禁止物理删除。Completed 不允许再次停止，重复执行同一种已成功操作返回已有结果。

提前结束时 Closed 和人工转派保留；若 A 不可用，先停止路由、保留 B 当前负责的工单并提示 Needs attention，恢复后自动重试。全部待归还项处理完毕后显示 Ended early。操作与归还在同一事务内处理，使用代理 RowVersion 和现有数据库锁协调后台与人工操作。

## 7. 提交附件与处理历史

2026-10-25 已确认精简：取消 Public Reply 和 Internal Note；Employee 只需提交 Title、Description、Category 和可选附件。分配、Start Processing、Resolve、Close、Reopen 保留，Resolve / Reopen 的说明要求保留。

附件仅在创建 Ticket 时上传，最多 5 个、每个 10 MiB；支持 PNG、JPG/JPEG、GIF、WEBP、BMP、PDF、DOC/DOCX、XLS/XLSX、TXT/LOG。提交前允许移除选择，提交后不能追加、编辑或删除。文件位于服务端私有目录，数据库存元数据。附件读取继承工单的当前权限，Closed 的既有附件仍可读取。

处理历史分工：

- TicketHistories：负责人、优先级、代理相关变化。
- WorkflowHistory：Start Processing、Resolve、Close、Reopen 等步骤变化和说明。

界面合并展示时间线，同一次状态变化不在两处重复存储。旧消息和其附件保留在数据库及存储中，但不再通过当前页面或接口提供；不删除历史 Migration。

创建失败整体回滚并清理文件，超过 24 小时的无引用文件由后台核对清理。当前未接入病毒扫描。

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

阶段 3 已实现默认模板初始化及只读查看页。System Admin 在 Workflow 页首次初始化 `HELPDESK_TICKET` V1；重复调用保留现有启用模板，不覆盖版本或启用已停用配置。阶段 5 起 Resolve / Reopen 备注必填，其他动作可按模板要求备注，单次备注最多 4000 字符；完整编辑入口保留到阶段 6。

Workflow Engine 已实现实例复制、可用动作、权限和 History。执行动作时以当前用户身份和数据库当前负责人校验，通过 Ticket 行锁及流程 RowVersion 防止负责人竞争和重复提交。阶段 4 已由内部 Ticket 创建用例接入流程启动，在同一事务创建并完整回滚；前端按钮复用流程 API。

## 9. 第一版验收场景

1. Employee 创建必填字段完整的 Ticket，进入 Open / Medium，提交身份来自当前登录用户。
2. Staff 无法列出或访问其他 Staff 的 Ticket，也不能领取未分配 Ticket。
3. Support Admin 分配后保持 Open；只有负责人或 System Admin 可执行 Start Processing。
4. IT Staff Resolve 后，Employee 可以 Close，或 Reopen 至 In Progress。
5. Closed Ticket 无法 Reopen，Employee 必须新建 Ticket。
6. 取消的消息 API 无法调用，旧消息附件不被当作提交附件暴露；转派后旧 Staff 失去附件访问权限。
7. 代理开始交接全部 Open / In Progress，期间新分给 A 的 Ticket 自动路由给 B。
8. 代理结束将本次代理的 Open / In Progress / Resolved 转回 A；Closed 和后续人工转派的 Ticket 保留。
9. B 原有的 Ticket 不受 A 的代理结束影响，重复执行到期处理不重复转派。
10. 发布新模板版本后，已有 Ticket 的流程副本保持不变；非法动作被后端拒绝。
11. 每个账号只有一个角色，普通用户不能管理模板或创建其他账号。

以上是后续业务实现的验收目标。阶段 1 已通过数据库约束、账号存储和启动迁移测试，但这些完整业务场景仍待后续 API 及页面实现后验收。

## 10. 实现前仍需决定的事项

- Identity 账号初始化、初始密码交付、修改和重置密码；JWT 的令牌存储、有效期、刷新、退出及停用后的会话处理。
- 生产附件持久卷、备份及恶意文件扫描部署。
- 代理边界与调度方式，尤其是人员停用和重叠安排。
- 工作流配置页的可编辑范围，以及哪些动作必须填写说明。
- 模板和数据库的部署初始化方式；附件限额已在阶段 5 确定，工单编号和核心表单长度已在阶段 4 确定。
- UI 组件库、Dashboard、通知，以及 GitHub Actions 触发方式、镜像仓库、服务器和 SQL Server 部署位置等细节。

这些事项不妨碍审核当前数据库草案，但涉及的实现不得把草案建议当作额外已批准业务功能。
