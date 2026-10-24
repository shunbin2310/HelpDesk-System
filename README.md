# HelpDesk-System

公司内部 IT Helpdesk System。当前已完成 Vue 3 + TypeScript / .NET 10 分层骨架，以及阶段 1 的 Identity、EF Core、SQL Server 数据模型和 Migration 基础。

## 项目结构

```text
HelpDesk.slnx
src/
├── HelpDesk.Api/              # HTTP 入口、Minimal API Endpoints
├── HelpDesk.Application/      # 账号存储接口，后续业务用例和 DTO
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

数据库基础已经接入，但默认关闭启动迁移，因此普通启动和 `/health` 不主动访问数据库。账号登录、JWT、业务接口及界面尚未实现。

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

打开 <http://localhost:5173>。首页会自动检查 API 连接，也可以点击 Check connection 重试。

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

## 阶段 1 测试

从仓库根目录运行单元测试：

```powershell
dotnet test .\tests\HelpDesk.UnitTests --configuration Release
```

在已初始化的专用开发 / 测试数据库运行 SQL Server 集成测试：

```powershell
$env:HELPDESK_TEST_CONNECTION = 'Server=BIN;Database=HelpDeskDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test .\HelpDesk.slnx --configuration Release
```

SQL Server 测试会写入临时测试数据，并用事务回滚或针对性清理移除自己创建的记录。未设置 `HELPDESK_TEST_CONNECTION` 时，5 项集成测试会明确显示为 Skipped；不能将跳过视为数据库验证通过。

本次验证：11 项单元测试和 5 项 SQL Server 集成测试全部通过，没有跳过；模型与 Migration Snapshot 一致，API 启动迁移和无待执行项的重启路径均已验证。

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

当前已完成阶段 1 数据基础和对应测试。IUserAccountStore 提供内部账号创建与单角色调整能力，尚未对外开放用户管理或登录 API。JWT 签发、CurrentUserContext、Ticket 业务流程、Workflow Engine、Delegation 自动任务及业务页面属于后续阶段。
