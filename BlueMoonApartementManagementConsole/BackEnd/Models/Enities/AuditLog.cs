namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities
{
    public enum AuditAction
    {
        LoginSuccess,
        LoginFailed,
        LoginLocked,
        Logout,
        Register,
        ApproveAccount,
        CreateAccount,
        DisableAccount,
        EnableAccount,
        UnlockAccount,
        ResetPassword,
        ChangePassword
    }

    public class AuditLog
    {
        public long Id { get; set; }
        public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
        public int? UserId { get; set; }          // người thực hiện
        public string Username { get; set; } = string.Empty;
        public AuditAction Action { get; set; }
        public string? Target { get; set; }       // Id của đối tượng bị tác động, dạng chuỗi
        public string? Detail { get; set; }
    }
}
