using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                    table.CheckConstraint("CK_Roles_Name", "[Name] IN ('EMPLOYEE','SUPPORT_IT_STAFF','SUPPORT_ADMIN','SYSTEM_ADMIN')");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Delegations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceUserId = table.Column<int>(type: "int", nullable: false),
                    TargetUserId = table.Column<int>(type: "int", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                    table.CheckConstraint("CK_Delegations_DifferentUsers", "[SourceUserId] <> [TargetUserId]");
                    table.CheckConstraint("CK_Delegations_Period", "[StartsAtUtc] < [EndsAtUtc]");
                    table.ForeignKey(
                        name: "FK_Delegations_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Delegations_AspNetUsers_SourceUserId",
                        column: x => x.SourceUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Delegations_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedToUserId = table.Column<int>(type: "int", nullable: true),
                    AssignmentVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.CheckConstraint("CK_Tickets_AssignmentVersion", "[AssignmentVersion] >= 0");
                    table.CheckConstraint("CK_Tickets_Category", "[Category] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Tickets_Priority", "[Priority] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_Tickets_AspNetUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tickets_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowProcessTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowProcessTemplates", x => x.Id);
                    table.CheckConstraint("CK_WorkflowTemplates_Active", "[IsActive] = 0 OR [IsPublished] = 1");
                    table.CheckConstraint("CK_WorkflowTemplates_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_WorkflowProcessTemplates_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DelegationTickets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegationId = table.Column<long>(type: "bigint", nullable: false),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    EntryReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OriginalAssigneeUserId = table.Column<int>(type: "int", nullable: false),
                    DelegatedToUserId = table.Column<int>(type: "int", nullable: false),
                    DelegatedAssignmentVersion = table.Column<long>(type: "bigint", nullable: false),
                    DelegatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ReturnOutcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationTickets", x => x.Id);
                    table.CheckConstraint("CK_DelegationTickets_Outcome", "[ReturnOutcome] IN ('AwaitingEnd','Returned','SkippedClosed','SkippedReassigned')");
                    table.CheckConstraint("CK_DelegationTickets_Reason", "[EntryReason] IN ('ExistingTicket','NewAssignment')");
                    table.CheckConstraint("CK_DelegationTickets_Version", "[DelegatedAssignmentVersion] >= 0");
                    table.ForeignKey(
                        name: "FK_DelegationTickets_AspNetUsers_DelegatedToUserId",
                        column: x => x.DelegatedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DelegationTickets_AspNetUsers_OriginalAssigneeUserId",
                        column: x => x.OriginalAssigneeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DelegationTickets_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "Delegations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DelegationTickets_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TicketHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OldAssigneeUserId = table.Column<int>(type: "int", nullable: true),
                    NewAssigneeUserId = table.Column<int>(type: "int", nullable: true),
                    OldPriority = table.Column<byte>(type: "tinyint", nullable: true),
                    NewPriority = table.Column<byte>(type: "tinyint", nullable: true),
                    DelegationId = table.Column<long>(type: "bigint", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketHistories", x => x.Id);
                    table.CheckConstraint("CK_TicketHistories_Event", "[EventType] IN ('Assigned','Reassigned','PriorityChanged','Delegated','DelegationReturned')");
                    table.CheckConstraint("CK_TicketHistories_NewPriority", "[NewPriority] IS NULL OR [NewPriority] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_TicketHistories_OldPriority", "[OldPriority] IS NULL OR [OldPriority] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_TicketHistories_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TicketHistories_AspNetUsers_NewAssigneeUserId",
                        column: x => x.NewAssigneeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TicketHistories_AspNetUsers_OldAssigneeUserId",
                        column: x => x.OldAssigneeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TicketHistories_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "Delegations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TicketHistories_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TicketMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorUserId = table.Column<int>(type: "int", nullable: false),
                    MessageType = table.Column<byte>(type: "tinyint", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketMessages", x => x.Id);
                    table.UniqueConstraint("AK_TicketMessages_TicketId_Id", x => new { x.TicketId, x.Id });
                    table.CheckConstraint("CK_Messages_Type", "[MessageType] IN (1,2)");
                    table.ForeignKey(
                        name: "FK_TicketMessages_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TicketMessages_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStepTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false),
                    IsTerminal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStepTemplates", x => x.Id);
                    table.UniqueConstraint("AK_WorkflowStepTemplates_ProcessTemplateId_Id", x => new { x.ProcessTemplateId, x.Id });
                    table.CheckConstraint("CK_WorkflowSteps_Template", "[Code] IN ('OPEN','IN_PROGRESS','RESOLVED','CLOSED') AND (([Code] = 'OPEN' AND [IsInitial] = 1) OR ([Code] <> 'OPEN' AND [IsInitial] = 0)) AND (([Code] = 'CLOSED' AND [IsTerminal] = 1) OR ([Code] <> 'CLOSED' AND [IsTerminal] = 0))");
                    table.ForeignKey(
                        name: "FK_WorkflowStepTemplates_WorkflowProcessTemplates_ProcessTemplateId",
                        column: x => x.ProcessTemplateId,
                        principalTable: "WorkflowProcessTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    TicketMessageId = table.Column<long>(type: "bigint", nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.CheckConstraint("CK_Attachments_Size", "[SizeBytes] >= 0");
                    table.ForeignKey(
                        name: "FK_Attachments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Attachments_TicketMessages_TicketId_TicketMessageId",
                        columns: x => new { x.TicketId, x.TicketMessageId },
                        principalTable: "TicketMessages",
                        principalColumns: new[] { "TicketId", "Id" });
                    table.ForeignKey(
                        name: "FK_Attachments_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    FromStepTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ToStepTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionTemplates", x => x.Id);
                    table.CheckConstraint("CK_WorkflowActions_Template", "[FromStepTemplateId] <> [ToStepTemplateId]");
                    table.ForeignKey(
                        name: "FK_WorkflowActionTemplates_WorkflowStepTemplates_ProcessTemplateId_FromStepTemplateId",
                        columns: x => new { x.ProcessTemplateId, x.FromStepTemplateId },
                        principalTable: "WorkflowStepTemplates",
                        principalColumns: new[] { "ProcessTemplateId", "Id" });
                    table.ForeignKey(
                        name: "FK_WorkflowActionTemplates_WorkflowStepTemplates_ProcessTemplateId_ToStepTemplateId",
                        columns: x => new { x.ProcessTemplateId, x.ToStepTemplateId },
                        principalTable: "WorkflowStepTemplates",
                        principalColumns: new[] { "ProcessTemplateId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionerTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ActionerType = table.Column<byte>(type: "tinyint", nullable: false),
                    ActionerRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActionerUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionerTemplates", x => x.Id);
                    table.CheckConstraint("CK_Actioners_Template", "([ActionerType] = 1 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NULL) OR ([ActionerType] = 2 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NULL) OR ([ActionerType] = 3 AND [ActionerRole] IS NOT NULL AND [ActionerRole] IN ('EMPLOYEE','SUPPORT_IT_STAFF','SUPPORT_ADMIN','SYSTEM_ADMIN') AND [ActionerUserId] IS NULL) OR ([ActionerType] = 4 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WorkflowActionerTemplates_AspNetUsers_ActionerUserId",
                        column: x => x.ActionerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowActionerTemplates_WorkflowActionTemplates_ActionTemplateId",
                        column: x => x.ActionTemplateId,
                        principalTable: "WorkflowActionTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionerInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    SourceActionerTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    ActionerType = table.Column<byte>(type: "tinyint", nullable: false),
                    ActionerRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActionerUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionerInstances", x => x.Id);
                    table.CheckConstraint("CK_Actioners_Instance", "([ActionerType] = 1 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NOT NULL) OR ([ActionerType] = 2 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NULL) OR ([ActionerType] = 3 AND [ActionerRole] IS NOT NULL AND [ActionerRole] IN ('EMPLOYEE','SUPPORT_IT_STAFF','SUPPORT_ADMIN','SYSTEM_ADMIN') AND [ActionerUserId] IS NULL) OR ([ActionerType] = 4 AND [ActionerRole] IS NULL AND [ActionerUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WorkflowActionerInstances_AspNetUsers_ActionerUserId",
                        column: x => x.ActionerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowActionerInstances_WorkflowActionerTemplates_SourceActionerTemplateId",
                        column: x => x.SourceActionerTemplateId,
                        principalTable: "WorkflowActionerTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActionInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    FromStepInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ToStepInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    SourceActionTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActionInstances", x => x.Id);
                    table.UniqueConstraint("AK_WorkflowActionInstances_ProcessInstanceId_Id", x => new { x.ProcessInstanceId, x.Id });
                    table.CheckConstraint("CK_WorkflowActions_Instance", "[FromStepInstanceId] <> [ToStepInstanceId]");
                    table.ForeignKey(
                        name: "FK_WorkflowActionInstances_WorkflowActionTemplates_SourceActionTemplateId",
                        column: x => x.SourceActionTemplateId,
                        principalTable: "WorkflowActionTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ActionInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    FromStepCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ToStepCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActionByUserId = table.Column<int>(type: "int", nullable: false),
                    ActionByNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowHistory_AspNetUsers_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowHistory_WorkflowActionInstances_ProcessInstanceId_ActionInstanceId",
                        columns: x => new { x.ProcessInstanceId, x.ActionInstanceId },
                        principalTable: "WorkflowActionInstances",
                        principalColumns: new[] { "ProcessInstanceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "WorkflowProcessInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    ProcessTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    CurrentStepInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowProcessInstances", x => x.Id);
                    table.CheckConstraint("CK_WorkflowInstances_Status", "([Status] = 1 AND [CompletedAtUtc] IS NULL) OR ([Status] = 2 AND [CompletedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WorkflowProcessInstances_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowProcessInstances_WorkflowProcessTemplates_ProcessTemplateId",
                        column: x => x.ProcessTemplateId,
                        principalTable: "WorkflowProcessTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStepInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    SourceStepTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false),
                    IsTerminal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStepInstances", x => x.Id);
                    table.UniqueConstraint("AK_WorkflowStepInstances_ProcessInstanceId_Id", x => new { x.ProcessInstanceId, x.Id });
                    table.CheckConstraint("CK_WorkflowSteps_Instance", "[Code] IN ('OPEN','IN_PROGRESS','RESOLVED','CLOSED') AND (([Code] = 'OPEN' AND [IsInitial] = 1) OR ([Code] <> 'OPEN' AND [IsInitial] = 0)) AND (([Code] = 'CLOSED' AND [IsTerminal] = 1) OR ([Code] <> 'CLOSED' AND [IsTerminal] = 0))");
                    table.ForeignKey(
                        name: "FK_WorkflowStepInstances_WorkflowProcessInstances_ProcessInstanceId",
                        column: x => x.ProcessInstanceId,
                        principalTable: "WorkflowProcessInstances",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowStepInstances_WorkflowStepTemplates_SourceStepTemplateId",
                        column: x => x.SourceStepTemplateId,
                        principalTable: "WorkflowStepTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 1, "helpdesk-role-EMPLOYEE", "EMPLOYEE", "EMPLOYEE" },
                    { 2, "helpdesk-role-SUPPORT_IT_STAFF", "SUPPORT_IT_STAFF", "SUPPORT_IT_STAFF" },
                    { 3, "helpdesk-role-SUPPORT_ADMIN", "SUPPORT_ADMIN", "SUPPORT_ADMIN" },
                    { 4, "helpdesk-role-SYSTEM_ADMIN", "SYSTEM_ADMIN", "SYSTEM_ADMIN" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_UserId",
                table: "AspNetUserRoles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_StorageKey",
                table: "Attachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_TicketId_TicketMessageId",
                table: "Attachments",
                columns: new[] { "TicketId", "TicketMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UploadedByUserId",
                table: "Attachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_CreatedByUserId",
                table: "Delegations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_SourceUserId_StartsAtUtc_EndsAtUtc",
                table: "Delegations",
                columns: new[] { "SourceUserId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_TargetUserId",
                table: "Delegations",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTickets_DelegatedToUserId",
                table: "DelegationTickets",
                column: "DelegatedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTickets_DelegationId_ReturnOutcome",
                table: "DelegationTickets",
                columns: new[] { "DelegationId", "ReturnOutcome" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTickets_DelegationId_TicketId",
                table: "DelegationTickets",
                columns: new[] { "DelegationId", "TicketId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTickets_OriginalAssigneeUserId",
                table: "DelegationTickets",
                column: "OriginalAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTickets_TicketId",
                table: "DelegationTickets",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketHistories_ActorUserId",
                table: "TicketHistories",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketHistories_DelegationId",
                table: "TicketHistories",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketHistories_NewAssigneeUserId",
                table: "TicketHistories",
                column: "NewAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketHistories_OldAssigneeUserId",
                table: "TicketHistories",
                column: "OldAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketHistories_TicketId_CreatedAtUtc_Id",
                table: "TicketHistories",
                columns: new[] { "TicketId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_AuthorUserId",
                table: "TicketMessages",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId_CreatedAtUtc_Id",
                table: "TicketMessages",
                columns: new[] { "TicketId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedToUserId_CreatedAtUtc",
                table: "Tickets",
                columns: new[] { "AssignedToUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_RequestedByUserId_CreatedAtUtc",
                table: "Tickets",
                columns: new[] { "RequestedByUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TicketNumber",
                table: "Tickets",
                column: "TicketNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionerInstances_ActionerUserId",
                table: "WorkflowActionerInstances",
                column: "ActionerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionerInstances_ActionInstanceId",
                table: "WorkflowActionerInstances",
                column: "ActionInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionerInstances_SourceActionerTemplateId",
                table: "WorkflowActionerInstances",
                column: "SourceActionerTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionerTemplates_ActionerUserId",
                table: "WorkflowActionerTemplates",
                column: "ActionerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionerTemplates_ActionTemplateId",
                table: "WorkflowActionerTemplates",
                column: "ActionTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionInstances_FromStepInstanceId_Code",
                table: "WorkflowActionInstances",
                columns: new[] { "FromStepInstanceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionInstances_ProcessInstanceId_FromStepInstanceId",
                table: "WorkflowActionInstances",
                columns: new[] { "ProcessInstanceId", "FromStepInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionInstances_ProcessInstanceId_ToStepInstanceId",
                table: "WorkflowActionInstances",
                columns: new[] { "ProcessInstanceId", "ToStepInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionInstances_SourceActionTemplateId",
                table: "WorkflowActionInstances",
                column: "SourceActionTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionTemplates_FromStepTemplateId_Code",
                table: "WorkflowActionTemplates",
                columns: new[] { "FromStepTemplateId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionTemplates_ProcessTemplateId_FromStepTemplateId",
                table: "WorkflowActionTemplates",
                columns: new[] { "ProcessTemplateId", "FromStepTemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActionTemplates_ProcessTemplateId_ToStepTemplateId",
                table: "WorkflowActionTemplates",
                columns: new[] { "ProcessTemplateId", "ToStepTemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistory_ActionByUserId",
                table: "WorkflowHistory",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistory_ProcessInstanceId_ActionAtUtc_Id",
                table: "WorkflowHistory",
                columns: new[] { "ProcessInstanceId", "ActionAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistory_ProcessInstanceId_ActionInstanceId",
                table: "WorkflowHistory",
                columns: new[] { "ProcessInstanceId", "ActionInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_Id_CurrentStepInstanceId",
                table: "WorkflowProcessInstances",
                columns: new[] { "Id", "CurrentStepInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_ProcessTemplateId",
                table: "WorkflowProcessInstances",
                column: "ProcessTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_TicketId",
                table: "WorkflowProcessInstances",
                column: "TicketId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessTemplates_Code_Version",
                table: "WorkflowProcessTemplates",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessTemplates_CreatedByUserId",
                table: "WorkflowProcessTemplates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessTemplates_EntityType",
                table: "WorkflowProcessTemplates",
                column: "EntityType",
                unique: true,
                filter: "[IsPublished] = 1 AND [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepInstances_ProcessInstanceId",
                table: "WorkflowStepInstances",
                column: "ProcessInstanceId",
                unique: true,
                filter: "[IsInitial] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepInstances_ProcessInstanceId_Code",
                table: "WorkflowStepInstances",
                columns: new[] { "ProcessInstanceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepInstances_SourceStepTemplateId",
                table: "WorkflowStepInstances",
                column: "SourceStepTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepTemplates_ProcessTemplateId",
                table: "WorkflowStepTemplates",
                column: "ProcessTemplateId",
                unique: true,
                filter: "[IsInitial] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStepTemplates_ProcessTemplateId_Code",
                table: "WorkflowStepTemplates",
                columns: new[] { "ProcessTemplateId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowActionerInstances_WorkflowActionInstances_ActionInstanceId",
                table: "WorkflowActionerInstances",
                column: "ActionInstanceId",
                principalTable: "WorkflowActionInstances",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowActionInstances_WorkflowStepInstances_ProcessInstanceId_FromStepInstanceId",
                table: "WorkflowActionInstances",
                columns: new[] { "ProcessInstanceId", "FromStepInstanceId" },
                principalTable: "WorkflowStepInstances",
                principalColumns: new[] { "ProcessInstanceId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowActionInstances_WorkflowStepInstances_ProcessInstanceId_ToStepInstanceId",
                table: "WorkflowActionInstances",
                columns: new[] { "ProcessInstanceId", "ToStepInstanceId" },
                principalTable: "WorkflowStepInstances",
                principalColumns: new[] { "ProcessInstanceId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowHistory_WorkflowProcessInstances_ProcessInstanceId",
                table: "WorkflowHistory",
                column: "ProcessInstanceId",
                principalTable: "WorkflowProcessInstances",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowProcessInstances_WorkflowStepInstances_Id_CurrentStepInstanceId",
                table: "WorkflowProcessInstances",
                columns: new[] { "Id", "CurrentStepInstanceId" },
                principalTable: "WorkflowStepInstances",
                principalColumns: new[] { "ProcessInstanceId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_AssignedToUserId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_RequestedByUserId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowProcessTemplates_AspNetUsers_CreatedByUserId",
                table: "WorkflowProcessTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowProcessInstances_Tickets_TicketId",
                table: "WorkflowProcessInstances");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowProcessInstances_WorkflowStepInstances_Id_CurrentStepInstanceId",
                table: "WorkflowProcessInstances");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "DelegationTickets");

            migrationBuilder.DropTable(
                name: "TicketHistories");

            migrationBuilder.DropTable(
                name: "WorkflowActionerInstances");

            migrationBuilder.DropTable(
                name: "WorkflowHistory");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "TicketMessages");

            migrationBuilder.DropTable(
                name: "Delegations");

            migrationBuilder.DropTable(
                name: "WorkflowActionerTemplates");

            migrationBuilder.DropTable(
                name: "WorkflowActionInstances");

            migrationBuilder.DropTable(
                name: "WorkflowActionTemplates");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropTable(
                name: "WorkflowStepInstances");

            migrationBuilder.DropTable(
                name: "WorkflowProcessInstances");

            migrationBuilder.DropTable(
                name: "WorkflowStepTemplates");

            migrationBuilder.DropTable(
                name: "WorkflowProcessTemplates");
        }
    }
}
