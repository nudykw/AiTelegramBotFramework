using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ServiceLayer.Utils;

public static class BrotliCompressionHelper
{
    public static byte[] Compress(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<byte>();

        var bytes = Encoding.UTF8.GetBytes(text);
        using var outputStream = new MemoryStream();
        using (var compressStream = new BrotliStream(outputStream, CompressionLevel.Fastest))
        {
            compressStream.Write(bytes, 0, bytes.Length);
        }
        return outputStream.ToArray();
    }

    public static string Decompress(byte[] compressedBytes)
    {
        if (compressedBytes == null || compressedBytes.Length == 0)
            return string.Empty;

        using var inputStream = new MemoryStream(compressedBytes);
        using var decompressStream = new BrotliStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        decompressStream.CopyTo(outputStream);
        return Encoding.UTF8.GetString(outputStream.ToArray());
    }
}
