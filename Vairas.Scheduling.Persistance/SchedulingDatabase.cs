using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Persistance;

public static class SchedulingDatabase
{
    private static readonly NpgsqlNullNameTranslator Names=new();
    public static void Configure(DbContextOptionsBuilder options,string connection) => options.UseNpgsql(connection,pg=>
        pg.MapEnum<RequestStatus>("request_status",nameTranslator:Names).MapEnum<LessonKind>("lesson_kind",nameTranslator:Names)
        .MapEnum<LessonStatus>("lesson_status",nameTranslator:Names).MapEnum<OperationStatus>("operation_status",nameTranslator:Names)
        .MapEnum<ReservationStatus>("reservation_status",nameTranslator:Names).MapEnum<RecurrenceFrequency>("recurrence_frequency",nameTranslator:Names));
    public static DbContextOptionsBuilder<SchedulingDbContext> UseScheduling(this DbContextOptionsBuilder<SchedulingDbContext> options,string connection)
    {Configure(options,connection);return options;}
}
