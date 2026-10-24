# HelpDesk-System

公司内部 IT Helpdesk System。当前已完成 Vue 3 + TypeScript / .NET 10 分层骨架、账号管理、Workflow Engine，以及阶段 4 Ticket 提交、分配和处理闭环。

## 项目结构

```text
HelpDesk.slnx
src/
├── HelpDesk.Api/              # HTTP 入口、Minimal API Endpoints
├── HelpDesk.Application/      # Authentication / Users / Workflows / Tickets 契约和 DTO
├── HelpDesk.Domain/           # Entities、Enums、角色及流程常量
├── HelpDesk.Infrastructure/   # Identity、EF Core、SQL Server 映射及 Migrations
└── HelpDesk.Web/              # Vue 3 + TypeScript + Vite
docs/
tests/
```

项目引用：Api → Application + Infrastructure；Infrastructure → Application；Application → Domain。Vue 在 Solution 中以文件夹展示，通过 HTTP 与 Api 通信，并独立使用 npm 构建。

## 开发环境

- .NET 10 SDK（global.json 允许使用较新的 .NET 10 feature band）。
- Node.js 20.19+ 或 22.12+；本次使用 Node.js 24 验证。
- npm（随 Node.js 安装）。

本机开发连接在 `src/HelpDesk.Api/appsettings.Development.json`：服务器 BIN、数据库 HelpDeskDb、Windows Authentication。使用当前进程的 Windows 身份；本地信任服务器证书的设置与本机 SSMS 配置一致。该连接不用于 Docker 或生产环境，其他环境通过 `ConnectionStrings__DefaultConnection` 提供自己的连接配置。

数据库默认关闭启动迁移；`/health` 不访问数据库，登录和账号接口会访问数据库。四个角色共用登录入口，每个账号一个角色，只有 System Admin 可以管理用户。

## 首次设置账号与 JWT

从仓库根目录运行一次本地管理员初始化命令：

```powershell
dotnet run --project .\src\HelpDesk.Api --launch-profile http -- bootstrap-admin
```

依次输入 Email、姓名、密码和确认密码。密码输入不回显；至少 10 位，包含大小写字母、数字和符号，最多 128 位。命令仅允许交互式 Development 终端，在没有任何 System Admin 时创建账号，然后退出，不启动 HTTP。已有管理员时拒绝再次初始化；后续账号在 Users 页面创建。数据库需先完成下方 Migration 初始化。

API 必须有独立的 JWT 签名密钥。当前开发机已配置在 .NET User Secrets；新开发机在首次启动 API 前执行一次：

```powershell
$helpdeskKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set 'Jwt:SigningKey' $helpdeskKey --project .\src\HelpDesk.Api
Remove-Variable helpdeskKey
```

密钥不在仓库中，也不应出现在日志或聊天中。其他环境通过 `Jwt__SigningKey` 注入；Issuer、Audience 和 60 分钟有效期配置在 appsettings.json。修改签名密钥会使旧 Token 失效。

登录成功后 JWT 存在当前标签页的 sessionStorage，刷新页面可以继续使用；关闭标签页或退出会清理浏览器会话。过期需重新登录，暂不提供 Refresh Token。退出不逐个撤销 Token；停用、角色修改、密码修改或重置会更新 SecurityStamp，旧 Token 在后续请求中被拒绝。登录密码连续错误 5 次锁定 15 分钟，登录入口另有每 IP 每分钟 10 次请求限制。

System Admin 可查看分页账号列表、创建账号、修改角色、启用或停用、重置密码。数据库事务锁保护首次初始化及最后一个可用 System Admin。用户可以修改自己的密码，成功后需重新登录；忘记密码由 System Admin 重置。

## Workflow 初始化与执行

更新代码后，先停止并重新启动 API，刷新前端；以 System Admin 登录，进入 **Workflow → Initialize default workflow**。该操作创建并发布 `HELPDESK_TICKET` 版本 1，包含四个步骤和四个动作。重复调用返回现有启用模板；不会覆盖其他版本，也不会自动启用已停用的旧模板。默认所有动作的备注可选。

