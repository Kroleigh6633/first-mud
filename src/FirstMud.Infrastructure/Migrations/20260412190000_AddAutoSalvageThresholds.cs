using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoSalvageThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoSalvageWeaponThreshold",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AutoSalvageArmorThreshold",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoSalvageWeaponThreshold",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "AutoSalvageArmorThreshold",
                table: "Players");
        }
    }
}
