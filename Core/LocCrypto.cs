using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ATLink.Core;

/// <summary>FC25+ loc files are AES-256-CBC with PKCS7. The same key/IV apply to every language.</summary>
public static class LocCrypto
{
    static readonly byte[] Key =
    [
        0x8F,0x5B,0xCA,0x17,0x7B,0x44,0x2B,0x80,0x2C,0x8C,0xCC,0xAA,0xB4,0x12,0x7E,0x69,
        0x54,0x5A,0xC0,0xCC,0x8B,0x9E,0x18,0xB9,0x29,0x8A,0x48,0x13,0x9F,0x31,0xEF,0x5F
    ];
    static readonly byte[] Iv =
    [
        0x7A,0xDC,0xDF,0x10,0x90,0x12,0x1E,0xD1,0x97,0xC3,0xA9,0x88,0x51,0xAA,0x61,0x6E
    ];
    static readonly byte[] Header = [68, 66, 0, 8, 0, 0, 0, 0];

    public static bool IsEncrypted(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32 || data.Length % 16 != 0) return false;
        if (data[..8].SequenceEqual(Header) || SquadFile.IsContainer(data)) return false;
        byte[] first = Transform(data[..16].ToArray(), PaddingMode.None, encrypt: false);
        return first.AsSpan(0, 8).SequenceEqual(Header);
    }

    public static byte[] Decrypt(byte[] data)
    {
        if (data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(Header)) return (byte[])data.Clone();
        if (data.Length < 32 || data.Length % 16 != 0)
            throw new InvalidDataException("Fichier LOC chiffré invalide.");
        byte[] plain = Transform(data, PaddingMode.PKCS7, encrypt: false);
        if (plain.Length < 28 || !plain.AsSpan(0, 8).SequenceEqual(Header))
            throw new InvalidDataException("Échec du déchiffrement LOC.");
        int size = BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(8, 4));
        if (size != plain.Length) throw new InvalidDataException("Taille LOC déchiffrée incohérente.");
        return plain;
    }

    public static byte[] Encrypt(byte[] plain)
    {
        if (plain.Length < 28 || !plain.AsSpan(0, 8).SequenceEqual(Header))
            throw new InvalidDataException("DB LOC brute attendue pour le chiffrement.");
        return Transform(plain, PaddingMode.PKCS7, encrypt: true);
    }

    static byte[] Transform(byte[] data, PaddingMode padding, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = padding;
        using var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        return transform.TransformFinalBlock(data, 0, data.Length);
    }
}
