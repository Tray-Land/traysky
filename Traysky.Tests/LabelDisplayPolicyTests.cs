using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class LabelDisplayPolicyTests
{
    private const string Subject = "did:plc:subject";
    private const string Labeler = "did:plc:labeler";

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<LabelDisplay> Resolve(
        IEnumerable<LabelInput> labels,
        Func<string, string, LabelText?>? definitions = null,
        Func<string, string, bool?>? ignored = null) =>
        LabelDisplayPolicy.Resolve(
            Subject,
            labels,
            definitions ?? ((_, _) => null),
            ignored ?? ((_, _) => null),
            source => source == Labeler ? "Community Labeler" : string.Empty);

    [TestMethod]
    public void NoLabels_ShowsNothing()
    {
        Assert.AreEqual(0, Resolve([]).Count);
        Assert.AreEqual(0, LabelDisplayPolicy.Resolve(Subject, null, (_, _) => null, (_, _) => null, _ => string.Empty).Count);
    }

    [TestMethod]
    public void LabelerDefinition_NamesTheLabel()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve(
            [new LabelInput(Labeler, "dotnet-dev", false, T0)],
            (source, value) => source == Labeler && value == "dotnet-dev" ? new LabelText(".NET developer", "Writes C#.", LabelSeverity.Inform) : null);

        Assert.AreEqual(1, shown.Count);
        Assert.AreEqual(".NET developer", shown[0].Name);
        Assert.AreEqual("Writes C#.", shown[0].Description);
        Assert.AreEqual("Labeled by Community Labeler", shown[0].SourceLine);
        Assert.IsFalse(shown[0].IsSelfApplied);
    }

    [TestMethod]
    public void GlobalLabel_UsesBuiltInWording()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve([new LabelInput("did:plc:mod", "porn", false, T0)]);

        Assert.AreEqual("Adult content", shown[0].Name);
        Assert.IsTrue(shown[0].IsAlert);
    }

    [TestMethod]
    public void UnknownLabel_FallsBackToHumanizedValue()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve([new LabelInput(Labeler, "dmca-violation", false, T0)]);

        Assert.AreEqual("Dmca violation", shown[0].Name);
        Assert.AreEqual(string.Empty, shown[0].Description);
        Assert.IsFalse(shown[0].HasDescription);
    }

    [TestMethod]
    public void LaterNegation_CancelsTheLabel()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve(
        [
            new LabelInput(Labeler, "spam", false, T0),
            new LabelInput(Labeler, "spam", true, T0.AddDays(1))
        ]);

        Assert.AreEqual(0, shown.Count);
    }

    [TestMethod]
    public void LabelReappliedAfterNegation_Shows()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve(
        [
            new LabelInput(Labeler, "spam", false, T0),
            new LabelInput(Labeler, "spam", true, T0.AddDays(1)),
            new LabelInput(Labeler, "spam", false, T0.AddDays(2))
        ]);

        Assert.AreEqual(1, shown.Count);
    }

    [TestMethod]
    public void NegationFromAnotherLabeler_DoesNotCancel()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve(
        [
            new LabelInput(Labeler, "spam", false, T0),
            new LabelInput("did:plc:other", "spam", true, T0.AddDays(1))
        ]);

        Assert.AreEqual(1, shown.Count);
    }

    [TestMethod]
    public void DuplicateLabels_AreCollapsed()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve(
        [
            new LabelInput(Labeler, "spam", false, T0),
            new LabelInput(Labeler, "spam", false, T0.AddHours(1))
        ]);

        Assert.AreEqual(1, shown.Count);
    }

    [TestMethod]
    public void IgnoredByUser_IsLeftOut_AndExplicitShowBeatsDefaultIgnore()
    {
        LabelText quiet = new("Quiet", string.Empty, LabelSeverity.Inform, IgnoredByDefault: true);
        Func<string, string, LabelText?> definitions = (_, _) => quiet;

        // Never chose: the labeler's default (ignore) applies.
        Assert.AreEqual(0, Resolve([new LabelInput(Labeler, "q", false, T0)], definitions).Count);

        // The user turned it on.
        Assert.AreEqual(1, Resolve([new LabelInput(Labeler, "q", false, T0)], definitions, (_, _) => false).Count);

        // The user turned a normally-shown label off.
        Assert.AreEqual(0, Resolve([new LabelInput(Labeler, "spam", false, T0)], ignored: (_, _) => true).Count);
    }

    [TestMethod]
    public void SelfApplied_IsFlaggedAndHasNoLabelerName()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve([new LabelInput(Subject, "porn", false, T0)]);

        Assert.IsTrue(shown[0].IsSelfApplied);
        Assert.AreEqual("Added by the account itself", shown[0].SourceLine);
    }

    [TestMethod]
    public void UnknownLabeler_HasNoSourceLine()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve([new LabelInput("did:plc:stranger", "spam", false, T0)]);

        Assert.IsFalse(shown[0].HasSourceLine);
    }

    [TestMethod]
    public void Alerts_SortFirst_KeepingOtherwiseStableOrder()
    {
        IReadOnlyList<LabelDisplay> shown = Resolve(
        [
            new LabelInput(Labeler, "fun-fact", false, T0),
            new LabelInput(Labeler, "porn", false, T0),
            new LabelInput(Labeler, "club-member", false, T0)
        ]);

        CollectionAssert.AreEqual(new[] { "porn", "fun-fact", "club-member" }, new List<string>(shown.Select(l => l.Value)));
    }

    [TestMethod]
    public void ParseSeverity_MapsLexiconValues()
    {
        Assert.AreEqual(LabelSeverity.Alert, LabelDisplayPolicy.ParseSeverity("alert"));
        Assert.AreEqual(LabelSeverity.Inform, LabelDisplayPolicy.ParseSeverity("inform"));
        Assert.AreEqual(LabelSeverity.None, LabelDisplayPolicy.ParseSeverity("none"));
        Assert.AreEqual(LabelSeverity.None, LabelDisplayPolicy.ParseSeverity(null));
    }
}
