# Helpdesk System 实施计划

日期：2026-10-24

本计划说明从当前项目骨架到第一版可用系统的开发顺序。创建本计划不代表已经实现后续功能，也不代表数据库草案中的全部字段和待定功能已经确认。

相关文档：[需求说明](requirements.md)、[数据库设计草案](database-design.md)、[启动说明](../README.md)。业务规则以已确认的需求为准；有新决定时先更新文档，再落实到相应阶段。

## 1. 当前进度

| 项目 | 状态 |
| --- | --- |
| 核心角色、Ticket 流程、Assignment、Delegation 规则 | 已讨论并记录；边界事项单独列为待确认 |
| SQL Server 数据库基础 | 已建立 BIN / HelpDeskDb，22 张应用表及 Migration 历史；阶段 1 已验证 |
| 本地开发连接 | BIN / HelpDeskDb / Windows Authentication；数据库已创建并应用全部 Migration |
| 账号基础 | 已实现 Identity 模型、内部账号存储及单角色约束；JWT 和登录 API 待阶段 2 实现 |
| Api / Application / Domain / Infrastructure | 已建立 .NET 10 项目及引用 |
| Web | 已建立 Vue 3 + TypeScript + Vite 骨架 |
| API Health 与开发代理 | 已实现 `/health` 及前端 `/api/health` 代理 |
| 前后端构建与基础连通 | 2026-10-23 已验证；本次编写计划未重新运行服务 |
| 阶段 1 测试 | 11 项单元测试、5 项 SQL Server 集成测试通过；登录和 Ticket 等业务功能仍待后续阶段 |

开发遵循 Api → Application + Infrastructure、Application → Domain、Infrastructure → Application 的引用方向。Frontend 位于 `src/HelpDesk.Web`，通过 HTTP 调用 Api。

参考优先级：公司项目作为分层架构和工程组织的主要参考，Project1 提供 JWT、Workflow、Migration 控制和测试等具体实现参考。Helpdesk 使用 GitHub + GitHub Actions + Docker，具体适配见第 16 节。

## 2. 总体顺序

```text
0. 项目骨架（已完成）
       ↓
1. Domain + EF Core + SQL Server（已完成）
       ↓
2. 登录、权限及用户管理
       ↓
3. Workflow Engine 与默认模板
       ↓
4. Ticket 创建、分配及完整状态流程
       ↓
5. 回复、内部备注、附件及历史页面
       ↓
6. Workflow Template 管理页面
       ↓
7. Delegation 与自动转回
       ↓
8. 第一版整体验收
       ↓
9. GitHub Actions + Docker 部署与发布（环境待确认）
```

每个功能阶段都包含对应 API、前端界面和必要测试。先完成核心 Ticket 闭环，再完成模板管理和自动代理；第一版功能完成需到阶段 8。

## 3. 阶段 0：基础项目骨架

状态：已完成。

- [x] 创建 `HelpDesk.slnx` 和四个 .NET 项目。
- [x] 配置项目引用及统一的 .NET 10 目标框架。
- [x] 创建 Vue 3 + TypeScript 前端。
- [x] 实现 `/health` 和前端开发代理。
- [x] 添加连接检查页面、依赖锁文件及 Git 忽略规则。
- [x] 编写本地启动和构建说明。
- [x] 完成后端 Release 构建、前端类型检查和构建、浏览器基础连通验证。

已有连通页面用于确认前后端可以工作，不代表业务系统或数据库已就绪。

## 4. 阶段 1：Domain、EF Core 与 SQL Server

状态：2026-10-24 已完成数据基础及本阶段验收。

- [x] 确认 BIN / HelpDeskDb / Windows Authentication，创建独立开发数据库。
- [x] 审核本阶段字段长度、外键、唯一性、并发及删除限制；工单编号具体格式、附件和代理政策保留到对应业务阶段确认。
- [x] 使用 ASP.NET Core Identity；角色关联表对 UserId 建唯一索引，内部账号创建和角色调整服务使用事务保证恰好一个角色。
- [x] 建立 Domain 的 Entities、Enums、角色和流程常量，不引用 EF Core 或 Identity。
- [x] 配置六类工单和代理业务表、七张 Identity 表，以及九张工作流定义、实例和历史表。
- [x] 添加 SQL Server Provider、HelpDeskDbContext、配置映射和依赖注入。
- [x] 落实唯一 Email、单工单单流程实例、UTC、rowversion 和同流程复合外键。
- [x] 生成并审核完整 Migration、Designer、Snapshot 文件，应用到本机开发库；文件保留供后续 Git 提交，未代替用户执行 Commit。
- [x] 添加默认关闭、显式开启的启动迁移服务；检查目标数据库，失败阻止启动，无待执行项时跳过。
- [x] 添加仓库级 dotnet-ef 工具清单及设计时 Factory，模型一致性检查通过。
- [x] 更新 README 的连接、启动迁移、生成 Migration 和测试步骤。

