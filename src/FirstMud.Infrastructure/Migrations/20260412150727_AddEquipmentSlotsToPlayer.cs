using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentSlotsToPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EquippedAccessoryId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "EquippedArmorId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "EquippedWeaponId",
                table: "Players");
        }
    }
}
