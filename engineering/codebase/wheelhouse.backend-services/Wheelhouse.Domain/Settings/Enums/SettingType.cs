namespace Wheelhouse.Domain.Settings.Enums;

/// <summary>Refers to what a setting's or secret's value must look like; Wheelhouse checks a value against it on save.</summary>
public enum SettingType
{
    /// <summary>Any one line; a secret may span lines.</summary>
    Text = 0,

    /// <summary>A decimal number in invariant form, such as <c>42</c> or <c>-0.5</c>.</summary>
    Number = 1,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean = 2,

    /// <summary>One of the values the declaration lists.</summary>
    Choice = 3,

    /// <summary>An absolute http or https address.</summary>
    Url = 4,

    /// <summary>A cron expression: five or six fields, or a macro such as <c>@daily</c>.</summary>
    Cron = 5,

    /// <summary>A date in the declaration's format, such as <c>yyyy-MM-dd</c>.</summary>
    Date = 6,

    /// <summary>An email address.</summary>
    Email = 7,
}
