using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vairas.Scheduling.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class DeactivationReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_inbox",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producer = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reviews_raised = table.Column<int>(type: "integer", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_inbox", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "lesson_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_review", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lesson_review_school_id_resolved_at",
                table: "lesson_review",
                columns: new[] { "school_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "IX_lesson_review_source_event_id_target_kind_target_id",
                table: "lesson_review",
                columns: new[] { "source_event_id", "target_kind", "target_id" },
                unique: true);

            // A review never changes its booking; only its single resolution may be written, and then it is immutable.
            migrationBuilder.Sql("""
ALTER TABLE lesson_review ADD CONSTRAINT lesson_review_target CHECK(target_kind IN ('Lesson','AssessmentAppointment')),
  ADD CONSTRAINT lesson_review_role CHECK(subject_role IN ('Teacher','Student')),
  ADD CONSTRAINT lesson_review_resolution CHECK((resolved_at IS NULL)=(resolved_by_user_id IS NULL) AND (resolved_at IS NULL)=(resolution_note IS NULL) AND (resolved_at IS NULL OR resolved_at>=raised_at));
ALTER TABLE integration_inbox ADD CONSTRAINT integration_inbox_fingerprint CHECK(fingerprint ~ '^[0-9a-f]{64}$'), ADD CONSTRAINT integration_inbox_reviews CHECK(reviews_raised>=0);
CREATE FUNCTION lesson_review_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF TG_OP='DELETE' OR OLD.resolved_at IS NOT NULL THEN RAISE EXCEPTION 'Lesson review history is immutable'; END IF;
 IF (NEW.id,NEW.school_id,NEW.target_kind,NEW.target_id,NEW.reason_code,NEW.subject_role,NEW.subject_user_id,NEW.source_event_id,NEW.raised_at)
    IS DISTINCT FROM (OLD.id,OLD.school_id,OLD.target_kind,OLD.target_id,OLD.reason_code,OLD.subject_role,OLD.subject_user_id,OLD.source_event_id,OLD.raised_at)
 THEN RAISE EXCEPTION 'Lesson review identity is immutable'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER lesson_review_guard BEFORE UPDATE OR DELETE ON lesson_review FOR EACH ROW EXECUTE FUNCTION lesson_review_guard();
CREATE FUNCTION integration_inbox_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN RAISE EXCEPTION 'Integration inbox is append-only'; END $$;
CREATE TRIGGER integration_inbox_guard BEFORE UPDATE OR DELETE ON integration_inbox FOR EACH ROW EXECUTE FUNCTION integration_inbox_guard();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER lesson_review_guard ON lesson_review; DROP TRIGGER integration_inbox_guard ON integration_inbox; DROP FUNCTION lesson_review_guard(); DROP FUNCTION integration_inbox_guard();");
            migrationBuilder.DropTable(
                name: "integration_inbox");

            migrationBuilder.DropTable(
                name: "lesson_review");
        }
    }
}
