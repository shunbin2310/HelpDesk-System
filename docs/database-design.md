# Helpdesk System 数据库设计草案

日期：2026-10-23

数据库：Microsoft SQL Server；通过 SSMS 管理，应用通过 EF Core 访问。

状态：实现前草案。字段类型、长度、索引和技术约束属于设计建议；业务规则以[需求说明](requirements.md)为准。本次不生成 SQL、Entity 或 Migration。

## 1. 设计原则

- 单 IT Support 团队，每个账号一个角色，Email 唯一。
- Tickets 保存工单资料及当前负责人；工作流实例保存当前步骤，避免两个可独立更新的状态来源。
- Template 定义流程，Instance 保存每张 Ticket 创建时的流程副本，History 保存真实操作。
- 分配和代理改变负责人，不改变流程步骤。
- 操作身份来自后端认证上下文。业务变更和对应历史在同一个数据库事务中提交。
- 时间统一使用 UTC 的 datetimeoffset(7)，页面按用户时区显示。本次用户时区为 Asia/Kuala_Lumpur。
- 主键建议使用 SQL Server IDENTITY：Users 使用 int，其余表使用 bigint；所有外键类型与目标一致。
- 需要并发修改的业务记录使用 rowversion；它是并发标记，不是日期。

## 2. 表与关系概览

共规划 16 张核心表：7 张业务表、4 张 Template 表、4 张 Instance 表、1 张 WorkflowHistory。

```text
Users ──< Tickets（提交人 / 当前负责人）
            ├──< TicketMessages ──< Attachments
            ├──< Attachments（直接附在 Ticket 的文件）
            ├──< TicketHistories
            ├──< DelegationTickets >── Delegations >── Users（A / B）
            └── WorkflowProcessInstance
                    ├──< WorkflowStepInstance
                    │        └──< WorkflowActionInstance
                    │                   └──< WorkflowActionerInstance
                    └──< WorkflowHistory

WorkflowProcessTemplate
    └──< WorkflowStepTemplate
             └──< WorkflowActionTemplate
                        └──< WorkflowActionerTemplate
```

每张 Ticket 对应且只对应一个 WorkflowProcessInstance。Action 通过 FromStep 和 ToStep 连接同一流程的两个 Step。

## 3. 固定代码

角色、分类和优先级暂使用 C# Enum 与数据库数值代码，不增加独立维护表。下面的数值是草案建议，上线后应保持稳定。

| 数据 | 建议代码 |
| --- | --- |
| Role | 1 Employee；2 SupportITStaff；3 SupportAdmin；4 SystemAdmin |
| Category | 1 Hardware；2 Software；3 Network；4 Account；5 Other |
| Priority | 1 Low；2 Medium；3 High；4 Critical |
| MessageType | 1 PublicReply；2 InternalNote |
| 业务 Step Code | OPEN、IN_PROGRESS、RESOLVED、CLOSED |
| Action Code | START_PROCESSING、RESOLVE、CLOSE、REOPEN |
| 流程实例运行状态 | Running、Completed；只有进入 CLOSED 才 Completed |

Step Code 表达业务状态，WorkflowProcessInstance.Status 表达整个流程是否结束，两者含义不同。没有 Pending 或 Reopened 业务状态。

## 4. 业务表

### 4.1 Users

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id | int IDENTITY，PK | 用户标识 |
| Email / NormalizedEmail | nvarchar(256) | 登录 Email / 用于唯一比较的规范化值 |
| FullName | nvarchar(200) | 显示姓名 |
| PasswordHash | nvarchar(max) | 使用可靠密码 Hash 组件的输出 |
| Role | tinyint | 单个角色代码 |
| IsActive | bit | 账号是否可用 |
| CreatedAtUtc / UpdatedAtUtc | datetimeoffset(7) | 建立和更新时间 |
| RowVersion | rowversion | 并发标记 |

NormalizedEmail 建唯一索引。没有 UsersRoles 多对多表。密码算法及身份框架尚未选定，不保存明文密码。

后续认证参考 Project1 的 JWT Bearer 实现。Project1 使用完整 ASP.NET Core Identity，具有 SecurityStamp、登录失败计数和锁定等账号字段，以及 Identity 的角色关联表；当前 16 表草案尚未包含这些完整结构。首次 Migration 前需确定是否采用 Identity，或在单角色 Users 上补充所需的会话版本 / 登录控制字段，再修订本节和表数量。参考 JWT 不表示直接复制多角色模型，也不表示这些字段已经建立。

