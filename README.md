# HelpDesk-System

公司内部 IT Helpdesk System。当前已建立 Vue 3 + TypeScript 前端、.NET 10 Minimal API 和分层 Solution 骨架。

## 项目结构

```text
HelpDesk.slnx
src/
├── HelpDesk.Api/              # HTTP 入口、Minimal API Endpoints
├── HelpDesk.Application/      # 后续业务用例、DTO、接口
├── HelpDesk.Domain/           # 后续 Entities、Enums、业务规则
├── HelpDesk.Infrastructure/   # 后续 EF Core / SQL Server 实现
└── HelpDesk.Web/              # Vue 3 + TypeScript + Vite
docs/
```

项目引用：Api → Application + Infrastructure；Infrastructure → Application；Application → Domain。Vue 在 Solution 中以文件夹展示，通过 HTTP 与 Api 通信，并独立使用 npm 构建。

## 开发环境

- .NET 10 SDK（global.json 允许使用较新的 .NET 10 feature band）。
- Node.js 20.19+ 或 22.12+；本次使用 Node.js 24 验证。
- npm（随 Node.js 安装）。

当前骨架不需要 SQL Server 即可启动。SQL Server、EF Core、认证及业务功能将在后续阶段接入。

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

目前完成的是基础项目骨架和 API 连通页面；登录、用户、Ticket、Workflow、Delegation 及数据库 Migration 尚未实现。测试项目将在实现相应业务行为时建立。
