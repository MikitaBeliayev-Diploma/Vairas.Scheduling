extern alias SchedulingContracts;
using System.Security.Claims;
using System.Text.Json;
using NUnit.Framework;
using LessonAction = SchedulingContracts::Vairas.ConfirmedActions.LessonAction;
using LessonActionBinding = SchedulingContracts::Vairas.ConfirmedActions.LessonActionBinding;
using LessonActions = SchedulingContracts::Vairas.ConfirmedActions.LessonActions;
using Vairas.Scheduling.Business;
namespace Vairas.Scheduling.Tests;

/// <summary>
/// Unit coverage for the shared ConfirmedLessonActionsV1 contract compiled into Scheduling.Business (a byte-identical copy
/// checked by McpServer/scripts/sync-policy.py) and for the owner's own guard before any confirmed command executes.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class LessonActionPolicyTests
{
    private const string Chain = "{\"sub\":\"mcp-server\",\"act\":{\"sub\":\"chat-backend\"}}";
    private static readonly Guid School = Guid.NewGuid();
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { schoolId = School, startsAt = "2030-01-12T09:00:00Z", minutes = 45 });
    private static readonly JsonElement Sources = JsonSerializer.SerializeToElement(new { schoolRevision = 3, lessonVersion = 1 });
    private static string Exp(int minutes = 1) => DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds().ToString();

    private static LessonActionBinding Binding(string kind = "Booking") => new(School, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        kind, new string('a', 64), LessonActions.Digest(kind, Arguments, Sources));

    private static ClaimsPrincipal Execution(LessonActionBinding b, string actor = Chain, params (string Type, string Value)[] replace)
    {
        var claims = new Dictionary<string, string>
        {
            ["vairas_delegation"] = LessonActions.Profile, [LessonActions.Claim] = JsonSerializer.Serialize(b, LessonActions.Json), ["act"] = actor,
            ["sub"] = b.IdentityId.ToString("D"), ["sid"] = b.SessionId.ToString("D"), ["school_id"] = b.SchoolId.ToString("D"),
            ["scope"] = LessonActions.Scope(b.Kind), ["exp"] = Exp()
        };
        foreach (var (type, value) in replace) claims[type] = value;
        return new(new ClaimsIdentity(claims.Select(c => new Claim(c.Key, c.Value)), "synthetic"));
    }

    [Test]
    public void Kinds_map_to_exactly_two_confirmed_tools_and_scopes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LessonActions.Tool("Booking"), Is.EqualTo("create_lesson_booking"));
            Assert.That(LessonActions.Tool("Rescheduling"), Is.EqualTo("reschedule_lesson_booking"));
            Assert.That(LessonActions.Scope("Booking"), Is.EqualTo("mcp.create_lesson_booking"));
            Assert.Throws<InvalidOperationException>(() => LessonActions.Tool("Cancellation"));
            Assert.That(LessonActions.InputSchema.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(LessonActions.OutputSchema.GetProperty("required").GetArrayLength(), Is.EqualTo(6));
            Assert.That(LessonActions.IsExecution(Execution(Binding())), Is.True);
            Assert.That(LessonActions.IsExecution(new ClaimsPrincipal(new ClaimsIdentity())), Is.False);
        });
    }

    [Test]
    public void Command_hash_is_order_independent_for_objects_but_not_arrays()
    {
        using var a = JsonDocument.Parse("{\"b\":[1,true,null,\"x\"],\"a\":{\"d\":false,\"c\":-2}}");
        using var b = JsonDocument.Parse("{\"a\":{\"c\":-2,\"d\":false},\"b\":[1,true,null,\"x\"]}");
        using var c = JsonDocument.Parse("{\"a\":{\"c\":-2,\"d\":false},\"b\":[\"x\",null,true,1]}");
        Assert.Multiple(() =>
        {
            Assert.That(LessonActions.Hash(a.RootElement), Is.EqualTo(LessonActions.Hash(b.RootElement)));
            Assert.That(LessonActions.Hash(c.RootElement), Is.Not.EqualTo(LessonActions.Hash(b.RootElement)));
            Assert.That(LessonActions.Hash(a.RootElement), Does.Match("^[0-9a-f]{64}$"));
            Assert.That(LessonActions.Digest("Booking", Arguments, Sources), Is.Not.EqualTo(LessonActions.Digest("Rescheduling", Arguments, Sources)));
        });
    }

    [TestCase("{\"a\":1,\"a\":2}")][TestCase("{\"n\":1.5}")][TestCase("{\"n\":9007199254740992}")][TestCase("{\"n\":-9007199254740992}")]
    public void Command_hash_rejects_ambiguous_or_unsafe_values(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<InvalidOperationException>(() => LessonActions.Hash(document.RootElement));
    }

    [TestCase("valid", true)][TestCase("empty-id", false)][TestCase("upper-digest", false)][TestCase("short-digest", false)]
    [TestCase("command", false)][TestCase("arguments", false)][TestCase("sources", false)][TestCase("kind", false)]
    public void Validate_accepts_only_a_self_consistent_binding(string variant, bool valid)
    {
        var b = Binding(); var scalar = JsonSerializer.SerializeToElement(1);
        var action = variant switch
        {
            "empty-id" => new LessonAction(b with { OperationId = Guid.Empty }, Arguments, Sources),
            "upper-digest" => new LessonAction(b with { ActionDigest = new string('A', 64) }, Arguments, Sources),
            "short-digest" => new LessonAction(b with { ActionDigest = "abc" }, Arguments, Sources),
            "command" => new LessonAction(b with { CommandDigest = new string('b', 64) }, Arguments, Sources),
            "arguments" => new LessonAction(b with { CommandDigest = LessonActions.Digest("Booking", scalar, Sources) }, scalar, Sources),
            "sources" => new LessonAction(b with { CommandDigest = LessonActions.Digest("Booking", Arguments, scalar) }, Arguments, scalar),
            "kind" => new LessonAction(b with { Kind = "Cancellation", CommandDigest = LessonActions.Digest("Cancellation", Arguments, Sources) }, Arguments, Sources),
            _ => new LessonAction(b, Arguments, Sources)
        };
        if (valid) Assert.DoesNotThrow(() => LessonActions.Validate(action));
        else Assert.Throws<InvalidOperationException>(() => LessonActions.Validate(action));
    }

    [Test]
    public void Parse_binds_execution_to_actor_chain_depth_identity_school_scope_and_expiry()
    {
        var b = Binding();
        Assert.Multiple(() =>
        {
            Assert.That(LessonActions.Parse(Execution(b), 2), Is.EqualTo(b));
            Assert.That(LessonActions.Parse(Execution(b, "{\"sub\":\"chat-backend\"}"), 1), Is.EqualTo(b));
            Assert.Throws<InvalidOperationException>(() => LessonActions.Parse(Execution(b), 1));
            Assert.Throws<InvalidOperationException>(() => LessonActions.Parse(Execution(b, "{\"sub\":\"mcp-server\",\"act\":{\"sub\":\"chat-backend\"},\"x\":1}"), 2));
            Assert.Throws<InvalidOperationException>(() => LessonActions.Parse(Execution(b with { ActionDigest = "x" }), 2));
        });
        foreach (var change in new (string, string)[] { ("vairas_delegation", "ReadPreviewV1"), ("sub", Guid.NewGuid().ToString()), ("sid", Guid.NewGuid().ToString()),
                     ("school_id", Guid.NewGuid().ToString()), ("scope", "mcp.reschedule_lesson_booking"), ("exp", Exp(-1)), ("exp", "soon") })
            Assert.Throws<InvalidOperationException>(() => LessonActions.Parse(Execution(b, replace: change), 2), change.Item1);
    }

    [Test]
    public void Duplicate_action_claims_are_rejected()
    {
        var principal = Execution(Binding());
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(LessonActions.Claim, JsonSerializer.Serialize(Binding(), LessonActions.Json)));
        Assert.Throws<InvalidOperationException>(() => LessonActions.Parse(principal, 2));
    }

    [Test]
    public void Owner_refuses_confirmed_commands_while_the_feature_is_disabled()
    {
        var w = new TestWorld();
        var error = Business.ThrowsBusinessAsync(async () => await w.Service.ExecuteConfirmedAsync(new(Binding(), Arguments, Sources), w.Actor, default));
        Assert.That(error!.Code, Is.EqualTo("LESSON_ACTIONS_DISABLED"));
    }

    [TestCase("web-channel")][TestCase("other-workload")][TestCase("other-preview")][TestCase("other-identity")][TestCase("other-school")]
    public void Owner_denies_confirmed_commands_not_bound_to_the_mcp_actor_and_preview(string variant)
    {
        var w = new TestWorld();
        var service = new SchedulingService(w.Store, w, w, w.Clock, new() { SyntheticMode = true, ConfirmedLessonActionsEnabled = true });
        var b = Binding() with { IdentityId = w.Actor.IdentityId, UserId = w.Actor.UserId };
        var arguments = Arguments;
        if (variant == "other-school")
        {
            arguments = JsonSerializer.SerializeToElement(new { schoolId = Guid.NewGuid(), startsAt = "2030-01-12T09:00:00Z", minutes = 45 });
            b = b with { CommandDigest = LessonActions.Digest("Booking", arguments, Sources) };
        }
        var actor = w.Actor with { Channel = "MCP", Workload = "mcp-server", ConfirmationId = b.PreviewId };
        actor = variant switch
        {
            "web-channel" => actor with { Channel = "Web" }, "other-workload" => actor with { Workload = "chat-backend" },
            "other-preview" => actor with { ConfirmationId = Guid.NewGuid() }, "other-identity" => actor with { IdentityId = Guid.NewGuid() }, _ => actor
        };
        var error = Business.ThrowsBusinessAsync(async () => await service.ExecuteConfirmedAsync(new(b, arguments, Sources), actor, default));
        Assert.That(error.HttpStatus(), Is.EqualTo(403));
    }
}