### 4.2 Tickets

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id | bigint IDENTITY，PK | 内部标识 |
| TicketNumber | nvarchar(32)，UNIQUE | 展示编号；具体格式待确认 |
| Title | nvarchar(200) | 标题 |
| Description | nvarchar(max) | 问题描述 |
| Category / Priority | tinyint | 分类及优先级，Priority 默认 Medium |
| RequestedByUserId | int，FK Users | 提交人，必须为 Employee |
| AssignedToUserId | int NULL，FK Users | 当前负责人，未分配时为空 |
| AssignmentVersion | bigint | 每次负责人安排变更时递增，辅助识别后续人工安排 |
| CreatedAtUtc / UpdatedAtUtc | datetimeoffset(7) | 时间 |
| RowVersion | rowversion | 防止同时覆盖修改 |

AssignedToUserId 非空时，目标必须为可用的 Support IT Staff。角色和可用性由业务服务检查，不能仅依赖 FK。

Tickets 不另存可独立修改的 Status。列表和详情从 WorkflowProcessInstance.CurrentStepInstanceId 对应的 Step Code 取得状态；若以后为查询性能增加状态缓存，应在同一事务更新并明确它不是第二个事实来源。

AssignmentVersion 是技术方案建议，用于防止代理任务覆盖较新的人工分配，例如 A → B 后人工转 C，再人工转 B。单看当前负责人等于 B 不足以识别这些后续安排。

### 4.3 TicketMessages

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id | bigint IDENTITY，PK | 消息标识 |
| TicketId | bigint，FK Tickets | 所属工单 |
| AuthorUserId | int，FK Users | 作者 |
| MessageType | tinyint | PublicReply / InternalNote |
| Body | nvarchar(max) | 消息内容 |
| CreatedAtUtc | datetimeoffset(7) | 发送时间 |

Employee 查询时必须过滤 InternalNote。消息编辑、删除及内容格式的具体策略待确认。

### 4.4 Attachments

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id | bigint IDENTITY，PK | 附件标识 |
| TicketId | bigint，FK Tickets | 所属工单 |
| TicketMessageId | bigint NULL，FK TicketMessages | 空表示直接附于 Ticket；否则附于消息 |
| UploadedByUserId | int，FK Users | 上传人 |
| OriginalFileName | nvarchar(255) | 原始文件名 |
| StorageKey | nvarchar(450) | 服务端存储标识，具体存储方案待确认 |
| ContentType | nvarchar(128) | 经服务端检查的内容类型 |
| SizeBytes | bigint | 文件大小 |
| CreatedAtUtc | datetimeoffset(7) | 上传时间 |

消息附件必须与消息属于同一 Ticket。附件权限继承 Ticket 和消息可见性，InternalNote 附件不能通过直接 URL 或附件接口被 Employee 读取。存储位置不作为授权依据，也不直接向客户端暴露内部文件路径。

### 4.5 TicketHistories

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id / TicketId | bigint PK / FK Tickets | 记录及所属工单 |
| EventType | nvarchar(64) | Assigned、Reassigned、PriorityChanged、Delegated、DelegationReturned 等 |
| ActorUserId | int NULL，FK Users | 操作人；后台到期任务可为空 |
| ActorNameSnapshot | nvarchar(200) | 留存操作人姓名或 System |
| OldAssigneeUserId / NewAssigneeUserId | int NULL，FK Users | 负责人变化 |
| OldPriority / NewPriority | tinyint NULL | 优先级变化 |
| DelegationId | bigint NULL，FK Delegations | 相关代理安排 |
| Comment | nvarchar(max) NULL | 操作说明 |
| CreatedAtUtc | datetimeoffset(7) | 操作时间 |

状态跳转只写 WorkflowHistory，不在这里再写一份同样的流程记录。历史原则上追加保存，不随用户改名而丢失当时显示信息。具体保留期限和管理更正策略待定。

### 4.6 Delegations

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id | bigint IDENTITY，PK | 代理标识 |
| SourceUserId / TargetUserId | int，FK Users | 原负责人 A / 代理人 B |
| StartsAtUtc / EndsAtUtc | datetimeoffset(7) | 代理有效区间，建议为 [开始, 结束) |
| Reason | nvarchar(1000) NULL | 代理原因；是否必填待确认 |
| CreatedByUserId | int，FK Users | 设置代理的 Admin |
| ActivatedAtUtc / CompletedAtUtc | datetimeoffset(7) NULL | 后台实际开始、完成处理的时间 |
| CreatedAtUtc / UpdatedAtUtc | datetimeoffset(7) | 记录时间 |
| RowVersion | rowversion | 并发标记 |

