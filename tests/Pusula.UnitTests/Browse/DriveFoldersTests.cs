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
    public void None_HasNoRoots() =>
        DriveFolders.None.Roots.ShouldBeEmpty();

    [Fact]
    public void ForThisMachine_IsThoseOfTheOperatingSystem() =>
        DriveFolders.ForThisMachine().Roots.ShouldBe(DriveFolders.For(OperatingSystem.IsMacOS()).Roots);
}
