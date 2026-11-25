using Vairas.Scheduling.Domain;

namespace Vairas.Scheduling.Business;

public interface INotificationSourceService { Task<NotificationSourceResponse> ReadAsync(Guid school, Guid lesson, CancellationToken ct); }
