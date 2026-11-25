using System.Linq.Expressions;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;

internal sealed class MemoryStore:ISchedulingStore
{
    internal List<object> Rows=[];
    public Task<ISchedulingSession> OpenAsync(Guid school,bool write,CancellationToken ct)=>Task.FromResult<ISchedulingSession>(new MemorySession(this,school,write));
    private sealed class MemorySession:ISchedulingSession
    {
        private readonly MemoryStore owner;private readonly Guid school;private readonly bool write;private readonly List<object> rows;
        public MemorySession(MemoryStore owner,Guid school,bool write){this.owner=owner;this.school=school;this.write=write;rows=owner.Rows.Select(x=>System.Text.Json.JsonSerializer.Deserialize(Wire.Save(x),x.GetType(),Wire.Json)!).ToList();}
        private IEnumerable<T> All<T>()where T:class=>rows.OfType<T>().Where(x=>
        {var p=typeof(T).GetProperty("SchoolId");return p is null || p.GetValue(x) is null || (Guid)p.GetValue(x)! == school;});
        public Task<T?> FindAsync<T>(Expression<Func<T,bool>> predicate,CancellationToken ct)where T:class=>Task.FromResult(All<T>().SingleOrDefault(predicate.Compile()));
        public Task<IReadOnlyList<T>> ListAsync<T>(Expression<Func<T,bool>> predicate,CancellationToken ct)where T:class=>Task.FromResult<IReadOnlyList<T>>(All<T>().Where(predicate.Compile()).ToArray());
        public void Add<T>(T row)where T:class=>rows.Add(row);
        public Task FlushAsync(CancellationToken ct)=>Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct){if(write)owner.Rows=rows;return Task.CompletedTask;}
        public Task GuardVehicleAsync(Guid school,Guid vehicle,CancellationToken ct)=>Task.CompletedTask;
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
