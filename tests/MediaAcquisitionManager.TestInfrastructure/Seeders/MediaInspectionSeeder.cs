using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IMediaInspectionSeeder"/>
public sealed class MediaInspectionSeeder(IServiceProvider services) : SeederBase(services), IMediaInspectionSeeder
{
    private IMediaInspectionDataFactory Readings => new MediaInspectionDataFactory(Db, Resolve<TimeProvider>());

    private IPhysicalFileDataFactory Files => new PhysicalFileDataFactory(Db, Resolve<TimeProvider>());

    public async Task<MediaInspectionEntity> Measure(string servicePath, TimeSpan duration)
    {
        var file = await Db.PhysicalFiles.AsNoTracking().SingleAsync(row => row.ServicePath == servicePath);
        // A fresh inode per reading: the readings' cache key (device, inode, size, modified time) is unique.
        var reading = await Readings.Create(
            TestMedia.DeviceId,
            Random.Shared.NextInt64(1, long.MaxValue),
            file.SizeBytes ?? 1,
            TestMedia.ModifiedUtc,
            durationMilliseconds: (long)duration.TotalMilliseconds);
        await Files.Link(file.Id, reading.Id);
        return reading;
    }
}
