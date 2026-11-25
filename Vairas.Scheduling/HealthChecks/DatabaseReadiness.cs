using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Vairas.Scheduling.Persistance;
namespace Vairas.Scheduling.HealthChecks;
public sealed class DatabaseReadiness(IDbContextFactory<SchedulingDbContext> factory):IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken ct=default)
    {
        try
        {
            await using var db=await factory.CreateDbContextAsync(ct);
            var valid=await db.Database.SqlQueryRaw<bool>("""
                SELECT (NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb AND NOT r.rolcreaterole
                  AND NOT has_schema_privilege('public','CREATE') AND NOT has_database_privilege(current_database(),'CREATE')
                  AND NOT has_database_privilege(current_database(),'TEMP')
                  AND NOT EXISTS(SELECT 1 FROM pg_tables t JOIN pg_roles o ON o.rolname=t.tableowner WHERE t.schemaname='public' AND pg_has_role(current_user,o.oid,'MEMBER'))
                  AND (SELECT count(*) FROM pg_tables WHERE schemaname='public' AND rowsecurity AND tablename IN ('lesson','lesson_participant','reservation_claim','scheduling_operation','school_barrier'))=5
                  AND to_regclass('public.outbox_message') IS NOT NULL AND to_regclass('public.lesson_review') IS NOT NULL) AS "Value"
                FROM pg_roles r WHERE rolname=current_user
                """).SingleAsync(ct);
            return valid?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("Database schema, isolation or runtime privileges are invalid.");
        }
        catch{return HealthCheckResult.Unhealthy("Database is unavailable.");}
    }
}
