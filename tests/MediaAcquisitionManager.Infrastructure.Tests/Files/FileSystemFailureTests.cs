using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-18 — the sentence a failed file system call becomes. On Linux the walk lists a folder through
// /proc/self/fd/<n>, and .NET ends its message with that path: the owner reads the sentence on the Library page, so it
// names the folder, never a descriptor. A call made on the path the sentence names keeps the system's words whole.
public sealed class FileSystemFailureTests
{
    private const string FolderAppPath = "/media/Films";
    private const string DescriptorPath = "/proc/self/fd/23";

    [Fact]
    public void For_an_io_error_of_a_call_on_another_path_leaves_that_path_out_of_the_sentence()
    {
        //Arrange
        var exception = new IOException($"Input/output error : '{DescriptorPath}'");

        //Act
        var result = FileSystemFailure.For<FileFacts>(exception, FolderAppPath, DescriptorPath);

        //Assert
        Assert.Equal(
            (FileInventoryStatus.Unreadable, null, "The app couldn't read “/media/Films”: Input/output error."),
            (result.Status, result.Value, result.Error));
        Assert.DoesNotContain("/proc/", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void For_an_io_error_without_the_called_path_keeps_the_systems_message_whole()
    {
        //Arrange
        var exception = new IOException($"Input/output error : '{DescriptorPath}'");

        //Act
        var result = FileSystemFailure.For<FileFacts>(exception, FolderAppPath);

        //Assert
        Assert.Equal(
            (FileInventoryStatus.Unreadable, null, "The app couldn't read “/media/Films”: Input/output error : '/proc/self/fd/23'."),
            (result.Status, result.Value, result.Error));
    }

    [Fact]
    public void For_an_io_error_that_names_the_called_path_in_its_own_words_keeps_the_message_as_it_is()
    {
        //Arrange
        // Only .NET's own ending is left out; a message that merely mentions the path is the system's to say.
        var exception = new IOException($"Stale file handle on {DescriptorPath}");

        //Act
        var result = FileSystemFailure.For<FileFacts>(exception, FolderAppPath, DescriptorPath);

        //Assert
        Assert.Equal("The app couldn't read “/media/Films”: Stale file handle on /proc/self/fd/23.", result.Error);
    }
}
