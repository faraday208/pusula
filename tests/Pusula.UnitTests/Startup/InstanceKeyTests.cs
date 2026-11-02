using System.Net;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class InstanceKeyTests
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static string FolderOf(TempDirectory state) => Path.Join(state.Path, "pusula");

    private static string FileOf(TempDirectory state) => Path.Join(FolderOf(state), InstanceKey.FileName);

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    // A key file as another pusula (or a person) left it, in a folder with the given mode.
    private static string Place(TempDirectory state, string content, UnixFileMode fileMode = Private, UnixFileMode folderMode = PrivateFolder)
    {
        string file = state.Write($"pusula/{InstanceKey.FileName}", content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, fileMode);
            File.SetUnixFileMode(FolderOf(state), folderMode);
        }

        return file;
    }

    private static void NeedsUnix() => Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs the file modes of Unix.");

    private static void NeedsAUserThatIsNotRoot() => Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.UserName == "root", "Needs Unix and a user that file modes bind.");

    private static bool Ensure(TempDirectory state, string[]? shown = null, Action? beforePublish = null) =>
        InstanceKey.EnsureExists(state.Path, shown ?? [], new CapturingLogger<object>(), beforePublish);

    // ---- Making the key ---------------------------------------------------------------------------------------------

    [Fact]
    public void EnsureExists_NoKeyYet_MakesOneLineOfBase64Of32Bytes()
    {
        using var state = new TempDirectory();

        Ensure(state).ShouldBeTrue();

        string text = File.ReadAllText(FileOf(state));
        text.Length.ShouldBe(45);
        text.ShouldEndWith("\n");
        Convert.FromBase64String(text.TrimEnd('\n')).Length.ShouldBe(32);
        InstanceKey.Read(state.Path).ShouldBe(Convert.FromBase64String(text.TrimEnd('\n')));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void EnsureExists_NoKeyYet_MakesTheFolderAndTheFileForTheUserAlone()
    {
        NeedsUnix();
        using var state = new TempDirectory();

        Ensure(state).ShouldBeTrue();

        File.GetUnixFileMode(FolderOf(state)).ShouldBe(PrivateFolder);
        File.GetUnixFileMode(FileOf(state)).ShouldBe(Private);
    }

    [Fact]
    public void EnsureExists_NoKeyYet_LeavesNoTemporaryFileAndSaysSo()
    {
        using var state = new TempDirectory();
        var logger = new CapturingLogger<object>();

        InstanceKey.EnsureExists(state.Path, [], logger).ShouldBeTrue();

        Directory.GetFileSystemEntries(FolderOf(state)).Select(Path.GetFileName).ShouldBe([InstanceKey.FileName]);
        logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Information]);
        logger.Entries[0].Message.ShouldContain(FileOf(state));
    }

    [Fact]
    public void EnsureExists_TwoKeysMade_AreNotTheSame()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        Ensure(first).ShouldBeTrue();
        Ensure(second).ShouldBeTrue();

        InstanceKey.Read(first.Path).ShouldNotBe(InstanceKey.Read(second.Path));
    }

    [Fact]
    public void EnsureExists_ATrustedKeyIsThere_KeepsItAndWritesNothing()
    {
        using var state = new TempDirectory();
        string key = NewKey();
        Place(state, key + "\n");
        var logger = new CapturingLogger<object>();

        InstanceKey.EnsureExists(state.Path, [], logger).ShouldBeTrue();

        File.ReadAllText(FileOf(state)).ShouldBe(key + "\n");
        Directory.GetFileSystemEntries(FolderOf(state)).Length.ShouldBe(1);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void EnsureExists_NoStateDirectory_DoesNothing()
    {
        InstanceKey.EnsureExists(string.Empty, [], new CapturingLogger<object>()).ShouldBeFalse();
        InstanceKey.Read(string.Empty).ShouldBeNull();
        InstanceKey.FolderOf(string.Empty).ShouldBeNull();
    }

    [Fact]
    public void EnsureExists_NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => InstanceKey.EnsureExists("x", null!, new CapturingLogger<object>()));
        Should.Throw<ArgumentNullException>(() => InstanceKey.EnsureExists("x", [], null!));
    }

    // ---- A file that is there and is no key -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("not base64 at all\n")]
    [InlineData("AAAA\n")]
    public void EnsureExists_FileWithAnotherContent_IsReplacedByAKey(string content)
    {
        using var state = new TempDirectory();
        Place(state, content);
        InstanceKey.Read(state.Path).ShouldBeNull();

        Ensure(state).ShouldBeTrue();

        InstanceKey.Read(state.Path).ShouldNotBeNull();
        Directory.GetFileSystemEntries(FolderOf(state)).Length.ShouldBe(1);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Read_KeyOfAnotherLength_IsNotAKey(int bytes)
    {
        using var state = new TempDirectory();
        Place(state, Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)) + "\n");

        InstanceKey.Read(state.Path).ShouldBeNull();
    }

    [Fact]
    public void Read_FileThatIsFarLongerThanAKey_IsNotRead()
    {
        using var state = new TempDirectory();
        Place(state, new string('A', 10_000));

        InstanceKey.Read(state.Path).ShouldBeNull();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("")]
    public void Read_KeyWithOrWithoutTheEndOfLine_IsTheKey(string ending)
    {
        using var state = new TempDirectory();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        Place(state, Convert.ToBase64String(key) + ending);

        InstanceKey.Read(state.Path).ShouldBe(key);
    }

    [Fact]
    public void Read_NoFile_IsNull()
    {
        using var state = new TempDirectory();

        InstanceKey.Read(state.Path).ShouldBeNull();
        Directory.Exists(FolderOf(state)).ShouldBeFalse();
    }

    // Read is for the pusula that only looks: it never makes anything.
    [Fact]
    public void Read_NeverWritesAnything()
    {
        using var state = new TempDirectory();

        InstanceKey.Read(state.Path);

        Directory.GetFileSystemEntries(state.Path).ShouldBeEmpty();
    }

    // The file is exactly the key and one line end: nothing is skipped, trimmed or ignored.
    [Theory]
    [InlineData("L", "\n")]
    [InlineData(" ", "")]
    [InlineData("", " ")]
    [InlineData("", " \n")]
    [InlineData("", "\n\n")]
    [InlineData("", "\r\r\n")]
    [InlineData("", "\n\r")]
    [InlineData("", "\t")]
    [InlineData("\n", "")]
    public void Read_KeyWithAnythingElseAround_IsNotAKey(string before, string after)
    {
        using var state = new TempDirectory();
        string key = NewKey();
        string text = before == "L" ? key[..22] + "\n" + key[22..] + after : before + key + after;
        Place(state, text);

        InstanceKey.Read(state.Path).ShouldBeNull();
    }

    [Fact]
    public void Read_KeyWithALineBreakInsideAndSpacesInside_IsNotAKey()
    {
        using var state = new TempDirectory();
        string key = NewKey();
        Place(state, key[..20] + " " + key[21..]);

        InstanceKey.Read(state.Path).ShouldBeNull();
    }

    // ---- A folder that is a link (Unix) -----------------------------------------------------------------------------

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_FolderThatIsALink_IsNotTrusted()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        string real = state.Write($"elsewhere/{InstanceKey.FileName}", NewKey() + "\n");
        File.SetUnixFileMode(real, Private);
        File.SetUnixFileMode(Path.GetDirectoryName(real)!, PrivateFolder);
        Directory.CreateSymbolicLink(FolderOf(state), Path.GetDirectoryName(real)!);

        InstanceKey.Read(state.Path).ShouldBeNull();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void EnsureExists_FolderThatIsALinkToAFolderOfOthers_WritesNothing()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        string elsewhere = state.CreateDirectory("elsewhere");
        Directory.CreateSymbolicLink(FolderOf(state), elsewhere);
        var logger = new CapturingLogger<object>();

        InstanceKey.EnsureExists(state.Path, [], logger).ShouldBeFalse();

        Directory.GetFileSystemEntries(elsewhere).ShouldBeEmpty();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Message.ShouldContain("a link");
    }

    // The shown folder is compared as a string, so the link is what keeps the key out of it.
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void EnsureExists_FolderThatIsALinkIntoAShownFolder_WritesNothingInIt()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        using var shown = new TempDirectory();
        Directory.CreateSymbolicLink(FolderOf(state), shown.Path);

        InstanceKey.EnsureExists(state.Path, [shown.Path], new CapturingLogger<object>()).ShouldBeFalse();

        Directory.GetFileSystemEntries(shown.Path).ShouldBeEmpty();
    }

    // ---- A path that cannot be one: logged, never thrown ------------------------------------------------------------

    [Theory]
    [InlineData("bad\0path")]
    [InlineData("bad\0")]
    public void EnsureExists_StateDirectoryWithACharacterNoPathHas_IsFalseAndThrowsNothing(string state)
    {
        var logger = new CapturingLogger<object>();

        InstanceKey.EnsureExists(state, [], logger).ShouldBeFalse();
        InstanceKey.Read(state).ShouldBeNull();

        logger.Entries.Count.ShouldBe(1);
    }

    // ---- The modes (Unix) -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupExecute)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherExecute)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
    [UnsupportedOSPlatform("windows")]
    public void Read_FileThatGroupOrOthersHaveAnyAccessTo_IsNotTrustedAndIsReplaced(UnixFileMode mode)
    {
        NeedsUnix();
        using var state = new TempDirectory();
        string old = NewKey();
        string file = Place(state, old + "\n", mode);
        InstanceKey.Read(state.Path).ShouldBeNull();

        Ensure(state).ShouldBeTrue();

        File.ReadAllText(file).ShouldNotBe(old + "\n");
        File.GetUnixFileMode(file).ShouldBe(Private);
        InstanceKey.Read(state.Path).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)]
    public void Read_FileForTheUserAlone_IsTrusted(UnixFileMode mode)
    {
        NeedsUnix();
        using var state = new TempDirectory();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        Place(state, Convert.ToBase64String(key) + "\n", mode);

        InstanceKey.Read(state.Path).ShouldBe(key);
    }

    [Theory]
    [InlineData(UnixFileMode.GroupWrite)]
    [InlineData(UnixFileMode.OtherWrite)]
    [InlineData(UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)]
    public void Read_FolderThatGroupOrOthersCanWriteTo_IsNotTrusted(UnixFileMode extra)
    {
        NeedsUnix();
        using var state = new TempDirectory();
        Place(state, NewKey() + "\n", Private, PrivateFolder | extra);

        InstanceKey.Read(state.Path).ShouldBeNull();
    }

    // Whoever can write there can put a key of their own: nothing is written into it, and what is in it is not trusted.
    [Theory]
    [InlineData(UnixFileMode.GroupWrite)]
    [InlineData(UnixFileMode.OtherWrite)]
    public void EnsureExists_FolderThatGroupOrOthersCanWriteTo_WritesNothingAndSaysSo(UnixFileMode extra)
    {
        NeedsUnix();
        using var state = new TempDirectory();
        string old = NewKey();
        string file = Place(state, old + "\n", Private, PrivateFolder | extra);
        var logger = new CapturingLogger<object>();

        InstanceKey.EnsureExists(state.Path, [], logger).ShouldBeFalse();

        File.ReadAllText(file).ShouldBe(old + "\n");
        Directory.GetFileSystemEntries(FolderOf(state)).Length.ShouldBe(1);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Message.ShouldContain("can be written to by others");
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void EnsureExists_FolderThatOthersCanReadButNotWrite_IsFine()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        Directory.CreateDirectory(FolderOf(state));
        File.SetUnixFileMode(FolderOf(state), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Ensure(state).ShouldBeTrue();
    }

    // A link is where somebody else chose the key to come from.
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_FileThatIsALink_IsNotTrusted()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        string real = state.Write("elsewhere/key", NewKey() + "\n");
        File.SetUnixFileMode(real, Private);
        Directory.CreateDirectory(FolderOf(state));
        File.CreateSymbolicLink(FileOf(state), real);

        InstanceKey.Read(state.Path).ShouldBeNull();
        Ensure(state).ShouldBeTrue();
        new FileInfo(FileOf(state)).LinkTarget.ShouldBeNull();
        InstanceKey.Read(state.Path).ShouldNotBeNull();
    }

    // ---- Two pusulas that start together ----------------------------------------------------------------------------

    [Fact]
    public void EnsureExists_TheOtherPusulaWasFaster_KeepsItsKeyAndLeavesNoTemporaryFile()
    {
        using var state = new TempDirectory();
        byte[] theirs = RandomNumberGenerator.GetBytes(32);

        bool made = Ensure(state, beforePublish: () => Place(state, Convert.ToBase64String(theirs) + "\n"));

        made.ShouldBeTrue();
        InstanceKey.Read(state.Path).ShouldBe(theirs);
        Directory.GetFileSystemEntries(FolderOf(state)).Select(Path.GetFileName).ShouldBe([InstanceKey.FileName]);
    }

    [Fact]
    public void EnsureExists_WhatAppearedMeanwhileIsNoKey_IsLeftAndNotTrusted()
    {
        using var state = new TempDirectory();

        bool made = Ensure(state, beforePublish: () => Place(state, "something else\n"));

        made.ShouldBeFalse();
        File.ReadAllText(FileOf(state)).ShouldBe("something else\n");
        Directory.GetFileSystemEntries(FolderOf(state)).Length.ShouldBe(1);
    }

    // ---- Never into a folder that is shown --------------------------------------------------------------------------

    [Fact]
    public void EnsureExists_StateFolderInsideAShownFolder_WritesNothing()
    {
        using var state = new TempDirectory();
        var logger = new CapturingLogger<object>();

        InstanceKey.EnsureExists(state.Path, [state.Path], logger).ShouldBeFalse();

        Directory.GetFileSystemEntries(state.Path).ShouldBeEmpty();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Message.ShouldContain("never writes there");
    }

    [Fact]
    public void EnsureExists_ShownFolderIsTheFolderOfPusulaItself_WritesNothing()
    {
        using var state = new TempDirectory();

        Ensure(state, [FolderOf(state)]).ShouldBeFalse();
        Ensure(state, [FolderOf(state) + Path.DirectorySeparatorChar]).ShouldBeFalse();

        Directory.GetFileSystemEntries(state.Path).ShouldBeEmpty();
    }

    // Written to a trusted key's place all the same: a key that is already there needs no write, but a shown folder is never touched.
    [Fact]
    public void EnsureExists_ShownFolderThatIsElsewhere_DoesNotGetInTheWay()
    {
        using var state = new TempDirectory();
        using var shown = new TempDirectory();

        Ensure(state, [shown.Path, state.Path + "-other"]).ShouldBeTrue();

        Directory.GetFileSystemEntries(shown.Path).ShouldBeEmpty();
    }

    // ---- Failures: logged, never thrown -----------------------------------------------------------------------------

    [Fact]
    public void EnsureExists_FolderOfPusulaCannotBeMade_IsFalseAndSaysWhy()
    {
        using var state = new TempDirectory();
        File.WriteAllText(FolderOf(state), "a file where the folder should be");
        var logger = new CapturingLogger<object>();

        InstanceKey.EnsureExists(state.Path, [], logger).ShouldBeFalse();

        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Information);
        logger.Entries[0].Message.ShouldContain("could not be written");
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void EnsureExists_FolderCannotBeWrittenByTheUser_IsFalseAndLeavesNoTemporaryFile()
    {
        NeedsAUserThatIsNotRoot();
        using var state = new TempDirectory();
        Directory.CreateDirectory(FolderOf(state));
        File.SetUnixFileMode(FolderOf(state), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var logger = new CapturingLogger<object>();

            InstanceKey.EnsureExists(state.Path, [], logger).ShouldBeFalse();

            logger.Entries.Count.ShouldBe(1);
            logger.Entries[0].Message.ShouldContain("could not be written");
            Directory.GetFileSystemEntries(FolderOf(state)).ShouldBeEmpty();
        }
        finally
        {
            File.SetUnixFileMode(FolderOf(state), PrivateFolder);
        }
    }

    [Fact]
    public void Read_FileThatTheUserCannotRead_IsNull()
    {
        NeedsAUserThatIsNotRoot();
        using var state = new TempDirectory();
        Place(state, NewKey() + "\n", UnixFileMode.UserWrite);

        InstanceKey.Read(state.Path).ShouldBeNull();
    }

    // ---- The proof --------------------------------------------------------------------------------------------------

    [Fact]
    public void Proof_IsTheHmacOfThePrefixTheChallengeAndTheEndpoint()
    {
        byte[] key = [.. Enumerable.Range(0, 32).Select(static i => (byte)i)];
        string challenge = Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        byte[] proof = InstanceKey.Proof(key, challenge, "127.0.0.1:5190");

        proof.ShouldBe(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"pusula-instance-proof-v1\n{challenge}\n127.0.0.1:5190")));
        proof.Length.ShouldBe(32);
    }

    [Fact]
    public void Proof_AnyPartThatChanges_ChangesTheProof()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string challenge = InstanceKey.NewChallenge();
        byte[] proof = InstanceKey.Proof(key, challenge, "127.0.0.1:5190");

        InstanceKey.Proof(key, challenge, "127.0.0.1:5190").ShouldBe(proof);
        InstanceKey.Proof(RandomNumberGenerator.GetBytes(32), challenge, "127.0.0.1:5190").ShouldNotBe(proof);
        InstanceKey.Proof(key, InstanceKey.NewChallenge(), "127.0.0.1:5190").ShouldNotBe(proof);
        InstanceKey.Proof(key, challenge, "127.0.0.1:5191").ShouldNotBe(proof);
        InstanceKey.Proof(key, challenge, "[::1]:5190").ShouldNotBe(proof);
    }

    [Theory]
    [InlineData("127.0.0.1", 5190, "127.0.0.1:5190")]
    [InlineData("::ffff:127.0.0.1", 5190, "127.0.0.1:5190")]
    [InlineData("::1", 5190, "[::1]:5190")]
    [InlineData("192.0.2.10", 80, "192.0.2.10:80")]
    [InlineData("fd7a:115c:a1e0::1", 5191, "[fd7a:115c:a1e0::1]:5191")]
    public void EndpointOf_IsWrittenTheSameWayOnBothSides(string address, int port, string expected) =>
        InstanceKey.EndpointOf(IPAddress.Parse(address), port).ShouldBe(expected);

    [Fact]
    public void NewChallenge_Is43Base64UrlCharactersOf32RandomBytes()
    {
        string first = InstanceKey.NewChallenge();

        first.Length.ShouldBe(43);
        InstanceKey.IsChallenge(first).ShouldBeTrue();
        InstanceKey.NewChallenge().ShouldNotBe(first);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // padded: 44 characters
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // base64, not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 44 characters
    public void IsChallenge_SomethingThatIsNotTheBase64UrlOf32Bytes_IsFalse(string? challenge) =>
        InstanceKey.IsChallenge(challenge).ShouldBeFalse();
}
