using Microsoft.EntityFrameworkCore;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Persistance;

public sealed class OutboxStore(IDbContextFactory<SchedulingDbContext> factory) : IOutboxStore
{
    public async Task<OutboxStats> StatsAsync(CancellationToken ct)
    {
        await using var db=await factory.CreateDbContextAsync(ct);
        return await db.Database.SqlQueryRaw<OutboxStats>("""
            SELECT count(*) AS "Pending",COALESCE(GREATEST(0,EXTRACT(EPOCH FROM clock_timestamp()-min(occurred_at))),0)::double precision AS "OldestAgeSeconds"
            FROM outbox_message WHERE published_at IS NULL
            """).SingleAsync(ct);
    }
    public async Task<OutboxMessage?> ClaimAsync(CancellationToken ct)
    {
        await using var db=await factory.CreateDbContextAsync(ct);
        var lease=Guid.NewGuid();
        return await db.Set<OutboxMessage>().FromSqlInterpolated($"""
            UPDATE outbox_message SET lease_id={lease}, lease_until=clock_timestamp()+interval '1 minute',attempts=attempts+1
            WHERE id=(SELECT id FROM outbox_message WHERE published_at IS NULL AND available_at<=clock_timestamp()
            AND (lease_until IS NULL OR lease_until<clock_timestamp()) ORDER BY occurred_at,id FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING *
            """).AsNoTracking().ToArrayAsync(ct) is var rows ? rows.SingleOrDefault() : null;
    }
    public async Task FinishAsync(Guid id,Guid lease,bool sent,CancellationToken ct)
    {
        await using var db=await factory.CreateDbContextAsync(ct);
        if(sent)await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_message SET published_at=clock_timestamp(),lease_id=NULL,lease_until=NULL WHERE id={id} AND lease_id={lease}",ct);
        else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_message SET available_at=clock_timestamp()+make_interval(secs => LEAST(300,(1 << LEAST(attempts,8)))),lease_id=NULL,lease_until=NULL WHERE id={id} AND lease_id={lease}",ct);
    }
}
