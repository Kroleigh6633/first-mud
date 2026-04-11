using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddZoneRecipeBaseAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BaseAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetType = table.Column<int>(type: "int", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    IsOperational = table.Column<bool>(type: "bit", nullable: false),
                    LastUpkeepAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextUpkeepDue = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpkeepCostAmount = table.Column<int>(type: "int", nullable: false),
                    UpkeepCostItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaseAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IngredientsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultCategory = table.Column<int>(type: "int", nullable: false),
                    ResultItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseWorkmanshipMin = table.Column<int>(type: "int", nullable: false),
                    BaseWorkmanshipMax = table.Column<int>(type: "int", nullable: false),
                    RequiredTaperType = table.Column<int>(type: "int", nullable: true),
                    RequiredWorld = table.Column<int>(type: "int", nullable: false),
                    RequiredCraftingSkill = table.Column<int>(type: "int", nullable: false),
                    IsDiscoverable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldId = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AsciiSymbol = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    DangerLevel = table.Column<int>(type: "int", nullable: false),
                    IsPortalZone = table.Column<bool>(type: "bit", nullable: false),
                    PortalDestination = table.Column<int>(type: "int", nullable: true),
                    LootTableIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EncounterTableIds = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BaseAssets_OwnerId_AssetType",
                table: "BaseAssets",
                columns: new[] { "OwnerId", "AssetType" });

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_RecipeId",
                table: "Recipes",
                column: "RecipeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zones_WorldId_ZoneId",
                table: "Zones",
                columns: new[] { "WorldId", "ZoneId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BaseAssets");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "Zones");
        }
    }
}
