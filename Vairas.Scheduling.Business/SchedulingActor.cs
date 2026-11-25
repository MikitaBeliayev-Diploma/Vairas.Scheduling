using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Authenticated current user and independently validated delegation.</summary>
public sealed record SchedulingActor(Guid IdentityId, Guid UserId, Guid? StudentId, Guid? TeacherId, string Channel, string? Workload, DateTimeOffset ValidUntil, Guid? ConfirmationId = null, Vairas.ConfirmedActions.LessonAction? ConfirmedAction = null);
