using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Current immutable request revision and safe validation issues.</summary>
public sealed record RequestResponse(Guid RequestId, int Revision, Guid SchoolId, RequestStatus Status, IReadOnlyList<string> MissingItems);
