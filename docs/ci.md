# GitHub Actions CI

配置文件为 `.github/workflows/ci.yml`。除 demo-links 外的 Push、Pull Request 和手动 Run workflow 执行验证；其他分支 / PR 的新运行取消旧运行，main 正在执行的运行不自动取消。demo-links 的自动跳转页更新由 GitHub Pages 单独发布，不触发应用镜像构建或部署。原仓库 main Push 全部检查通过后发布 GHCR 镜像；服务器准备完成且 DOCKER_DEPLOY_ENABLED=true 时继续部署 Ubuntu。镜像及本机拉取方式见[GHCR 说明](ghcr.md)，首次服务器设置见[Ubuntu 部署说明](ubuntu-deployment.md)。

## 检查内容

| Job | 检查 |
| --- | --- |
| Backend tests and migrations | .NET 10 restore / build、EF 模型与 Snapshot 一致性、空库 Migration、单元测试及 SQL Server 集成测试 |
| Frontend checks and build | Node.js 24、npm ci、Prettier 检查、vue-tsc 类型检查及 Vite 生产构建 |
| Build Docker images | 等后端和前端都通过，再使用已有 Dockerfile 构建 API / Web 镜像 |
| Publish GHCR images | 仅原仓库 main Push，等待 images 成功后发布两个 Commit SHA 版本的 GHCR 镜像；PR / 手动运行跳过 |
| Automatically update Ubuntu | 依赖 publish 成功及部署开关 true，经 Tailscale OIDC / 固定 SSH 入口部署两个 exact digest；PR / 手动运行跳过 |

Backend Job 还在隔离 Runner 中执行部署和开机补偿测试，使用假 IP / Docker / HTTP 与临时文件，验证地址延迟、失败标记、权限及部署锁，并使用 systemd-analyze 校验 unit 语法，不连接 Ubuntu。

Quick Tunnel 脚本也在该 Job 使用本地 HTTP / 假进程与假 GitHub 响应验证，不访问真实 Cloudflare / GitHub，也不需要 Token；安装器在临时文件系统测试，不修改 Runner 的服务。

Build Docker images 还使用 compose.ubuntu.yml 的真实权限运行 Web 健康检查；API 不启动、不连接 SQL、不发布主机端口，测试项目和临时卷在退出时清理。

SQL Server 2022 使用固定镜像 digest，在 GitHub-hosted Ubuntu 24.04 Runner 的临时容器内运行。健康检查通过后才执行步骤；先在 HelpDeskCi 应用 Migration，因为部分现有数据库约束及账号存储测试需要已初始化的库。其余流程和账号测试继续创建自己的随机测试库并清理。

`HELPDESK_TEST_CONNECTION` 由 Workflow 写死为 Runner 自己的回环测试端口；`database update` 的连接明确取自此值。模型一致性检查则使用不可连接的占位地址，走现有设计时 Factory，只比较 EF 模型和 Snapshot，不启动 API、不运行后台任务、不更新数据库。

本流程不读取本机 `.env.docker.local`、`.env.docker.host-sql`、Windows BIN / HelpDeskDb 或部署凭据。Workflow 中的 SQL 密码是公开的临时 CI 值，只供该次临时服务使用，不要用于任何长期数据库。运行 CI 不需要配置 SQL 或部署 Secrets。

## 测试结果与失败行为

单元测试和集成测试分别生成 TRX。CI 检查两份报告：测试总数必须大于零，全部执行且全部通过，缺少报告、空测试集或 Skipped 都会失败。这样即使 SQL 测试因缺少配置被跳过，也不能显示全绿。

后端步骤失败时输出临时 SQL Server 日志；测试报告尽可能作为 `backend-test-results` Artifact 保存 7 天。任何前置检查失败，Docker 构建 Job 就不执行。

原三个检查 Job 的权限只有 `contents: read`，Checkout 不保留 Git 凭据。main Push 发布 Job 单独使用 `packages: write` 和内置 `GITHUB_TOKEN` 登录 GHCR；部署 Job 单独使用 `id-token: write` 和 HelpDesk 专用 SSH 凭据。检查 Job 不读取部署 Secrets，也不调用本机 Compose。Actions 固定到已核对的 commit SHA。PR 使用普通 `pull_request`，不使用 `pull_request_target`。

## 提交后查看

进入仓库 **Actions → HelpDesk CI** 查看 Job。PR / 功能分支的原三个检查运行，发布 Job 跳过；main Push 会在检查通过后继续发布。修复失败并再次 Push，会触发新的检查。手动 **Run workflow** 入口需要 Workflow 已存在于默认分支，手动运行只验证，不发布。

用户已配置 main Ruleset，原三个 required check 名称保持不变；发布 Job 不需要添加为 PR 必需检查。本轮没有修改远端仓库规则。

## 本地验证结果（2026-10-25）

- 官方 actionlint 1.7.12：Workflow、Actions 输入、表达式及内嵌 Shell / Python 检查通过。
- 干净的 Linux .NET SDK 10 容器中执行后端步骤：构建 0 警告 / 0 错误，66 项单元测试和 32 项 SQL 集成测试全部通过，无跳过。
- SQL 测试使用独立临时容器，没有发布主机端口；从空库应用 4 份现有 Migration。模型检查使用不可连接的占位地址并通过。
- Node.js 24 Linux 容器中 npm ci、格式检查、类型检查及生产构建通过。
- 使用 Workflow 中的两个 Docker build 命令，API 和 Web 镜像构建通过，没有推送镜像。
- 使用真实 TRX 验证报告检查通过，并确认它会拒绝 skipped、failed、empty、missing 和 missing-counters 五类报告。
- Linux TRX 结果保留在本机 Git 忽略的 `artifacts/ci-validation/` 下。临时验证容器、网络和源码副本在完成后清理。

以上是初版 CI 的本地 Linux 等效验证。其后 PR #6 的三项检查通过；PR #7 合并后 main `14abc12` 的四个 Job 全部成功，实际 GHCR 发布与本机拉取运行已验收。PR #8 合并后 main `4236b68` 的[运行 40355611450](https://github.com/shunbin2310/HelpDesk-System/actions/runs/40355611450) 五个 Job 全部成功，首次 Ubuntu 自动部署已验证。2026-10-27 已恢复 Web 端口映射并确认登录及列表接口；完整业务、持久化及开机恢复验收仍待完成。

参考：[actions/setup-dotnet](https://github.com/actions/setup-dotnet)、[actions/setup-node](https://github.com/actions/setup-node)、[actionlint 使用说明](https://github.com/rhysd/actionlint/blob/main/docs/usage.md)。
