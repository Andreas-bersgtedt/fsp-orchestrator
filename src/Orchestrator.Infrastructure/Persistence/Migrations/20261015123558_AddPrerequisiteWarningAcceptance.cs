using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrerequisiteWarningAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WarningAcceptanceReason",
                table: "PrerequisiteResults",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WarningAcceptedAt",
                table: "PrerequisiteResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarningAcceptedByObjectId",
                table: "PrerequisiteResults",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarningAcceptanceReason",
                table: "PrerequisiteResults");

            migrationBuilder.DropColumn(
                name: "WarningAcceptedAt",
                table: "PrerequisiteResults");

            migrationBuilder.DropColumn(
                name: "WarningAcceptedByObjectId",
                table: "PrerequisiteResults");
        }
    }
}
