using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Vairas.Scheduling.Domain.Rules;

public sealed record RuleProblem(string Code, string Severity, string Message);
