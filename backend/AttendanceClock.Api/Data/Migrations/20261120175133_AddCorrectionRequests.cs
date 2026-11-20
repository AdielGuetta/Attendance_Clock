using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceClock.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrectionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorrectionRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftId = table.Column<long>(type: "bigint", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    RequestedClockIn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequestedClockOut = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionRequests", x => x.Id);
                    table.CheckConstraint("CK_CorrectionRequests_AsksForAChange", "[RequestedClockIn] IS NOT NULL OR [RequestedClockOut] IS NOT NULL");
                    table.CheckConstraint("CK_CorrectionRequests_DecidedFields", "([Status] = 'Pending' AND [DecidedByEmployeeId] IS NULL AND [DecidedAt] IS NULL) OR ([Status] <> 'Pending' AND [DecidedByEmployeeId] IS NOT NULL AND [DecidedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_CorrectionRequests_Status", "[Status] IN ('Pending', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_CorrectionRequests_Employees_DecidedByEmployeeId",
                        column: x => x.DecidedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionRequests_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_DecidedByEmployeeId",
                table: "CorrectionRequests",
                column: "DecidedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_EmployeeId_CreatedAt",
                table: "CorrectionRequests",
                columns: new[] { "EmployeeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_Status_CreatedAt",
                table: "CorrectionRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CorrectionRequests_OnePendingPerShift",
                table: "CorrectionRequests",
                column: "ShiftId",
                unique: true,
                filter: "[Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectionRequests");
        }
    }
}
