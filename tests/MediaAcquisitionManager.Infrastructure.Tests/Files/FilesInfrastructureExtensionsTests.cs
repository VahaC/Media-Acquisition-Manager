using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

public sealed class FilesInfrastructureExtensionsTests
{
    [Fact]
    public void AddFilesInfrastructure_registers_the_local_inventory_as_one_singleton()
    {
        //Arrange
        using var services = new ServiceCollection().AddFilesInfrastructure().BuildServiceProvider();

        //Act
        var inventory = services.GetRequiredService<IFileInventory>();

        //Assert
        Assert.IsType<LocalFileInventory>(inventory);
        // Stateless, so the inspection's parallel reads share one instance.
        Assert.Same(inventory, services.GetRequiredService<IFileInventory>());
    }

    [Fact]
    public void AddFilesInfrastructure_registers_the_local_quarantine_as_one_singleton()
    {
        //Arrange
        using var services = new ServiceCollection().AddFilesInfrastructure().BuildServiceProvider();

        //Act
        var quarantine = services.GetRequiredService<IFileQuarantine>();

        //Assert
        // MAM-26 — the one way the app writes to the media folders, stateless like the inventory beside it.
        Assert.IsType<LocalFileQuarantine>(quarantine);
        Assert.Same(quarantine, services.GetRequiredService<IFileQuarantine>());
    }

    [Fact]
    public void AddFilesInfrastructure_registers_ffprobe_as_the_media_probe()
    {
        //Arrange
        using var services = new ServiceCollection().AddFilesInfrastructure().BuildServiceProvider();

        //Act
        var probe = services.GetRequiredService<IMediaProbeProcess>();

        //Assert
        Assert.IsType<FfprobeProcess>(probe);
        Assert.Same(probe, services.GetRequiredService<IMediaProbeProcess>());
    }
}
