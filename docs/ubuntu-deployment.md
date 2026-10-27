# Ubuntu 自动部署：与 Purchasing System 共用服务器

已确认：使用现有 Ubuntu，SQL Server 中新建独立 HelpDeskDb，不搬迁 Windows 的账号、工单或附件。沿用现有 Docker / SQL / Tailscale；不重装、不重启共享 SQL、不修改 Purchasing 的 Compose、Nginx 或部署用户。

以下保留首次安装和日常恢复步骤；当前已验证的部署及未完成的验收见文末。不要在已有安装上重复创建数据库、账号或覆盖私密配置。

## 1. 先检查现有服务器

在现有 Ubuntu **管理员的可信 SSH 会话**运行，并核对输出；不要发送密码、私钥、.env 或完整容器 inspect 输出：

```bash
hostname
uname -m
sudo docker version --format '{{.Server.Version}}'
sudo docker compose version
sudo docker ps --format 'table {{.Names}}\t{{.Ports}}\t{{.Status}}'
sudo ss -lntp 'sport = :8085'
systemctl is-active mssql-server
tailscale ip -4
free -h
df -h /opt /var/opt/mssql
```

当前脚本只接受 hostname 为 homelab-server。必须核对真实 hostname；不为满足脚本重命名共享服务器。端口 8085 有服务时先停止本方案，选好新端口后同步审阅 Compose、脚本和测试。

网页暂按独立的私网 IPv4:8085 准备；在 .env 的 HELPDESK_BIND_IP 选择该机的 LAN 或 Tailscale IP。默认 127.0.0.1 只允许服务器自身访问，不绑定 0.0.0.0。不新增公网转发，HTTP 入口只用于可信私网测试；公网使用需先配置 HTTPS。选 Tailscale IP 时，客户端需要加入对应 tailnet，且服务器开机后应先有该地址再启动容器。

## 2. 安装独立文件与部署用户

Windows 仓库中的文件：

| 文件 | Ubuntu 安装位置 |
| --- | --- |
| compose.ubuntu.yml | /opt/helpdesk-auto/compose.yml |
| .env.docker.ubuntu.example | /opt/helpdesk-auto/.env，私下填写 |
| scripts/deploy/auto-deploy.sh | /usr/local/sbin/helpdesk-auto-deploy |
| scripts/deploy/helpdesk-auto-deploy.sudoers | /etc/sudoers.d/helpdesk-auto-deploy |

先在 Windows 仓库根目录，通过已核对主机指纹的管理员 SSH 上传四个文件（替换地址；目录已存在时先检查，不覆盖）：

```powershell
$Server = 'ADMIN_USER@SERVER_LAN_IP'
ssh -o StrictHostKeyChecking=yes $Server 'mkdir -m 700 ~/helpdesk-setup'
if ($LASTEXITCODE -ne 0) { throw '请先检查现有准备目录或 SSH 连接。' }
scp -o StrictHostKeyChecking=yes .\compose.ubuntu.yml .\.env.docker.ubuntu.example .\scripts\deploy\auto-deploy.sh .\scripts\deploy\helpdesk-auto-deploy.sudoers "${Server}:helpdesk-setup/"
if ($LASTEXITCODE -ne 0) { throw '上传失败，先检查。' }
```

不要把服务器密码、配置文件或 Project1 私钥复制进本仓库。以下安装命令在 Ubuntu 执行，仅用于第一次；若用户 / 目录 / 文件已经存在，先检查，不覆盖：

```bash
sudo test ! -e /opt/helpdesk-auto
sudo test ! -e /usr/local/sbin/helpdesk-auto-deploy
getent passwd helpdesk_deploy
```

前两个应成功，最后一个应无输出。如果发现现有安装，停止并检查。确认后：

