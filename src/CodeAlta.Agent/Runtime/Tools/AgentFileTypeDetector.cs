using System.Buffers;

namespace CodeAlta.Agent.Runtime.Tools;

// 模块功能：通过读取文件头部字节检测文件是否为二进制文件
internal static class AgentFileTypeDetector
{
    // 说明：探测二进制文件所读取的最大字节数
    private const int BinaryProbeByteCount = 8192;

    // 函数功能：读取文件前 8192 字节，若包含空字节则判定为二进制文件；path 为待检测文件路径，返回 true 表示可能是二进制文件
    public static bool IsProbablyBinaryFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var buffer = ArrayPool<byte>.Shared.Rent(BinaryProbeByteCount);
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.SequentialScan);

            var totalRead = 0;
            while (totalRead < BinaryProbeByteCount)
            {
                var read = stream.Read(buffer, totalRead, BinaryProbeByteCount - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return buffer.AsSpan(0, totalRead).IndexOf((byte)0) >= 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
