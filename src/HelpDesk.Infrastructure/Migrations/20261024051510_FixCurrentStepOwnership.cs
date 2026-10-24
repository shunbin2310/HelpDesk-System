using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixCurrentStepOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowProcessInstances_WorkflowStepInstances_Id_CurrentStepInstanceId",
                table: "WorkflowProcessInstances");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowProcessInstances_Id_CurrentStepInstanceId",
                table: "WorkflowProcessInstances");

            migrationBuilder.AddColumn<long>(
                name: "CurrentStepProcessInstanceId",
                table: "WorkflowProcessInstances",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [WorkflowProcessInstances] SET [CurrentStepProcessInstanceId] = [Id] WHERE [CurrentStepInstanceId] IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_CurrentStepProcessInstanceId_CurrentStepInstanceId",
                table: "WorkflowProcessInstances",
                columns: new[] { "CurrentStepProcessInstanceId", "CurrentStepInstanceId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkflowInstances_CurrentStepOwner",
                table: "WorkflowProcessInstances",
                sql: "([CurrentStepInstanceId] IS NULL AND [CurrentStepProcessInstanceId] IS NULL) OR ([CurrentStepInstanceId] IS NOT NULL AND [CurrentStepProcessInstanceId] IS NOT NULL AND [CurrentStepProcessInstanceId] = [Id])");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowProcessInstances_WorkflowStepInstances_CurrentStepProcessInstanceId_CurrentStepInstanceId",
                table: "WorkflowProcessInstances",
                columns: new[] { "CurrentStepProcessInstanceId", "CurrentStepInstanceId" },
                principalTable: "WorkflowStepInstances",
                principalColumns: new[] { "ProcessInstanceId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowProcessInstances_WorkflowStepInstances_CurrentStepProcessInstanceId_CurrentStepInstanceId",
                table: "WorkflowProcessInstances");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowProcessInstances_CurrentStepProcessInstanceId_CurrentStepInstanceId",
                table: "WorkflowProcessInstances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkflowInstances_CurrentStepOwner",
                table: "WorkflowProcessInstances");

            migrationBuilder.DropColumn(
                name: "CurrentStepProcessInstanceId",
                table: "WorkflowProcessInstances");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowProcessInstances_Id_CurrentStepInstanceId",
                table: "WorkflowProcessInstances",
                columns: new[] { "Id", "CurrentStepInstanceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowProcessInstances_WorkflowStepInstances_Id_CurrentStepInstanceId",
                table: "WorkflowProcessInstances",
                columns: new[] { "Id", "CurrentStepInstanceId" },
                principalTable: "WorkflowStepInstances",
                principalColumns: new[] { "ProcessInstanceId", "Id" });
        }
    }
}