本轮新增代码位于 Domain、Application/Users、Infrastructure、Api 的启动配置及两个测试项目。JWT 签发、登录及用户管理 API、Workflow Engine、Ticket 页面和 Delegation 后台任务尚未实现。

验证结果：

| 检查 | 结果 |
| --- | --- |
| Release 构建 | 通过，0 警告、0 错误 |
| 启动迁移单元测试 | 11 / 11 通过 |
| 实际 SQL Server 集成测试 | 5 / 5 通过，无跳过 |
| 模型与 Snapshot | has-pending-model-changes 通过 |
| 数据库 | 22 张应用表 + __EFMigrationsHistory |
| Migration | InitialCreate、AddWorkflowInstanceSequence、FixCurrentStepOwnership 均已应用 |
| 预置数据 | 4 个固定角色；无默认用户、工单或流程实例 |
| 重启与 Health | 无待执行项时跳过，/health 返回 healthy |

真实数据库测试发现并修正了当前步骤可空复合外键与流程主键的相互影响：流程 Id 改用 sequence / HiLo；当前步骤使用独立所属列、CHECK 和复合外键。已应用 Migration 保留，通过追加 Migration 修正；集成测试同时验证合法实例创建与跨工单步骤拒绝。

## 5. 阶段 2：登录、权限与用户管理

目标：让四个角色使用真实账号进入系统，后端能够识别当前用户并限制操作。

已确认 ASP.NET Core Identity + JWT，并限制每个账号一个角色。实现前仍需确认：令牌存储位置、有效期、是否需要 Refresh Token、退出是否要求服务端立即失效、首个 System Admin 的初始化、初始密码交付与重置，以及账号停用和角色变更后的会话处理。Identity 表结构及单角色约束在阶段 1 生成 Migration 前落实。

工作内容：

- [ ] 实现 Email + Password 登录及退出流程。
- [ ] 参考 JwtOptions / JwtTokenService 实现 JWT 签发和 Bearer 验证，配置独立的 Issuer、Audience、SigningKey 和有效期。
- [ ] 使用密码 Hash 组件，规范化 Email，执行唯一性检查。
- [ ] 建立后端 CurrentUserContext 和角色授权策略，身份取自经过验证的凭据。
- [ ] 参考 Project1 的用户可用性及 SecurityStamp 校验，实现经确认的停用、角色变更和会话失效规则。
- [ ] 实现 System Admin 创建账号、分配一个角色、停用账号的 API 和页面。
- [ ] 建立前端登录页、基础导航及路由权限。
- [ ] 参考 Project1 的 authService、API 请求封装和路由守卫，统一附带 Bearer Token 并处理过期、401、403；适配 Helpdesk 现有的 `/api` 开发代理。
- [ ] 按已确认方式初始化首个 System Admin，避免使用提交到仓库的固定默认密码。
- [ ] 建立相应测试项目，验证登录、停用、重复 Email、单角色和越权访问。

完成标准：四种角色可以分别登录；只有 System Admin 可以管理账号；修改浏览器提交的 UserId 或 Role 不能获得额外权限。

## 6. 阶段 3：Workflow Engine 与默认模板

目标：参考 Project1，建立 Helpdesk 自己的 Template + Instance + History 流程引擎。

工作内容：

- [ ] 实现 WorkflowProcess / Step / Action / Actioner 的 Template 与 Instance 处理。
- [ ] 预置 Helpdesk Workflow V1：OPEN、IN_PROGRESS、RESOLVED、CLOSED。
- [ ] 定义 START_PROCESSING、RESOLVE、CLOSE、REOPEN。
- [ ] 支持 Requester 和动态 CurrentAssignee；按已确认配置范围处理 Role / User。
- [ ] 实现启动流程、查询流程及可用动作、执行动作和追加 History。
- [ ] 允许 RESOLVED → IN_PROGRESS，只有 CLOSED 才标记流程 Completed。
- [ ] 复制模板版本到实例，使旧工单不受后续模板发布影响。
- [ ] 使用业务权限检查和并发控制，限制非法跳转及重复提交。
- [ ] 验证模板版本隔离、操作人权限、Reopen 循环和事务回滚。

