extern alias SchoolTests;
extern alias SchedulingApi;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.OpenApi;
using NUnit.Framework;
using Vairas.Scheduling.Business;
using SchoolEnvironment=SchoolTests::Vairas.SchoolManagement.Tests.IntegrationEnvironment;
using SchedulingProgram=SchedulingApi::Program;
namespace Vairas.Scheduling.Tests;

[TestFixture,Category("Integration"),NonParallelizable]
public sealed class FourServiceTests
{
    private SchoolEnvironment env=null!;private PostgresFixture pg=null!;private SchedulingFactory factory=null!;private HttpClient api=null!;
    [OneTimeSetUp]
    public async Task Start()
    {
        pg=new();await pg.Start();env=new();
        env.ConfigureAuth=config=>
        {
            config["Authentication:UserAudiences:3"]="vairas-scheduling";
            config["Authentication:WorkloadClients:school-management:Scopes:3"]="scheduling.school-gate";
            var scopes=new[]{"identity.validate","school.scheduling.read","school.coordination.read","school.eligibility.read","school.participants.read"};
            for(var i=0;i<scopes.Length;i++)config[$"Authentication:WorkloadClients:scheduling:Scopes:{i}"]=scopes[i];
            config["Authentication:ScopeAudiences:school.scheduling.read:0"]="vairas-school-management-internal";
            config["Authentication:ScopeAudiences:school.coordination.read:0"]="vairas-school-management-internal";
            config["Authentication:ScopeAudiences:scheduling.school-gate:0"]="vairas-scheduling-internal";
        };
        env.ConfigureSchool=config=>
        {
            config["SchedulingDependency:Enabled"]="true";
            config["Dependencies:Scope"]="identity.validate profiles.status.read profiles.binding.read scheduling.school-gate";
            config["Authentication:WorkloadClients:scheduling:2"]="school.scheduling.read";
            config["Authentication:WorkloadClients:scheduling:3"]="school.coordination.read";
        };
        await env.StartAsync();
        var settings=new Dictionary<string,string?>
        {
            ["ConnectionStrings:Scheduling"]=pg.Runtime,["ConnectionStrings:ReservationAuthority"]=pg.Authority,
            ["Scheduling:SyntheticMode"]="true",["Dependencies:ClientSecret"]=env.Secret,["Dependencies:Scope"]="identity.validate school.scheduling.read school.coordination.read",
            ["Authentication:WorkloadClients:school-management:0"]="scheduling.school-gate"
        };
        factory=new(settings,env);api=factory.CreateClient(new(){BaseAddress=new("https://localhost:7300")});env.Scheduling=api;
    }
    [OneTimeTearDown]
    public async Task Stop(){api?.Dispose();if(factory is not null)await factory.DisposeAsync();if(env is not null)await env.DisposeAsync();if(pg is not null)await pg.DisposeAsync();}
    [Test]
    public async Task Openapi_health_and_anonymous_denial_are_real_HTTP_pipeline_results()
    {
        Assert.That((await api.GetAsync("/health/ready")).StatusCode,Is.EqualTo(HttpStatusCode.OK));
        using var response=await api.GetAsync("/openapi/v1.json");var text=await response.Content.ReadAsStringAsync();Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK),text);
        var parsed=OpenApiDocument.Parse(text,"json");Assert.That(parsed.Diagnostic!.Errors,Is.Empty);Assert.That(parsed.Document!.Validate(ValidationRuleSet.GetDefaultRuleSet()).ToArray(),Is.Empty);
        await File.WriteAllTextAsync(Path.Combine(PostgresFixture.Root,"api/scheduling.openapi.json"),text);
        using var denied=await api.PostAsJsonAsync("/api/v1/practical-lessons",new{});Assert.That(denied.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
        using var internalDenied=await SchoolEnvironment.Send(api,HttpMethod.Post,"/internal/v1/vehicle-blocks",env.Administrator.Tokens.AccessToken,new{});Assert.That(internalDenied.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    [Test]
    public async Task Real_Auth_UM_School_Scheduling_activate_first_staff_membership_and_student_enrollment()
    {
        var school=await env.SeedSchool();
        using var issued=await env.Request(HttpMethod.Post,$"/api/v1/schools/{school}/invitations",env.Administrator,new{targetUserId=env.Target.UserId,role="Teacher"},Guid.NewGuid());
        Assert.That(issued.StatusCode,Is.EqualTo(HttpStatusCode.Created),await issued.Content.ReadAsStringAsync());
        var invitation=(await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("invitationId").GetGuid();
        using var role=await SchoolEnvironment.Send(env.Users,HttpMethod.Post,"/api/v1/users/me/teacher-profile",env.Target.Tokens.AccessToken,new{schoolId=school,invitationId=invitation},Guid.NewGuid());
        Assert.That(role.StatusCode,Is.EqualTo(HttpStatusCode.Created),await role.Content.ReadAsStringAsync());
        var operation=Guid.NewGuid();
        using var accepted=await env.Request(HttpMethod.Post,$"/api/v1/schools/{school}/invitations/{invitation}/accept",env.Target,new{expectedRevision=1},operation);
        Assert.That(accepted.StatusCode,Is.EqualTo(HttpStatusCode.OK),await accepted.Content.ReadAsStringAsync());
        Assert.That((await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationStatus").GetString(),Is.EqualTo("Committed"));
        using var replay=await env.Request(HttpMethod.Post,$"/api/v1/schools/{school}/invitations/{invitation}/accept",env.Target,new{expectedRevision=1},operation);
        Assert.That(replay.StatusCode,Is.EqualTo(HttpStatusCode.OK),await replay.Content.ReadAsStringAsync());
        using var student=await SchoolEnvironment.Send(env.Users,HttpMethod.Post,"/api/v1/users/me/student-profile",env.Outsider.Tokens.AccessToken,new{},Guid.NewGuid());
        Assert.That(student.StatusCode,Is.EqualTo(HttpStatusCode.Created),await student.Content.ReadAsStringAsync());
        using var profile=await SchoolEnvironment.Send(env.Users,HttpMethod.Get,"/api/v1/users/me",env.Outsider.Tokens.AccessToken);
        var studentId=(await profile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("studentId").GetGuid();
        using var enrollment=await env.Request(HttpMethod.Post,$"/api/v1/schools/{school}/enrollments",env.Administrator,new{studentId,licenseCategory="B"},Guid.NewGuid());
        Assert.That(enrollment.StatusCode,Is.EqualTo(HttpStatusCode.OK),await enrollment.Content.ReadAsStringAsync());
        var enrollmentData=await enrollment.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(enrollmentData.GetProperty("state").GetString(),Is.EqualTo("Committed"));
        using var self=await SchoolEnvironment.Send(api,HttpMethod.Get,$"/api/v1/users/me/lessons?schoolId={school}&from=2026-11-25T00:00:00Z&to=2026-11-26T00:00:00Z",env.Outsider.Tokens.AccessToken);
        Assert.That(self.StatusCode,Is.EqualTo(HttpStatusCode.OK),await self.Content.ReadAsStringAsync());

        using var branchResponse=await env.Request(HttpMethod.Post,$"/api/v1/schools/{school}/branches",env.Administrator,new{name="Synthetic branch"},Guid.NewGuid());
        Assert.That(branchResponse.StatusCode,Is.EqualTo(HttpStatusCode.Created),await branchResponse.Content.ReadAsStringAsync());
        var branch=(await branchResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("branchId").GetGuid();
        using var groupResponse=await env.Request(HttpMethod.Post,$"/api/v1/schools/{school}/groups",env.Administrator,new{name="Synthetic group",branchId=branch,categoryCode="B"},Guid.NewGuid());
        Assert.That(groupResponse.StatusCode,Is.EqualTo(HttpStatusCode.Created),await groupResponse.Content.ReadAsStringAsync());
        var group=(await groupResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("groupId").GetGuid();
        using var teacherProfile=await SchoolEnvironment.Send(env.Users,HttpMethod.Get,"/api/v1/users/me",env.Target.Tokens.AccessToken);
        var teacher=(await teacherProfile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("teacherId").GetGuid();
        var assignmentOperation=Guid.NewGuid();
        using var assigned=await env.Request(HttpMethod.Post,$"/api/v1/schools/{school}/groups/{group}/teacher-assignments",env.Administrator,new{teacherId=teacher,expectedRevision=1},assignmentOperation);
        Assert.That(assigned.StatusCode,Is.EqualTo(HttpStatusCode.OK),await assigned.Content.ReadAsStringAsync());
        var enrollmentId=enrollmentData.GetProperty("resultId").GetGuid();
        var association=Guid.NewGuid();
        // Retained programme/group evidence is a fixture: the missing Learning provider is outside this HTTP interoperability test.
        await SchoolEnvironment.Sql(env.SchoolOwner,$"""
            INSERT INTO student_group_membership(membership_id,school_id,enrollment_id,group_id,joined_at,association_program_version_id,association_verified_at,revision,last_operation_id)
            VALUES('{association}','{school}','{enrollmentId}','{group}',clock_timestamp(),'{Guid.NewGuid()}',clock_timestamp(),1,'{assignmentOperation}');
            UPDATE training_group SET membership_revision=2,revision=revision+1 WHERE school_id='{school}' AND group_id='{group}';
            INSERT INTO group_roster_revision(group_id,school_id,membership_revision,recorded_at,operation_id) VALUES('{group}','{school}',2,clock_timestamp(),'{assignmentOperation}');
            INSERT INTO group_roster_member(group_id,school_id,membership_revision,membership_id,enrollment_id,student_id,enrollment_revision,enrollment_status,category_code,enrollment_started_at,enrollment_ended_at,joined_at,left_at)
            SELECT '{group}','{school}',2,'{association}',e.enrollment_id,e.student_id,e.revision,e.status,e.category_code,e.started_at,e.ended_at,m.joined_at,m.left_at
            FROM student_enrollment e JOIN student_group_membership m USING(enrollment_id,school_id) WHERE m.membership_id='{association}' AND e.school_id='{school}';
            """);
        var start=new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(10),TimeSpan.Zero);
        using var booking=await SchoolEnvironment.Send(api,HttpMethod.Post,"/api/v1/theory-lessons",env.Administrator.Tokens.AccessToken,
            new{schoolId=school,branchId=branch,teacherId=teacher,category="B",groupId=group,startsAt=start,endsAt=start.AddMinutes(45)},Guid.NewGuid());
        Assert.That(booking.StatusCode,Is.EqualTo(HttpStatusCode.Created),await booking.Content.ReadAsStringAsync());
        var lesson=(await booking.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lessonId").GetGuid();
        using var studentRead=await SchoolEnvironment.Send(api,HttpMethod.Get,$"/api/v1/theory-lessons/{lesson}?schoolId={school}",env.Outsider.Tokens.AccessToken);
        Assert.That(studentRead.StatusCode,Is.EqualTo(HttpStatusCode.OK),await studentRead.Content.ReadAsStringAsync());
        using var cancelled=await SchoolEnvironment.Send(api,HttpMethod.Post,$"/api/v1/theory-lessons/{lesson}/cancellation?schoolId={school}",env.Administrator.Tokens.AccessToken,new{expectedVersion=1,reason="Synthetic cancellation"},Guid.NewGuid());
        Assert.That(cancelled.StatusCode,Is.EqualTo(HttpStatusCode.OK),await cancelled.Content.ReadAsStringAsync());
    }
    [Test]
    public async Task Live_membership_admission_precedes_global_availability_and_missing_Vehicle_fails_closed()
    {
        var school=await env.SeedSchool();var token=env.Administrator.Tokens.AccessToken;
        using var response=await SchoolEnvironment.Send(api,HttpMethod.Post,"/api/v1/practical-lessons",token,
            new{schoolId=school,branchId=Guid.NewGuid(),teacherId=Guid.NewGuid(),category="B",startsAt="2030-01-07T08:00:00Z",endsAt="2030-01-07T08:45:00Z",enrollmentId=Guid.NewGuid(),studentId=Guid.NewGuid(),vehicleId=Guid.NewGuid()},Guid.NewGuid());
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.Forbidden),await response.Content.ReadAsStringAsync());
        // Unknown resources are rejected by the real School/UM boundary before any reservation is attempted.
        Assert.That(await PostgresFixture.Scalar(pg.Owner,$"SELECT count(*) FROM lesson WHERE school_id='{school}'"),Is.EqualTo(0L));
    }
    private sealed class SchedulingFactory(Dictionary<string,string?> config,SchoolEnvironment env):WebApplicationFactory<SchedulingProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.UseContentRoot(Path.Combine(PostgresFixture.Root,"Vairas.Scheduling"));builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(config));
            builder.ConfigureServices(services=>
            {
                services.AddHttpClient("dependencies").ConfigurePrimaryHttpMessageHandler(()=>new Bridge(env));
                foreach(var name in new[]{"User","Workload"})services.PostConfigure<JwtBearerOptions>(name,o=>
                {o.Authority="https://localhost:7100";o.MetadataAddress="https://localhost:7100/.well-known/openid-configuration";o.ConfigurationManager=new ConfigurationManager<OpenIdConnectConfiguration>(o.MetadataAddress,new OpenIdConnectConfigurationRetriever(),new HttpDocumentRetriever(env.Auth){RequireHttps=true});});
            });
        }
    }
    private sealed class Bridge(SchoolEnvironment env):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken ct)
        {
            var client=message.RequestUri!.Port switch{7100=>env.Auth,7001=>env.Users,7200=>env.Client,_=>throw new HttpRequestException("Unknown test origin.")};
            using var next=new HttpRequestMessage(message.Method,message.RequestUri);
            foreach(var header in message.Headers)next.Headers.TryAddWithoutValidation(header.Key,header.Value);
            if(message.Content is not null){next.Content=new ByteArrayContent(await message.Content.ReadAsByteArrayAsync(ct));foreach(var header in message.Content.Headers)next.Content.Headers.TryAddWithoutValidation(header.Key,header.Value);}
            return await client.SendAsync(next,ct);
        }
    }
}
