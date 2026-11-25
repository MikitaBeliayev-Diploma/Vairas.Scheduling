using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Physical completion facts, distinct from credited teaching minutes.</summary>
public sealed record CompleteLessonRequest(int ExpectedVersion, DateTimeOffset ActualStart, DateTimeOffset ActualEnd);
