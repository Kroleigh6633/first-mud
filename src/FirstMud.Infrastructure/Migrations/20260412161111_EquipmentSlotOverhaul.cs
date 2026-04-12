using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentSlotOverhaul : Migration
    {
        // EquipmentSlot int values matching Domain.Enums.EquipmentSlot
        // None=0, MeleeWeapon=1, RangedWeapon=2, Focus=3, Head=4, Chest=5, Legs=6, Hands=7, Feet=8, Accessory=9
        // ItemCategory int values: Weapon=0, Armor=1, Accessory=2

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1 — Add the new Slot column to Items (default 0 = None)
            migrationBuilder.AddColumn<int>(
                name: "Slot",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Step 2 — Set slot based on existing Category:
            //   Weapon (0) → MeleeWeapon (1)
            //   Armor  (1) → Chest       (5)
            //   Accessory (2) → Accessory (9)
            migrationBuilder.Sql(
                "UPDATE Items SET Slot = 1 WHERE Category = 0;");   // Weapon → MeleeWeapon
            migrationBuilder.Sql(
                "UPDATE Items SET Slot = 5 WHERE Category = 1;");   // Armor  → Chest
            migrationBuilder.Sql(
                "UPDATE Items SET Slot = 9 WHERE Category = 2;");   // Accessory → Accessory

            // Step 3 — Add the new EquippedItemsJson column (empty dict by default)
            migrationBuilder.AddColumn<string>(
                name: "EquippedItemsJson",
                table: "Players",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            // Step 4 — Migrate existing equipped IDs into the JSON dictionary
            //   Weapon  slot = 1 (MeleeWeapon)
            //   Armor   slot = 5 (Chest)
            //   Accessory slot = 9 (Accessory)
            migrationBuilder.Sql(@"
UPDATE Players
SET EquippedItemsJson =
    (
        SELECT '{' +
            CASE WHEN EquippedWeaponId    IS NOT NULL THEN '""1"":""'    + CAST(EquippedWeaponId    AS NVARCHAR(36)) + '"",' ELSE '' END +
            CASE WHEN EquippedArmorId     IS NOT NULL THEN '""5"":""'    + CAST(EquippedArmorId     AS NVARCHAR(36)) + '"",' ELSE '' END +
            CASE WHEN EquippedAccessoryId IS NOT NULL THEN '""9"":""'    + CAST(EquippedAccessoryId AS NVARCHAR(36)) + '"",' ELSE '' END +
        '}'
    )
WHERE EquippedWeaponId IS NOT NULL
   OR EquippedArmorId IS NOT NULL
   OR EquippedAccessoryId IS NOT NULL;
");

            // Step 5 — Remove the trailing comma before the closing brace (SQL Server compatible).
            // The generated JSON looks like {"1":"guid","5":"guid",} so we must strip 2 characters
            // (the trailing comma AND the closing brace) then re-append just the closing brace.
            // Using LEN()-1 was a bug: it only removed the } and added it back, leaving the comma.
            migrationBuilder.Sql(@"
UPDATE Players
SET EquippedItemsJson = LEFT(EquippedItemsJson, LEN(EquippedItemsJson) - 2) + '}'
WHERE EquippedItemsJson LIKE '%,}'
  AND EquippedItemsJson <> '{}';
");

            // Step 6 — Drop the old three-column equipment slots
            migrationBuilder.DropColumn(
                name: "EquippedWeaponId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "EquippedArmorId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "EquippedAccessoryId",
                table: "Players");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EquippedItemsJson",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Slot",
                table: "Items");

            migrationBuilder.AddColumn<Guid>(
                name: "EquippedAccessoryId",
                table: "Players",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EquippedArmorId",
                table: "Players",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EquippedWeaponId",
                table: "Players",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