分配、重新分配和代理不是状态跳转，交由 Ticket / Delegation 业务处理。CurrentAssignee 按 Ticket 当前负责人动态判断，不把 Staff 角色配置为全部可操作。

完成标准：流程引擎可以执行约定的四状态流程，保留完整历史，并拒绝错误身份及不适用于当前状态的动作。

## 7. 阶段 4：Ticket 核心闭环

目标：Employee、Support Admin 和 Support IT Staff 可以完成一张真实工单的全部核心处理过程。

工作内容：

- [ ] 实现 Employee 提交表单：Title、Description、Category；默认 Medium。
- [ ] 在同一事务中创建 Ticket、流程实例和 START 历史，缺少有效模板时整体回滚。
- [ ] 实现列表、详情、分页和必要的状态筛选。
- [ ] Employee 只能看自己的 Ticket；Staff 只能看当前分配给自己的 Ticket；两种 Admin 可看全部。
- [ ] 实现 Support Admin / System Admin 的 Assignment 页面、重新分配及优先级调整。
- [ ] 实现 Start Processing、Resolve、Close、Reopen 的 API 和前端按钮。
- [ ] 按需求落实 System Admin 的管理覆盖，同时保持合法状态限制和仅 Employee 提交的规则。
- [ ] 记录分配和优先级变化，前端展示当前负责人和流程状态。
- [ ] 验证负责人变化后，原 Staff 的列表、详情和操作权限立即失效。

核心演示路径：

```text
Employee 创建 → Open / Medium
Support Admin 分配 → 保持 Open
负责人 Start Processing → In Progress
负责人 Resolve → Resolved
Employee Close → Closed
```

另一条验收路径是 Resolved → Employee Reopen → In Progress → 再次 Resolve。Closed 不允许 Reopen，需要新建 Ticket。

完成标准：不同角色在界面中走通上述流程；直接调用 API 也不能越权或跳过状态。附件功能在阶段 5 补齐，本阶段不视为完整第一版。

## 8. 阶段 5：沟通、附件与历史

目标：员工与 IT 可以围绕工单沟通，IT 可以保留内部处理资料。

实现前确认：附件类型、大小、数量和存储方式，回复及附件是否可以编辑或删除，Resolve / Reopen 是否必须填写说明。

工作内容：

- [ ] 实现 Public Reply 和 Internal Note 的 API、输入区和时间线。
- [ ] 实现 Ticket 创建和回复中的附件上传、查看与下载。
- [ ] 附件继承 Ticket 及所属消息的可见性，内部附件也执行后端权限检查。
- [ ] 合并展示 TicketHistories 与 WorkflowHistory，明确动作、负责人变化及发生时间。
- [ ] 新负责人能看到完整 IT 处理历史；Employee 的回复和历史查询不会暴露内部内容。
- [ ] 验证 Employee 看不到 Internal Note，旧负责人无法通过附件 Id 下载文件。

完成标准：提交、回复、内部协作和交接资料可用，消息与附件权限一致，失败的上传不会产生无法解释的工单或文件记录。

## 9. 阶段 6：Workflow Template 管理

目标：System Admin 可以管理流程定义及版本，已有 Ticket 仍使用自己的流程副本。

实现前确认：第一版允许编辑哪些步骤、动作、操作人和说明要求；新增状态是否开放。没有额外决定时，保持已确认的四状态业务语义。

工作内容：

- [ ] 实现仅 System Admin 可访问的模板列表、详情、草稿编辑及发布页面。
- [ ] 实现从已发布版本建立新草稿版本。
- [ ] 已发布定义保持不变；发布新版本时切换新 Ticket 使用的启用版本。
- [ ] 校验步骤关联、初始步骤、终点、动作唯一性和可达路径，同时允许 Reopen 循环。
- [ ] 禁止模板配置绕过 Ticket 可见性、负责人和提交人等业务约束。
- [ ] 展示模板版本、步骤、动作及允许的 Actioners。
- [ ] 验证旧工单沿用 V1，新工单使用已发布的 V2。

完成标准：管理员能通过页面维护经确认范围内的定义，版本发布不改变正在处理的工单。

