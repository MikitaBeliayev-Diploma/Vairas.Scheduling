using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Vairas.Scheduling.Persistance;
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<SchedulingDbContext>().UseScheduling(
        Environment.GetEnvironmentVariable("VAIRAS_MIGRATION_CONNECTION")??"Host=localhost;Database=vairas_scheduling;Username=v_scheduling_migration").Options);
}
