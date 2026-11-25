using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public interface IReservationAuthority
{
    Task<IReadOnlyList<Session>> ReadAsync(Guid school, IReadOnlyCollection<Guid> students, DateTimeOffset from, DateTimeOffset to, CancellationToken ct, IReadOnlyCollection<Guid>? instructors = null, IReadOnlyCollection<Guid>? vehicles = null);
}
