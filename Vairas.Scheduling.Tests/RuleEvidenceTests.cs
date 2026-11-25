using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Tests;
[TestFixture,Category("Unit")]
public sealed class RuleEvidenceTests
{
    [Test]
    public async Task Operation_evidence_excludes_rule_versions_for_an_unrelated_learner()
    {
        var w=new TestWorld();var s=w.Service;
        var rule=await s.PublishRuleAsync(new(w.School,RuleCodes.SchoolMaxLessonsPerDay,1,0,"Lessons","Student",DateTimeOffset.Parse("2029-01-01T00:00:00Z"),"Synthetic policy",ScopeResourceId:Guid.NewGuid(),Reason:"Other learner scope"),Guid.NewGuid(),w.Actor,default);
        Assert.That((await s.RuleAsync(w.School,rule.RuleVersionId,w.Actor,default)).Revision,Is.EqualTo(1));
        var operation=Guid.NewGuid();await s.CreateAsync(w.Practical(),LessonKind.Practical,operation,w.Actor,default);
        var evidence=w.Store.Rows.OfType<SchedulingOperationRule>().Where(x=>x.OperationId==operation).ToArray();
        Assert.That(evidence.Any(x=>x.RuleVersionId==rule.RuleVersionId),Is.False);Assert.That(evidence.Length,Is.EqualTo(RuleCodes.MandatoryLegal.Count));
    }
}
