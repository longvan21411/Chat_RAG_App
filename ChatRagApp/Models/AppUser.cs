namespace ChatRagApp.Models;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = "google";
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime LastLogin { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
