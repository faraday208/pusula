using Pusula.Sources;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

// The home directory here is a scratch directory: nothing reads or depends on the one of whoever runs the tests.
public sealed class SourceRequestValidatorTests
{
    private static readonly string Separator = Path.DirectorySeparatorChar.ToString();

    private static NewSource Accept(CreateSourceRequest? request, string home)
    {
        SourceRequestValidator.TryValidate(request, home, out NewSource? source, out EditError? error).ShouldBeTrue(error?.ToString());
        error.ShouldBeNull();
        return source.ShouldNotBeNull();
    }

    private static EditError Reject(CreateSourceRequest? request, string home)
    {
        SourceRequestValidator.TryValidate(request, home, out NewSource? source, out EditError? error).ShouldBeFalse();
        source.ShouldBeNull();
        return error.ShouldNotBeNull();
    }

    private static CreateSourceRequest Request(string? path, string? name = null, string? profile = null) =>
        new() { Path = path, Name = name, Profile = profile };

    // ---- What is accepted ---------------------------------------------------------------------------------------

    [Fact]
    public void TryValidate_ExistingFolder_NamesItAfterTheFolderAndLeavesTheProfileToAuto()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("Documents/Notlarım");

        NewSource source = Accept(Request(folder), home.Path);

        source.ShouldBe(new NewSource(folder, folder, "Notlarım", "auto"));
    }

    [Fact]
    public void TryValidate_NameAndProfile_AreTakenTrimmedAndTheProfileInLowercase()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("vault");

        NewSource source = Accept(Request(folder, "  Work notes  ", " Vault "), home.Path);

        source.Name.ShouldBe("Work notes");
        source.Profile.ShouldBe("vault");
    }

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("AUTO", "auto")]
    [InlineData("claude", "claude")]
    [InlineData("Claude", "claude")]
    [InlineData("vault", "vault")]
    [InlineData("MARKDOWN", "markdown")]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("   ", "auto")]
    public void TryValidate_Profile_IsOneOfTheFourOrLeftOut(string? profile, string expected)
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("x");

        Accept(Request(folder, profile: profile), home.Path).Profile.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_BlankName_IsTheNameOfTheFolder(string? name)
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("Project 2026");

        Accept(Request(folder, name), home.Path).Name.ShouldBe("Project 2026");
    }

    [Fact]
    public void TryValidate_NameOfExactlyEightyCharacters_IsAccepted()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("x");
        string name = new('n', SourceRequestValidator.MaxNameLength);

        Accept(Request(folder, name), home.Path).Name.ShouldBe(name);
    }

    [Fact]
    public void TryValidate_PathWithTrailingSeparator_IsWrittenAndResolvedWithoutIt()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("notes");

        NewSource source = Accept(Request("  " + folder + Separator + "  "), home.Path);

        source.Path.ShouldBe(folder);
        source.FullPath.ShouldBe(folder);
    }

    [Fact]
    public void TryValidate_PathWithDotDot_IsResolvedButWrittenAsTyped()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("a/notes");
        home.CreateDirectory("b");
        string typed = Path.Join(home.Path, "b", "..", "a", "notes");

        NewSource source = Accept(Request(typed), home.Path);

        source.Path.ShouldBe(typed);
        source.FullPath.ShouldBe(folder);
    }

    [Fact]
    public void TryValidate_TildePath_IsExpandedToTheHomeDirectoryButWrittenWithTheTilde()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("Documents/notes");

        NewSource source = Accept(Request("~/Documents/notes"), home.Path);

        source.Path.ShouldBe("~/Documents/notes");
        source.FullPath.ShouldBe(folder);
    }

    [Fact]
    public void TryValidate_TildePathWithTrailingSeparator_IsWrittenWithoutIt()
    {
        using var home = new TempDirectory();
        home.CreateDirectory("Documents/notes");

        Accept(Request("~/Documents/notes/"), home.Path).Path.ShouldBe("~/Documents/notes");
    }

    [Fact]
    public void TryValidate_FolderInsideTheHomeDirectoryAndTheParentOfIt_AreBothFine()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("people/me");
        string inside = temp.CreateDirectory("people/me/notes");

        Accept(Request(inside), home).FullPath.ShouldBe(inside);
        Accept(Request(temp.Resolve("people")), home).FullPath.ShouldBe(temp.Resolve("people"));
    }

    [Fact]
    public void TryValidate_FolderThatIsALink_IsAcceptedAsItIs()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a symbolic link needs a privilege on Windows.");
        using var home = new TempDirectory();
        string real = home.CreateDirectory("real");
        string link = home.Resolve("link");
        Directory.CreateSymbolicLink(link, real);

        Accept(Request(link), home.Path).FullPath.ShouldBe(link);
    }

    // ---- What is not --------------------------------------------------------------------------------------------

    [Fact]
    public void TryValidate_NoRequestAtAll_IsPathRequired()
    {
        using var home = new TempDirectory();

        Reject(null, home.Path).ShouldBe(EditError.PathRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TryValidate_NoPath_IsPathRequired(string? path)
    {
        using var home = new TempDirectory();

        Reject(Request(path), home.Path).ShouldBe(EditError.PathRequired);
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("notes/sub")]
    [InlineData("./notes")]
    [InlineData("../notes")]
    [InlineData("~notes")]
    [InlineData("~other/notes")]
    [InlineData("$HOME/notes")]
    public void TryValidate_PathThatIsNeitherAbsoluteNorHomeRelative_IsPathNotAbsolute(string path)
    {
        using var home = new TempDirectory();
        home.CreateDirectory("notes");

        Reject(Request(path), home.Path).ShouldBe(EditError.PathNotAbsolute);
    }

    [Fact]
    public void TryValidate_TildeWithoutAHomeDirectory_IsPathNotAbsolute() =>
        Reject(Request("~/notes"), string.Empty).ShouldBe(EditError.PathNotAbsolute);

    [Fact]
    public void TryValidate_FolderThatDoesNotExist_IsFolderNotFound()
    {
        using var home = new TempDirectory();

        Reject(Request(home.Resolve("missing")), home.Path).ShouldBe(EditError.FolderNotFound);
        Reject(Request("~/missing"), home.Path).ShouldBe(EditError.FolderNotFound);
    }

    [Fact]
    public void TryValidate_PathOfAFile_IsFolderNotFound()
    {
        using var home = new TempDirectory();
        string file = home.Write("note.md", "x");

        Reject(Request(file), home.Path).ShouldBe(EditError.FolderNotFound);
    }

    [Fact]
    public void TryValidate_PathThatCannotBeAPath_IsFolderNotFound()
    {
        using var home = new TempDirectory();

        Reject(Request(home.Path + Separator + "a\0b"), home.Path).ShouldBe(EditError.FolderNotFound);
        Reject(Request(home.Path + Separator + new string('x', 20_000)), home.Path).ShouldBe(EditError.FolderNotFound);
    }

    // A character that no path has (the null character; on Windows the others of Path.GetInvalidPathChars) is a folder
    // that cannot exist, whatever else is wrong with the path, and never an exception.
    [Fact]
    public void TryValidate_PathWithACharacterNoPathHas_IsFolderNotFoundWhateverElseIsWrongWithIt()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("x");

        foreach (char invalid in Path.GetInvalidPathChars())
        {
            foreach (string path in new[]
            {
                folder + invalid + "y",
                folder + Separator + invalid,
                invalid + folder,
                "relative" + invalid + "x",
                "~/x" + invalid,
                "~" + invalid,
                invalid.ToString(),
                "  " + invalid + "  ",
            })
            {
                // The request is trimmed first: a tab or a line break (characters that no path has on Windows) at either end of it is not part of the path.
                if (!path.Trim().Contains(invalid))
                {
                    continue;
                }

                Reject(Request(path), home.Path).ShouldBe(EditError.FolderNotFound);
            }
        }
    }

    [Fact]
    public void TryValidate_RootOfTheFileSystem_IsTooBroad()
    {
        using var home = new TempDirectory();
        string root = Path.GetPathRoot(home.Path)!;

        Reject(Request(root), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request(root + Separator), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request(Path.Join(root, "..", "..")), home.Path).ShouldBe(EditError.TooBroad);
    }

    [Fact]
    public void TryValidate_TheHomeDirectoryItself_IsTooBroadHoweverItIsWritten()
    {
        using var home = new TempDirectory();
        home.CreateDirectory("Documents");

        Reject(Request(home.Path), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request(home.Path + Separator), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request(home.Resolve("Documents") + Separator + ".."), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request("~"), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request("~/"), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request("~/."), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request("~/Documents/.."), home.Path).ShouldBe(EditError.TooBroad);
    }

    [Theory]
    [InlineData(81)]
    [InlineData(500)]
    public void TryValidate_NameLongerThanEightyCharacters_IsInvalidName(int length)
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("x");

        Reject(Request(folder, new string('n', length)), home.Path).ShouldBe(EditError.InvalidName);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    [InlineData("a\u001bb")]
    [InlineData("a\u0085b")]
    [InlineData("\u0007bell")]
    public void TryValidate_NameWithAControlCharacter_IsInvalidName(string name)
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("x");

        Reject(Request(folder, name), home.Path).ShouldBe(EditError.InvalidName);
    }

    [Theory]
    [InlineData("obsidian")]
    [InlineData("auto claude")]
    [InlineData("none")]
    [InlineData("0")]
    public void TryValidate_ProfileThatIsNotOneOfTheFour_IsInvalidProfile(string profile)
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("x");

        Reject(Request(folder, profile: profile), home.Path).ShouldBe(EditError.InvalidProfile);
    }

    [Fact]
    public void TryValidate_SeveralMistakes_ReportsThePathBeforeTheNameAndTheNameBeforeTheProfile()
    {
        using var home = new TempDirectory();
        string folder = home.CreateDirectory("x");
        string badName = new('n', 81);

        Reject(Request(null, badName, "nope"), home.Path).ShouldBe(EditError.PathRequired);
        Reject(Request("relative", badName, "nope"), home.Path).ShouldBe(EditError.PathNotAbsolute);
        Reject(Request(home.Path, badName, "nope"), home.Path).ShouldBe(EditError.TooBroad);
        Reject(Request(home.Resolve("missing"), badName, "nope"), home.Path).ShouldBe(EditError.FolderNotFound);
        Reject(Request(folder, badName, "nope"), home.Path).ShouldBe(EditError.InvalidName);
        Reject(Request(folder, "fine", "nope"), home.Path).ShouldBe(EditError.InvalidProfile);
    }
}
