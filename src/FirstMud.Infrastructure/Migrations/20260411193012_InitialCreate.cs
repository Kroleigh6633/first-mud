using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Element = table.Column<int>(type: "int", nullable: false),
                    CurrentLayer = table.Column<int>(type: "int", nullable: false),
                    UsageCounter = table.Column<int>(type: "int", nullable: false),
                    DriftAccumulator = table.Column<float>(type: "real", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPermanentlyGone = table.Column<bool>(type: "bit", nullable: false),
                    EvolutionTier = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    EvolutionBranch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RelationshipDepth = table.Column<int>(type: "int", nullable: false),
                    IgnoredWarnings = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    WorkmanshipValue = table.Column<int>(type: "int", nullable: false),
                    MagicalElement = table.Column<int>(type: "int", nullable: true),
                    MagicalPolarity = table.Column<int>(type: "int", nullable: true),
                    AppliedTaper = table.Column<int>(type: "int", nullable: true),
                    TaperQuality = table.Column<int>(type: "int", nullable: true),
                    IsWyrdTouched = table.Column<bool>(type: "bit", nullable: false),
                    IsArdweldOrigin = table.Column<bool>(type: "bit", nullable: false),
                    Durability = table.Column<int>(type: "int", nullable: false),
                    MaxDurability = table.Column<int>(type: "int", nullable: false),
                    IsSalvageable = table.Column<bool>(type: "bit", nullable: false),
                    OriginWorld = table.Column<int>(type: "int", nullable: false),
                    DiscoveredByPlayerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Experience = table.Column<int>(type: "int", nullable: false),
                    PrimaryElement = table.Column<int>(type: "int", nullable: false),
                    Polarity = table.Column<int>(type: "int", nullable: false),
                    CurrentWeave = table.Column<int>(type: "int", nullable: false),
                    MaxWeave = table.Column<int>(type: "int", nullable: false),
                    ElementRevealed = table.Column<bool>(type: "bit", nullable: false),
                    PolarityRevealed = table.Column<bool>(type: "bit", nullable: false),
                    WyrdTangle = table.Column<int>(type: "int", nullable: false),
                    HasWyrdThread = table.Column<bool>(type: "bit", nullable: false),
                    Strength = table.Column<int>(type: "int", nullable: false),
                    Agility = table.Column<int>(type: "int", nullable: false),
                    Intellect = table.Column<int>(type: "int", nullable: false),
                    Fortitude = table.Column<int>(type: "int", nullable: false),
                    Speed = table.Column<int>(type: "int", nullable: false),
                    CraftingSkill = table.Column<int>(type: "int", nullable: false),
                    SalvageSkill = table.Column<int>(type: "int", nullable: false),
                    WorldId = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: false),
                    PosX = table.Column<int>(type: "int", nullable: false),
                    PosY = table.Column<int>(type: "int", nullable: false),
                    CraftingSeed = table.Column<int>(type: "int", nullable: false),
                    CurrentHp = table.Column<int>(type: "int", nullable: false),
                    MaxHp = table.Column<int>(type: "int", nullable: false),
                    ActionPoints = table.Column<int>(type: "int", nullable: false),
                    MaxActionPoints = table.Column<int>(type: "int", nullable: false),
                    ActiveCompanionIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnlockedPortals = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerFactionReputations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FactionId = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    HasPermanentFloor = table.Column<bool>(type: "bit", nullable: false),
                    PermanentFloor = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerFactionReputations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerFactionReputations_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companions_OwnerId",
                table: "Companions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_OwnerId",
                table: "Items",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerFactionReputations_PlayerId_FactionId",
                table: "PlayerFactionReputations",
                columns: new[] { "PlayerId", "FactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Name",
                table: "Players",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Companions");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "PlayerFactionReputations");

            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
