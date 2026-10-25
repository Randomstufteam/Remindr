namespace Remindr.Models;

public sealed class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3B82F6";
    public string AvatarInitial { get; set; } = "?";
    public string? PinHash { get; set; }
    public string? PinSalt { get; set; }
    /// <summary>'builtin:name' | 'file:avatars/id.png' | null (= coloured initial).</summary>
    public string? Avatar { get; set; }
    public string CreatedAt { get; set; } = "";

    public bool HasPin => !string.IsNullOrEmpty(PinHash);
}
