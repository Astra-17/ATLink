using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ATLink.Core;

internal static class LinkProjectFile
{
    const int HeaderSize = 33, TagSize = 16, MaxFiles = 10_000, MaxPathBytes = 4096;
    const int MaxCiphertext = 512 * 1024 * 1024;
    const ulong MaxExpanded = 2UL * 1024 * 1024 * 1024;
    static readonly UTF8Encoding Utf8 = new(false, true);

    internal static void Write(string folder, string path, string? password, bool replace, Action<bool>? fault)
    {
        if(password is not null)ValidatePassword(password);
        using var compressed = new LimitedBuffer();
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        if (files.Length is 0 or > MaxFiles) throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid project file count."));
        ulong remaining = MaxExpanded - sizeof(uint);
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new BinaryWriter(deflate))
        {
            writer.Write((uint)files.Length);
            foreach (string file in files.Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                byte[] name = Utf8.GetBytes(relative);
                if (name.Length is 0 or > MaxPathBytes) throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid project path length."));
                ValidatePath(relative);
                using var input = File.OpenRead(file);
                ulong size = (ulong)input.Length;
                Charge(ref remaining, 12UL + (ulong)name.Length);
                Charge(ref remaining, size);
                writer.Write((uint)name.Length);
                writer.Write(name);
                writer.Write(size);
                input.CopyTo(deflate);
            }
        }

        byte[] header = new byte[HeaderSize];
        "ATLK"u8.CopyTo(header);
        header[4] = password is null?(byte)2:(byte)1;
        RandomNumberGenerator.Fill(header.AsSpan(5));
        byte[] cipher = new byte[checked((int)compressed.Length)];
        byte[] tag = new byte[TagSize];
        Span<byte> plaintext = compressed.GetBuffer().AsSpan(0, cipher.Length);
        try
        {
            Transform(password, header, cipher, tag, plaintext, encrypt: true);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }

        string target = Path.GetFullPath(path);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                output.Write(header);
                fault?.Invoke(false);
                output.Write(cipher);
                output.Write(tag);
                output.Flush(flushToDisk: true);
            }
            fault?.Invoke(true);
            if (replace) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally { if (created) File.Delete(temporary); }
    }

    internal static void Extract(string path, string folder, string? password)
    {
        using var input = File.OpenRead(path);
        long size = input.Length - HeaderSize - TagSize;
        if (size <= 0 || size > MaxCiphertext) throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid .link file size."));
        byte[] header = new byte[HeaderSize];
        input.ReadExactly(header);
        if (!header.AsSpan(0, 4).SequenceEqual("ATLK"u8)) throw new InvalidDataException(ATLink.Core.UiText.Get("This is not an ATLink project."));
        if (header[4] is not (1 or 2)) throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Unsupported .link version: "), header[4]));
        if(header[4]==1)
        {
            if(password is null)throw new InvalidDataException(ATLink.Core.UiText.Get("This old password-protected .link format is no longer supported. Export a new project with the current ATLink version."));
            ValidatePassword(password);
        }
        byte[] cipher = new byte[(int)size];
        byte[] tag = new byte[TagSize];
        input.ReadExactly(cipher);
        input.ReadExactly(tag);
        byte[] plaintext = new byte[cipher.Length];
        try
        {
            try { Transform(password, header, cipher, tag, plaintext, encrypt: false); }
            catch (AuthenticationTagMismatchException)
            {
                throw new InvalidDataException(header[4]==2?ATLink.Core.UiText.Get("Damaged .link file."):ATLink.Core.UiText.Get("Incorrect password or damaged .link file."));
            }
            using var compressed = new MemoryStream(plaintext, writable: false);
            using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
            using var reader = new BinaryReader(deflate);
            uint count = reader.ReadUInt32();
            if (count is 0 or > MaxFiles) throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid project file count."));
            ulong remaining = MaxExpanded - sizeof(uint);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte[] buffer = new byte[81920];
            for (uint i = 0; i < count; i++)
            {
                uint length = reader.ReadUInt32();
                if (length is 0 or > MaxPathBytes) throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid project path length."));
                Charge(ref remaining, 12UL + length);
                byte[] name = new byte[(int)length];
                deflate.ReadExactly(name);
                string relative = Utf8.GetString(name);
                ValidatePath(relative);
                if (!seen.Add(relative)) throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Duplicate project path: "), relative));
                ulong bytes = reader.ReadUInt64();
                Charge(ref remaining, bytes);
                string target = Path.Combine(folder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                while (bytes > 0)
                {
                    int chunk = (int)Math.Min((ulong)buffer.Length, bytes);
                    deflate.ReadExactly(buffer.AsSpan(0, chunk));
                    output.Write(buffer, 0, chunk);
                    bytes -= (ulong)chunk;
                }
            }
            if (deflate.ReadByte() != -1 || !seen.Contains("project.json"))
                throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid project payload or missing project.json."));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    static void Transform(string? password, byte[] header, Span<byte> cipher, Span<byte> tag, Span<byte> plaintext, bool encrypt)
    {
        byte[] passwordBytes = Utf8.GetBytes(password??"");
        Span<byte> key = stackalloc byte[32];
        try
        {
            // V2 is automatically readable by ATLink, not protected by a user secret.
            if(header[4]==2)SHA256.HashData([.."ATLink.link.v2.automatic-key"u8,..header.AsSpan(5,16)],key);
            else Rfc2898DeriveBytes.Pbkdf2(passwordBytes, header.AsSpan(5, 16), key, 600_000, HashAlgorithmName.SHA256);
            using var aes = new AesGcm(key, TagSize);
            var nonce = header.AsSpan(21, 12);
            if (encrypt) aes.Encrypt(nonce, plaintext, cipher, tag, header);
            else aes.Decrypt(nonce, cipher, tag, plaintext, header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new InvalidDataException(ATLink.Core.UiText.Get("Enter a password."));
    }

    static void Charge(ref ulong remaining, ulong bytes)
    {
        if (bytes > remaining) throw new InvalidDataException(ATLink.Core.UiText.Get("The expanded project exceeds 2 GiB."));
        remaining -= bytes;
    }

    static void ValidatePath(string path)
    {
        foreach (string part in path.Split('/'))
        {
            string device = part.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (part.Length is 0 or > 255 || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"\\|?*".Contains(c)) ||
                device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) &&
                    "0123456789¹²³".Contains(device[3])))
                throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Unsafe project path: "), path));
        }
    }

    sealed class LimitedBuffer : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }
        void Check(int count)
        {
            if (Position + count > MaxCiphertext) throw new InvalidDataException(ATLink.Core.UiText.Get("The compressed project exceeds 512 MiB."));
        }
    }
}
