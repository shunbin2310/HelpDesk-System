# 本机 Docker 测试

当前已按用户选择切换到 **Docker Web / API 连接 Windows 现有 BIN / HelpDeskDb**，请使用[连接本机数据库说明](docker-host-sql.md)中的 compose.host-sql.yml 和 .env.docker.host-sql 命令。下文保留原来三个容器、独立测试库的运行方式；它的数据卷仍保留。

本配置只用于本机 Docker Desktop 的 Linux containers，不包含 GitHub Actions、服务器部署或 HTTPS。Web、API、SQL Server 都运行在容器内；SQL Server 使用独立数据卷，不连接 Windows 的 BIN / HelpDeskDb，原来的账号和工单不会自动带过来。

## 启动

从仓库根目录运行。Docker Desktop 必须已启动：

```powershell
docker desktop start
```

本次已生成本机专用 `.env.docker.local`，并被 Git 忽略。新克隆仓库时，先生成一次；已有文件不能覆盖，以免改变现有数据库密码和 JWT Key：

```powershell
if (Test-Path .env.docker.local) { throw '保留现有 .env.docker.local，不要覆盖。' }
$sqlSecret = 'Hd!9' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$jwtSecret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
@("HELPDESK_SQL_PASSWORD='$sqlSecret'", "HELPDESK_JWT_KEY='$jwtSecret'", 'HELPDESK_WEB_PORT=8080', 'HELPDESK_SQL_PORT=14330') |
  Set-Content .env.docker.local -Encoding utf8
```

以上生成命令使用 PowerShell 7。也可复制 `.env.docker.local.example`，手动填写独立强密码和至少 32 字节的随机 JWT Key。SQL 密码不要包含分号或引号，它会用于连接字符串；修改环境文件不会自动修改已有 SQL Server 数据卷内的登录密码。

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml config --quiet
docker compose --env-file .env.docker.local -f compose.local.yml up -d --build --wait --wait-timeout 240
docker compose --env-file .env.docker.local -f compose.local.yml ps
```

打开 <http://localhost:8080>。此处使用构建后的 Vue 文件和 Nginx，不需要 `npm run dev` 或在主机单独启动 API。现有的 localhost:5173 / localhost:5080 开发进程可以继续使用，它们连接的是各自配置的开发环境。

## 首个管理员与测试账号

全新空库没有默认账号。首次运行后，在交互终端执行：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml run --rm --no-deps -e ASPNETCORE_ENVIRONMENT=Development api bootstrap-admin
```

按提示填写 Email、Full name、Password 和 Confirm password。密码至少 10 位，包含大小写字母、数字及符号。此命令只将一次性的本机初始化进程切换为 Development，常驻 API 保持 Production；连接仍由 Compose 指向容器 SQL Server。必须使用交互终端，不能用管道传入密码。已经有 System Admin 时，使用 Users 页面创建其他账号。

首次登录后进入 Workflow，点击 **Initialize default workflow**，然后在 Users 创建 Employee、Support IT Staff 和 Support Admin。这些初始化动作不会在每次启动时自动重复。

本次本机验收会保留四个 Docker 测试角色账号，以及一张带五个 10 MiB LOG 附件的示例工单。账号、随机测试密码和示例工单 ID 记录在本机 `.env.docker.test-accounts`（Git 忽略文件）；该文件不是应用配置，不会被打进镜像。新克隆仓库不会有此文件，应按上面的交互命令初始化自己的管理员。

## 连接与持久化

| 项目 | 本机配置 |
| --- | --- |
| 网页 | http://localhost:8080；可通过 HELPDESK_WEB_PORT 调整 |
| API | 仅容器网络内的 api:8080；网页通过同源 /api/ 转发并去掉 /api 前缀 |
| SQL Server | SSMS 使用 127.0.0.1,14330（IPv4）；可通过 HELPDESK_SQL_PORT 调整；本机 localhost 可能优先解析为未绑定的 IPv6 地址 |
| SQL 认证 | SQL Server Authentication；Login 为 sa，密码读取 .env.docker.local 的 HELPDESK_SQL_PASSWORD；本机连接需勾选 Trust server certificate |
| 数据库 | 容器内的 HelpDeskDb，与 Windows 上同名数据库相互独立 |
| 数据卷 | helpdesk-local_sql-data：数据库；helpdesk-local_attachments：私有附件；helpdesk-local_data-protection：ASP.NET Core Data Protection keys |

端口仅绑定 127.0.0.1。SQL Server 健康后才启动 API；API 每次启动先校验 ExpectedDatabase、检查并应用现有 Pending Migration，再启动 HTTP 和后台 Delegation。查询或迁移失败时拒绝启动，不会现场生成 Migration。API 健康后才启动 Web。

API 以非 root 的 app 用户运行，附件写入 /data/attachments，仅通过授权 API 下载；Nginx 不挂载附件卷。上传代理上限为 51 MiB，与 API 的总请求上限一致，支持五个每个 10 MiB 的附件及表单开销。Data Protection keys 存于独立本机卷；生产环境的密钥加密、最小数据库权限和 HTTPS 仍留在部署阶段。

`/health` 是进程检查，不代表登录、数据库业务和附件权限均正常。Web 的 `/health` 检查静态服务；`/api/health` 通过 Nginx 到 API。验收时仍需登录并实际创建、查看和下载附件。

## 更新、检查与停止

代码改动后重新执行 `up -d --build --wait --wait-timeout 240`，构建并替换容器，数据卷保留。Compose 更新依赖服务时会重新启动对应的下游服务。

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml logs --tail 60 api web
docker compose --env-file .env.docker.local -f compose.local.yml down
```

`down` 停止并移除本项目容器和网络，保留数据卷。不要添加 `-v`：它会删除本套测试环境的数据库、附件和 Data Protection keys。不要删除 `.env.docker.local`，它保存了重新连接数据库和验证 JWT 所需的本机值。`config --quiet` 只验证配置，避免普通 `config` 把展开后的密码打印到终端。

## 验证方式

- Compose 配置校验、前后端真实镜像构建及三个服务健康检查。
- 空 SQL Server 数据卷自动建库和应用 Migration。
- 通过 Nginx 的登录、SPA 深层路由和真实工单附件请求。
- 五个每个 10 MiB 的附件完整上传和下载哈希比对，验证反向代理大小限制与非 root 文件写入权限。
- 使用独立 Docker SQL Server 执行现有单元 / SQL 集成测试，连接使用 `Server=tcp:127.0.0.1,14330`。首次 localhost 连接尝试超时后，改用已验证的 IPv4 地址重跑。
- 重建容器后确认账号、示例工单及附件内容仍保留，并确认无 Pending Migration。

参考：[Docker Compose 启动依赖](https://docs.docker.com/compose/how-tos/startup-order/)、[SQL Server 容器](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-ver17)、[.NET 容器构建](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)。
