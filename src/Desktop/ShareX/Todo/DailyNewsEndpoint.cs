using System;
using System.Net;

namespace ShareX;

internal static class DailyNewsEndpoint
{
    internal static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("API 地址须为不含账号、查询参数和片段的完整 HTTP/HTTPS 地址。");
        bool local = uri.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local))
            throw new ArgumentException("资讯 API 仅允许 HTTPS；HTTP 仅限 localhost、127.0.0.1 或 ::1 等本机回环地址。请修改地址，原配置与密钥已保留。");
        return uri;
    }

    internal static string Recipient(string value) => Validate(value).GetLeftPart(UriPartial.Authority);
}
