using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

public interface ISchedulingSession : IAsyncDisposable
{
    Task<T?> FindAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct) where T : class;
    Task<IReadOnlyList<T>> ListAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct) where T : class;
    void Add<T>(T row) where T : class;
    Task FlushAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task GuardVehicleAsync(Guid school, Guid vehicle, CancellationToken ct);
}
