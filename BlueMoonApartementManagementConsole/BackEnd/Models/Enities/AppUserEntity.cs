namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities
{
    public enum UserRole
    {
        BQT,
        KE_TOAN,
        CU_DAN
    }
    public enum UserStatus
    {
        PendingApproval,
        Approved
    }
    public class AppUserEntity
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public UserStatus Status { get; set; } = UserStatus.PendingApproval;
        public int? ApprovedById { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public int FailedAttempts { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public int? GrantedById { get; set; }          // người cấp tài khoản; null với admin gốc và cư dân tự đăng ký chưa duyệt
        public bool MustChangePassword { get; set; }   // true khi mật khẩu do BQT cấp lại, buộc đổi ở lần đăng nhập sau
        public DateTimeOffset? PasswordChangedAt { get; set; }
    }
}