```bash
sudo useradd --create-home --shell /bin/bash helpdesk_deploy
sudo passwd -l helpdesk_deploy
sudo install -d -m 0700 -o root -g root /opt/helpdesk-auto
sudo install -m 0644 -o root -g root ~/helpdesk-setup/compose.ubuntu.yml /opt/helpdesk-auto/compose.yml
sudo install -m 0600 -o root -g root ~/helpdesk-setup/.env.docker.ubuntu.example /opt/helpdesk-auto/.env
sudo install -m 0755 -o root -g root ~/helpdesk-setup/auto-deploy.sh /usr/local/sbin/helpdesk-auto-deploy
sudo bash -n /usr/local/sbin/helpdesk-auto-deploy
sudo visudo -cf ~/helpdesk-setup/helpdesk-auto-deploy.sudoers
sudo install -m 0440 -o root -g root ~/helpdesk-setup/helpdesk-auto-deploy.sudoers /etc/sudoers.d/helpdesk-auto-deploy
sudo visudo -c
```

不把 helpdesk_deploy 加到 docker 组，不授予 sudo docker、sudo bash 或 ALL 权限。固定入口校验三项参数、机器名和受保护文件，只操作 helpdesk-auto 项目。

## 3. 单独准备 HelpDesk 数据库与凭据

先在 SSMS / sqlcmd 用数据库管理员查询：

```sql
SELECT SERVERPROPERTY('MachineName') AS MachineName,
       DB_ID(N'HelpDeskDb') AS ExistingDatabase,
       SUSER_ID(N'helpdesk_app') AS ExistingLogin;
```

确认是目标 Ubuntu，且数据库、登录都不存在，才在该实例执行下面的首次准备。若任一已存在，停止检查，不删除、不重复授权。

私下生成两组随机值：SQL 密码在 `openssl rand -hex 32` 的结果前加上固定前缀 `Hd1!`，保证包含大小写、数字和符号；JWT 使用 `openssl rand -hex 48`。把它们保存到 root:root 0600 的 /opt/helpdesk-auto/.env，填入已选私网 IP。不要将结果贴聊天、放命令行参数或提交 Git。

已有私密配置后，在 Ubuntu 管理员交互终端运行下面整段。它读取同一密码，生成仅 root 可读的一次性 SQL 文件；完成或 SQL 报错后删除该文件。Password 提示输入现有 sa 密码，不是应用密码。已有数据库或登录时停止，不重置密码、不删除资源；若中途失败，先检查，不盲目重跑。

```bash
(
set -e
sudo -v
sudo python3 - <<'PY'
from pathlib import Path
import os
import re

settings = Path('/opt/helpdesk-auto/.env')
if settings.is_symlink() or settings.stat().st_uid != 0 or settings.stat().st_mode & 0o777 != 0o600:
    raise SystemExit('Configuration must be a root-owned 0600 file.')
values = dict(line.split('=', 1) for line in settings.read_text().splitlines()
              if line and not line.startswith('#'))
password = values['HELPDESK_SQL_PASSWORD']
if not re.fullmatch(r'Hd1![0-9a-f]{64}', password):
    raise SystemExit('Unexpected SQL password format. Stopping.')
fd = os.open('/opt/helpdesk-auto/.prepare-database.sql',
             os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
with os.fdopen(fd, 'w') as output:
    output.write(f"""SET NOCOUNT ON;
USE [master];
IF ISNULL(CONVERT(nvarchar(128), SERVERPROPERTY('MachineName')), N'') <> N'homelab-server'
    THROW 50000, 'Wrong SQL Server host. No changes made.', 1;
IF DB_ID(N'HelpDeskDb') IS NOT NULL OR SUSER_ID(N'helpdesk_app') IS NOT NULL
    THROW 50001, 'Database or login already exists. Stop and inspect.', 1;
GO
CREATE DATABASE [HelpDeskDb];
GO
CREATE LOGIN [helpdesk_app] WITH PASSWORD = N'{password}', CHECK_POLICY = ON,
    DEFAULT_DATABASE = [HelpDeskDb];
GO
USE [HelpDeskDb];
CREATE USER [helpdesk_app] FOR LOGIN [helpdesk_app];
ALTER ROLE [db_owner] ADD MEMBER [helpdesk_app];
SELECT DB_NAME() AS DatabaseName,
       IS_ROLEMEMBER(N'db_owner', N'helpdesk_app') AS AppIsDbOwner,
       IS_SRVROLEMEMBER(N'sysadmin', N'helpdesk_app') AS AppIsServerAdmin;
GO
""")
print('Private SQL preparation file created.')
PY
trap 'sudo rm -f -- /opt/helpdesk-auto/.prepare-database.sql' EXIT
sudo /opt/mssql-tools18/bin/sqlcmd \
  -S tcp:127.0.0.1,1433 -U sa -C -b -W \
  -i /opt/helpdesk-auto/.prepare-database.sql
)
```

