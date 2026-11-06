using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintShelf.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "models",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FileSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    FileBytes = table.Column<long>(type: "bigint", nullable: false),
                    TriCount = table.Column<int>(type: "integer", nullable: false),
                    BboxX = table.Column<decimal>(type: "numeric(10,3)", nullable: false),
                    BboxY = table.Column<decimal>(type: "numeric(10,3)", nullable: false),
                    BboxZ = table.Column<decimal>(type: "numeric(10,3)", nullable: false),
                    VolumeMm3 = table.Column<decimal>(type: "numeric(14,3)", nullable: true),
                    PrintUnit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_models", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_tags",
                columns: table => new
                {
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_tags", x => new { x.ModelId, x.TagId });
                    table.ForeignKey(
                        name: "FK_model_tags_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_model_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_tags_TagId",
                table: "model_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_models_Category",
                table: "models",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_models_FileSha256",
                table: "models",
                column: "FileSha256");

            migrationBuilder.CreateIndex(
                name: "IX_models_Name",
                table: "models",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Slug",
                table: "tags",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_tags");

            migrationBuilder.DropTable(
                name: "models");

            migrationBuilder.DropTable(
                name: "tags");
        }
    }
}
