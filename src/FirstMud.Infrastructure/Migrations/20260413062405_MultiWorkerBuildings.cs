using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultiWorkerBuildings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new JSON column with a default empty array
            migrationBuilder.AddColumn<string>(
                name: "AssignedCompanionIdsJson",
                table: "HomesteadBuildings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            // Migrate existing single companion ID to the JSON array format
            migrationBuilder.Sql(@"
                UPDATE HomesteadBuildings
                SET AssignedCompanionIdsJson = '[""' + CAST(AssignedCompanionId AS NVARCHAR(36)) + '""]'
                WHERE AssignedCompanionId IS NOT NULL
            ");

            // Drop the old single-companion column
            migrationBuilder.DropColumn(
                name: "AssignedCompanionId",
                table: "HomesteadBuildings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedCompanionId",
                table: "HomesteadBuildings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE HomesteadBuildings
                SET AssignedCompanionId = TRY_CAST(
                    JSON_VALUE(AssignedCompanionIdsJson, '$[0]') AS UNIQUEIDENTIFIER
                )
                WHERE AssignedCompanionIdsJson != '[]' AND AssignedCompanionIdsJson IS NOT NULL
            ");

            migrationBuilder.DropColumn(
                name: "AssignedCompanionIdsJson",
                table: "HomesteadBuildings");
        }
    }
}
