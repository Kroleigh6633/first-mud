using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstMud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHomesteadCompanionDuty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SalvageQueueJson",
                table: "Homesteads",
                type: "nvarchar(max)",
                nullable: true,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "AssignedDuty",
                table: "Companions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DutyStartedAt",
                table: "Companions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalvageQueueJson",
                table: "Homesteads");

            migrationBuilder.DropColumn(
                name: "AssignedDuty",
                table: "Companions");

            migrationBuilder.DropColumn(
                name: "DutyStartedAt",
                table: "Companions");
        }
    }
}
