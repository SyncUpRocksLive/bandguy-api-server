using System.Security.Cryptography;

namespace SyncUpRocks.Types;
public static class Checksums
{
    public static async Task<string> GetSha256Hash(Stream stream)
    {
        // Important: Reset stream position if it has been read before
        if (!stream.CanSeek)
            throw new Exception("Unsupported Stream - cannot generated checksum");

        stream.Position = 0;

        byte[] hashBytes = await SHA256.HashDataAsync(stream);

        stream.Position = 0;

        // Convert to a hex string
        return Convert.ToHexString(hashBytes).ToLower();
    }
}