约束：SourceUserId 不等于 TargetUserId；StartsAtUtc 小于 EndsAtUtc。A / B 必须为可用的 Support IT Staff，设置者必须为 Support Admin / System Admin。

当前是否生效应结合开始、结束时间判断，不能只依赖某个后台任务是否已经更新标记。调度方案、重叠或链式代理、提前结束及人员停用的业务政策尚未决定。

### 4.7 DelegationTickets

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id | bigint IDENTITY，PK | 代理交接记录 |
| DelegationId / TicketId | bigint，FK | 对应代理及工单，组合唯一 |
| EntryReason | nvarchar(32) | ExistingTicket / NewAssignment |
| OriginalAssigneeUserId | int，FK Users | 原本应负责的 A |
| DelegatedToUserId | int，FK Users | 接手的 B |
| DelegatedAssignmentVersion | bigint | 本次交给 B 后的 AssignmentVersion |
| DelegatedAtUtc | datetimeoffset(7) | 实际交接时间 |
| ReturnOutcome | nvarchar(32) | AwaitingEnd、Returned、SkippedClosed、SkippedReassigned 等 |
| ProcessedAtUtc | datetimeoffset(7) NULL | 到期处理完成时间 |

ReturnOutcome 是代理记录的技术处理结果，不是 Ticket 状态；枚举细节在实现时确定。不能按 B 当前所有 Ticket 批量转回，必须通过本表识别本次代理范围。

## 5. Workflow Template 表

四张表参考 Project1 的职责，但字段及规则按 Helpdesk 设计，不直接复制业务代码。

| 表 | 建议主要字段 |
| --- | --- |
| WorkflowProcessTemplate | Id、Code、Name、EntityType（Ticket）、Version、IsPublished、IsActive、CreatedByUserId、CreatedAtUtc、PublishedAtUtc、RowVersion |
| WorkflowStepTemplate | Id、ProcessTemplateId、Code、Name、DisplayOrder、IsInitial、IsTerminal |
| WorkflowActionTemplate | Id、FromStepTemplateId、ToStepTemplateId、Code、Name、RequiresComment |
| WorkflowActionerTemplate | Id、ActionTemplateId、ActionerType、ActionerRole、ActionerUserId |

通用类型：Id 与指向这些表的 FK 使用 bigint；Code 建议 nvarchar(64)，Name nvarchar(200)，EntityType nvarchar(64)，Version / DisplayOrder 为 int，标志为 bit，时间为 datetimeoffset(7)。ActionerRole 使用与 Users.Role 相同的代码，ActionerUserId 为指向 Users 的 int FK。

ActionerType 建议支持 Requester、CurrentAssignee、Role、User：

| 类型 | 解析方式 |
| --- | --- |
| Requester | 根据该 Ticket 的 RequestedByUserId 判断 |
| CurrentAssignee | 动态比较 Ticket.AssignedToUserId，同时验证 Staff 身份 |
| Role | 使用 ActionerRole 与登录用户唯一角色比较 |
| User | 使用 ActionerUserId 与登录用户 Id 比较 |

Requester / CurrentAssignee 不填写固定 User 或 Role；Role 仅填写 Role；User 仅填写 User。建议用 CHECK 约束避免互相矛盾的配置。User、Role 是结构预留，不意味着配置后可绕过 Ticket 的基础权限。

模板建议约束：

- (Code, Version) 唯一；同一 EntityType 同时最多一个已发布且启用的版本。
- Step 的 (ProcessTemplateId, Code) 唯一；Action 的 (FromStepTemplateId, Code) 唯一。
- FromStep / ToStep 必须属于同一个 ProcessTemplate。
- 第一版预置 OPEN 为初始步骤，CLOSED 为唯一终点；RESOLVED 不能设为终点。
- OPEN → IN_PROGRESS → RESOLVED → CLOSED，另允许 RESOLVED → IN_PROGRESS。
- 所有步骤可从初始步骤到达，且每个非终点都有到 CLOSED 的路径；允许业务需要的循环。
- Assignment / Delegation 不配置成 Open → Open 之类的动作，因为它们属于负责人变更。
- 已发布版本不可直接修改；新建草稿版本，验证后发布，旧版本保留用于历史引用。

