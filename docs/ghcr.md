# GHCR 镜像发布与本机验证

Workflow 保留原来的三个必需检查：Backend tests and migrations、Frontend checks and build、Build Docker images。新增 Publish GHCR images，只有原仓库 shunbin2310/HelpDesk-System 的 main Push 才运行，并等待镜像构建 Job 成功；后者仍依赖后端与前端检查通过。

PR、功能分支 Push、Tag、Fork 和手动 Run workflow 都只验证，不发布。不要把 Publish GHCR images 加到 PR 的 required checks。main 的正在执行运行不会因新提交而自动取消，减少两个镜像上传到一半被中断的情况；其他分支和 PR 仍取消旧运行。

## 镜像与版本

| 镜像 | 发布引用 |
| --- | --- |
| API | ghcr.io/shunbin2310/helpdesk-api:完整的 main Commit SHA |
| Web | ghcr.io/shunbin2310/helpdesk-web:同一个完整 Commit SHA |

两个镜像先都构建成功，再依次推送。OCI Labels 包含来源仓库 URL 和源码 Commit SHA。使用固定提交版本，不额外维护 latest；需要锁定实际二进制镜像时可使用 registry digest。SHA Tag 表示源码版本，重新运行相同提交的构建仍可能因基础镜像更新而产生新 digest。

GitHub Summary 会列出两个发布引用及 Source commit。请从 **main 的成功发布运行**复制这个 SHA，尤其 Squash merge 后不要直接使用 PR 分支的旧 SHA。两个上传不是事务：只有整个 Publish GHCR images 成功后，才使用该版本；任一上传失败时，先解决错误并重新运行对应 main Push 的失败 Job。

## GitHub 权限

只有 Publish GHCR images Job 获得 `packages: write`，使用 GitHub 自动生成的 `GITHUB_TOKEN` 登录 GHCR，完成后退出。其他三个 Job 保持 `contents: read`。不需要手动把 PAT、数据库连接、SQL 密码或部署凭据加入 Actions Secrets。

GHCR 新包默认 private，本实现不修改包可见性。来源仓库 Label 用于将包关联到当前仓库。若包名称已被预先创建且发布返回 permission_denied，检查对应 Package 的 Settings → Manage Actions access，确认 HelpDesk-System 有写入权限；不要将 PR Job 或整个仓库默认 Token 改为广泛写权限来绕过。

## 首次发布

1. 提交本轮修改并创建 PR，确认原来的三个检查通过。
2. 合并到 main；main Push 会再次验证，并在通过后发布两个镜像。
3. 在 Actions → HelpDesk CI → Publish GHCR images 查看日志和 Summary，也可在账号 Packages 页面查看 helpdesk-api 和 helpdesk-web。

发布本身不更新本机 Docker。Ubuntu 部署 Job 只有 DOCKER_DEPLOY_ENABLED=true 时才执行；首次安装及开机恢复见[Ubuntu 部署说明](ubuntu-deployment.md)。

## 本机拉取与运行

新包默认私有。本机 Docker 如果尚未登录拥有读取权限的 GitHub 账号，先运行：

```powershell
docker login ghcr.io --username shunbin2310
```

在 Password 提示中填写具有 `read:packages` 权限的 PAT classic，不是 GitHub 登录密码，也不是应用账号密码。不要将 Token 放到 Compose、环境示例、Git 或聊天中。已有可用 GHCR 登录时可直接拉取。

在仓库根目录，设置已成功发布的 main SHA，然后执行：

```powershell
$env:HELPDESK_IMAGE_TAG = '替换为发布 Summary 中的完整 Commit SHA'
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml -f compose.ghcr.yml config --quiet
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml -f compose.ghcr.yml up -d --no-build --pull always --wait --wait-timeout 180
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml -f compose.ghcr.yml ps
```

compose.ghcr.yml 只覆盖镜像引用；数据库连接、端口、附件绑定和 Data Protection keys 都沿用 compose.host-sql.yml。基础文件仍带有 build 定义，所以本模式必须保留 `--no-build --pull always`，确保从 Registry 拉取该版本，并且拉取失败时不会改为本机临时构建。

`HELPDESK_IMAGE_TAG` 可追加到本机 Git 忽略的 .env.docker.host-sql 中保存；否则新 PowerShell 终端需要重新设置。没有版本值时 Compose 会直接报错。

打开 <http://localhost:8080>（或环境文件配置的网页端口），用原 HelpDesk 账号登录，检查原工单和附件下载。此模式连接 Windows 的真实 BIN / HelpDeskDb，新镜像启动时仍会应用 Pending Migration；涉及删列等修改时先备份数据库与附件。切回旧镜像不会自动回退数据库 Schema，需确认兼容性。

检查实际使用的镜像：

```powershell
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml -f compose.ghcr.yml images
```

如需切回本地源码构建的运行方式：

```powershell
docker compose --env-file .env.docker.host-sql -f compose.host-sql.yml up -d --build --wait --wait-timeout 180
```

## 本轮验证范围

- actionlint 通过；六种事件 / 仓库 / 分支组合验证只有原仓库 main Push 能发布。
- main 不自动取消、其他分支 / PR 仍取消旧运行的条件验证通过。
- 两个 GHCR 名称的候选镜像在本机构建成功，来源 / revision Labels 正确；镜像环境未包含运行时 SQL / JWT 凭据。
- Compose 合并配置保留原 host SQL 目标和现有附件绑定，缺少版本时正确拒绝。
- 本轮没有修改业务源码或数据库，也没有重新运行整套业务测试。
- 首次实际发布已完成：PR #7 合并后的 main `14abc12f64b1f92160f9122cd1b2e559622a7c36` 在 [Actions run 40207526290](https://github.com/shunbin2310/HelpDesk-System/actions/runs/40207526290) 的四个 Job 均成功。本机从 GHCR 拉取两个镜像，确认标签 / digest、容器健康、登录和工单查询、3 个现有附件下载哈希及记录数量；2 个旧消息附件仍隐藏。
- CD 在两个推送都成功后，从 Registry 读取 digest 并输出到 Summary，传给 Ubuntu 固定部署入口。PR #8 合并后 main `4236b68` 的[运行 40355611450](https://github.com/shunbin2310/HelpDesk-System/actions/runs/40355611450) 已成功执行发布和 Ubuntu 自动更新；服务器 release.env 与发布版本一致。完整业务、持久化及开机恢复验收仍待完成。

参考：[GitHub Container registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)、[发布 Docker 镜像](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images#publishing-images-to-github-packages)。
