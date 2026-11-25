using Microsoft.EntityFrameworkCore.Migrations;
namespace Vairas.Scheduling.Persistance.Migrations;
public partial class Reliability:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE scheduling_operation ADD COLUMN fingerprint varchar(64) NOT NULL DEFAULT '', ADD COLUMN result_json text NOT NULL DEFAULT '', ADD COLUMN http_status integer NOT NULL DEFAULT 0;
ALTER TABLE scheduling_operation DROP CONSTRAINT operation_action;
ALTER TABLE scheduling_operation ADD CONSTRAINT operation_action CHECK(action_type IN ('ConfirmSchedule','CreateLesson','Reschedule','Cancel','ChangeGroup','CompleteLesson','CorrectActualInterval','CreateAssessment','CancelAssessment','CreateDraft','ReviseDraft','SubmitRequest','GenerateProposal','PublishRule'));
ALTER TABLE scheduling_request ADD COLUMN inputs_json text NOT NULL DEFAULT '{}', ADD COLUMN basis_json text NOT NULL DEFAULT '', ADD COLUMN school_revision bigint NOT NULL DEFAULT 0;
ALTER TABLE assessment_appointment ADD COLUMN readiness_evaluation_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000', ADD COLUMN revision integer NOT NULL DEFAULT 1;
ALTER TABLE lesson_participant ADD COLUMN is_active boolean NOT NULL DEFAULT true;
""");
            migrationBuilder.CreateTable(
                name: "gate_command",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_command", x => x.id);
                });
            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_version = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.id);
                });
            migrationBuilder.CreateTable(
                name: "school_barrier",
                columns: table => new
                {
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_floor = table.Column<long>(type: "bigint", nullable: false),
                    pending_operation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_barrier", x => x.school_id);
                });
            migrationBuilder.CreateTable(
                name: "school_transition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_revision = table.Column<long>(type: "bigint", nullable: false),
                    after_revision = table.Column<long>(type: "bigint", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_transition", x => x.id);
                });
            migrationBuilder.CreateTable(
                name: "vehicle_block",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_block", x => x.id);
                });
            migrationBuilder.CreateTable(
                name: "vehicle_guard",
                columns: table => new
                {
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_guard", x => x.vehicle_id);
                });
            migrationBuilder.CreateIndex(
                name: "IX_outbox_message_published_at_available_at",
                table: "outbox_message",
                columns: new[] { "published_at", "available_at" });
            migrationBuilder.CreateIndex(
                name: "IX_school_transition_school_id_status",
                table: "school_transition",
                columns: new[] { "school_id", "status" });
            migrationBuilder.CreateIndex(
                name: "IX_vehicle_block_vehicle_id_status",
                table: "vehicle_block",
                columns: new[] { "vehicle_id", "status" });
        migrationBuilder.Sql(CanonicalSql.Read("Hardening.sql"));
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Retained commands and gates cannot be dropped automatically.");
}
