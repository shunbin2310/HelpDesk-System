# Docker 连接 Windows 本机 HelpDeskDb

当前使用方式：Vue / Nginx 和 API 运行在 Docker，数据库使用 Windows 默认 SQL Server 实例 BIN 上现有的 HelpDeskDb，附件使用原来的 Windows 目录。它与主机 `dotnet run` 共用账号、工单和附件，任一端修改都会作用于同一份数据。

## 本机已完成的准备

- 默认实例已支持 SQL Authentication，TCP 1433 可从 Docker 的 host.docker.internal 访问；未修改认证模式、网络设置或重启 SQL Server。
- 经用户明确授权，创建 SQL 登录 helpdesk_docker_local，只在 HelpDeskDb 中加入 db_owner，以支持业务操作和每次启动自动 Migration。未加入服务器管理员角色，sa 仍保持停用。
- `.env.docker.host-sql` 保存专用 SQL 密码、独立 JWT Key、端口和现有附件路径，被 Git 忽略，不进入镜像。
- 现有附件目录为 `C:/Users/shun bin/AppData/Local/HelpDesk-System/Attachments`，挂载到 API 的 /data/attachments。目录必须事先存在，Compose 不自动创建空目录来替代它。

这里的 SQL 登录只供 API 连接数据库，不是网页登录账号。网页继续使用原来 Windows 开发环境中的账号；不需要 bootstrap-admin 或重新初始化已有 Workflow。之前独立 Docker 测试库中的 docker-*@example.invalid 账号属于另一份数据库。

## 启动与更新

从仓库根目录运行。两套配置默认都使用网页端口 8080，先停止之前的独立容器数据库模式，保留它的数据卷：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml down
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml config --quiet
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml up -d --build --wait --wait-timeout 180
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml ps
```

打开 <http://localhost:8080>，使用原有 HelpDesk 账号登录。当前模式正常显示 api、web 两个服务，不创建 SQL Server 容器。Windows SQL Server 必须先处于运行状态。

之后更新代码时，只需：

```powershell
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml up -d --build --wait --wait-timeout 180
```

如需手动删除并重建应用镜像：

```powershell
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml down
docker image rm helpdesk-host-sql-api:latest helpdesk-host-sql-web:latest
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml build --no-cache
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml up -d --wait --wait-timeout 180
```

重建镜像不会重建 Windows 数据库。API 启动仍会检查并应用 Pending Migration，因此删列或其他破坏性 Migration 会影响真实的本机 HelpDeskDb；此类修改前备份数据库及附件。没有待执行项时不改变 Schema。

## 配置位置

| 设置 | 位置 / 值 |
| --- | --- |
| Compose | compose.host-sql.yml，项目名 helpdesk-host-sql |
| 本机私有配置 | .env.docker.host-sql；新克隆可参考 .env.docker.host-sql.example，但仍需管理员配置对应 SQL 登录和权限 |
| SQL 主机 | 容器内使用 tcp:host.docker.internal,1433，指向已验证的 Windows 默认实例 BIN |
| 数据库 | HelpDeskDb；ExpectedDatabase 同样为 HelpDeskDb |
| SQL 登录 | helpdesk_docker_local，密码从私有环境文件读取 |
| 附件 | HELPDESK_ATTACHMENTS_PATH，绑定到 API 私有目录；Web 不挂载此目录 |
| 网页 | 127.0.0.1:8080；HELPDESK_WEB_PORT 可调整本机端口 |
| Data Protection keys | helpdesk-host-sql_data-protection 独立数据卷 |

本机 SQL Server 只监听已有回环入口，通过 Docker Desktop 已验证的主机访问路径连接；本次没有开放 LAN 防火墙规则。生产服务器的连接、最小权限和 HTTPS 仍需另行配置。

## 停止、检查与切回

```powershell
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml logs --tail 60 api web
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml down
```

`down` 只停止此套 Web / API，不停止 Windows SQL Server，也不删除现有数据库或绑定的附件文件。平常不要加 `-v`，以保留本项目的 Data Protection keys。保留私有环境文件，否则专用 SQL 密码和 JWT Key 会丢失。

原独立 Docker SQL Server 的数据仍在 helpdesk-local_sql-data，示例附件仍在 helpdesk-local_attachments，原 .env.docker.local 也保留。若要切回原独立测试环境，先停止 host-sql 模式，再执行：

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait --wait-timeout 240
```

两套模式的数据和 SQL 登录不同；切换不是数据迁移。Windows 开发 API 与 Docker API 若同时运行，会共用本机数据库；现有 Delegation 数据库锁处理并发，后台任务保持启用。

参考：[Docker Desktop 主机网络访问](https://docs.docker.com/desktop/features/networking/)。

## 本机验证结果（2026-10-25）

- 真实容器 SQL 客户端连接到 BIN / HelpDeskDb，专用登录的 sysadmin 标记为 0、此库 db_owner 标记为 1。
- 两个应用镜像构建通过，API 和 Web 均 Healthy；启动日志确认没有待执行 Migration。
- 原有 6 个应用账号、9 张工单、5 条附件记录和 4 条 Migration 历史保留。验证未创建应用测试账号或工单，未更改 Schema。
- 通过 localhost:8080 的反向代理使用原管理员登录，工单列表总数为 9。
- 容器中挂载的 5 个文件与 Windows 原文件 SHA-256 一致；3 个直接属于 Ticket 的附件下载哈希一致，2 个旧消息附件继续返回 404，符合移除回复功能后的既有规则。
- API 以非 root app 用户读取现有文件，目录读写权限检查通过。验证过程中没有新增或删除附件文件。
- Compose 配置、Git 忽略规则和 diff 空白检查通过。本次没有修改业务源码，没有重新运行已通过的整套业务测试；验证集中于新的连接、权限边界、现有数据与附件读取。
