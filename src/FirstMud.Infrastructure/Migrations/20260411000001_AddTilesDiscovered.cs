using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTilesDiscovered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TilesDiscovered",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TilesDiscovered",
                table: "Players");
        }
    }
}
