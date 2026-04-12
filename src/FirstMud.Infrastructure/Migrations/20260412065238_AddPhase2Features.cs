using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxInventorySlots",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "SavedReturnPosX",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedReturnPosY",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedReturnWorldId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedReturnZoneId",
                table: "Players",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Homesteads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StorageSlots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Homesteads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HomesteadStorageItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HomesteadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomesteadStorageItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    RemainingYield = table.Column<int>(type: "int", nullable: false),
                    MaxYield = table.Column<int>(type: "int", nullable: false),
                    RegenerationRate = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceNodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Homesteads_PlayerId",
                table: "Homesteads",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HomesteadStorageItems_HomesteadId",
                table: "HomesteadStorageItems",
                column: "HomesteadId");

            migrationBuilder.CreateIndex(
                name: "IX_HomesteadStorageItems_ItemId",
                table: "HomesteadStorageItems",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceNodes_ZoneId",
                table: "ResourceNodes",
                column: "ZoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Homesteads");

            migrationBuilder.DropTable(
                name: "HomesteadStorageItems");

            migrationBuilder.DropTable(
                name: "ResourceNodes");

            migrationBuilder.DropColumn(
                name: "MaxInventorySlots",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SavedReturnPosX",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SavedReturnPosY",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SavedReturnWorldId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SavedReturnZoneId",
                table: "Players");
        }
    }
}
