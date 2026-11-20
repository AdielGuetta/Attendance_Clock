using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceClock.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkLocation",
                table: "Shifts",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Shifts_WorkLocation",
                table: "Shifts",
                sql: "[WorkLocation] IS NULL OR [WorkLocation] IN ('Office', 'Remote')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Shifts_WorkLocation",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "WorkLocation",
                table: "Shifts");
        }
    }
}
