using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>Tests for the SDK <see cref="FileMasterKeyBroker"/> as the vault's root key file: a base64 256-bit key file reads as the key (a fresh copy each
/// time); a missing, empty or malformed file reads as why the vault is sealed; a changed file changes the stamp; the file
/// check passes a file closed to all but its owner and names a missing, unreadable or exposed one. Each holds as it did for
/// Wheelhouse's own key source, so a key placed for it still unseals.</summary>
public sealed class FileMasterKeyBrokerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("wheelhouse-key-").FullName;
    private string File => Path.Combine(_folder, "root.key");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AKeyFile_ShouldReadAsItsKey_AFreshCopyEachTime()
    {
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        System.IO.File.WriteAllText(File, Convert.ToBase64String(key) + "\n");
        var source = new FileMasterKeyBroker(File);
        var first = source.Read();
        first.Key.Should().Equal(key);
        first.Problem.Should().BeNull();
        first.ToString().Should().NotContain(Convert.ToBase64String(key));
        first.Key![0] ^= 0xFF;
        source.Read().Key.Should().Equal(key, "a caller clearing its copy leaves the next read whole");
    }

    [Fact]
    public void AMissingOrMalformedFile_ShouldSayWhy()
    {
        var source = new FileMasterKeyBroker(File);
        source.Read().Should().Match<MasterKeyRead>(read => read.Key == null && read.Problem == MasterKeyRead.Missing);
        foreach (var content in new[] { "", "not base64!", Convert.ToBase64String(new byte[16]), Convert.ToBase64String(new byte[33]) })
        {
            System.IO.File.WriteAllText(File, content);
            source.Read().Problem.Should().Be(MasterKeyRead.Invalid, content);
        }
    }

    [Fact]
    public void TheFileCheck_ShouldPassAnOwnerOnlyFile_AndNameAMissingUnreadableOrExposedOne()
    {
        var source = new FileMasterKeyBroker(File);
        source.CheckAccess().Should().Be(MasterKeyRead.Missing);
        System.IO.File.WriteAllText(File, Convert.ToBase64String(new byte[32]));
        if (OperatingSystem.IsWindows())
            return;
        foreach (var mode in new[] { UnixFileMode.UserRead, UnixFileMode.UserRead | UnixFileMode.UserWrite })
        {
            System.IO.File.SetUnixFileMode(File, mode);
            source.CheckAccess().Should().BeNull(mode.ToString());
        }
        foreach (var mode in new[] { UnixFileMode.UserRead | UnixFileMode.GroupRead, UnixFileMode.UserRead | UnixFileMode.OtherRead,
                     UnixFileMode.UserRead | UnixFileMode.OtherWrite })
        {
            System.IO.File.SetUnixFileMode(File, mode);
            source.CheckAccess().Should().Be(MasterKeyRead.Exposed, mode.ToString());
        }
        System.IO.File.SetUnixFileMode(File, UnixFileMode.None);
        if (Environment.UserName != "root")
            source.CheckAccess().Should().Be(MasterKeyRead.Unreadable, "the console's user may not open it");
        System.IO.File.SetUnixFileMode(File, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void AChangedFile_ShouldChangeTheStamp()
    {
        System.IO.File.WriteAllText(File, Convert.ToBase64String(new byte[32]));
        var source = new FileMasterKeyBroker(File);
        var before = source.Read().Stamp;
        System.IO.File.WriteAllText(File, Convert.ToBase64String(new byte[32]) + "\n");
        System.IO.File.SetLastWriteTimeUtc(File, DateTime.UtcNow.AddMinutes(1));
        source.Read().Stamp.Should().NotBe(before);
    }

    [Fact]
    public void TheStamp_ShouldBeTheFilesWriteTimeTicksAndSize()
    {
        System.IO.File.WriteAllText(File, Convert.ToBase64String(new byte[32]) + "\n");
        var file = new FileInfo(File);

        new FileMasterKeyBroker(File).Read().Stamp.Should().Be(
            file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + file.Length);
        new FileMasterKeyBroker(File).Location.Should().Be(File);
    }
}
