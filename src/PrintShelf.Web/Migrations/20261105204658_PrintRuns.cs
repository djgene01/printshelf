using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintShelf.Web.Migrations
{
    /// <inheritdoc />
    public partial class PrintRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "print_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Printer = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Material = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NozzleMm = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    LayerMm = table.Column<decimal>(type: "numeric(4,3)", nullable: true),
                    DurationMin = table.Column<int>(type: "integer", nullable: true),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PrintedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_runs", x => x.Id);
                    table.CheckConstraint("CK_print_runs_result", "\"Result\" IN ('success','failed','partial')");
                    table.ForeignKey(
                        name: "FK_print_runs_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_print_runs_ModelId_PrintedAt",
                table: "print_runs",
                columns: new[] { "ModelId", "PrintedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "print_runs");
        }
    }
}