## 10. 阶段 7：Delegation 与自动转回

目标：休假或临时不在时，工单可以自动交接，并在代理结束时按已确认规则归还。

实现前确认：代理重叠或链式代理、取消或提前结束、代理双方停用、后台调度延迟及恢复的处理。

工作内容：

- [ ] 实现 Support Admin / System Admin 设置 A、B、开始及结束时间的页面和 API。
- [ ] 生效时交接 A 的全部 Open / In Progress，保持原状态。
- [ ] 有效期间，新分配给 A 的 Ticket 实际路由给 B。
- [ ] 逐 Ticket 写入 DelegationTickets，区分本次代理工单与 B 原有工单。
- [ ] 到期转回本次代理的 Open / In Progress / Resolved，保持原状态；Closed 不转回。
- [ ] 保留 Admin 后来作出的人工重新分配，例如 A → B 后再转给 C。
- [ ] 实现后台开始、到期处理及失败恢复；同一任务重试不得重复转派或重复记录结果。
- [ ] 保证交接、转回、历史和处理标记在事务内保持一致，并处理与人工分配的并发。
- [ ] 验证交接后 A 无权查看，转回后 B 无权查看；Resolved 转回 A 后 Reopen 由 A 继续处理。

完成标准：现有工单、新分配工单、Resolved 到期转回、Closed 保留、人工转派、重复任务等场景均通过测试。

## 11. 阶段 8：整体验收

目标：确认第一版约定功能完整、权限一致，并能从空开发环境复现。

- [ ] 使用四个角色完成一次完整界面验收，并覆盖 Reopen 和 Delegation 两条分支。
- [ ] 按需求说明逐项执行验收场景，核对状态、负责人、历史及附件可见性。
- [ ] 验证并发操作、重复提交、账号停用和模板版本变化。
- [ ] 完成 Domain / Application 的业务规则测试，以及 API、数据库和核心前端流程的集成测试。
- [ ] 在专用 SQL Server 测试环境验证 Migration、外键、唯一性及并发标记。
- [ ] 验证启动迁移默认关闭、无待执行项时跳过、目标数据库错误时拒绝执行，以及迁移失败时阻止启动；执行模型与 Migration Snapshot 一致性检查。
- [ ] 验证前后端构建，以及全新环境安装依赖和初始化数据库的步骤。
- [ ] 整理错误提示、表单校验、空列表、加载状态和小屏基本可用性。
- [ ] 更新 README、配置示例和开发运行说明，记录已知限制。

完成标准：核心验收场景通过，文档与实际功能一致。测试应针对业务、权限和失败路径，不以重复实现细节的测试代替验收。

## 12. 阶段 9：部署与发布

技术方向已确定为 GitHub、GitHub Actions、Docker。服务器、镜像仓库、触发分支、自动部署条件及 SQL Server 部署位置待确认后展开，不沿用其他仓库的环境配置。

- [ ] 确认服务器、SQL Server 所在位置、域名及 HTTPS 方案。
- [ ] 参考公司项目编写前后端独立的多阶段 Dockerfile，以及适合目标环境的容器编排配置。
- [ ] 建立 GitHub Actions Workflow：后端构建与测试、前端类型检查与构建、必要测试及 Migration 模型检查通过后，再构建并推送镜像。
- [ ] 确认镜像仓库、部署触发方式、反向代理和目标环境，并实现对应 CD 步骤。
- [ ] 配置生产前端与 API 路由；开发 Vite 代理不能直接作为生产部署方案。
- [ ] 配置生产凭据、首个管理员、附件持久化、日志及后台代理任务运行方式。
- [ ] 按第 15 节配置启动迁移的显式开关、预期数据库、命令超时和数据库权限，并确定备份、恢复和发布失败后的处理。
- [ ] 在 GitHub Actions 中加入 `migrations has-pending-model-changes`，避免模型变化缺少 Migration 文件却继续发布。
- [ ] 在测试环境验证构建、部署、重启、文件保留和到期代理恢复。
- [ ] 完成发布后的登录、工单及代理检查。

具体服务器、资源费用和自动化配置在本阶段开始前另行决定。

## 13. 每个阶段的工作方式

