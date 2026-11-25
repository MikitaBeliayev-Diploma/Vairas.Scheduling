namespace Vairas.Scheduling.Business;
/// <summary>Discoverable latest school request revision and retained planning inputs.</summary>
public sealed record RequestListItem(RequestResponse Request, DraftInputs Inputs, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, bool HasProposal);
