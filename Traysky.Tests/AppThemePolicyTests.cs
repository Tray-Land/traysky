using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Models;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class AppThemePolicyTests
{
    [TestMethod]
    public void Parse_RejectsUnknownValues()
    {
        Assert.AreEqual(AppThemePreference.System, AppThemePolicy.Parse(-1));
        Assert.AreEqual(AppThemePreference.System, AppThemePolicy.Parse(3));
        Assert.AreEqual(AppThemePreference.Light, AppThemePolicy.Parse(1));
        Assert.IsFalse(AppThemePolicy.TryParse(3, out AppThemePreference invalid));
        Assert.AreEqual(AppThemePreference.System, invalid);
    }

    [TestMethod]
    public void IsDark_ResolvesOverridesAndSystem()
    {
        Assert.IsFalse(AppThemePolicy.IsDark(AppThemePreference.Light, systemIsDark: true));
        Assert.IsTrue(AppThemePolicy.IsDark(AppThemePreference.Dark, systemIsDark: false));
        Assert.IsFalse(AppThemePolicy.IsDark(AppThemePreference.System, systemIsDark: false));
        Assert.IsTrue(AppThemePolicy.IsDark(AppThemePreference.System, systemIsDark: true));
    }
}
