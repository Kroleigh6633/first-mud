using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MaxInventorySlots",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 40,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MaxInventorySlots",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 20,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 40);
        }
    }
}
