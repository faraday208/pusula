using Pusula.Browse;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class DriveFoldersTests
{
    [Fact]
    public void For_NotMacOs_IsMntAndMediaFourLevelsDeep() =>
        DriveFolders.For(isMacOs: false).Roots.ShouldBe([new SearchRoot("/mnt", 4), new SearchRoot("/media", 4)]);

    [Fact]
    public void For_MacOs_AddsVolumesThreeLevelsDeep() =>
        DriveFolders.For(isMacOs: true).Roots.ShouldBe([new SearchRoot("/mnt", 4), new SearchRoot("/media", 4), new SearchRoot("/Volumes", 3)]);

    [Fact]
    public void For_MountFolders_DoNotAskTheirLastLevel()
    {
        // An external disk may sleep or be slow: asking its last level would open a folder for each, so only the folders that the walk reads can be folders of notes there.
        DriveFolders.For(isMacOs: true).Roots.ShouldAllBe(root => !root.ProbesLastLevel);
    }

    [Fact]
    public void None_HasNoRoots() =>
        DriveFolders.None.Roots.ShouldBeEmpty();

    [Fact]
    public void ForThisMachine_IsThoseOfTheOperatingSystem()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has its drives instead of /mnt and /media.");

        DriveFolders.ForThisMachine().Roots.ShouldBe(DriveFolders.For(OperatingSystem.IsMacOS()).Roots);
    }

    [Fact]
    public void ForWindowsDrives_Drives_AreRootsFourLevelsDeepThatSkipTheSystemFoldersAtTheirTop()
    {
        SearchRoot[] roots = [.. DriveFolders.ForWindowsDrives([@"C:\", @"D:\"]).Roots];

        roots.Select(root => root.Path).ShouldBe([@"C:\", @"D:\"]);
        roots.Select(root => root.MaxDepth).ShouldBe([4, 4]);
        foreach (SearchRoot root in roots)
        {
            root.SkippedAtTop.ShouldBe(DriveFolders.WindowsSystemFolders);
        }
    }

    [Fact]
    public void ForWindowsDrives_SystemDrive_IsTheOnlyDriveThatAsksItsLastLevel()
    {
        SearchRoot[] roots = [.. DriveFolders.ForWindowsDrives([@"C:\", @"D:\", @"E:\"], systemDrive: @"C:\").Roots];

        roots.Select(root => (root.Path, root.ProbesLastLevel)).ShouldBe([(@"C:\", true), (@"D:\", false), (@"E:\", false)]);
    }

    [Fact]
    public void ForWindowsDrives_NoSystemDriveGiven_NoDriveAsksItsLastLevel() =>
        DriveFolders.ForWindowsDrives([@"C:\", @"D:\"]).Roots.ShouldAllBe(root => !root.ProbesLastLevel);

    [Fact]
    public void ForWindowsDrives_SystemDriveThatIsNotAmongTheDrives_MakesNoDriveAskItsLastLevel() =>
        DriveFolders.ForWindowsDrives([@"D:\", @"E:\"], systemDrive: @"C:\").Roots.ShouldAllBe(root => !root.ProbesLastLevel);

    [Fact]
    public void ForWindowsDrives_NoDrives_HasNoRoots() =>
        DriveFolders.ForWindowsDrives([]).Roots.ShouldBeEmpty();

    [Fact]
    public void ForWindowsDrives_DrivesThatComeAndGo_AreFollowedEachTimeTheRootsAreRead()
    {
        var drives = new List<string> { @"C:\" };
        DriveFolders folders = DriveFolders.ForWindowsDrives(drives);

        folders.Roots.Select(root => root.Path).ShouldBe([@"C:\"]);

        // A memory stick is plugged in while the server runs.
        drives.Add(@"E:\");
        folders.Roots.Select(root => root.Path).ShouldBe([@"C:\", @"E:\"]);

        drives.Remove(@"C:\");
        folders.Roots.Select(root => root.Path).ShouldBe([@"E:\"]);
    }

    [Fact]
    public void WindowsSystemFolders_AreTheSystemFoldersAndUsersAndEveryNameThatStartsWithADollar() =>
        DriveFolders.WindowsSystemFolders.ShouldBe(
        [
            "Windows",
            "Program Files",
            "Program Files (x86)",
            "ProgramData",
            "Users",
            "PerfLogs",
            "Recovery",
            "System Volume Information",
            "$*",
        ]);

    [Fact]
    public void ForThisMachine_Windows_IsTheRootOfEveryReadyFixedOrRemovableDrive()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the drives of Windows.");

        SearchRoot[] roots = [.. DriveFolders.ForThisMachine().Roots];

        roots.ShouldNotBeEmpty();
        foreach (SearchRoot root in roots)
        {
            var drive = new DriveInfo(root.Path);
            // The root of a drive and nothing below it, such as C:\.
            root.Path.ShouldBe(Path.GetPathRoot(root.Path));
            drive.DriveType.ShouldBeOneOf(DriveType.Fixed, DriveType.Removable);
            drive.IsReady.ShouldBeTrue();
            root.MaxDepth.ShouldBe(DriveFolders.MountDepth);
            root.SkippedAtTop.ShouldBe(DriveFolders.WindowsSystemFolders);
        }

        // The drive that Windows is on is one of them.
        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!;
        roots.ShouldContain(root => string.Equals(root.Path, systemDrive, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ForThisMachine_Windows_OnlyTheDriveThatWindowsIsOnAsksItsLastLevel()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the drives of Windows.");

        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;

        string[] asking = [.. DriveFolders.ForThisMachine().Roots.Where(root => root.ProbesLastLevel).Select(root => root.Path)];

        asking.Length.ShouldBe(1);
        string.Equals(asking[0], systemDrive, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Fact]
    public void ForThisMachine_Windows_LeavesOutTheDrivesThatAreNeitherFixedNorRemovable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the drives of Windows.");

        string[] paths = [.. DriveFolders.ForThisMachine().Roots.Select(root => root.Path)];

        // Network drives, optical drives and the like, wherever this machine has any.
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.DriveType is not (DriveType.Fixed or DriveType.Removable)))
        {
            paths.ShouldNotContain(drive.Name);
        }
    }
}
