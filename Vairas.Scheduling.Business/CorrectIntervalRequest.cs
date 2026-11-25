using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Audited replacement of physical completion facts.</summary>
public sealed record CorrectIntervalRequest(int ExpectedVersion, DateTimeOffset ActualStart, DateTimeOffset ActualEnd, string Reason);
