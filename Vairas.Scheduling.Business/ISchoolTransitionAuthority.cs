using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

public interface ISchoolTransitionAuthority {Task<SchoolDecision> DecisionAsync(Guid school,Guid operation,CancellationToken ct);}
