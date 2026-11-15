using Traysky.Models;

namespace Traysky.Services;

public static class AppThemePolicy
{
    public static AppThemePreference Parse(int value) =>
        TryParse(value, out AppThemePreference theme) ? theme : AppThemePreference.System;

    public static bool TryParse(int value, out AppThemePreference theme)
    {
        if (value is >= (int)AppThemePreference.System and <= (int)AppThemePreference.Dark)
        {
            theme = (AppThemePreference)value;
            return true;
        }

        theme = AppThemePreference.System;
        return false;
    }

    public static bool IsDark(AppThemePreference preference, bool systemIsDark) =>
        preference switch
        {
            AppThemePreference.Light => false,
            AppThemePreference.Dark => true,
            _ => systemIsDark,
        };
}
