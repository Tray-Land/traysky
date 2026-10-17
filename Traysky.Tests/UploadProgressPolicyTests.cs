using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class UploadProgressPolicyTests
{
    [TestMethod]
    public void Begin_FromIdleAndTerminalPhases_StartsUploading()
    {
        foreach (UploadPhase phase in new[] { UploadPhase.Idle, UploadPhase.Completed, UploadPhase.Failed, UploadPhase.Cancelled })
            Assert.AreEqual(UploadPhase.Uploading, UploadProgressPolicy.Begin(phase));
    }

    [TestMethod]
    public void Begin_WhileActive_DoesNotRestart()
    {
        Assert.AreEqual(UploadPhase.Processing, UploadProgressPolicy.Begin(UploadPhase.Processing));
        Assert.AreEqual(UploadPhase.Uploading, UploadProgressPolicy.Begin(UploadPhase.Uploading));
    }

    [TestMethod]
    public void BeginProcessing_OnlyFromUploading()
    {
        Assert.AreEqual(UploadPhase.Processing, UploadProgressPolicy.BeginProcessing(UploadPhase.Uploading));
        Assert.AreEqual(UploadPhase.Cancelled, UploadProgressPolicy.BeginProcessing(UploadPhase.Cancelled));
        Assert.AreEqual(UploadPhase.Idle, UploadProgressPolicy.BeginProcessing(UploadPhase.Idle));
    }

    [TestMethod]
    public void Complete_FromUploadingOrProcessing()
    {
        Assert.AreEqual(UploadPhase.Completed, UploadProgressPolicy.Complete(UploadPhase.Uploading));
        Assert.AreEqual(UploadPhase.Completed, UploadProgressPolicy.Complete(UploadPhase.Processing));
    }

    [TestMethod]
    public void Complete_AfterCancel_StaysCancelled()
    {
        Assert.AreEqual(UploadPhase.Cancelled, UploadProgressPolicy.Complete(UploadPhase.Cancelled));
    }

    [TestMethod]
    public void Fail_AfterCancel_StaysCancelled()
    {
        Assert.AreEqual(UploadPhase.Cancelled, UploadProgressPolicy.Fail(UploadPhase.Cancelled));
        Assert.AreEqual(UploadPhase.Failed, UploadProgressPolicy.Fail(UploadPhase.Processing));
    }

    [TestMethod]
    public void Cancel_DuringUploadingAndProcessing()
    {
        Assert.AreEqual(UploadPhase.Cancelled, UploadProgressPolicy.Cancel(UploadPhase.Uploading));
        Assert.AreEqual(UploadPhase.Cancelled, UploadProgressPolicy.Cancel(UploadPhase.Processing));
    }

    [TestMethod]
    public void Cancel_Twice_IsIdempotent()
    {
        UploadPhase once = UploadProgressPolicy.Cancel(UploadPhase.Processing);
        Assert.AreEqual(UploadPhase.Cancelled, UploadProgressPolicy.Cancel(once));
    }

    [TestMethod]
    public void Cancel_WhenNotActive_ChangesNothing()
    {
        Assert.AreEqual(UploadPhase.Idle, UploadProgressPolicy.Cancel(UploadPhase.Idle));
        Assert.AreEqual(UploadPhase.Completed, UploadProgressPolicy.Cancel(UploadPhase.Completed));
        Assert.AreEqual(UploadPhase.Failed, UploadProgressPolicy.Cancel(UploadPhase.Failed));
    }

    [TestMethod]
    public void CanCancel_OnlyWhileActive()
    {
        Assert.IsTrue(UploadProgressPolicy.CanCancel(UploadPhase.Uploading));
        Assert.IsTrue(UploadProgressPolicy.CanCancel(UploadPhase.Processing));
        Assert.IsFalse(UploadProgressPolicy.CanCancel(UploadPhase.Idle));
        Assert.IsFalse(UploadProgressPolicy.CanCancel(UploadPhase.Completed));
        Assert.IsFalse(UploadProgressPolicy.CanCancel(UploadPhase.Failed));
        Assert.IsFalse(UploadProgressPolicy.CanCancel(UploadPhase.Cancelled));
    }

    [TestMethod]
    public void CanEditAttachments_OnlyWhenNotActive()
    {
        Assert.IsFalse(UploadProgressPolicy.CanEditAttachments(UploadPhase.Uploading));
        Assert.IsFalse(UploadProgressPolicy.CanEditAttachments(UploadPhase.Processing));
        Assert.IsTrue(UploadProgressPolicy.CanEditAttachments(UploadPhase.Cancelled));
        Assert.IsTrue(UploadProgressPolicy.CanEditAttachments(UploadPhase.Failed));
        Assert.IsTrue(UploadProgressPolicy.CanEditAttachments(UploadPhase.Idle));
    }

    [TestMethod]
    public void PollBudget_BoundaryAtMaxAttempts()
    {
        Assert.IsFalse(UploadProgressPolicy.IsPollBudgetSpent(0));
        Assert.IsFalse(UploadProgressPolicy.IsPollBudgetSpent(UploadProgressPolicy.MaxPollAttempts - 1));
        Assert.IsTrue(UploadProgressPolicy.IsPollBudgetSpent(UploadProgressPolicy.MaxPollAttempts));
    }

    [TestMethod]
    public void StatusText_PerPhase()
    {
        Assert.AreEqual("Uploading video…", UploadProgressPolicy.StatusText(UploadPhase.Uploading, true, 1));
        Assert.AreEqual("Uploading image…", UploadProgressPolicy.StatusText(UploadPhase.Uploading, false, 1));
        Assert.AreEqual("Uploading images…", UploadProgressPolicy.StatusText(UploadPhase.Uploading, false, 3));
        Assert.AreEqual("Processing video…", UploadProgressPolicy.StatusText(UploadPhase.Processing, true, 1));
        Assert.AreEqual("Upload cancelled", UploadProgressPolicy.StatusText(UploadPhase.Cancelled, true, 1));
        Assert.IsNull(UploadProgressPolicy.StatusText(UploadPhase.Idle, false, 0));
        Assert.IsNull(UploadProgressPolicy.StatusText(UploadPhase.Completed, true, 1));
        Assert.IsNull(UploadProgressPolicy.StatusText(UploadPhase.Failed, true, 1));
    }
}
