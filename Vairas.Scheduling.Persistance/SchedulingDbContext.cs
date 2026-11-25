using Vairas.Scheduling.Domain;
// EF Core/Npgsql 9+ example relational mapping (adjust package versions to your backend).
using Microsoft.EntityFrameworkCore;

namespace Vairas.Scheduling.Persistance;

public sealed partial class SchedulingDbContext : DbContext
{
    public SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : base(options) { }

    public DbSet<SchedulingRequest> SchedulingRequestRows => Set<SchedulingRequest>();
    public DbSet<SchedulingRequestEnrollment> SchedulingRequestEnrollmentRows => Set<SchedulingRequestEnrollment>();
    public DbSet<SchedulingRequestGroup> SchedulingRequestGroupRows => Set<SchedulingRequestGroup>();
    public DbSet<SchedulingRequestResource> SchedulingRequestResourceRows => Set<SchedulingRequestResource>();
    public DbSet<SchedulingRequestWindow> SchedulingRequestWindowRows => Set<SchedulingRequestWindow>();
    public DbSet<SchedulingRuleVersion> SchedulingRuleVersionRows => Set<SchedulingRuleVersion>();
    public DbSet<SchedulingRequestRule> SchedulingRequestRuleRows => Set<SchedulingRequestRule>();
    public DbSet<SchedulingOperationRule> SchedulingOperationRuleRows => Set<SchedulingOperationRule>();
    public DbSet<CandidateLesson> CandidateLessonRows => Set<CandidateLesson>();
    public DbSet<CandidateLessonParticipant> CandidateLessonParticipantRows => Set<CandidateLessonParticipant>();
    public DbSet<RecurringLessonSeries> RecurringLessonSeriesRows => Set<RecurringLessonSeries>();
    public DbSet<Lesson> LessonRows => Set<Lesson>();
    public DbSet<LessonParticipant> LessonParticipantRows => Set<LessonParticipant>();
    public DbSet<SchedulingOperation> SchedulingOperationRows => Set<SchedulingOperation>();
    public DbSet<ReservationClaim> ReservationClaimRows => Set<ReservationClaim>();
    public DbSet<LessonChange> LessonChangeRows => Set<LessonChange>();
    public DbSet<AssessmentAppointment> AssessmentAppointmentRows => Set<AssessmentAppointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureExtensions(modelBuilder);
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<SchedulingRequest>(e =>
        {
            e.ToTable("scheduling_request", tb =>
            {
                tb.HasCheckConstraint("request_revision_positive", @"revision > 0");
                tb.HasCheckConstraint("request_period", @"planning_start IS NULL OR planning_end IS NULL OR planning_end >= planning_start");
                tb.HasCheckConstraint("request_submitted_pair", @"(submitted_at IS NULL) = (submitted_by_user_id IS NULL)");
                tb.HasCheckConstraint("request_confirmed_metadata", @"(status = 'Confirmed' AND confirmed_at IS NOT NULL AND confirmed_operation_id IS NOT NULL) OR (status <> 'Confirmed' AND confirmed_at IS NULL AND confirmed_operation_id IS NULL)");
                tb.HasCheckConstraint("request_validated_metadata", @"status NOT IN ('Submitted','Ready','Infeasible','Stale','Superseded','Confirmed') OR (submitted_at IS NOT NULL AND planning_start IS NOT NULL AND planning_end IS NOT NULL)");
                tb.HasCheckConstraint("request_ready_metadata", @"status NOT IN ('Ready','Confirmed') OR generated_at IS NOT NULL");
            });
            e.HasKey(e => new { e.Id, e.Revision });
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.Revision).HasColumnName("revision").IsRequired();
            e.Property(x => x.SchoolId).HasColumnName("school_id").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("request_status").IsRequired().HasDefaultValue(RequestStatus.Draft);
            e.Property(x => x.PlanningStart).HasColumnName("planning_start").HasColumnType("date");
            e.Property(x => x.PlanningEnd).HasColumnName("planning_end").HasColumnType("date");
            e.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id");
            e.Property(x => x.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamp with time zone");
            e.Property(x => x.GeneratedAt).HasColumnName("generated_at").HasColumnType("timestamp with time zone");
            e.Property(x => x.ConfirmedOperationId).HasColumnName("confirmed_operation_id");
            e.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            e.HasAlternateKey(e => new { e.Id, e.Revision, e.SchoolId });
            e.HasIndex(e => new { e.SchoolId, e.Status });
            e.HasOne<SchedulingOperation>().WithMany()
                .HasForeignKey(e => new { e.ConfirmedOperationId, e.SchoolId })
                .HasPrincipalKey(p => new { p.Id, p.SchoolId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulingRequestEnrollment>(e =>
        {
            e.ToTable("scheduling_request_enrollment", tb =>
            {
                tb.HasCheckConstraint("scheduling_request_enrollment_revision", @"request_revision > 0");
            });
            e.HasKey(e => new { e.RequestId, e.RequestRevision, e.EnrollmentId });
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.RequestRevision).HasColumnName("request_revision").IsRequired();
            e.Property(x => x.EnrollmentId).HasColumnName("enrollment_id").IsRequired();
            e.Property(x => x.StudentId).HasColumnName("student_id");
            e.Property(x => x.EnrollmentRevision).HasColumnName("enrollment_revision");
            e.Property(x => x.TrainingProgramVersionId).HasColumnName("training_program_version_id");
            e.Property(x => x.CategoryCode).HasColumnName("category_code").HasMaxLength(16);
            e.HasOne<SchedulingRequest>().WithMany().HasForeignKey(e => new { e.RequestId, e.RequestRevision }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulingRequestGroup>(e =>
        {
            e.ToTable("scheduling_request_group", tb =>
            {
                tb.HasCheckConstraint("scheduling_request_group_revision", @"request_revision > 0");
            });
            e.HasKey(e => new { e.RequestId, e.RequestRevision, e.GroupId });
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.RequestRevision).HasColumnName("request_revision").IsRequired();
            e.Property(x => x.TrainingProgramVersionId).HasColumnName("training_program_version_id");
            e.Property(x => x.CategoryCode).HasColumnName("category_code").HasMaxLength(16);
            e.Property(x => x.GroupId).HasColumnName("group_id").IsRequired();
            e.Property(x => x.MembershipRevision).HasColumnName("membership_revision");
            e.HasOne<SchedulingRequest>().WithMany().HasForeignKey(e => new { e.RequestId, e.RequestRevision }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulingRequestResource>(e =>
        {
            e.ToTable("scheduling_request_resource", tb =>
            {
                tb.HasCheckConstraint("scheduling_request_resource_revision", @"request_revision > 0");
                tb.HasCheckConstraint("resource_kind_valid", @"resource_kind IN ('Instructor','Vehicle','Branch')");
                tb.HasCheckConstraint("resource_source_revision", @"eligibility_revision IS NULL OR eligibility_revision > 0");
            });
            e.HasKey(e => new { e.RequestId, e.RequestRevision, e.ResourceKind, e.ResourceId });
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.RequestRevision).HasColumnName("request_revision").IsRequired();
            e.Property(x => x.ResourceKind).HasColumnName("resource_kind").HasMaxLength(24).IsRequired();
            e.Property(x => x.ResourceId).HasColumnName("resource_id").IsRequired();
            e.Property(x => x.EligibilityRevision).HasColumnName("eligibility_revision");
            e.HasOne<SchedulingRequest>().WithMany().HasForeignKey(e => new { e.RequestId, e.RequestRevision }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulingRequestWindow>(e =>
        {
            e.ToTable("scheduling_request_window", tb =>
            {
                tb.HasCheckConstraint("window_interval", @"ends_at > starts_at");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.RequestRevision).HasColumnName("request_revision").IsRequired();
            e.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamp with time zone").IsRequired();
            e.HasOne<SchedulingRequest>().WithMany().HasForeignKey(e => new { e.RequestId, e.RequestRevision }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulingRuleVersion>(e =>
        {
            e.ToTable("scheduling_rule_version", tb =>
            {
                tb.HasCheckConstraint("rule_revision", @"revision > 0");
                tb.HasCheckConstraint("rule_source_kind", @"source_kind IN ('Legal','School')");
                tb.HasCheckConstraint("rule_school_scope", @"(source_kind = 'Legal' AND school_id IS NULL) OR (source_kind = 'School' AND school_id IS NOT NULL)");
                tb.HasCheckConstraint("rule_severity", @"severity IN ('Hard','Preference') AND (source_kind <> 'Legal' OR severity = 'Hard')");
                tb.HasCheckConstraint("rule_verification", @"verification_status IN ('Verified','Pending','Rejected')");
                tb.HasCheckConstraint("rule_decision_actor", @"(verification_status = 'Pending' AND decided_at IS NULL AND decided_by_user_id IS NULL) OR (verification_status IN ('Verified','Rejected') AND decided_at IS NOT NULL AND decided_by_user_id IS NOT NULL)");
                tb.HasCheckConstraint("rule_effective_interval", @"effective_to IS NULL OR effective_to > effective_from");
                tb.HasCheckConstraint("rule_override_policy", @"override_policy IN ('Locked','SchoolEditable') AND ((source_kind = 'Legal' AND override_policy = 'Locked') OR (source_kind = 'School' AND override_policy = 'SchoolEditable'))");
                tb.HasCheckConstraint("rule_scope", @"scope_kind IN ('School','Instructor','Vehicle','Branch','Group','Student') AND ((scope_kind = 'School' AND scope_resource_id IS NULL) OR (scope_kind <> 'School' AND scope_resource_id IS NOT NULL))");
                tb.HasCheckConstraint("rule_typed_limit", @"(numeric_limit IS NULL AND unit IS NULL) OR (numeric_limit > 0 AND unit IS NOT NULL)");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.RuleId).HasColumnName("rule_id").IsRequired();
            e.Property(x => x.Revision).HasColumnName("revision").IsRequired();
            e.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(16).IsRequired();
            e.Property(x => x.SchoolId).HasColumnName("school_id");
            e.Property(x => x.CategoryCode).HasColumnName("category_code").HasMaxLength(16);
            e.Property(x => x.RuleCode).HasColumnName("rule_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(16).IsRequired();
            e.Property(x => x.OverridePolicy).HasColumnName("override_policy").HasMaxLength(24).IsRequired();
            e.Property(x => x.ScopeKind).HasColumnName("scope_kind").HasMaxLength(24).IsRequired();
            e.Property(x => x.ScopeResourceId).HasColumnName("scope_resource_id");
            e.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500);
            e.Property(x => x.NumericLimit).HasColumnName("numeric_limit").HasColumnType("numeric(12,3)");
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(32);
            e.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("timestamp with time zone");
            e.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(400).IsRequired();
            e.Property(x => x.VerificationStatus).HasColumnName("verification_status").HasMaxLength(24).IsRequired();
            e.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamp with time zone");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
            e.HasIndex(e => new { e.RuleId, e.Revision }).IsUnique();
            e.HasIndex(e => new { e.SourceKind, e.SchoolId, e.RuleCode });
        });

        modelBuilder.Entity<SchedulingOperationRule>(e =>
        {
            e.ToTable("scheduling_operation_rule");
            e.HasKey(e => new { e.OperationId, e.RuleVersionId });
            e.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
            e.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").IsRequired();
            e.HasOne<SchedulingOperation>().WithMany().HasForeignKey(e => e.OperationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchedulingRuleVersion>().WithMany().HasForeignKey(e => e.RuleVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulingRequestRule>(e =>
        {
            e.ToTable("scheduling_request_rule", tb =>
            {
                tb.HasCheckConstraint("scheduling_request_rule_revision", @"request_revision > 0");
            });
            e.HasKey(e => new { e.RequestId, e.RequestRevision, e.RuleVersionId });
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.RequestRevision).HasColumnName("request_revision").IsRequired();
            e.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").IsRequired();
            e.HasOne<SchedulingRuleVersion>().WithMany().HasForeignKey(e => e.RuleVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchedulingRequest>().WithMany().HasForeignKey(e => new { e.RequestId, e.RequestRevision }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CandidateLesson>(e =>
        {
            e.ToTable("candidate_lesson", tb =>
            {
                tb.HasCheckConstraint("candidate_interval", @"ends_at > starts_at");
                tb.HasCheckConstraint("candidate_type", @"(kind = 'Practical' AND enrollment_id IS NOT NULL AND student_id IS NOT NULL AND vehicle_id IS NOT NULL AND group_id IS NULL) OR (kind = 'Theory' AND group_id IS NOT NULL AND enrollment_id IS NULL AND student_id IS NULL AND vehicle_id IS NULL)");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.RequestRevision).HasColumnName("request_revision").IsRequired();
            e.Property(x => x.Kind).HasColumnName("kind").HasColumnType("lesson_kind").IsRequired();
            e.Property(x => x.StudentId).HasColumnName("student_id");
            e.Property(x => x.EnrollmentId).HasColumnName("enrollment_id");
            e.Property(x => x.GroupId).HasColumnName("group_id");
            e.Property(x => x.TeacherId).HasColumnName("teacher_id").IsRequired();
            e.Property(x => x.VehicleId).HasColumnName("vehicle_id");
            e.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
            e.Property(x => x.SeriesId).HasColumnName("series_id");
            e.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.CategoryCode).HasColumnName("category_code").HasMaxLength(16).IsRequired();
            e.HasIndex(e => new { e.RequestId, e.RequestRevision });
            e.HasAlternateKey(e => new { e.Id, e.RequestId, e.RequestRevision });
            e.HasOne<SchedulingRequest>().WithMany().HasForeignKey(e => new { e.RequestId, e.RequestRevision }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RecurringLessonSeries>().WithMany().HasForeignKey(e => e.SeriesId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CandidateLessonParticipant>(e =>
        {
            e.ToTable("candidate_lesson_participant", tb =>
            {
                tb.HasCheckConstraint("candidate_lesson_participant_membership_revision", @"membership_revision > 0");
            });
            e.HasKey(e => new { e.CandidateLessonId, e.EnrollmentId });
            e.Property(x => x.CandidateLessonId).HasColumnName("candidate_lesson_id").IsRequired();
            e.Property(x => x.EnrollmentId).HasColumnName("enrollment_id").IsRequired();
            e.Property(x => x.StudentId).HasColumnName("student_id").IsRequired();
            e.Property(x => x.MembershipRevision).HasColumnName("membership_revision").IsRequired();
            e.HasIndex(e => new { e.CandidateLessonId, e.StudentId }).IsUnique();
            e.HasOne<CandidateLesson>().WithMany().HasForeignKey(e => e.CandidateLessonId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringLessonSeries>(e =>
        {
            e.ToTable("recurring_lesson_series", tb =>
            {
                tb.HasCheckConstraint("recurrence_positive", @"recurrence_interval > 0 AND duration_minutes > 0");
                tb.HasCheckConstraint("recurrence_end_choice", @"(ends_on IS NOT NULL)::int + (occurrence_count IS NOT NULL)::int = 1");
                tb.HasCheckConstraint("recurrence_end_range", @"ends_on IS NULL OR ends_on >= starts_on");
                tb.HasCheckConstraint("recurrence_count", @"occurrence_count IS NULL OR occurrence_count > 0");
                tb.HasCheckConstraint("recurrence_weekdays", @"(frequency = 'Daily' AND coalesce(cardinality(weekdays),0) = 0) OR (frequency = 'Weekly' AND weekdays IS NOT NULL AND cardinality(weekdays) BETWEEN 1 AND 7 AND weekdays <@ ARRAY[1,2,3,4,5,6,7]::smallint[])");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.SchoolId).HasColumnName("school_id").IsRequired();
            e.Property(x => x.Kind).HasColumnName("kind").HasColumnType("lesson_kind").IsRequired();
            e.Property(x => x.Frequency).HasColumnName("frequency").HasColumnType("recurrence_frequency").IsRequired();
            e.Property(x => x.RecurrenceInterval).HasColumnName("recurrence_interval").IsRequired().HasDefaultValue(1);
            e.Property(x => x.Weekdays).HasColumnName("weekdays").HasColumnType("smallint[]");
            e.Property(x => x.LocalStartTime).HasColumnName("local_start_time").HasColumnType("time").IsRequired();
            e.Property(x => x.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
            e.Property(x => x.TimeZone).HasColumnName("time_zone").HasMaxLength(80).IsRequired();
            e.Property(x => x.StartsOn).HasColumnName("starts_on").HasColumnType("date").IsRequired();
            e.Property(x => x.EndsOn).HasColumnName("ends_on").HasColumnType("date");
            e.Property(x => x.OccurrenceCount).HasColumnName("occurrence_count");
            e.HasAlternateKey(e => new { e.Id, e.SchoolId, e.Kind });
        });

        modelBuilder.Entity<Lesson>(e =>
        {
            e.ToTable("lesson", tb =>
            {
                tb.HasCheckConstraint("lesson_revision_positive", @"revision > 0");
                tb.HasCheckConstraint("lesson_membership_basis", @"(kind = 'Practical' AND group_membership_revision IS NULL) OR (kind = 'Theory' AND group_membership_revision IS NOT NULL AND group_membership_revision > 0)");
                tb.HasCheckConstraint("lesson_interval", @"ends_at > starts_at");
                tb.HasCheckConstraint("lesson_type", @"(kind = 'Practical' AND enrollment_id IS NOT NULL AND student_id IS NOT NULL AND vehicle_id IS NOT NULL AND group_id IS NULL) OR (kind = 'Theory' AND group_id IS NOT NULL AND enrollment_id IS NULL AND student_id IS NULL AND vehicle_id IS NULL)");
                tb.HasCheckConstraint("lesson_reason", @"reason IS NULL OR (kind = 'Practical' AND reason = btrim(reason) AND reason <> '')");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.SchoolId).HasColumnName("school_id").IsRequired();
            e.Property(x => x.Kind).HasColumnName("kind").HasColumnType("lesson_kind").IsRequired();
            e.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
            e.Property(x => x.TeacherId).HasColumnName("teacher_id").IsRequired();
            e.Property(x => x.EnrollmentId).HasColumnName("enrollment_id");
            e.Property(x => x.StudentId).HasColumnName("student_id");
            e.Property(x => x.VehicleId).HasColumnName("vehicle_id");
            e.Property(x => x.GroupId).HasColumnName("group_id");
            e.Property(x => x.ActualStart).HasColumnName("actual_start");
            e.Property(x => x.ActualEnd).HasColumnName("actual_end");
            e.Property(x => x.GroupMembershipRevision).HasColumnName("group_membership_revision");
            e.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("lesson_status").IsRequired().HasDefaultValue(LessonStatus.Scheduled);
            e.Property(x => x.Revision).HasColumnName("revision").IsRequired().HasDefaultValue(1).IsConcurrencyToken();
            e.Property(x => x.SeriesId).HasColumnName("series_id");
            e.Property(x => x.SourceCandidateId).HasColumnName("source_candidate_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.CategoryCode).HasColumnName("category_code").HasMaxLength(16).IsRequired();
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
            e.HasAlternateKey(e => new { e.Id, e.SchoolId });
            e.HasIndex(e => new { e.SchoolId, e.StartsAt });
            e.HasIndex(e => new { e.TeacherId, e.StartsAt, e.EndsAt });
            e.HasIndex(e => e.SourceCandidateId).IsUnique();
            e.HasOne<CandidateLesson>().WithMany().HasForeignKey(e => e.SourceCandidateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RecurringLessonSeries>().WithMany().HasForeignKey(e => new { e.SeriesId, e.SchoolId, e.Kind }).HasPrincipalKey(p => new { p.Id, p.SchoolId, p.Kind }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LessonParticipant>(e =>
        {
            e.ToTable("lesson_participant", tb =>
            {
                tb.HasCheckConstraint("lesson_participant_membership_revision", @"membership_revision > 0");
            });
            e.HasKey(e => new { e.LessonId, e.EnrollmentId });
            e.Property(x => x.LessonId).HasColumnName("lesson_id").IsRequired();
            e.Property(x => x.EnrollmentId).HasColumnName("enrollment_id").IsRequired();
            e.Property(x => x.StudentId).HasColumnName("student_id").IsRequired();
            e.Property(x => x.MembershipRevision).HasColumnName("membership_revision").IsRequired();
            e.HasIndex(e => new { e.LessonId, e.StudentId }).IsUnique();
            e.HasOne<Lesson>().WithMany().HasForeignKey(e => e.LessonId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchedulingOperation>(e =>
        {
            e.ToTable("scheduling_operation", tb =>
            {
                tb.HasCheckConstraint("operation_action", @"action_type IN ('ConfirmSchedule','CreateLesson','Reschedule','Cancel','ChangeGroup','CompleteLesson','CreateAssessment','CancelAssessment')");
                tb.HasCheckConstraint("operation_channel", @"channel IN ('Web','MCP','Internal')");
                tb.HasCheckConstraint("operation_request_pair", @"(request_id IS NULL) = (request_revision IS NULL)");
                tb.HasCheckConstraint("operation_finished", @"(status = 'Pending' AND finished_at IS NULL) OR (status = 'ReconciliationPending') OR (status IN ('Committed','Rejected') AND finished_at IS NOT NULL)");
                tb.HasCheckConstraint("operation_expected_revision", @"expected_lesson_revision IS NULL OR expected_lesson_revision > 0");
                tb.HasCheckConstraint("operation_target_xor", @"target_lesson_id IS NULL OR target_appointment_id IS NULL");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.SchoolId).HasColumnName("school_id").IsRequired();
            e.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(32).IsRequired();
            e.Property(x => x.RequestId).HasColumnName("request_id");
            e.Property(x => x.RequestRevision).HasColumnName("request_revision");
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(16).IsRequired();
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("operation_status").IsRequired().HasDefaultValue(OperationStatus.Pending);
            e.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamp with time zone");
            e.Property(x => x.TargetLessonId).HasColumnName("target_lesson_id");
            e.Property(x => x.ExpectedLessonRevision).HasColumnName("expected_lesson_revision");
            e.Property(x => x.TargetAppointmentId).HasColumnName("target_appointment_id");
            e.Property(x => x.ResultLessonId).HasColumnName("result_lesson_id");
            e.Property(x => x.ResultAppointmentId).HasColumnName("result_appointment_id");
            e.Property(x => x.OutcomeCode).HasColumnName("outcome_code").HasMaxLength(64);
            e.Property(x => x.UserConfirmedAt).HasColumnName("user_confirmed_at").HasColumnType("timestamp with time zone");
            e.HasAlternateKey(e => new { e.Id, e.SchoolId });
            e.HasOne<SchedulingRequest>().WithMany().HasForeignKey(e => new { e.RequestId, e.RequestRevision, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.Revision, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lesson>().WithMany().HasForeignKey(e => new { e.TargetLessonId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lesson>().WithMany().HasForeignKey(e => new { e.ResultLessonId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AssessmentAppointment>().WithMany().HasForeignKey(e => new { e.TargetAppointmentId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AssessmentAppointment>().WithMany().HasForeignKey(e => new { e.ResultAppointmentId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReservationClaim>(e =>
        {
            e.ToTable("reservation_claim", tb =>
            {
                tb.HasCheckConstraint("claim_interval", @"ends_at > starts_at");
                tb.HasCheckConstraint("claim_owner", @"(lesson_id IS NOT NULL)::int + (assessment_appointment_id IS NOT NULL)::int = 1");
                tb.HasCheckConstraint("claim_kind", @"resource_kind IN ('Student','Instructor','Vehicle','Group')");
                tb.HasCheckConstraint("claim_release", @"(status = 'Active' AND released_at IS NULL) OR (status = 'Released' AND released_at IS NOT NULL AND released_at >= created_at)");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
            e.Property(x => x.LessonId).HasColumnName("lesson_id");
            e.Property(x => x.AssessmentAppointmentId).HasColumnName("assessment_appointment_id");
            e.Property(x => x.SchoolId).HasColumnName("school_id").IsRequired();
            e.Property(x => x.ResourceKind).HasColumnName("resource_kind").HasMaxLength(24).IsRequired();
            e.Property(x => x.ResourceKey).HasColumnName("resource_key").IsRequired();
            e.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("reservation_status").IsRequired().HasDefaultValue(ReservationStatus.Active);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.ReleasedAt).HasColumnName("released_at").HasColumnType("timestamp with time zone");
            e.HasIndex(e => new { e.ResourceKind, e.ResourceKey, e.StartsAt, e.EndsAt });
            e.HasIndex(e => new { e.LessonId, e.ResourceKind, e.ResourceKey });
            e.HasOne<SchedulingOperation>().WithMany().HasForeignKey(e => new { e.OperationId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lesson>().WithMany().HasForeignKey(e => new { e.LessonId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AssessmentAppointment>().WithMany().HasForeignKey(e => new { e.AssessmentAppointmentId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LessonChange>(e =>
        {
            e.ToTable("lesson_change", tb =>
            {
                tb.HasCheckConstraint("history_revision_positive", @"revision > 1");
                tb.HasCheckConstraint("history_previous_interval", @"previous_end > previous_start");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.SchoolId).HasColumnName("school_id").IsRequired();
            e.Property(x => x.LessonId).HasColumnName("lesson_id").IsRequired();
            e.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
            e.Property(x => x.Revision).HasColumnName("revision").IsRequired();
            e.Property(x => x.PreviousActualStart).HasColumnName("previous_actual_start");
            e.Property(x => x.PreviousActualEnd).HasColumnName("previous_actual_end");
            e.Property(x => x.PreviousStatus).HasColumnName("previous_status").HasColumnType("lesson_status").IsRequired();
            e.Property(x => x.PreviousStart).HasColumnName("previous_start").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.PreviousEnd).HasColumnName("previous_end").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.PreviousTeacherId).HasColumnName("previous_teacher_id").IsRequired();
            e.Property(x => x.PreviousVehicleId).HasColumnName("previous_vehicle_id");
            e.Property(x => x.PreviousBranchId).HasColumnName("previous_branch_id").IsRequired();
            e.Property(x => x.PreviousSeriesId).HasColumnName("previous_series_id");
            e.Property(x => x.PreviousGroupMembershipRevision).HasColumnName("previous_group_membership_revision");
            e.Property(x => x.ChangedAt).HasColumnName("changed_at").HasColumnType("timestamp with time zone").IsRequired();
            e.HasIndex(e => new { e.LessonId, e.Revision }).IsUnique();
            e.HasOne<Lesson>().WithMany().HasForeignKey(e => new { e.LessonId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchedulingOperation>().WithMany().HasForeignKey(e => new { e.OperationId, e.SchoolId }).HasPrincipalKey(p => new { p.Id, p.SchoolId }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssessmentAppointment>(e =>
        {
            e.ToTable("assessment_appointment", tb =>
            {
                tb.HasCheckConstraint("assessment_interval", @"ends_at > starts_at");
                tb.HasCheckConstraint("assessment_type", @"(kind = 'Practical' AND vehicle_id IS NOT NULL) OR (kind = 'Theory' AND vehicle_id IS NULL)");
            });
            e.HasKey(e => e.Id);
            e.Property(x => x.Id).HasColumnName("id").IsRequired();
            e.Property(x => x.SchoolId).HasColumnName("school_id").IsRequired();
            e.Property(x => x.Kind).HasColumnName("kind").HasColumnType("lesson_kind").IsRequired();
            e.Property(x => x.EnrollmentId).HasColumnName("enrollment_id").IsRequired();
            e.Property(x => x.StudentId).HasColumnName("student_id").IsRequired();
            e.Property(x => x.AssessmentDefinitionId).HasColumnName("assessment_definition_id").IsRequired();
            e.Property(x => x.OutstandingRequirementId).HasColumnName("outstanding_requirement_id");
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
            e.Property(x => x.TeacherId).HasColumnName("teacher_id").IsRequired();
            e.Property(x => x.VehicleId).HasColumnName("vehicle_id");
            e.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
            e.Property(x => x.CategoryCode).HasColumnName("category_code").HasMaxLength(16).IsRequired();
            e.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("lesson_status").IsRequired().HasDefaultValue(LessonStatus.Scheduled);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            e.HasAlternateKey(e => new { e.Id, e.SchoolId });
        });

    }
}
