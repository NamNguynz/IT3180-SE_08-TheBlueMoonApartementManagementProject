namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>Tên các claim riêng của app trong cookie đăng nhập.</summary>
public static class AppClaimTypes
{
    /// <summary>Bản sao AppUserEntity.SecurityStamp lúc đăng nhập. Khác với DB thì phiên bị đăng xuất (Program.cs, OnValidatePrincipal).</summary>
    public const string SecurityStamp = "security_stamp";
}
