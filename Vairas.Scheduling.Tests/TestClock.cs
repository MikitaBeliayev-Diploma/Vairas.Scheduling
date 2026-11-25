using System.Linq.Expressions;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;

internal sealed class TestClock:TimeProvider
{public DateTimeOffset Now {get;set;}=DateTimeOffset.Parse("2030-01-10T14:00:00Z");public override DateTimeOffset GetUtcNow()=>Now;}
