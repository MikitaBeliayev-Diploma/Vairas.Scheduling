using Microsoft.EntityFrameworkCore;
using Vairas.Scheduling.Persistance;

namespace Vairas.Scheduling.Integrations;

internal sealed class OutboxContextFactory(string connection) : IDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SchedulingDbContext>().UseScheduling(connection).Options);
}
