using BPP.Core;

namespace BPP.Core.Tests;

public class DistroOwnershipTests
{
    private const string Ours = @"C:\Users\jan\AppData\Local\BPP\wsl";

    [Fact]
    public void Not_listed_is_nobodys()
        => Assert.Equal(DistroOwner.None, DistroOwnership.Classify(listed: false, registeredDir: null, Ours));

    [Fact]
    public void Not_listed_wins_over_a_stale_registry_entry()
        => Assert.Equal(DistroOwner.None, DistroOwnership.Classify(listed: false, Ours, Ours));

    [Fact]
    public void Listed_in_our_directory_is_ours()
        => Assert.Equal(DistroOwner.Ours, DistroOwnership.Classify(listed: true, Ours, Ours));

    [Fact]
    public void Listed_elsewhere_is_foreign()
        => Assert.Equal(DistroOwner.Foreign, DistroOwnership.Classify(listed: true, @"D:\wsl\BPP", Ours));

    [Fact]
    public void Sibling_of_our_directory_is_foreign()
        => Assert.Equal(DistroOwner.Foreign, DistroOwnership.Classify(listed: true, Ours + "2", Ours));

    [Fact]
    public void Unknown_location_goes_by_the_name()
        => Assert.Equal(DistroOwner.Ours, DistroOwnership.Classify(listed: true, registeredDir: null, Ours));

    [Theory]
    [InlineData(@"\\?\C:\Users\jan\AppData\Local\BPP\wsl")]
    [InlineData(@"C:\Users\jan\AppData\Local\BPP\wsl\")]
    [InlineData(@"c:\users\JAN\appdata\local\bpp\WSL")]
    [InlineData("C:/Users/jan/AppData/Local/BPP/wsl")]
    [InlineData(@"  \\?\C:\Users\jan\AppData\Local\BPP\wsl\ ")]
    public void Same_directory_in_other_spellings(string spelling) => Assert.True(DistroOwnership.SameDirectory(spelling, Ours));
}
