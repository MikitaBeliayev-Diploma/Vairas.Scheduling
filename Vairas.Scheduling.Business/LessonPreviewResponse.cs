using System.Security.Cryptography;
using System.Text.Json;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;

namespace Vairas.Scheduling.Business;

/// <summary>Exact advisory owner basis. No lesson, operation, claim or event is written.</summary>
public sealed record LessonPreviewResponse(string PreviewVersion, string Kind, JsonElement Arguments, JsonElement SourceRevisions, JsonElement Display);
