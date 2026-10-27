# HelpDesk：Quick Tunnel 与固定 Live Demo 入口

沿用 Project1 的做法，README 只保存固定入口：

`https://shunbin2310.github.io/HelpDesk-System/`

Ubuntu 的 `helpdesk-demo.service` 在开机后等待 HelpDesk 本地首页和 `/api/health`，启动独立 cloudflared，取得新的 `https://*.trycloudflare.com` 地址。确认公网也返回 `status=healthy`、`service=HelpDesk.Api` 后，自动更新本仓库 **demo-links/index.html**，由 GitHub Pages 发布跳转页。README 和受保护的 main 不需要在每次开机时修改。

## 1. 当前范围

- HelpDesk 源站为 `http://192.168.1.125:8085`；不通过 Purchasing 的主机 Nginx 80 端口。
- 使用独立 `/opt/helpdesk-demo`、`/etc/helpdesk-demo` 和 `helpdesk-demo.service`；不读取 Project1 Token，不修改或重启 `project1-demo.service`。
- 服务使用 DynamicUser 和 systemd LoadCredential，没有 Docker / sudo / SQL 权限，不重建容器。网站自身的开机恢复仍由已安装的 `helpdesk-web-boot.service` 负责。
- GitHub 写入目标锁定 `shunbin2310/HelpDesk-System/demo-links`。不会写 main，也不会修改 Purchasing 仓库；同名分支有非受管理页面时拒绝覆盖。
- Python 只使用标准库。服务器已确认安装 cloudflared，无需重装。
- 这是公开的临时演示入口；知道 URL 的人可以打开登录页，业务接口仍要求原有 JWT / 角色权限。不要在 README 发布管理员密码，也不要使用真实敏感工单作演示。

## 2. 上传并安装独立服务

先按正常 PR / CI 流程合并这次文件；应用 CI 排除 `demo-links` Push，Pages 更新不会触发镜像构建或 Ubuntu 部署。不要手动从 main 创建 demo-links，也不要把 demo-links 合并到 main。

在 **Windows PowerShell，HelpDesk-System 仓库根目录**执行。首次准备目录已存在时先检查，不重复覆盖：

```powershell
ssh -o StrictHostKeyChecking=yes shunbin@192.168.1.125 'mkdir -m 700 ~/helpdesk-demo-setup'
if ($LASTEXITCODE -ne 0) { throw '准备目录失败，请先检查现有目录和连接。' }
scp -o StrictHostKeyChecking=yes .\scripts\homelab\quick_tunnel.py .\scripts\homelab\helpdesk-demo.service .\scripts\homelab\demo.env.example .\scripts\homelab\install.sh shunbin@192.168.1.125:helpdesk-demo-setup/
if ($LASTEXITCODE -ne 0) { throw '上传失败。' }
```

在 **Ubuntu 管理员 SSH 终端**执行：

```bash
cd ~/helpdesk-demo-setup
sudo bash install.sh
```

安装器校验主机、前置程序、Python / unit 语法及 HelpDesk 开机恢复 unit 已启用；只安装文件，不启动服务。同名目录或 unit 已存在时停止，不覆盖私密配置。修改已安装版本时先比较文件，再由管理员明确更新；不要删除目录后盲目重装。

| 安装位置 | 权限与用途 |
| --- | --- |
| /opt/helpdesk-demo/quick_tunnel.py | root:root 0644，服务脚本；目录 0755 |
| /etc/systemd/system/helpdesk-demo.service | root:root 0644，开机 unit |
| /etc/helpdesk-demo/config | root:root 0600，非秘密配置；目录 0700 |
| /etc/helpdesk-demo/github-token | 下一步由管理员私下创建，root:root 0600 |

默认 config 的 HELPDESK_ORIGIN 与已选 LAN 绑定一致。若源站地址变化，由管理员修改 config；只允许私网 / loopback / Tailscale IPv4 的 HTTP 8085，不接受公网 IP、账号密码、路径或 query string。

## 3. 创建 HelpDesk 专用 GitHub Token

GitHub 个人 **Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token**：

| 设置 | 值 |
| --- | --- |
| Token name | helpdesk-demo-link |
| Expiration | 例如 90 days，到期前更换 |
| Resource owner | shunbin2310 |
| Repository access | Only select repositories → HelpDesk-System |
| Repository permissions | Contents → Read and write；Metadata 保留自动只读 |

不授予 Workflows、Actions、Administration 或 Pages 写权限；Pages 来源稍后由你在设置页面选择。不复用 Project1 Token。Token 能创建和更新 demo-links，但受保护 main 的规则继续保留；不要为此开放 main bypass。

Token 只私下粘贴到 Ubuntu 的隐藏输入提示，不发送聊天、写入 shell 参数或提交 Git。在 Ubuntu 管理员交互终端执行：

```bash
sudo python3 - <<'PY'
from getpass import getpass
import os
from pathlib import Path

token = getpass('Paste the HelpDesk GitHub token (hidden): ').strip()
if not token or any(char.isspace() for char in token):
    raise SystemExit('Expected one non-empty token; nothing was written.')
path = Path('/etc/helpdesk-demo/github-token')
fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
with os.fdopen(fd, 'w', encoding='utf-8') as output:
    output.write(token + '\n')
print('Token saved privately. Its value was not printed.')
PY
```

