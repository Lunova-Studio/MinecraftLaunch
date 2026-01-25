using MinecraftLaunch.Base.Enums;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Authentication;

[JsonDerivedType(typeof(OfflineAccount), typeDiscriminator: "offline")]
[JsonDerivedType(typeof(MicrosoftAccount), typeDiscriminator: "microsoft")]
[JsonDerivedType(typeof(YggdrasilAccount), typeDiscriminator: "yggdrasil")]
public abstract record Account(string Name, Guid Uuid, string AccessToken)
{
    public abstract AccountType Type { get; }

    public string Name { get; init; } = Name;
    public Guid Uuid { get; init; } = Uuid;
    public string AccessToken { get; set; } = AccessToken;

    public override int GetHashCode()
    {
        return Type.GetHashCode() ^ Name.GetHashCode() ^ Uuid.GetHashCode();
    }

    public virtual bool ProfileEquals(Account account)
    {
        if (account.Type.Equals(this.Type)
            && account.Uuid.Equals(this.Uuid)
            && account.Name.Equals(this.Name))
            return true;

        return false;
    }
}

public record MicrosoftAccount(
    string Name,
    Guid Uuid,
    string AccessToken,
    string RefreshToken,
    DateTime LastRefreshTime) : Account(Name, Uuid, AccessToken)
{
    public override AccountType Type => AccountType.Microsoft;

    public DateTime LastRefreshTime { get; set; } = LastRefreshTime;

    public string RefreshToken { get; set; } = RefreshToken;

    public override bool ProfileEquals(Account account)
    {
        if (account is MicrosoftAccount microsoftAccount
            && microsoftAccount.Uuid.Equals(this.Uuid))
            return true;

        return false;
    }

    public override int GetHashCode() => Type.GetHashCode() ^ Uuid.GetHashCode();
}

public record YggdrasilAccount(
    string Name,
    Guid Uuid,
    string AccessToken,
    string YggdrasilServerUrl,
    string ClientToken = default) : Account(Name, Uuid, AccessToken)
{
    public override AccountType Type => AccountType.Yggdrasil;

    public string ClientToken { get; set; } = ClientToken;
    public string YggdrasilServerUrl { get; set; } = YggdrasilServerUrl;

    public Dictionary<string, string> MetaData { get; set; } = [];

    public override bool ProfileEquals(Account account)
    {
        if (account is YggdrasilAccount yggdrasilAccount
            && yggdrasilAccount.YggdrasilServerUrl.Equals(this.YggdrasilServerUrl)
            && yggdrasilAccount.Uuid.Equals(this.Uuid))
            return true;

        return false;
    }

    public override int GetHashCode() => Type.GetHashCode() ^ YggdrasilServerUrl.GetHashCode() ^ Uuid.GetHashCode();
}

public record OfflineAccount(
    string Name,
    Guid Uuid,
    string AccessToken) : Account(Name, Uuid, AccessToken)
{
    public override AccountType Type => AccountType.Offline;
}