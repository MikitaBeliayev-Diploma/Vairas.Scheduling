using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Vairas.Scheduling.Persistance.Migrations;
[DbContext(typeof(SchedulingDbContext)),Migration("20261115192213_InitialScheduling")]
public sealed class InitialScheduling:Migration
{
    protected override void Up(MigrationBuilder builder)=>builder.Sql(CanonicalSql.Initial());
    protected override void Down(MigrationBuilder builder)=>throw new NotSupportedException("Restore a reviewed backup; retained scheduling evidence cannot be dropped automatically.");
}
