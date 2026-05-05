using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Firestarter.Core.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Extensions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ScriptPath = table.Column<string>(type: "TEXT", nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Extensions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GitlabSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PatCredentialName = table.Column<string>(type: "TEXT", nullable: true),
                    SyncIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentUsername = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitlabSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdeRegistrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutablePath = table.Column<string>(type: "TEXT", nullable: false),
                    ArgTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdeRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeyValueSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyValueSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GitlabId = table.Column<long>(type: "INTEGER", nullable: false),
                    PathWithNamespace = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultBranch = table.Column<string>(type: "TEXT", nullable: true),
                    WebUrl = table.Column<string>(type: "TEXT", nullable: false),
                    SshUrlToRepo = table.Column<string>(type: "TEXT", nullable: true),
                    HttpUrlToRepo = table.Column<string>(type: "TEXT", nullable: true),
                    LastActivityAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastVisitedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastSyncedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastGitlabUpdatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Etag = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Sha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branches_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExtensionRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExtensionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    FinishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    WorkingDirectory = table.Column<string>(type: "TEXT", nullable: true),
                    StdoutPath = table.Column<string>(type: "TEXT", nullable: true),
                    StderrPath = table.Column<string>(type: "TEXT", nullable: true),
                    StatsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtensionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtensionRuns_Extensions_ExtensionId",
                        column: x => x.ExtensionId,
                        principalTable: "Extensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExtensionRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MergeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GitlabIid = table.Column<long>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceBranch = table.Column<string>(type: "TEXT", nullable: false),
                    TargetBranch = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorUsername = table.Column<string>(type: "TEXT", nullable: true),
                    AssigneeUsernames = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewerUsernames = table.Column<string>(type: "TEXT", nullable: true),
                    WebUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Draft = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MergeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MergeRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_ProjectId_Name",
                table: "Branches",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtensionRuns_ExtensionId",
                table: "ExtensionRuns",
                column: "ExtensionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtensionRuns_ProjectId",
                table: "ExtensionRuns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtensionRuns_StartedAt",
                table: "ExtensionRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Extensions_Name",
                table: "Extensions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdeRegistrations_Name",
                table: "IdeRegistrations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MergeRequests_ProjectId_GitlabIid",
                table: "MergeRequests",
                columns: new[] { "ProjectId", "GitlabIid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MergeRequests_State",
                table: "MergeRequests",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_GitlabId",
                table: "Projects",
                column: "GitlabId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PathWithNamespace",
                table: "Projects",
                column: "PathWithNamespace");

            migrationBuilder.CreateIndex(
                name: "IX_SyncCursors_Entity_Scope",
                table: "SyncCursors",
                columns: new[] { "Entity", "Scope" },
                unique: true);

            migrationBuilder.Sql(@"
CREATE VIRTUAL TABLE ProjectFts USING fts5(
    PathWithNamespace,
    Name,
    Description,
    content='Projects',
    content_rowid='Id',
    tokenize='trigram'
);
");

            migrationBuilder.Sql(@"
CREATE TRIGGER Projects_ai AFTER INSERT ON Projects BEGIN
    INSERT INTO ProjectFts(rowid, PathWithNamespace, Name, Description)
    VALUES (new.Id, new.PathWithNamespace, new.Name, COALESCE(new.Description, ''));
END;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER Projects_ad AFTER DELETE ON Projects BEGIN
    INSERT INTO ProjectFts(ProjectFts, rowid, PathWithNamespace, Name, Description)
    VALUES ('delete', old.Id, old.PathWithNamespace, old.Name, COALESCE(old.Description, ''));
END;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER Projects_au AFTER UPDATE ON Projects BEGIN
    INSERT INTO ProjectFts(ProjectFts, rowid, PathWithNamespace, Name, Description)
    VALUES ('delete', old.Id, old.PathWithNamespace, old.Name, COALESCE(old.Description, ''));
    INSERT INTO ProjectFts(rowid, PathWithNamespace, Name, Description)
    VALUES (new.Id, new.PathWithNamespace, new.Name, COALESCE(new.Description, ''));
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Projects_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Projects_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Projects_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS ProjectFts;");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "ExtensionRuns");

            migrationBuilder.DropTable(
                name: "GitlabSettings");

            migrationBuilder.DropTable(
                name: "IdeRegistrations");

            migrationBuilder.DropTable(
                name: "KeyValueSettings");

            migrationBuilder.DropTable(
                name: "MergeRequests");

            migrationBuilder.DropTable(
                name: "SyncCursors");

            migrationBuilder.DropTable(
                name: "Extensions");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