System Admin 可编辑范围仍需审核。第一版至少应保护约定的状态语义和权限，不能通过配置把 Employee 变成任意工单处理人。

## 6. Workflow Instance 表

| 表 | 建议主要字段 |
| --- | --- |
| WorkflowProcessInstance | Id、TicketId、ProcessTemplateId、TemplateCode、TemplateName、TemplateVersion、CurrentStepInstanceId、Status、StartedAtUtc、CompletedAtUtc、RowVersion |
| WorkflowStepInstance | Id、ProcessInstanceId、SourceStepTemplateId、Code、Name、DisplayOrder、IsInitial、IsTerminal |
| WorkflowActionInstance | Id、FromStepInstanceId、ToStepInstanceId、SourceActionTemplateId、Code、Name、RequiresComment |
| WorkflowActionerInstance | Id、ActionInstanceId、SourceActionerTemplateId、ActionerType、ActionerRole、ActionerUserId |

快照字段的类型沿用对应 Template。TicketId、SourceTemplate FK 及 Step / Action FK 使用 bigint。ProcessInstance.Status 存 Running / Completed 代码；CurrentStepInstanceId 在对外可见的已建立实例中必须有值。

Requester 在建立实例时可将该 Ticket 的提交人 Id 固定保存到 ActionerUserId，以保留身份；CurrentAssignee 保留为动态类型，不固化成最初分配的 A 或代理人 B。

创建 Process 和其 Steps 存在互相引用，可在同一事务内先插入 CurrentStepInstanceId 为空的 Process，再插入步骤并设置当前步骤，全部成功后提交；中间状态不得对外发布。数据库字段可空是为解决创建顺序，不代表业务允许没有当前步骤的运行实例。

唯一与关联约束：

- WorkflowProcessInstance.TicketId 唯一。
- (ProcessInstanceId, Code) 在 StepInstance 内唯一。
- (FromStepInstanceId, Code) 在 ActionInstance 内唯一。
- CurrentStepInstance 必须属于该 ProcessInstance。
- Action 的两个 Step 必须属于同一个 ProcessInstance。
- SourceTemplate 关联用于追溯，运行时使用 Instance 快照，不读取新模板替换旧步骤。

同流程关联应通过服务验证及适当的复合 FK 保护。删除已引用的模板、用户、Ticket 或流程记录不能造成历史级联丢失。

## 7. WorkflowHistory

| 字段 | 建议类型 | 用途 |
| --- | --- | --- |
| Id | bigint IDENTITY，PK | 历史标识 |
| ProcessInstanceId | bigint，FK | 所属工单流程 |
| ActionInstanceId | bigint NULL，FK | START 初始化记录可为空 |
| FromStepCode / ToStepCode | nvarchar(64)，From 可空 | 前后步骤快照 |
| ActionCode | nvarchar(64) | START、START_PROCESSING、RESOLVE、CLOSE、REOPEN |
| ActionByUserId | int，FK Users | 真实操作人 |
| ActionByNameSnapshot | nvarchar(200) | 当时显示姓名 |
| Comment | nvarchar(max) NULL | 操作说明 |
| ActionAtUtc | datetimeoffset(7) | 操作时间 |

ActionInstance 是该 Ticket 的可用动作副本，WorkflowHistory 才是每次执行记录。重复 Resolve / Reopen 可引用同一组 ActionInstance，但每次产生独立 History。

## 8. 事务与代理处理

### 8.1 建立 Ticket

一个事务中：验证 Employee → 建立 Ticket（Medium、未分配）→ 查找已发布启用模板 → 复制流程实例及子记录 → 设置 OPEN → 记录 START → 提交。缺少有效模板时整体回滚，不能留下无流程 Ticket。

### 8.2 执行流程动作

后端验证当前登录身份、Ticket 可见性、当前步骤、动作授权及必要说明；更新 CurrentStep 和运行状态，追加 WorkflowHistory，在同一事务提交。并发请求必须重新验证最新步骤，避免重复 Close 或用过期状态 Resolve。

### 8.3 分配与生效交接