这个应用登录只在 HelpDeskDb 获得 db_owner，用于启动时 Migration；不授予服务器角色、其他业务数据库或创建数据库权限。SQL 管理员凭据只用于一次性准备，不放进容器、GitHub Secrets 或 .env。

成功查询应显示 HelpDeskDb、AppIsDbOwner=1、AppIsServerAdmin=0。保留 .env 的 root:root 0600 权限；这些配置由 Ubuntu 保存，不通过 Actions 传输。使用上述前缀及 hex 值可避免 Compose 的 `$` 插值问题。

API 通过 host.docker.internal:1433 访问现有主机 SQL。共享 SQL 的监听和防火墙需沿用已批准的 Project1 方式，检查 HelpDesk 容器网段是否可达；不要为了部署关闭防火墙或重新开放公网 1433。

新建的 helpdesk-auto_attachments 和 helpdesk-auto_data-protection 卷会保留上传文件和 Data Protection keys，不使用 Windows 路径。备份必须同时覆盖 HelpDeskDb 与 attachments 卷。日常更新不运行 down -v、volume prune 或 system prune。

## 4. SSH、Tailscale 与 GHCR

为 HelpDesk 新建专用 SSH 密钥，不复用 Project1 的私钥。Windows 交互运行，专用 CI 密钥不设 passphrase，保留私钥在自己电脑和 GitHub Secret 中：

```powershell
ssh-keygen -t ed25519 -f "$HOME/.ssh/helpdesk_github_deploy" -C 'helpdesk-github-deploy'
```

将 .pub 公钥上传 Ubuntu，再安装到 /home/helpdesk_deploy/.ssh/authorized_keys，目录 0700、文件 0600、所有者 helpdesk_deploy。公钥整行前加 `restrict `，禁止转发和 PTY；不覆盖任何已有 authorized_keys。管理员自己的 SSH 连接不受这个新账号限制。

在可信的 Ubuntu 管理会话取得主机公钥：

```bash
printf '%s ' "$(tailscale ip -4)"
sudo cut -d ' ' -f 1,2 /etc/ssh/ssh_host_ed25519_key.pub
```

把输出整行作为 DEPLOY_SSH_KNOWN_HOSTS；不要用未经核对的 ssh-keyscan 替代。

Tailscale 增加 HelpDesk 专用 OIDC federated identity：

- Issuer：GitHub Actions，https://token.actions.githubusercontent.com。
- Subject：`repo:shunbin2310@138860553/HelpDesk-System@1431043746:ref:refs/heads/main`，限定本仓库 main。2026-10-26 已通过 GitHub OIDC 设置 API 核对 use_default=true、use_immutable_subject=true 及 sub_claim_prefix；必须保留 owner / repository ID。
- Custom claim：event_name = push。
- Custom claim：workflow_ref = shunbin2310/HelpDesk-System/.github/workflows/ci.yml@refs/heads/main。
- Scopes：Auth Keys / auth_keys 写入权限，标签仅选 tag:helpdesk-deploy。
- Runner tag：tag:helpdesk-deploy；只给它访问该服务器 TCP 22 的网络权限。
- 保存该身份自己的 client ID 和 audience，不能直接沿用只信任 Purchasing 仓库的 OIDC 身份。
- 对现有 tailnet policy 只增加必要规则，保留 Purchasing 的规则和现有服务器 tags。

GitHub → HelpDesk-System → Settings → Secrets and variables → Actions：

