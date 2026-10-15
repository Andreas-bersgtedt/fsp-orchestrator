using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestrator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    PrerequisiteRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentPreviewId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorTenantId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActorObjectId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ModifyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DeleteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentApprovals_DeploymentProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "DeploymentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentPreviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PrerequisiteRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValidationCorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    WhatIfCorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ModifyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DeleteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentPreviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentPreviews_DeploymentProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "DeploymentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentWhatIfChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    DeltaJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentWhatIfChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentWhatIfChanges_DeploymentPreviews_PreviewId",
                        column: x => x.PreviewId,
                        principalTable: "DeploymentPreviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentApprovals_ProfileId_ApprovedAt",
                table: "DeploymentApprovals",
                columns: new[] { "ProfileId", "ApprovedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentPreviews_ProfileId_CreatedAt",
                table: "DeploymentPreviews",
                columns: new[] { "ProfileId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentProfiles_UpdatedAt",
                table: "DeploymentProfiles",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentWhatIfChanges_PreviewId_ResourceId",
                table: "DeploymentWhatIfChanges",
                columns: new[] { "PreviewId", "ResourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentApprovals");

            migrationBuilder.DropTable(
                name: "DeploymentWhatIfChanges");

            migrationBuilder.DropTable(
                name: "DeploymentPreviews");

            migrationBuilder.DropTable(
                name: "DeploymentProfiles");
        }
    }
}