已有 Token 文件时命令拒绝覆盖；轮换时使用可信管理员终端私下修改，并保持 root:root 0600，随后重启 **helpdesk-demo.service**。systemd 只把凭据副本交给动态服务用户，cloudflared 子进程不继承 Token 或 CREDENTIALS_DIRECTORY。

## 4. 启动 Tunnel

```bash
curl --noproxy '*' -fsS http://192.168.1.125:8085/api/health
sudo systemctl enable --now helpdesk-demo.service
sudo journalctl -u helpdesk-demo.service -n 80 --no-pager
```

期望先看到 `Detected demo URL: https://....trycloudflare.com`，本地及公网检查成功后看到 `Redirect updated on demo-links`。启动、DNS 或 GitHub 暂时失败会等待或重试；相同 URL 不重复提交，GitHub 冲突会重新获取文件 SHA，不强制覆盖历史。

先打开日志中的 Quick Tunnel，确认是 HelpDesk 并能登录。此步骤只证明 Tunnel 和跳转分支已发布，尚需开启 Pages。

## 5. 一次性开启 GitHub Pages

脚本首次发布会创建只有 `index.html` 和 `.nojekyll` 的独立 demo-links 分支，不复制应用代码。看到成功日志后，在 **HelpDesk-System → Settings → Pages → Build and deployment** 设置：

| 设置 | 值 |
| --- | --- |
| Source | Deploy from a branch |
| Branch | demo-links |
| Folder | / (root) |

点击 Save。若看不到分支，先检查服务日志并刷新；不要提前从 main 创建。若该仓库已有 Pages 网站，先检查用途，不覆盖现有来源。

等待 GitHub Pages 的部署成功后，README 的 **Open Live Demo** 应自动跳到最新 Tunnel。Pages 只托管跳转页，Vue / API / SQL 仍在 Ubuntu；更新可能需要几分钟，期间旧 Tunnel 链接可能已失效。

## 6. 验收与日常使用

1. 用手机移动数据打开 README 的固定入口，确认进入 HelpDesk。
2. 验证登录、原工单和附件；关闭 SSH 后再访问，确认后台服务持续运行。
3. 在允许短暂更换演示地址时，执行 `sudo systemctl restart helpdesk-demo.service`，确认新 URL 发布，Pages 入口随后跳到新地址。它不重启 Purchasing Tunnel。
4. 共享服务器真正重启需要另选维护时间；重启后检查 HelpDesk Web、开机恢复 unit 和 demo unit，再用相同 Pages 入口验证。不要把一次服务重启当作整机重启已验收。

```bash
sudo systemctl status helpdesk-demo.service --no-pager
sudo journalctl -u helpdesk-demo.service -b -n 80 --no-pager
```

关机时 Demo 离线，Pages 保留上次地址；本方案不配置午夜自动关机。临时重连由 cloudflared 自己处理，进程退出由 systemd 重启并生成新地址。既有 API / Web 部署不会主动重启 Tunnel；它继续指向相同 8085 入口。

Cloudflare Quick Tunnel 没有可用性保证、每条最多 200 个并发请求且不支持 SSE，适合测试演示。需要固定域名或生产保证时再改 Named Tunnel，本次不增加该范围。

## 7. 排查与停用

| 现象 | 检查 |
| --- | --- |
| Waiting for the local website | 8085、两个容器和 HelpDesk /api/health；不是 Purchasing 的 80 端口 |
| Waiting for the local and public website/API | 新 URL 的 DNS、cloudflared 连接及 HelpDesk 健康 JSON |
| GitHub HTTP 401 / 403 | Token 是否到期、仅授权了其他仓库或缺少 Contents write |
| GitHub HTTP 409 / 422 | demo-links 是否也被要求 PR / CI，或发生并发更新；不要移除 main 保护 |
| Redirect update failed (ValueError) | 同名分支页面缺少本脚本唯一标记，脚本拒绝覆盖 |
| Pages 404 | 是否选择 demo-links / root、Pages 部署是否完成 |
| Error 1033 或旧 URL | 当前服务及最新 URL 是否仍在线；查看最新分支、Pages 部署并无痕刷新 |

HTTP 错误只打印经截断和 Token 脱敏的 message，不打印完整响应或请求头。不要发送 Token、/etc/helpdesk-demo/github-token、应用 .env 或数据库凭据。

只停用 HelpDesk 演示入口，保留网站和 Purchasing：

```bash
sudo systemctl disable --now helpdesk-demo.service
```

## 验证状态

2026-10-27 已参考 Project1 当前源码并实际检查：project1-demo.service 正在运行，cloudflared 已安装，HelpDesk LAN /api/health 正常。公共 GitHub API 尚未返回 HelpDesk 的 Pages 站点或 demo-links 分支。28 项离线 Tunnel / 安装测试全部通过，systemd unit 与安装器语法校验、actionlint 和 diff 检查通过；测试使用临时 Linux 容器、本地 HTTP 和假 GitHub，不使用真实凭据。本次只准备仓库文件；Token、服务器安装、真实 Tunnel、Pages 和重启后的链接更新尚未验证，不代表公开 Demo 已上线。

官方参考：[Cloudflare Quick Tunnel](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/)、[GitHub Pages 发布来源](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site)、[GitHub Contents API](https://docs.github.com/en/rest/repos/contents#create-or-update-file-contents)。
