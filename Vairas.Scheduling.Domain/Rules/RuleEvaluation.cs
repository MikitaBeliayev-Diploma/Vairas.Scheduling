using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vairas.Scheduling.Domain.Rules;

public sealed record RuleEvaluation(IReadOnlyList<RuleProblem> Problems,IReadOnlyList<RuleVersion>? AppliedRules=null)
{
    public bool CanCommit => Problems.All(p => p.Severity != "Hard");
}
