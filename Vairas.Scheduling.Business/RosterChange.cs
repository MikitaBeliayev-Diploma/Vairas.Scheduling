using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Future lesson's complete replacement roster under an exact School membership version.</summary>
public sealed record RosterChange(Guid LessonId,int ExpectedRevision,long MembershipRevision,Participant[] Participants);
