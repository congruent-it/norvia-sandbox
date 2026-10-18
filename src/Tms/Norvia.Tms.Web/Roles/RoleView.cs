namespace Norvia.Tms.Web.Roles;

/// <summary>A view a role works in, as the main navigation links to it.</summary>
/// <param name="Path">Where the view opens.</param>
/// <param name="Title">The view as the navigation names it.</param>
/// <param name="Group">The menu the navigation lists the view under, or null when it stands in the header on its own.</param>
public sealed record RoleView(string Path, string Title, string? Group = null);
