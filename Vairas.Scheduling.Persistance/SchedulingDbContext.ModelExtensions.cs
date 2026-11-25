using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Persistance;

public sealed partial class SchedulingDbContext
{
    private static void ConfigureExtensions(ModelBuilder b)
    {
        b.Entity<LessonParticipant>().Property(x=>x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Entity<SchedulingOperation>(e=> {e.Property(x=>x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);e.Property(x=>x.ResultJson).HasColumnName("result_json");e.Property(x=>x.HttpStatus).HasColumnName("http_status");});
        b.Entity<SchedulingRequest>(e=> {e.Property(x=>x.InputsJson).HasColumnName("inputs_json");e.Property(x=>x.BasisJson).HasColumnName("basis_json");e.Property(x=>x.SchoolRevision).HasColumnName("school_revision");e.Property(x=>x.Status).IsConcurrencyToken();});
        b.Entity<AssessmentAppointment>(e=> {e.Property(x=>x.ReadinessEvaluationId).HasColumnName("readiness_evaluation_id");e.Property(x=>x.Revision).HasColumnName("revision").IsConcurrencyToken();});
        b.Entity<VehicleGuard>().ToTable("vehicle_guard").HasKey(x=>x.VehicleId);
        b.Entity<VehicleBlock>().ToTable("vehicle_block").HasKey(x=>x.Id);
        b.Entity<GateCommand>().ToTable("gate_command").HasKey(x=>x.Id);
        b.Entity<SchoolBarrier>().ToTable("school_barrier").HasKey(x=>x.SchoolId);
        b.Entity<SchoolTransition>().ToTable("school_transition").HasKey(x=>x.Id);
        b.Entity<OutboxMessage>().ToTable("outbox_message").HasKey(x=>x.Id);
        b.Entity<VehicleBlock>().HasIndex(x=>new{x.VehicleId,x.Status});
        b.Entity<SchoolTransition>().HasIndex(x=>new{x.SchoolId,x.Status});
        b.Entity<OutboxMessage>().HasIndex(x=>new{x.PublishedAt,x.AvailableAt});
        b.Entity<LessonReview>(e=>{e.ToTable("lesson_review").HasKey(x=>x.Id);e.HasIndex(x=>new{x.SourceEventId,x.TargetKind,x.TargetId}).IsUnique();e.HasIndex(x=>new{x.SchoolId,x.ResolvedAt});
            e.Property(x=>x.TargetKind).HasMaxLength(32);e.Property(x=>x.ReasonCode).HasMaxLength(64);e.Property(x=>x.SubjectRole).HasMaxLength(16);e.Property(x=>x.ResolutionNote).HasMaxLength(500);});
        b.Entity<IntegrationInbox>(e=>{e.ToTable("integration_inbox").HasKey(x=>x.EventId);e.Property(x=>x.Producer).HasMaxLength(32);e.Property(x=>x.EventType).HasMaxLength(64);
            e.Property(x=>x.Fingerprint).HasMaxLength(64);e.Property(x=>x.Outcome).HasMaxLength(32);});
        var names=new NpgsqlSnakeCaseNameTranslator();
        foreach(var type in new[]{typeof(VehicleGuard),typeof(VehicleBlock),typeof(GateCommand),typeof(SchoolBarrier),typeof(SchoolTransition),typeof(OutboxMessage),typeof(LessonReview),typeof(IntegrationInbox)})
            foreach(var property in b.Entity(type).Metadata.GetProperties()) property.SetColumnName(names.TranslateMemberName(property.Name));
    }
}
