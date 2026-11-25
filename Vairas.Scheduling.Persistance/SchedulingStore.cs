using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Persistance;

public sealed class SchedulingStore(IDbContextFactory<SchedulingDbContext> factory) : ISchedulingStore
{
    public Task<ISchedulingSession> OpenAsync(Guid school,bool write,CancellationToken ct)=>DatabaseExceptionTranslator.GuardAsync(()=>OpenCoreAsync(school,write,ct));
    private async Task<ISchedulingSession> OpenCoreAsync(Guid school,bool write,CancellationToken ct)
    {
        var db=await factory.CreateDbContextAsync(ct);
        try
        {
            if(!write)db.ChangeTracker.QueryTrackingBehavior=QueryTrackingBehavior.NoTracking;
            var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('vairas.school_id',{school.ToString("D")},true)",ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'",ct);
            if(write)await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(760125151)",ct);
            return new StoreSession(db,transaction);
        }
        catch{await db.DisposeAsync();throw;}
    }
    private sealed class StoreSession(SchedulingDbContext db,IDbContextTransaction tx) : ISchedulingSession
    {
        public Task<T?> FindAsync<T>(Expression<Func<T,bool>> predicate,CancellationToken ct) where T:class=>DatabaseExceptionTranslator.GuardAsync(()=>db.Set<T>().SingleOrDefaultAsync(predicate,ct));
        public async Task<IReadOnlyList<T>> ListAsync<T>(Expression<Func<T,bool>> predicate,CancellationToken ct) where T:class=>await DatabaseExceptionTranslator.GuardAsync<IReadOnlyList<T>>(async()=>await db.Set<T>().Where(predicate).ToArrayAsync(ct));
        public void Add<T>(T row) where T:class=>db.Add(row);
        public Task FlushAsync(CancellationToken ct)=>DatabaseExceptionTranslator.GuardAsync(()=>db.SaveChangesAsync(ct));
        public Task CommitAsync(CancellationToken ct)=>DatabaseExceptionTranslator.GuardAsync(async()=>{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);});
        public Task GuardVehicleAsync(Guid school,Guid vehicle,CancellationToken ct)=>DatabaseExceptionTranslator.GuardAsync(()=>GuardVehicleCoreAsync(school,vehicle,ct));
        private async Task GuardVehicleCoreAsync(Guid school,Guid vehicle,CancellationToken ct)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO vehicle_guard(vehicle_id,school_id) VALUES({vehicle},{school}) ON CONFLICT DO NOTHING",ct);
            if(await db.Set<VehicleGuard>().FromSqlInterpolated($"SELECT * FROM vehicle_guard WHERE vehicle_id={vehicle} AND school_id={school} FOR UPDATE").SingleOrDefaultAsync(ct) is null)
                throw SchedulingErrors.Conflict("RESOURCE_UNAVAILABLE");
        }
        public async ValueTask DisposeAsync(){await tx.DisposeAsync();await db.DisposeAsync();}
    }
}