- Open → Start Processing → In Progress：当前负责的 Support IT Staff。
- In Progress → Resolve → Resolved：当前负责的 Support IT Staff。
- Resolved → Close → Closed，或 Reopen → In Progress：提交该 Ticket 的 Employee。
- System Admin 可执行当前可用动作，但不能跳过状态规则；Support Admin 可查看，不能因管理角色自动执行处理或确认动作。

每张 Ticket 复制 Process / Steps / Actions / Actioners，并记录模板版本、操作者姓名快照和流程历史。Requester 在复制时绑定提交人；CurrentAssignee 执行时重新读取 Ticket 当前负责人，转派不会改变流程状态。Role / User Actioner 只能进一步限制业务权限，不能让其他 Staff 或 Employee 获得越权操作。

Application 契约在 `Workflows/IWorkflowEngine.cs`，Domain 规则在 `TicketWorkflowPolicy`，Infrastructure 提供 `WorkflowEngine` 和 `WorkflowTemplateService`。初始化入口和流程查看 / 执行 API 在 Api 的 `WorkflowEndpoints`，Vue 设置页为 `WorkflowView.vue`。

| API 路径（前端请求时加 `/api`） | 用途 |
| --- | --- |
| GET /workflow-templates/active | System Admin 查看当前模板；未配置返回 404 |
| POST /workflow-templates/default | System Admin 幂等初始化默认模板 |
| GET /tickets/{ticketId}/workflow | 按 Ticket 可见性返回当前步骤、可用动作和历史 |
| POST /tickets/{ticketId}/workflow/actions | 提交 ActionCode、RowVersion 和可选 Comment |

动作请求示例：`{"actionCode":"RESOLVE","rowVersion":"<GET 返回的 Base64 RowVersion>","comment":"Fixed"}`。RowVersion 必须是 8 字节 SQL rowversion；旧版本或重复动作返回 409，无权限动作返回 403，不可见的 Ticket 返回 404，非法参数或缺少配置要求的备注返回 400。备注最多 4000 个字符。

`StartAsync` 只供内部用例使用，不开放独立 HTTP 启动接口。阶段 4 创建 Ticket 时，调用方必须先在同一事务保存 Ticket，再启动流程；任何失败都回滚整个创建事务。引擎通过自有事务或调用方事务中的 Savepoint 保证复制、当前步骤和历史一致，且不会提交调用方事务。调用前必须保存 Ticket 的改动；失败后应终止该用例，避免继续使用已清理的 ChangeTracker。执行动作会锁定 Ticket 行，防止授权检查与保存之间负责人改变，并通过流程 RowVersion 防止重放。

流程引擎使用既有表结构，没有新增 Migration。阶段 4 已接入下方 Ticket 页面；完整模板编辑器属于阶段 6。

## Ticket 核心流程

停止并重新启动 API，刷新前端后，即可看到 **Tickets**；Support Admin / System Admin 另有 **Assignments**。先由 System Admin 初始化 Workflow，并通过 Users 创建 Employee、Support IT Staff、Support Admin 账号，再使用各自账号验收。

Employee 在 **Submit ticket** 输入 Title（必填，最多 200 字符）、Description（必填，最多 4000 字符）及 Category。后台固定使用当前登录用户作为提交人，新工单为 Open / Medium、未分配；其他角色不能提交，客户端额外提供的提交人、状态、负责人及优先级不会覆盖这些规则。

编号采用已确认的 `HD-20261024-000001`：日期按马来西亚时间，后缀来自全局 SQL ID，至少 6 位，不按天重置；事务回滚、Identity 缓存等允许产生跳号。创建时使用事务内唯一占位编号，取得 ID 后替换为正式编号；Ticket、流程副本和 START 历史全部成功才提交。缺少或无效模板时完整回滚。

列表支持状态筛选及每页 25 条分页，按最近更新时间排序。Employee 只看自己提交的；IT Staff 只看当前分配给自己的；两种 Admin 查看全部。Assignments 默认筛选未分配工单，打开详情即可选择负责人或调整优先级；转派时可以取消 Unassigned only 查看已分配工单。