1. 开始前说明本阶段范围，逐一确认会影响业务结果的未决问题；常规代码组织按已确认架构处理。
2. 给出具体实施计划，确认后修改代码，不把未来阶段的功能同时加入。
3. 按功能完成前后端及必要测试，检查相关失败和越权路径。
4. 用中文交付：修改文件、关键变化、可测试步骤、验证结果和剩余限制。
5. 完成验收后更新本计划的状态及相关文档，不提前勾选未完成项目。

下一步为阶段 2：确认凭据和首个管理员初始化细节，再实现登录、JWT、CurrentUserContext 及用户管理 API / 页面。阶段 1 数据基础已经完成。

## 14. Project1 功能参考清单

参考来源：本机 `C:\Users\shun bin\source\repos\Project1`。2026-10-24 已查看下列实现；Identity 数据基础及启动迁移已经适配，JWT 和其他业务部分仍按对应阶段实施。

| 功能 | Project1 参考位置 | Helpdesk 适配重点 |
| --- | --- | --- |
| JWT 配置与签发 | `backend/Project1.Api/Authentication/JwtOptions.cs`；`Services/Authentication/JwtTokenService.cs` | 参考 Issuer、Audience、签名、有效期、用户与角色 Claims；采用 Helpdesk 自己的配置 |
| Bearer Token 验证 | `backend/Project1.Api/Program.cs` 的 AddJwtBearer / OnTokenValidated | 验证 Token，并结合当前账号可用性和会话版本；注册放在 Helpdesk Api 的启动或扩展方法中 |
| 登录与用户查询 | `backend/Project1.Api/Services/Authentication/AuthService.cs`；`Controllers/AuthController.cs` | 参考 Email 登录、密码验证、登录失败处理及当前用户查询；HTTP 入口改为 Minimal API Endpoints |
| 当前登录身份 | `backend/Project1.Api/Services/Authentication/CurrentUserContext.cs` | 保留 UserId、姓名及单个角色；不引入 Project1 的 Department 或采购角色 |
| 用户管理与会话失效 | `backend/Project1.Api/Services/Users/UserManagementService.cs` | 参考账号创建、停用、角色调整后更新 SecurityStamp 的方式；保持 Helpdesk 单角色规则 |
| 前端登录与请求 | `frontend/src/stores/auth.ts`；`services/authService.ts`、`authSession.ts`、`apiClient.ts`；`router/index.ts` | 参考登录状态、Authorization Header、统一错误和路由权限；角色、存储键及 API 路径按 Helpdesk 调整 |
| Workflow | `backend/Project1.Api/Entities/Workflows/`；`Services/Workflows/WorkflowEngine.cs`；`Services/WorkflowTemplates/WorkflowTemplateService.cs` | 参考 Template / Instance / History，支持 CurrentAssignee 和 Reopen 循环 |
| EF Core 启动迁移 | `backend/Project1.Api/Data/StartupDatabaseMigration.cs`；`Program.cs` | 参考开关、目标数据库检查、GetPendingMigrationsAsync、MigrateAsync 和失败阻止启动；适配 Infrastructure 分层 |
| Migration 模型检查与测试 | `.github/workflows/ci.yml`；`tests/Project1.Api.Tests/StartupDatabaseMigrationTests.cs`；`tests/Project1.Migrations.Tests/` | 参考缺少 Migration 的检查、迁移服务测试和 SQL Server 集成测试，生成与检查阶段关闭启动迁移 |
| 测试场景 | `tests/Project1.Api.Tests/Authentication/`；`frontend/src/services/__tests__/apiClient.spec.ts`；`stores/__tests__/auth.spec.ts` | 参考断言场景，编写针对 Helpdesk 单角色、Token 及工单权限的测试 |

说明：表中省略前缀的 Services / Controllers 路径均位于 `backend/Project1.Api`；省略前缀的前端路径位于 `frontend/src`。

适配约束：

- 不直接引用 Project1 的程序集，不复制数据库连接、SigningKey、默认账号或环境配置。
- Helpdesk 已确认使用 ASP.NET Core Identity 及角色关联表，取代早期单个 Role 列的草案；角色关联表对 UserId 增加唯一约束，服务层保证每个账号恰好一个角色。Project1 的多角色业务不能直接照搬。
- Project1 的前端会话使用 sessionStorage，退出为清除本地会话；在已检查的认证链路中未发现 Refresh Token 或服务端 logout 接口。这些是参考项目现状，不自动成为 Helpdesk 的已确认策略。
- 参考项目的 Token 验证会查询 IsActive / SecurityStamp，用户管理会更新 SecurityStamp。Helpdesk 如采用相同方式，需要先在账号模型及持久化设计中表达相关字段和更新规则。
- 项目间可以参考通用技术做法，业务权限始终采用 Helpdesk 已确认的四个角色、单团队和当前负责人规则。

