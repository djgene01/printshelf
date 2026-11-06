using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintShelf.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_Name",
                table: "categories",
                column: "Name",
                unique: true);

            // Promote every category already used by a model into the new table,
            // so existing catalogues keep their grouping and gain an editable colour.
            migrationBuilder.Sql("""
                INSERT INTO categories ("Id", "Name", "Color", "CreatedAt")
                SELECT gen_random_uuid(), src."Category",
                       CASE src."Category"
                           WHEN 'Functional'  THEN '#67e8f9'
                           WHEN 'Miniature'   THEN '#86efac'
                           WHEN 'Tools'       THEN '#fcd34d'
                           WHEN 'Decor'       THEN '#d8b4fe'
                           WHEN 'Spare parts' THEN '#fca5a5'
                           WHEN 'Prototype'   THEN '#93c5fd'
                           ELSE '#c7d2de'
                       END,
                       now()
                FROM (SELECT DISTINCT "Category" FROM models
                      WHERE "Category" IS NOT NULL AND "Category" <> '') AS src
                ON CONFLICT ("Name") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}
