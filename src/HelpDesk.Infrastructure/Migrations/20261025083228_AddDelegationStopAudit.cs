using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDesk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegationStopAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StoppedAtUtc",
                table: "Delegations",
                type: "datetimeoffset(7)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoppedByUserId",
                table: "Delegations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_StoppedByUserId",
                table: "Delegations",
                column: "StoppedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Delegations_StopAudit",
                table: "Delegations",
                sql: "([StoppedAtUtc] IS NULL AND [StoppedByUserId] IS NULL) OR ([StoppedAtUtc] IS NOT NULL AND [StoppedByUserId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Delegations_AspNetUsers_StoppedByUserId",
                table: "Delegations",
                column: "StoppedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Delegations_AspNetUsers_StoppedByUserId",
                table: "Delegations");

            migrationBuilder.DropIndex(
                name: "IX_Delegations_StoppedByUserId",
                table: "Delegations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Delegations_StopAudit",
                table: "Delegations");

            migrationBuilder.DropColumn(
                name: "StoppedAtUtc",
                table: "Delegations");

            migrationBuilder.DropColumn(
                name: "StoppedByUserId",
                table: "Delegations");
        }
    }
}
