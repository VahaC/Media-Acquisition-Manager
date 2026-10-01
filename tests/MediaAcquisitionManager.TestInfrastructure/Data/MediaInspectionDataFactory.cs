using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IMediaInspectionDataFactory"/>
public sealed class MediaInspectionDataFactory(ApplicationDbContext db, TimeProvider time) : IMediaInspectionDataFactory
{
    public async Task<MediaInspectionEntity> Create(
        long deviceId,
        long inode,
        long sizeBytes,
        DateTime modifiedUtc,
        IReadOnlyList<MediaInspectionTrackEntity>? tracks = null,
        string? error = null,
        long? durationMilliseconds = null,
        long? bitRate = null)
    {
        var reading = new MediaInspectionEntity
        {
            DeviceId = deviceId,
            Inode = inode,
            SizeBytes = sizeBytes,
            ModifiedUtc = modifiedUtc,
            Error = error,
            DurationMilliseconds = durationMilliseconds,
            BitRate = bitRate,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.MediaInspections.Add(reading);
        var position = 0;
        foreach (var track in tracks ?? [])
        {
            track.MediaInspectionId = reading.Id;
            track.Position = position++;
            db.MediaInspectionTracks.Add(track);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return reading;
    }
}
