using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public interface IOutboxStore
{
    Task<OutboxMessage?> ClaimAsync(CancellationToken ct);
    Task FinishAsync(Guid id, Guid lease, bool sent, CancellationToken ct);
    Task<OutboxStats> StatsAsync(CancellationToken ct);
}