| 类型 | 名称 | 值 |
| --- | --- | --- |
| Variable | DOCKER_DEPLOY_ENABLED | 初始 false，首次服务器验收通过后才改 true |
| Variable | SERVER_TAILSCALE_IP | 该 Ubuntu 的真实 Tailscale IPv4 |
| Secret | TS_OAUTH_CLIENT_ID | 上面新建的 OIDC client ID，名称沿用 Action 输入 |
| Secret | TS_AUDIENCE | 上面新建的 audience |
| Secret | DEPLOY_SSH_PRIVATE_KEY | HelpDesk 专用私钥全文，私下填写 |
| Secret | DEPLOY_SSH_KNOWN_HOSTS | 已核对的服务器主机公钥整行 |

不把部署 Job 加到 PR required checks。PR / 手动运行只验证，发布和部署仅原仓库 main Push；部署还要求开关 true。Job 使用 id-token: write 获取短期 OIDC 身份，不配置长期 Tailscale auth key。

Ubuntu 用 root 的 Docker 配置读取私有 GHCR 包，确保账号对两个 HelpDesk 包都有 read 权限：

```bash
sudo docker login ghcr.io --username shunbin2310
```

在隐藏 Password 提示填写 PAT classic（read:packages），不把 token 写到参数或聊天。若 root 已有 Project1 的 GHCR 登录，先测试它能否读取两个包；替换共享 root 登录前确认原有包仍可读取。不要从凭据文件提取 token，不修改包可见性。

## 5. 首次部署与账号初始化

从 **成功的 main 发布 Job Summary** 取得完整 Commit SHA、API digest、Web digest，管理员在 Ubuntu 手动执行。下面三个全大写值都是占位符，必须替换；digest 包含 sha256: 前缀，不使用 latest：

```bash
sudo /usr/local/sbin/helpdesk-auto-deploy COMMIT_SHA API_DIGEST WEB_DIGEST
```

部署会验证配置，拉取两个 digest，再停止旧 HelpDesk API 并重建两个容器。API 以 Production 启动，始终检查并应用 HelpDeskDb 的 pending migrations；HTTP / Delegation worker 在 Migration 成功后才启动。健康检查覆盖容器及 Web → /api/health 转发，只代表服务响应，业务验收仍需完成。

首次空库部署成功后，在**管理员的交互式 Ubuntu 终端**创建首个账号。只把这个一次性命令容器设为 Development，以复用现有本地 bootstrap-admin 限制；不改变常驻 API 的 Production 配置。数据库连接仍取自 Compose 环境，后台服务和 HTTP 不会启动：

```bash
sudo docker compose --project-directory /opt/helpdesk-auto \
  --env-file /opt/helpdesk-auto/.env --env-file /opt/helpdesk-auto/release.env \
  -f /opt/helpdesk-auto/compose.yml run --rm --no-deps \
  -e ASPNETCORE_ENVIRONMENT=Development api bootstrap-admin
```

按提示私下输入邮箱、姓名、密码及确认密码。不能通过管道或受 restrict 限制的 CI 用户执行。已有管理员时命令会拒绝创建，不重置原账号。

打开 http://所选私网IP:8085，登录后执行 Workflow → Initialize default workflow，建立测试 Employee / Support Admin / Support IT Staff，并测试提交、附件、分配、Resolve、Close / Reopen 与代理。新库没有 Windows 的原账号、数据或默认模板。

确认两个容器 Healthy、业务正常，重建 / 重启后原工单与附件仍在，再把 DOCKER_DEPLOY_ENABLED 改为 true。之后合并 main，检查 **Automatically update Ubuntu** 首次远端执行成功。手动 Run workflow 不发布、不部署；不要期待仅点击 Re-run deploy 就能部署已过时的提交。

## 6. 更新、失败和恢复

正常更新：生成并审核 Migration → PR 三项必需检查 → 合并 main → 发布两个镜像 → 自动部署 exact digest。main 运行不会被后续提交自动取消，部署前会重新核对 main SHA；正在执行的更新不会被中途强杀。

