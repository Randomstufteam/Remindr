using System.Windows.Automation;

namespace Remindr.Shots;

/// <summary>Reusable UI Automation moves inside Remindr (English UI names; other languages are switched with the saved setting instead).</summary>
internal static class Flow
{
    public static void EnterProfile(AppWindow app, string name)
    {
        // The profile picker shows first on every launch. The tile button is named after the profile.
        AppWindow.Activate(app.Need(name, type: ControlType.Button));
        app.Need(id: "ProfileNavItem", timeoutMs: 8000);
        app.Settle(900);
    }

    public static void Nav(AppWindow app, string name)
    {
        var item = app.Need(name, type: ControlType.Button, within: app.Root);
        AppWindow.Activate(item);
        app.Settle(900);
    }

    /// <summary>The card element for a reminder title (any page).</summary>
    public static AutomationElement? CardFor(AppWindow app, string title)
    {
        var texts = app.Root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "TitleText"));
        foreach (AutomationElement t in texts)
            if (t.Current.Name == title) return TreeWalker.ControlViewWalker.GetParent(t);
        return null;
    }

    public static void EditCard(AppWindow app, string title)
    {
        var card = CardFor(app, title) ?? throw new InvalidOperationException("No card titled " + title);
        AppWindow.Activate(app.Need(id: null, name: "Edit reminder", type: ControlType.Button, within: card));
        app.Need(id: "TitleBox");
    }

    /// <summary>Language-independent navigation: the n-th entry of the main rail (0 Today, 1 Calendar, 2 All Reminders) or footer (0 profile, 1 Settings).</summary>
    public static void NavIndex(AppWindow app, bool footer, int index)
    {
        var list = app.Need(id: footer ? "PART_FooterMenuItemsItemsControl" : "PART_MenuItemsItemsControl");
        var items = list.FindAll(TreeScope.Children, Condition.TrueCondition);
        var btn = items[index].FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "PART_InvokeButton"));
        AppWindow.Activate(btn);
        app.Settle(900);
    }

    public static void ScrollPageToEnd(AppWindow app)
    {
        var host = app.Need(id: "PART_NavigationViewContentPresenter");
        var scroller = host.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
        if (scroller?.GetCurrentPattern(ScrollPattern.Pattern) is ScrollPattern sp)
            sp.SetScrollPercent(ScrollPattern.NoScroll, 100);
        app.Settle(500);
    }

    public static void OpenPicturePopup(AppWindow app)
    {
        AppWindow.Activate(app.Need(id: "PictureButton"));
        app.Need(id: "PictureSearch");
    }

    /// <summary>Closes an open dialog or popup with its Cancel/Close button, whatever it is called.</summary>
    public static void CancelDialog(AppWindow app)
    {
        var cancel = app.Find("Cancel", type: ControlType.Button);
        if (cancel is not null) AppWindow.Activate(cancel);
    }
}

