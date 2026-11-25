using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public interface ISchedulingStore
{
    Task<ISchedulingSession> OpenAsync(Guid school, bool write, CancellationToken ct);
}