拉取失败发生在停止旧 API 之前，旧服务继续运行。停止阶段开始前写入 /opt/helpdesk-auto/needs-attention；启动、HTTP 检查、容器 restart 设置或发布记录失败时，脚本停止候选 API并保留该标记。断电 / 强制终止时也会留下标记，需要管理员查看实际容器和数据库状态。

查看当前成功版本及日志：

```bash
sudo cat /opt/helpdesk-auto/release.env
sudo cat /opt/helpdesk-auto/needs-attention
sudo docker ps -a --filter label=com.docker.compose.project=helpdesk-auto
sudo docker logs --tail 100 helpdesk-auto-api-1
```

needs-attention 在成功时不存在；失败日志可能包含业务信息，先检查再分享。不要删除标记后盲目反复启动，也不要自动切回旧镜像：Migration 可能已经改变数据库。确认修复、Schema 兼容或备份恢复策略后，由管理员移除该标记，再明确重新执行选定版本的固定部署命令。

成功记录为 release.env，上一次成功记录为 previous-release.env；这不是数据库或附件备份。服务器上的 root-owned Compose / 配置 / 脚本不会随着镜像自动更新，修改这些文件须由管理员审阅后安装。

## 7. 开机时 LAN 地址尚未出现

2026-10-27 现场确认：Docker 启动时 `192.168.1.125` 尚不可用，Web 报 `cannot assign requested address`。后续 `docker start` 虽启动了进程，仍出现 Configured 有 `8085`、Actual 为空；只重建 Web 后端口映射和 `/api/health` 恢复。Docker 已有 `After=network-online.target`，不能仅添加同样的顺序解决。

`scripts/deploy/helpdesk-web-boot.service` 是 HelpDesk 独立的一次性开机补偿：等待 `.env` 中的绑定 IPv4 实际出现，最多 180 秒；取得与 CI/CD 共用的部署锁后，再检查失败标记。如果主机端口的 `/health` 已响应，则不改容器；否则按 `release.env` 已记录的镜像重建 Web，并检查主机端口。它不拉镜像、不重建 API、不执行数据库更新，也不修改 Docker / SQL / Purchasing 的启动配置。

该 unit 不持续监控容器、不提供 systemd Restart / ExecStop；正常容器重启仍由既有 Docker restart policy 负责。它只在开机或管理员明确 start 时执行，不绕过 `needs-attention`，不在失败时反复重建。

先在 Windows 仓库根目录上传新文件（使用已核对的管理员 SSH）：

```powershell
scp -o StrictHostKeyChecking=yes .\scripts\deploy\helpdesk-web-boot.service shunbin@192.168.1.125:helpdesk-setup/
if ($LASTEXITCODE -ne 0) { throw '上传失败，先检查。' }
```

再由 Ubuntu 管理员首次安装；已有同名 unit 时停止，先审阅，不覆盖：

```bash
(
set -e
sudo -v
test "$(hostname)" = homelab-server
unit=/etc/systemd/system/helpdesk-web-boot.service
if sudo test -e "$unit" || sudo test -L "$unit"; then
    echo 'Unit already exists. Stop and inspect.'
    exit 1
fi
sudo systemd-analyze verify --man=no ~/helpdesk-setup/helpdesk-web-boot.service
sudo install -m 0644 -o root -g root ~/helpdesk-setup/helpdesk-web-boot.service "$unit"
sudo systemctl daemon-reload
sudo systemctl enable --now helpdesk-web-boot.service
sudo systemctl show helpdesk-web-boot.service -p UnitFileState -p Result -p ExecMainStatus
sudo journalctl -u helpdesk-web-boot.service -n 20 --no-pager
curl --noproxy '*' -fsS http://192.168.1.125:8085/api/health
)
```

当前网站健康时应记录 `HelpDesk Web already responds; no container changes.`；oneshot 成功后显示 inactive/dead 属正常，检查 Result=success、ExecMainStatus=0、UnitFileState=enabled。不要为这项安装重启共享 Docker 或 SQL。