## 15. EF Core Migration 与启动更新

用户补充的参考方向：参考公司项目 API 启动时查询并应用 Migration 的方式，结合 Project1 的开关、目标数据库校验和 CI 模型检查。当前已核对两边代码；Helpdesk 已接入 DbContext 和启动迁移服务并完成阶段 1 验证。

公司项目在 `src/AHAM.CCIF.Api/Program.cs` 的 Build 之后直接调用 GetPendingMigrationsAsync，有待执行项才调用 MigrateAsync，失败阻止后续启动。Project1 把这部分抽成独立服务，并提供显式开关。Helpdesk 采用便于分层和测试的服务形式，保留默认关闭、部署显式开启的计划；不会因为参考公司实现而自动改为每次启动都迁移。

### 15.1 区分三个步骤

| 步骤 | 检查或执行什么 | 是否更新数据库 |
| --- | --- | --- |
| 开发时生成 Migration | Entity / EF 模型变化后，通过 EF CLI 生成 Migration、Designer 和 Snapshot，审核后提交 | 生成文件本身不更新数据库 |
| 模型一致性检查 | `migrations has-pending-model-changes` 检查当前模型相对 Migration Snapshot 是否还有未记录的变化 | 不更新数据库，也不自动生成 Migration |
| API 启动迁移 | 查询当前应用中已有、但目标数据库尚未应用的 Migration；有待执行项才调用 MigrateAsync | 开关开启且存在待执行项时更新数据库 |

不能把“没有待应用 Migration”当作“模型一定没有漏写 Migration 文件”。模型一致性检查不等同于对目标数据库结构、手工修改或现有数据兼容性的完整验证。

### 15.2 Helpdesk 的计划启动顺序

```text
加载配置、注册服务并 Build
          ↓
检查 Database:MigrateOnStartup
          ├── false → 不执行启动迁移，继续普通启动流程
          └── true
                ↓
        校验 ExpectedDatabase 和超时设置
                ↓
        GetPendingMigrationsAsync
                ├── 无待执行项 → 跳过更新
                └── 有待执行项 → MigrateAsync
                                      ↓
                     成功后才进行初始化并启动 HTTP / 后台任务
                     失败则记录错误并中止 API 启动
```

关闭启动迁移只表示该机制不访问或修改数据库，不表示后续业务或初始化不需要数据库。开启启动迁移时，初始化和后台 Delegation 任务不能抢在迁移完成前运行。

Program.cs 仅负责在正确时间创建服务 Scope 并调用迁移服务；EF Core 数据库操作放在 Infrastructure，通过接口支持迁移服务测试。模板和首个账号的初始化方式仍按前述阶段另行确定。

计划配置：

- `Database:MigrateOnStartup`：默认 false，部署环境显式开启；环境变量为 `Database__MigrateOnStartup`。
- `Database:ExpectedDatabase`：必须与连接目标一致，防止在非预期数据库执行。
- `Database:CommandTimeoutSeconds`：配置迁移命令超时；取值范围在实现时定义。

### 15.3 开发与发布流程

1. 修改 Entity、配置及业务代码。
2. 关闭启动迁移，通过 EF CLI 生成 Migration。
3. 审核 Up / Down、Designer、Snapshot，并提交所有对应文件。
4. 执行构建、必要的 SQL Server 集成测试和模型一致性检查。
5. 发布时携带已经生成的 Migration，服务端不现场生成 Migration 源码。
6. 启动 API 时，按环境配置应用目标数据库尚未执行的 Migration；成功后才开放服务。

CI 的模型检查可以发现缺少 Migration 文件，但不能保证删列、改类型或新增约束适用于现有数据。涉及这类变化时，在对应发布阶段审查备份、数据兼容和恢复方案；具体操作不在本次文档修改中执行。

部署前还需确定迁移所需数据库权限及多实例启动协调。Helpdesk 不复制 Project1 的数据库名称、连接配置、服务器权限或部署脚本。

### 15.4 迁移验收场景

