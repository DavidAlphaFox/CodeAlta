namespace CodeAlta.Agent;

// 模块功能：解析 data URI 中的 base64 编码内容，提取媒体类型与 base64 数据字符串
internal static class AgentDataUri
{
    // 函数功能：尝试从 data URI 字符串中解析 base64 载荷；成功时输出 mediaType 与 base64Data，否则返回 false
    public static bool TryParseBase64(string value, out string mediaType, out string base64Data)
    {
        mediaType = string.Empty;
        base64Data = string.Empty;
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaIndex = value.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex < 0)
        {
            return false;
        }

        var metadata = value.AsSpan(5, commaIndex - 5);
        var isBase64 = false;
        foreach (var segmentRange in metadata.Split(';'))
        {
            if (metadata[segmentRange].Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                isBase64 = true;
                break;
            }
        }

        if (!isBase64)
        {
            return false;
        }

        var payload = value[(commaIndex + 1)..].Trim();
        if (payload.Length == 0)
        {
            return false;
        }

        try
        {
            _ = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return false;
        }

        var separatorIndex = metadata.IndexOf(';');
        var parsedMediaType = separatorIndex < 0 ? metadata : metadata[..separatorIndex];
        mediaType = parsedMediaType.IsWhiteSpace() ? "application/octet-stream" : parsedMediaType.Trim().ToString();
        base64Data = payload;
        return true;
    }
}