真实重启验收需另选可中断 Purchasing 的维护时间，由管理员明确执行。重启后检查 unit 日志、两个容器 Healthy、`docker port helpdesk-auto-web-1`、Web → API、登录及原工单 / 附件。未做实际重启前，不能把安装成功当作开机恢复已验收。

如果 180 秒后仍无绑定 IP，unit 会超时；先恢复该地址，再运行 `sudo systemctl start helpdesk-web-boot.service`。若 DHCP 改了服务器地址，应先确认新的固定地址及入口配置，不自动改为 `0.0.0.0`。停用这项补偿可执行 `sudo systemctl disable helpdesk-web-boot.service`，不会停止现有容器。

## 验证状态

- 2026-10-25 main `14abc12` 的测试、镜像构建及 GHCR 发布已成功；本机实际拉取两个镜像，并验证登录、工单、3 个附件下载哈希和数据库记录数量。
- 本次新增 CD 的 actionlint、14 项隔离 Linux 成功 / 失败 / 并发 / 权限测试、正式 Compose 配置断言和真实 Web 镜像启动检查通过；不使用服务器凭据，不连接共享数据库。Web 仅保留 Nginx 初始化缓存和切换 worker 用户需要的 CHOWN / SETGID / SETUID，API 移除全部 capabilities。
- 2026-10-26 用户在 homelab-server / 192.168.1.125 完成安装：文件 root 权限及专用 sudo 入口检查通过；HelpDeskDb 和 helpdesk_app 已新建，专用账号实际登录成功，db_owner=1、sysadmin=0。两个 GHCR digest 拉取成功，固定脚本首次手动部署 `14abc12` 成功，API / Web Healthy，服务器自身的 Web → API 检查通过；Purchasing 先前检查的 RestartCount 为 0。
- 首个管理员已创建，真实 HTTP 登录及默认 Workflow 查询通过。HelpDesk 专用密钥的 LAN SSH 登录、部署用户身份、限定 sudo 权限、固定入口的参数拒绝和网站健康检查均已验证。
- PR #8 合并后 main `4236b6891562d7e1403fe94a1b0cf7d6888b8879` 的[运行 40355611450](https://github.com/shunbin2310/HelpDesk-System/actions/runs/40355611450) 五个 Job 全部成功，包括 Publish GHCR images 和 Automatically update Ubuntu。2026-10-27 已通过 GitHub API 核对；用户提供的服务器 release.env 与该版本和两个发布 digest 一致，首次 Runner OIDC / Tailscale / SSH 自动部署已跑通。
- 2026-10-27 修复现场 Web 端口丢失后，API / Web Healthy、`192.168.1.125:8085` 映射及 `/api/health` 通过；另验证管理员登录、身份、默认 Workflow、用户 / Ticket / Delegation 列表接口成功。完整四角色业务和附件持久化验收仍待完成。
- 开机补偿 unit 的 systemd 语法校验、9 项恢复测试及既有 14 项部署测试全部通过；actionlint 通过。测试在本机一次性 Linux 容器中运行，使用假 IP / Docker / HTTP，不连接服务器。2026-10-27 用户已在 Ubuntu 安装并启用 unit，首次执行记录 `HelpDesk Web already responds; no container changes.`，Result=success、ExecMainStatus=0、UnitFileState=enabled。随后经 SSH 核对已安装文件 SHA256 与仓库一致，Web → API 健康检查通过。真实重启及启动故障恢复验收仍待完成，不能把健康站点上的首次执行当作开机恢复已通过。

参考：[Tailscale GitHub Action](https://tailscale.com/docs/integrations/github/github-action)、[OIDC federation](https://tailscale.com/docs/features/workload-identity-federation)、[GitHub OIDC Subject](https://docs.github.com/en/actions/reference/security/oidc)、[Docker Compose up](https://docs.docker.com/reference/cli/docker/compose/up/)、[Registry manifest inspection](https://docs.docker.com/reference/cli/docker/buildx/imagetools/inspect/)、[systemd service](https://www.freedesktop.org/software/systemd/man/latest/systemd.service.html)。
