using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Vairas.Scheduling.Persistance.Migrations;
[DbContext(typeof(SchedulingDbContext)),Migration("20261122000000_ActualInterval")]
public sealed class ActualInterval:Migration
{
    protected override void Up(MigrationBuilder builder)=>builder.Sql(CanonicalSql.Actual());
    protected override void Down(MigrationBuilder builder)=>throw new NotSupportedException("Physical source evidence requires a reviewed recovery procedure.");
}