负责人只能选择启用的 Support IT Staff。分配、转派和优先级变更记录 TicketHistory，不改变当前 Workflow 状态；同值保存不重复记录历史。转派后原 Staff 的列表、详情和操作请求均失去权限，新 Staff 可以查看完整已有历史。详情页同时展示流程历史及分配 / 优先级历史。

Ticket 详情按后端返回的可用动作展示按钮：负责人 Start Processing / Resolve，提交人 Close / Reopen，System Admin 按合法状态覆盖执行；Closed 只提供提交新工单的入口。备注仍遵循模板要求。收到 409 时刷新并重新核对内容，不自动重试写入。

| API | 权限 / 用途 |
| --- | --- |
| GET /tickets?page=1&status=OPEN&onlyUnassigned=true | 根据当前角色过滤；参数可省略 |
| GET /tickets/{id} | 详情、流程及管理历史；不可见时 404 |
| POST /tickets | 仅 Employee；Title、Description、数值 Category；成功 201 |
| GET /tickets/assignees | 两种 Admin；当前启用的 IT Staff |
| PUT /tickets/{id}/assignment | 两种 Admin；AssignedToUserId、RowVersion |
| PUT /tickets/{id}/priority | 两种 Admin；数值 Priority、RowVersion |

前端开发请求在上述路径前加 `/api`。Category 为 Hardware=1、Software=2、Network=3、Account=4、Other=5；Priority 为 Low=1、Medium=2、High=3、Critical=4。分配 / 优先级请求使用详情顶层的 `rowVersion`；流程动作继续使用 `workflow.rowVersion`。两者都是 Base64 SQL rowversion，不能混用。流程动作更新 Ticket 更新时间及版本；分配不修改流程版本，但每次执行仍重新检查当前负责人。

Application 契约为 `Tickets/ITicketService.cs`，Infrastructure 的 `TicketService` 实现用例和事务，Api 的 `TicketEndpoints` 验证角色；Vue 的 `TicketsView`、`CreateTicketView`、`TicketDetailView` 复用现有请求封装及路由守卫。没有新增数据库表、Migration 或前端依赖。

## 本地启动

以下命令使用 PowerShell。从仓库根目录打开第一个终端，启动 API：

```powershell
dotnet run --project .\src\HelpDesk.Api --launch-profile http
```

API 地址：<http://localhost:5080>。检查接口：<http://localhost:5080/health>。

再从仓库根目录打开第二个终端，启动前端：

```powershell
Set-Location .\src\HelpDesk.Web
npm.cmd ci
npm.cmd run dev
```

打开 <http://localhost:5173>，使用初始化的账号登录。System Admin 可进入 Users，所有角色都可以修改自己的密码。

开发请求 `/api/health` 由 Vite 代理到 `http://localhost:5080/health`。修改 API 端口时，应同时修改 `launchSettings.json` 和 `vite.config.ts` 的目标地址。5173 已启用 strictPort，端口占用时会明确报错。

`/health` 只表示 API 进程可以响应，不检查数据库或其他尚未接入的服务。

## 数据库与 Migration

2026-10-24 已在 BIN 创建 HelpDeskDb，并应用全部三个 Migration。数据库包含 22 张应用表，以及 EF 的 `__EFMigrationsHistory` 表；预置四个角色，不创建默认账号。

Startup Migration 默认关闭。需要应用待执行 Migration 时，从仓库根目录显式开启：

```powershell
dotnet run --project .\src\HelpDesk.Api --launch-profile http -- --Database:MigrateOnStartup=true --Database:ExpectedDatabase=HelpDeskDb
```

启动顺序为：校验预期数据库 → 查询待执行 Migration → 有待执行项才应用 → 成功后启动 HTTP。无待执行项时跳过，失败则拒绝启动。验证数据库名不会替代对服务器及连接配置的确认。

