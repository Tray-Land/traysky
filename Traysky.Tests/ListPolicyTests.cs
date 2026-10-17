using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ListPolicyTests
{
    [TestMethod]
    public void IsShown_HidesReferenceAndUnknownLists()
    {
        Assert.IsTrue(ListPolicy.IsShown(ListKind.Curate));
        Assert.IsTrue(ListPolicy.IsShown(ListKind.Moderation));
        Assert.IsFalse(ListPolicy.IsShown(ListKind.Reference));
        Assert.IsFalse(ListPolicy.IsShown(ListKind.Unknown));
    }

    [TestMethod]
    public void CanManage_OnlyForTheCreator()
    {
        Assert.IsTrue(ListPolicy.CanManage("did:plc:me", "did:plc:me"));
        Assert.IsFalse(ListPolicy.CanManage("did:plc:other", "did:plc:me"));
        Assert.IsFalse(ListPolicy.CanManage("did:plc:me", null));
        Assert.IsFalse(ListPolicy.CanManage(null, null));
        Assert.IsFalse(ListPolicy.CanManage("", ""));
    }

    [TestMethod]
    public void NormalizeName_TrimsAndRejectsEmptyOrTooLong()
    {
        Assert.AreEqual("Friends", ListPolicy.NormalizeName("  Friends "));
        Assert.IsNull(ListPolicy.NormalizeName("   "));
        Assert.IsNull(ListPolicy.NormalizeName(null));
        Assert.IsNotNull(ListPolicy.NormalizeName(new string('a', ListPolicy.NameMaxBytes)));
        Assert.IsNull(ListPolicy.NormalizeName(new string('a', ListPolicy.NameMaxBytes + 1)));
    }

    [TestMethod]
    public void NormalizeName_LimitIsUtf8BytesNotCharacters()
    {
        // 32 two-byte characters fit exactly; 33 do not, though both are well under 64 characters.
        Assert.IsNotNull(ListPolicy.NormalizeName(new string('é', 32)));
        Assert.IsNull(ListPolicy.NormalizeName(new string('é', 33)));
    }

    [TestMethod]
    public void Description_CountsGraphemesNotUtf16Units()
    {
        // 300 family emoji are 300 graphemes (each is several code points).
        string family = "\U0001F468‍\U0001F469‍\U0001F467";
        Assert.IsTrue(ListPolicy.IsDescriptionValid(string.Concat(System.Linq.Enumerable.Repeat(family, 300))));
        Assert.IsFalse(ListPolicy.IsDescriptionValid(string.Concat(System.Linq.Enumerable.Repeat(family, 301))));
        Assert.IsTrue(ListPolicy.IsDescriptionValid(null));
    }

    [TestMethod]
    public void CanSave_NeedsAValidNameAndDescription()
    {
        Assert.IsTrue(ListPolicy.CanSave("Friends", ""));
        Assert.IsFalse(ListPolicy.CanSave("", "something"));
        Assert.IsFalse(ListPolicy.CanSave("Friends", new string('x', ListPolicy.DescriptionMaxGraphemes + 1)));
    }

    [TestMethod]
    public void Change_MapsCheckboxToServerAction()
    {
        Assert.AreEqual(MembershipChange.None, ListPolicy.Change(false, false));
        Assert.AreEqual(MembershipChange.None, ListPolicy.Change(true, true));
        Assert.AreEqual(MembershipChange.Add, ListPolicy.Change(false, true));
        Assert.AreEqual(MembershipChange.Remove, ListPolicy.Change(true, false));
    }

    [TestMethod]
    public void Text_SingularAndPlural()
    {
        Assert.AreEqual("1 account", ListPolicy.MemberCountText(1));
        Assert.AreEqual("0 accounts", ListPolicy.MemberCountText(0));
        Assert.AreEqual("1,200 accounts", ListPolicy.MemberCountText(1200));
        Assert.AreEqual("Moderation list · 3 accounts", ListPolicy.Subtitle(ListKind.Moderation, 3));
        Assert.AreEqual("Curation list · 1 account", ListPolicy.Subtitle(ListKind.Curate, 1));
    }

    [TestMethod]
    public void EmptyText_DependsOnWhoseListItIs()
    {
        Assert.AreEqual("You haven't made any lists", ListPolicy.EmptyText(isSelf: true));
        Assert.AreEqual("No lists", ListPolicy.EmptyText(isSelf: false));
        StringAssert.Contains(ListPolicy.EmptyMembersText(canManage: true), "Search above");
        Assert.AreEqual("No one is on this list.", ListPolicy.EmptyMembersText(canManage: false));
    }

    [TestMethod]
    public void CanSubscribe_OnlyModerationLists()
    {
        Assert.IsTrue(ListPolicy.CanSubscribe(ListKind.Moderation));
        Assert.IsFalse(ListPolicy.CanSubscribe(ListKind.Curate));
        Assert.IsFalse(ListPolicy.CanSubscribe(ListKind.Reference));
        Assert.IsFalse(ListPolicy.CanSubscribe(ListKind.Unknown));
    }

    [TestMethod]
    public void SubscriptionLabels_FlipWithState()
    {
        Assert.AreEqual("Mute list", ListPolicy.MuteLabel(false));
        Assert.AreEqual("Unmute list", ListPolicy.MuteLabel(true));
        Assert.AreEqual("Block list", ListPolicy.BlockLabel(false));
        Assert.AreEqual("Unblock list", ListPolicy.BlockLabel(true));
    }

    [TestMethod]
    public void SubscriptionStatus_DescribesWhatIsOn()
    {
        Assert.AreEqual(string.Empty, ListPolicy.SubscriptionStatus(false, false));
        StringAssert.Contains(ListPolicy.SubscriptionStatus(true, false), "mute everyone");
        StringAssert.Contains(ListPolicy.SubscriptionStatus(false, true), "block everyone");
        StringAssert.Contains(ListPolicy.SubscriptionStatus(true, true), "mute and block");
    }

    [TestMethod]
    public void SubscriptionFailureText_SaysWhichActionFailed()
    {
        StringAssert.Contains(ListPolicy.SubscriptionFailureText(block: false, turningOn: true), "mute the list");
        StringAssert.Contains(ListPolicy.SubscriptionFailureText(block: false, turningOn: false), "unmute the list");
        StringAssert.Contains(ListPolicy.SubscriptionFailureText(block: true, turningOn: true), "block the list");
        StringAssert.Contains(ListPolicy.SubscriptionFailureText(block: true, turningOn: false), "unblock the list");
    }

    [TestMethod]
    public void MembershipFailureText_NamesTheList()
    {
        StringAssert.Contains(ListPolicy.MembershipFailureText(MembershipChange.Add, "Friends"), "add them to Friends");
        StringAssert.Contains(ListPolicy.MembershipFailureText(MembershipChange.Remove, "Friends"), "remove them from Friends");
    }
}
