using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Move the slot without changing participant identity.</summary>
public sealed record RescheduleLessonRequest(int ExpectedVersion, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Reason);
