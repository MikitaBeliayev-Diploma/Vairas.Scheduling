using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Persistance;
namespace Vairas.Scheduling.Tests;

internal sealed class PostgresFixture:IAsyncDisposable
{
    private PostgreSqlContainer? container;private string admin="";private string dbName="";
    private readonly string suffix=Guid.NewGuid().ToString("N");
    public string Owner="",Runtime="",Authority="",Worker="";
    public static string Root=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
    public async Task Start()
    {
        admin=Environment.GetEnvironmentVariable("VAIRAS_TEST_ADMIN_CONNECTION")??"";
        if(admin.Length==0){container=new PostgreSqlBuilder("postgres:17-bookworm").Build();await container.StartAsync();admin=container.GetConnectionString();}
        dbName="scheduling_"+suffix;var roles=Roles;
        await Sql(admin,string.Join("\n",roles.Values.Select(x=>$"CREATE ROLE {x} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;")));
        await Sql(admin,$"CREATE DATABASE {dbName} OWNER {roles["v_scheduling_migration"]}");
        var baseConnection=new NpgsqlConnectionStringBuilder(admin){Database=dbName};
        // SET ROLE avoids inventing persistent role passwords in the externally supplied disposable administrator cluster.
        // Actual role connections are provisioned with unique synthetic test-only passwords.
        var secret=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        foreach(var role in roles.Values)await Sql(admin,$"ALTER ROLE {role} PASSWORD '{secret}'");
        Owner=new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString){Username=roles["v_scheduling_migration"],Password=secret}.ConnectionString;
        Runtime=new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString){Username=roles["v_scheduling_runtime"],Password=secret}.ConnectionString;
        Authority=new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString){Username=roles["v_scheduling_reservation_authority"],Password=secret}.ConnectionString;
        Worker=new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString){Username=roles["v_scheduling_outbox"],Password=secret}.ConnectionString;
        await Sql(baseConnection.ConnectionString,$"ALTER SCHEMA public OWNER TO {roles["v_scheduling_migration"]};REVOKE ALL ON SCHEMA public FROM PUBLIC;");
        await using(var db=OwnerDb()){await db.Database.MigrateAsync();await db.Database.OpenConnectionAsync();await ((NpgsqlConnection)db.Database.GetDbConnection()).ReloadTypesAsync();}
        foreach(var file in new[]{"database-roles.sql","reliability-grants.sql","review-grants.sql"})
        {var script=await File.ReadAllTextAsync(Path.Combine(Root,"database",file));foreach(var (from,to) in roles)script=script.Replace(from,to,StringComparison.Ordinal);await Sql(Owner,script);}
        await using(var db=OwnerDb()){foreach(var rule in new TestWorld().Store.Rows.OfType<SchedulingRuleVersion>())db.Add(rule);await db.SaveChangesAsync();}
        TestContext.Out.WriteLine("PostgreSQL "+await Scalar(Owner,"SHOW server_version"));
    }
    private Dictionary<string,string> Roles=>new(){["v_scheduling_reservation_authority"]="sc_authority_"+suffix,["v_scheduling_migration"]="sc_owner_"+suffix,["v_scheduling_runtime"]="sc_runtime_"+suffix,["v_scheduling_outbox"]="sc_outbox_"+suffix};
    public SchedulingDbContext OwnerDb()=>new(new DbContextOptionsBuilder<SchedulingDbContext>().UseScheduling(Owner).Options);
    public SchedulingStore Store=>new(new PooledDbContextFactory<SchedulingDbContext>(new DbContextOptionsBuilder<SchedulingDbContext>().UseScheduling(Runtime).Options));
    public ReservationAuthority Reader=>new(Authority);
    public SchedulingService Service(TestWorld w)=>new(Store,w,Reader,w.Clock,new(){SyntheticMode=true});
    public GateService Gates(TestWorld w)=>new(Store,w,Reader,w.Clock,w);
    public static async Task Sql(string connection,string sql){await using var db=new NpgsqlConnection(connection);await db.OpenAsync();await using var command=new NpgsqlCommand(sql,db);await command.ExecuteNonQueryAsync();}
    public static async Task<object?> Scalar(string connection,string sql){await using var db=new NpgsqlConnection(connection);await db.OpenAsync();await using var command=new NpgsqlCommand(sql,db);return await command.ExecuteScalarAsync();}
    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if(admin.Length>0 && dbName.Length>0){await Sql(admin,$"DROP DATABASE IF EXISTS {dbName} WITH(FORCE)");foreach(var role in Roles.Values)await Sql(admin,$"DROP ROLE IF EXISTS {role}");}
        if(container is not null)await container.DisposeAsync();
    }
}
