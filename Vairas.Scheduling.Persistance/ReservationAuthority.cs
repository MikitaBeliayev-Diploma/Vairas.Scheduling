using Npgsql;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Persistance;

/// <summary>Minimal same-database read role, separate from tenant runtime. It cannot read profile/contact data.</summary>
public sealed class ReservationAuthority(string connection) : IReservationAuthority
{
    public async Task<IReadOnlyList<Session>> ReadAsync(Guid school,IReadOnlyCollection<Guid> students,DateTimeOffset from,DateTimeOffset to,CancellationToken ct,IReadOnlyCollection<Guid>? instructors=null,IReadOnlyCollection<Guid>? vehicles=null)
    {
        if(string.IsNullOrWhiteSpace(connection))throw SchedulingErrors.Dependency("RESERVATION_AUTHORITY_UNAVAILABLE");
        await using var db=new NpgsqlConnection(connection);await db.OpenAsync(ct);
        await using var command=new NpgsqlCommand("""
            SELECT l.id,l.school_id,l.kind::text,l.teacher_id,l.branch_id,l.vehicle_id,l.student_id,l.group_id,l.category_code,l.starts_at,l.ends_at,
                   CASE WHEN l.kind='Practical' THEN ARRAY[l.student_id] ELSE ARRAY(SELECT p.student_id FROM lesson_participant p WHERE p.lesson_id=l.id AND p.is_active ORDER BY p.student_id) END, false
            FROM lesson l WHERE l.status IN ('Scheduled','Completed','NoShow') AND l.starts_at < @until AND l.ends_at > @since
              AND (l.school_id=@school OR l.teacher_id=ANY(@teachers) OR l.vehicle_id=ANY(@vehicles) OR l.student_id=ANY(@students) OR EXISTS(SELECT 1 FROM lesson_participant p WHERE p.lesson_id=l.id AND p.is_active AND p.student_id=ANY(@students)))
            UNION ALL
            SELECT a.id,a.school_id,a.kind::text,a.teacher_id,a.branch_id,a.vehicle_id,
                   CASE WHEN a.kind='Practical' THEN a.student_id END,
                   -- Same shape as at booking: an individual theory assessment is a synthetic one-person group.
                   CASE WHEN a.kind='Theory' THEN a.id END,
                   a.category_code,a.starts_at,a.ends_at,ARRAY[a.student_id],true
            FROM assessment_appointment a WHERE a.status IN ('Scheduled','Completed') AND a.starts_at < @until AND a.ends_at > @since AND (a.school_id=@school OR a.teacher_id=ANY(@teachers) OR a.vehicle_id=ANY(@vehicles) OR a.student_id=ANY(@students))
            """,db);
        command.Parameters.AddWithValue("school",school);command.Parameters.AddWithValue("students",students.ToArray());command.Parameters.AddWithValue("since",from);command.Parameters.AddWithValue("until",to);
        command.Parameters.AddWithValue("teachers",(instructors??[]).ToArray());command.Parameters.AddWithValue("vehicles",(vehicles??[]).ToArray());
        var rows=new List<Session>();await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))rows.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetGuid(3),reader.GetGuid(4),reader.IsDBNull(5)?null:reader.GetGuid(5),reader.IsDBNull(6)?null:reader.GetGuid(6),reader.IsDBNull(7)?null:reader.GetGuid(7),reader.GetFieldValue<Guid[]>(11),reader.GetString(8),reader.GetFieldValue<DateTimeOffset>(9),reader.GetFieldValue<DateTimeOffset>(10),reader.GetBoolean(12),true,true));
        return rows;
    }
}
