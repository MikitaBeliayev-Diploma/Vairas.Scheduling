namespace Vairas.Scheduling.Business;
/// <summary>A bounded page of authorized school scheduling requests.</summary>
public sealed record RequestPage(RequestListItem[] Items, int? NextOffset);
