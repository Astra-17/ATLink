using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ATLink.Core;

static class LinkProjectChecks
{
    const string Password = " Mot de passe é漢 ";

    public static void Run(string root)
    {
        string work = Directory.CreateTempSubdirectory("ATLink-check-").FullName;
        try { Run(root, work); }
        finally { Directory.Delete(work, recursive: true); }
    }

    static void Run(string root, string work)
    {
        var store = new ApplicationConfigurationStore(Path.Combine(work,"configuration.json"),Path.Combine(root,"files"));
        string limits = store.Game("FC27").LimitsPath;
        var configured = store.Current.WithGame("FC27",store.Game("FC27") with
        { Configured=true,DatabaseLanguage="eng_us" });
        var data = store.CreateData("FC27",configured);
        string source = Path.Combine(work, "original");
        string installed = Path.Combine(work, "second-machine");
        CopyCompdata(data.CompdataFolder, source);
        CopyCompdata(source, installed);
        using var origin = MakeContext(source);
        string fixture = Path.Combine(work, "fixture");
        CopyCompdata(Path.Combine(root, "files/export/SuperLeague-FC27-O-InitialTeams"), fixture);
        string manifestPath = Path.Combine(fixture, "project.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["CompdataSource"] = source;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        using var editing = origin.OpenProject(fixture);
        string key = editing.CompetitionTranslationKeys(editing.Objects.Single(o => o.Kind == 3 && o.Name == "Super League"))[0];
        editing.Execute("Translations", () =>
        {
            editing.SetTranslation("eng_us", key, "Super League LINK");
            editing.SetTranslation("fre_fr", key, "Super Ligue Élite LINK");
        });
        string link = Path.Combine(work, "project.link");
        string second = Path.Combine(work, "second.link");
        string folder = Path.Combine(work, "folder");
        editing.Export(folder);
        editing.ExportLink(link, Password);
        editing.ExportLink(second, Password);
        byte[] original = File.ReadAllBytes(link);
        Assert(original.AsSpan(0, 5).SequenceEqual("ATLK\u0001"u8), "V1 signature");
        Assert(original.Length > 49 && !original.AsSpan(5, 28).SequenceEqual(File.ReadAllBytes(second).AsSpan(5, 28)), "Fresh salt and nonce");
        Reject(() => { using var zip = ZipFile.OpenRead(link); _ = zip.Entries.Count; }, "File is not a ZIP");
        string extracted = Directory.CreateDirectory(Path.Combine(work, "inspect")).FullName;
        LinkProjectFile.Extract(link, extracted, Password);
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(folder, file);
            string unpacked = Path.Combine(extracted, relative);
            if (relative == "project.json")
            {
                var left = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file));
                var right = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(unpacked))!.AsObject();
                Assert(right.Remove("LinkReference") && System.Text.Json.Nodes.JsonNode.DeepEquals(left, right), "Manifest plus compatibility fingerprint");
            }
            else Assert(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(unpacked)), "Exact export bytes: " + relative);
        }
        using (var legacy = editing.OpenProject(folder))
            Assert(legacy.ReadTranslations("fre_fr").Any(row => row.Key == key && row.Text == "Super Ligue Élite LINK"), "Legacy folder still opens");

        editing.Execute("Unsaved translation", () => editing.SetTranslation("fre_fr", key, "Brouillon"));
        foreach (bool failBeforePublish in new[] { false, true })
        {
            Reject(() => editing.ExportLink(link, Password, replace: true, fault: stage =>
            {
                if (stage == failBeforePublish) throw new IOException("Injected save failure.");
            }), failBeforePublish ? "Failure before replace" : "Failure during write");
            Assert(editing.HasUnexportedChanges, "Failed export keeps unsaved state");
            Assert(File.ReadAllBytes(link).SequenceEqual(original), "Failed export preserves existing file");
            Assert(!Directory.EnumerateFiles(work, "*.tmp").Any(), "Temporary file removed");
            using var previous = editing.OpenLink(link, Password);
            Assert(previous.ReadTranslations("fre_fr").Any(row => row.Key == key && row.Text == "Super Ligue Élite LINK"), "Previous file remains readable");
        }
        Reject(() => editing.OpenLink(link, Password.Trim()), "Exact password required");
        string tampered = Path.Combine(work, "tampered.link");
        foreach (int offset in new[] { 4, 5, 21, 33, original.Length - 1 })
        {
            byte[] changed = (byte[])original.Clone();
            changed[offset] ^= 1;
            File.WriteAllBytes(tampered, changed);
            Reject(() => editing.OpenLink(tampered, Password), "Modified byte refused: " + offset);
        }
        foreach (string[] names in new[] { new[] { "../escape" }, new[] { "/absolute" }, new[] { "A.txt", "a.txt" } })
        {
            WriteAuthenticatedFixture(tampered, names);
            Reject(() => editing.OpenLink(tampered, Password), "Unsafe payload refused: " + string.Join(", ", names));
        }
        Assert(!File.Exists(Path.Combine(work, "escape")), "No escaped file");

        Directory.Delete(source, recursive: true);
        Directory.Delete(folder, recursive: true);
        Directory.Delete(fixture, recursive: true);
        using var receiver = MakeContext(installed);
        string metadata;
        using (var reopened = receiver.OpenLink(link, Password))
        {
            metadata = reopened.LocMetadata;
            Assert(reopened.Project.SourceFolder == installed, "Reference matches on another installation path");
            Assert(reopened.ReadTranslations("eng_us").Any(row => row.Key == key && row.Text == "Super League LINK"), "English round trip");
            Assert(reopened.ReadTranslations("fre_fr").Any(row => row.Key == key && row.Text == "Super Ligue Élite LINK"), "French round trip");
            Assert(reopened.PrepareFifaModFiles().All(file => !file.GamePath.StartsWith("data/db/")), "Main database remains excluded from FIFAMOD");
            Assert(reopened.PrepareFifaModFiles().Single(file=>file.GamePath.EndsWith(CompdataProject.LimitsFileName)).Data.SequenceEqual(File.ReadAllBytes(limits)), "Integrated limits survive link reopening");
            string mod=Path.Combine(work,"integrated.fifamod");
            reopened.ExportFifaMod(mod,store.Game("FC27").FifaModCachePath,new("Integrated sources test","ATLink","1.0",""));
            var packaged=FifaModFile.Read(mod);
            Assert(packaged.GameBuild.SequenceEqual(FifaModCacheSelection.ReadBuild(store.Game("FC27").FifaModCachePath)),"FIFAMOD uses the integrated cache build");
            Assert(packaged.Resources.All(resource=>!resource.Name.StartsWith("data/db/",StringComparison.OrdinalIgnoreCase)),"Generated FIFAMOD excludes the main database");
            foreach(var input in reopened.PrepareFifaModFiles())
                Assert(packaged.ExtractStoredFile(mod,packaged.Resources.Single(resource=>resource.Name==input.GamePath)).SequenceEqual(input.Data),"Integrated export bytes: "+input.GamePath);
            reopened.Execute("Rename French", () => reopened.SetTranslation("fre_fr", key, "Réexport"));
            reopened.ExportLink(second, Password, replace: true);
            using var again = receiver.OpenLink(second, Password);
            Assert(again.ReadTranslations("fre_fr").Any(row => row.Key == key && row.Text == "Réexport"), "Re-export and replace round trip");
        }
        Assert(!File.Exists(metadata), "Extracted metadata removed when project closes");
        File.AppendAllText(Path.Combine(installed, "settings.txt"), "\n# different reference\n");
        Reject(() => receiver.OpenLink(link, Password), "Mismatched reference refused");
        using var otherGame = new CompetitionProjectContext(CompdataProject.Open(installed), "FC26");
        Reject(() => otherGame.OpenLink(link, Password), "Different game refused");

        CompetitionProjectContext MakeContext(string path)
        {
            var language = data.Languages.Single(item => item.Code == "eng_us");
            var context = new CompetitionProjectContext(CompdataProject.Open(path), "FC27",
                DatabaseDocument.Open(data.BaseDatabase, data.Metadata),
                data.OpenLocalization(language.Code), data.Metadata, language.MetadataPath);
            context.SetLanguages(data.Languages);
            context.SetInstalledCompdata(path);
            context.SetConfiguredLimits(limits);
            return context;
        }
    }

    static void CopyCompdata(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string output = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(file, output);
        }
    }

    static void WriteAuthenticatedFixture(string path, string[] names)
    {
        using var compressed = new MemoryStream();
        using (var stream = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((uint)names.Length);
            foreach (string name in names)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(name);
                writer.Write((uint)bytes.Length);
                writer.Write(bytes);
                writer.Write(0UL);
            }
        }
        byte[] header = new byte[33];
        "ATLK\u0001"u8.CopyTo(header);
        RandomNumberGenerator.Fill(header.AsSpan(5));
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), header[5..21], 600_000, HashAlgorithmName.SHA256, 32);
        byte[] payload = compressed.ToArray(), cipher = new byte[payload.Length], tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(header[21..33], payload, cipher, tag, header);
        File.WriteAllBytes(path, [..header, ..cipher, ..tag]);
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            Console.WriteLine("PASS " + message);
            return;
        }
        throw new Exception("Expected rejection: " + message);
    }
}
