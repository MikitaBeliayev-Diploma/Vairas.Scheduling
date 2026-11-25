using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vairas.Scheduling.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class LessonReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "lesson",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "lesson_reason",
                table: "lesson",
                sql: "reason IS NULL OR (kind = 'Practical' AND reason = btrim(reason) AND reason <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "lesson_reason",
                table: "lesson");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "lesson");
        }
    }
}