- 分配给 A 前，按当前时间检查 A 是否有生效的 Delegation；存在时实际负责人写 B，同时建立 DelegationTickets。
- 代理开始时，为 A 的 Open / In Progress Ticket 记录交接，改负责人为 B，并递增 AssignmentVersion。
- Ticket 更新、DelegationTickets 和 TicketHistories 在同一事务提交。
- 开始任务与新分配并发时，仍须保证有效期间分给 A 的 Ticket 按已确认规则交给 B；实现需协调分配和代理记录的并发读取，不能只各跑一次批处理。

### 8.4 到期转回

对本次 DelegationTickets 中尚未完成到期处理的记录逐一检查：

1. 已 Closed：标记 SkippedClosed，保留当前负责人。
2. 当前负责人已不是 B，或 AssignmentVersion 表明存在后续安排：标记 SkippedReassigned，保留人工安排。
3. 当前为 Open / In Progress / Resolved，且仍是本次代理分配：转回 A，保持原步骤，递增 AssignmentVersion，记录历史并标记 Returned。

转回及处理结果在同一事务提交，支持任务重试而不重复转回。B 原有的工单没有本次 DelegationTickets 记录，因此不受影响。Source / Target 被停用或出现重叠代理时的策略待确认。

## 9. 索引与删除策略建议

- Users：NormalizedEmail 唯一。
- Tickets：(RequestedByUserId, CreatedAtUtc)、(AssignedToUserId, CreatedAtUtc)；未分配查询可按实际 SQL 增加筛选索引。
- TicketMessages、Attachments、TicketHistories：按 TicketId 建索引；消息及历史追加时间和 Id，支持稳定排序。
- Delegations：按 SourceUserId、StartsAtUtc、EndsAtUtc 查询有效安排。
- DelegationTickets：(DelegationId, TicketId) 唯一，另支持按 DelegationId、ReturnOutcome 查找到期待处理项。
- WorkflowProcessTemplate：(Code, Version) 唯一，并用筛选唯一索引限制每种 EntityType 的启用发布版本。
- WorkflowProcessInstance：TicketId 唯一；CurrentStepInstanceId、模板 FK 按查询和约束需要建索引。
- WorkflowHistory：(ProcessInstanceId, ActionAtUtc, Id)。

SQL Server 的多重级联删除路径需在 EF 配置时避免。建议以 NoAction / Restrict 保护历史引用，第一版账号用停用代替物理删除。允许删除哪些草稿及附件的完整政策仍需另定。

## 10. 审核与验证清单

- 16 张核心表的关系和名称与需求一致。
- 每用户单角色，Staff 的访问依据当前负责人，而不是通用 Staff 角色或历史参与关系。
- 只有四个业务状态，Resolved 仍 Running，Closed 才 Completed。
- 模板支持 Reopen 循环，版本副本隔离，配置不能绕过工单业务权限。
- 代理开始不包含 Resolved，到期转回包含 Resolved；Closed 不转回。
- 人工重新分配不会被到期任务覆盖；重复任务不生成重复交接或转回记录。
- Public / Internal 消息及其附件的权限一致。
- 当前步骤、动作、模板引用属于正确流程；业务修改与历史保存原子提交。

## 11. Migration 管理与启动更新

参考公司项目的 EF Core 启动迁移流程，并结合 Project1 的开发生成、模型检查、开关和目标数据库校验。EF Core Migration 与 Snapshot 存放在 Infrastructure，Api 的 Program.cs 在服务启动前调用迁移服务。公司项目的数据库 Provider 及部署配置不直接沿用，Helpdesk 使用 SQL Server。

- 修改 Entity 后需显式生成、审核并提交 Migration，不依赖 API 启动自动生成文件。
- `migrations has-pending-model-changes` 用于检查模型变化是否缺少对应文件；设计时和 CI 检查关闭启动迁移。
- 启动迁移默认关闭，显式开启后检查 ExpectedDatabase、查询待执行 Migration，有待执行项才调用 MigrateAsync。
- 查询或执行失败时不继续初始化用户、模板、后台 Delegation 任务或 HTTP 服务。
- 已应用的版本由 EF 的迁移历史跟踪；部署携带已有 Migration，不在服务器重新生成源码。
- 首次创建开发数据库、已有数据库的升级，以及多实例发布时的权限与协调方式，在实现和部署阶段分别验证。

完整流程及验收场景见[实施计划第 15 节](implementation-plan.md#15-ef-core-migration-与启动更新)。本次只更新设计文档，未连接 SQL Server，也未创建或执行数据库 Migration。
