using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentObservationAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AttemptId",
                table: "Observations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ObservationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeploymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObservationAttempts_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_AttemptId",
                table: "Observations",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservationAttempts_DeploymentId_CompletedAtUnixMilliseconds",
                table: "ObservationAttempts",
                columns: new[] { "DeploymentId", "CompletedAtUnixMilliseconds" });

            migrationBuilder.AddForeignKey(
                name: "FK_Observations_ObservationAttempts_AttemptId",
                table: "Observations",
                column: "AttemptId",
                principalTable: "ObservationAttempts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Observations_ObservationAttempts_AttemptId",
                table: "Observations");

            migrationBuilder.DropTable(
                name: "ObservationAttempts");

            migrationBuilder.DropIndex(
                name: "IX_Observations_AttemptId",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "AttemptId",
                table: "Observations");
        }
    }
}