- 默认关闭时，不调用启动迁移数据库接口。
- 显式开启且无待执行项时，不调用 MigrateAsync。
- 有待执行项时，按配置应用并记录结果。
- 预期数据库不一致时，在查询和执行迁移前拒绝操作。
- 查询或执行失败时，阻止初始化、后台处理和 HTTP 启动。
- 模型变化缺少对应 Migration 时，模型检查失败；生成正确文件后检查通过。
- 在独立 SQL Server 测试库验证实际 Schema 及迁移历史，不能只靠迁移服务的单元测试。

## 16. 公司项目作为主要工程参考

参考来源：本机 `C:\Users\shun bin\source\repos\AHAM - Agent CIF 2026`。2026-10-24 已查看 Solution、项目引用、API 启动迁移、前后端 Dockerfile 及 Azure Pipelines 的阶段组织。

### 16.1 参考内容与适配

| 参考内容 | 公司项目位置 | Helpdesk 采用方式 |
| --- | --- | --- |
| Solution 分层 | `AHAM.CCIF.slnx`、`src/` 及各 csproj | 保持 Api / Application / Domain / Infrastructure / Web，按本系统需求建立模块 |
| Application 模块组织 | `src/AHAM.CCIF.Application/` | 参考 Dto、Interfaces、Mappings、Services；替换为 Users、Tickets、Workflows、Delegations |
| 数据库与启动迁移 | `src/AHAM.CCIF.Infrastructure/`、`src/AHAM.CCIF.Api/Program.cs` | 参考 EF 模型、配置及启动查询和应用 Migration，数据库使用已确认的 SQL Server |
| API 镜像 | `docker/api/Dockerfile` | 参考 SDK 构建、按 csproj 还原、发布后进入 runtime 镜像的多阶段构建 |
| Web 镜像 | `docker/web/Dockerfile` | 参考 npm ci、前端构建、Nginx 托管产物及运行时相对 API 路径 |
| 容器编排 | `docker/docker-compose.yml`、`docker/docker-compose.sit.yml` | 参考服务组织和环境配置分离，按 Helpdesk 实际端口、卷及健康检查设计 |
| CI/CD 分阶段 | `pipelines/azure-pipelines-sit.yml` 及部署 Pipeline | 借鉴构建、推送、部署分离的做法，在 GitHub Actions 中实现 |

公司项目还包含独立 DBMigration 工具及相关 Pipeline，它与 EF Core Schema Migration 的职责不同。Helpdesk 当前是新系统，保留 Infrastructure 中的 EF Migration 设计，不因目录相似而增加旧数据搬迁工具。

### 16.2 GitHub Actions 与 Docker 的计划流程

```text
GitHub PR / 选定的分支或发布事件（触发规则待确认）
       ↓
GitHub Actions：构建、类型检查、测试、Migration 模型检查
       ↓
按选定发布条件构建 API / Web Docker 镜像
       ↓
推送到选定镜像仓库
       ↓
目标环境拉取并启动镜像
       ↓
API 按显式配置完成待执行 Migration
       ↓
初始化、健康检查和关键业务验证通过
       ↓
发布完成
```

PR 检查不直接修改部署数据库。发布条件、分支、Tag、环境审核和部署权限在部署阶段确定；不默认每次 Push 都直接发布生产。

| 公司项目的平台概念 | Helpdesk 对应方向 |
| --- | --- |
| Azure Pipelines YAML 与 Stage / Job | `.github/workflows/` 中的 GitHub Actions Workflow / Job |
| Pipeline 变量和受保护配置 | GitHub Actions Secrets / Variables / 环境配置，按部署目标设置 |
| Docker Build / Push | GitHub Actions 中构建并推送 API / Web 镜像 |
| 环境部署步骤 | 面向已选目标的 CD Job、执行条件及健康检查 |

### 16.3 保持独立的配置

- GitHub Actions 和 Docker 是已确定方向；GHCR、其他镜像仓库、Ubuntu、其他服务器或云环境尚未选择。
- SQL Server 位于宿主机还是容器中，数据库及附件的持久化位置，需按实际环境决定。
- Dockerfile 和代理配置必须使用 Helpdesk 的项目路径、启动端口及 API 路由，不能直接带入公司的路径或地址。
- 不复制公司的账号、证书、服务连接、镜像地址、数据库凭据、AWS 配置或 Azure Pipelines 环境值。
- 后续配置如与 Project1 不同，以 Helpdesk 已确认的需求和部署环境为准。

本节只记录参考关系和实施方向，本次不创建 Dockerfile、GitHub Actions Workflow 或实际部署资源。