生成新 Migration 前先停止正在运行的 API，避免 Windows 构建文件锁；在仓库根目录执行：

```powershell
dotnet tool restore
dotnet tool run dotnet-ef -- migrations add DescribeYourChange --project .\src\HelpDesk.Infrastructure --startup-project .\src\HelpDesk.Api --context HelpDeskDbContext --configuration Release
dotnet tool run dotnet-ef -- migrations has-pending-model-changes --project .\src\HelpDesk.Infrastructure --startup-project .\src\HelpDesk.Api --context HelpDeskDbContext --configuration Release
```

EF 工具通过设计时 Factory 创建 DbContext，不启动 API，也不执行启动迁移。Factory 默认指向本机 BIN / HelpDeskDb，可通过 `ConnectionStrings__DefaultConnection` 覆盖。生成文件不等于更新数据库；应一起保留、审核和提交 Migration、Designer 与 Snapshot。

框架参考：[Identity 模型定制](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0)、[EF Core Migration 应用方式](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)。

流程实例的 Id 使用 SQL Server sequence / HiLo 自动分配，其他业务表使用 IDENTITY；编号不要求连续。当前步骤使用独立的所属列、复合外键及 CHECK 约束，确保只能关联本流程的步骤。

在 SSMS 中连接 BIN，刷新 Databases → HelpDeskDb → Tables，即可查看表结构。

## 测试

从仓库根目录运行单元测试：

```powershell
dotnet test .\tests\HelpDesk.UnitTests --configuration Release
```

在已初始化的专用开发 / 测试数据库运行 SQL Server 集成测试：

```powershell
$env:HELPDESK_TEST_CONNECTION = 'Server=BIN;Database=HelpDeskDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test .\HelpDesk.slnx --configuration Release
```

SQL Server 测试使用事务回滚或针对性清理移除自己创建的记录。账号和流程测试会在同一 SQL Server 创建独立、带随机名称的 `HelpDeskAuthTests_*` / `HelpDeskWorkflowTests_*` 数据库，应用现有 Migration 并在结束时删除这些测试库，因此测试身份需要创建和删除数据库的权限；不会删除配置指向的 HelpDeskDb。若测试进程被强制结束，可能需要按对应随机名称手动清理残留测试库。

未设置 `HELPDESK_TEST_CONNECTION` 时，14 项集成测试会显示为 Skipped；不能将跳过视为数据库验证通过。2026-10-24 验证：48 项单元测试、14 项 SQL Server 集成测试全部通过，无跳过。在原有账号和流程测试上，新增 Ticket 完整 API 闭环、提交人等字段防篡改、分页与可见性、分配和优先级历史、并发编辑、失效权限和创建失败回滚；编号测试覆盖马来西亚午夜 / 跨年、全局 ID 及字段长度。

## 构建

在仓库根目录构建后端：

```powershell
dotnet build .\HelpDesk.slnx --configuration Release
```

在前端目录构建：

```powershell
Set-Location .\src\HelpDesk.Web
npm.cmd ci
npm.cmd run build
```

前端 build 会先执行 vue-tsc 类型检查，再产出 dist。可以单独运行 `npm.cmd run type-check`。`npm.cmd run preview` 只预览前端构建产物，不包含开发代理；生产环境的 API 路由及部署方式尚未配置。

工具文档：[Vue TypeScript](https://vuejs.org/guide/typescript/overview.html)、[Vite](https://vite.dev/guide/)。

## 设计文档

- [实施计划与开发步骤](docs/implementation-plan.md)
- [需求说明](docs/requirements.md)
- [数据库设计草案](docs/database-design.md)

当前已完成阶段 4。默认流程仍由 System Admin 在 Workflow 页首次初始化，现有账号不受影响。下一阶段接入公开回复、内部备注和附件。

JWT 验证遵循 [ASP.NET Core Bearer 文档](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)，前端权限导航使用 [Vue Router guards](https://router.vuejs.org/guide/advanced/navigation-guards)。界面导航只提供交互限制，实际权限由 API Bearer 验证和角色授权执行。
