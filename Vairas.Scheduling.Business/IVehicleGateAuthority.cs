using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public interface IVehicleGateAuthority {Task RequireOwnershipAsync(Guid school,Guid vehicle,CancellationToken ct);}
