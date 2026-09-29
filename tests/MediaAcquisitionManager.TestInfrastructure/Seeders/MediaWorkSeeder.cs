using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IMediaWorkSeeder"/>
public sealed class MediaWorkSeeder(IServiceProvider services) : SeederBase(services), IMediaWorkSeeder
{
    /// <summary>The library's own id of the item that reported every row.</summary>
    public const string SourceItemId = "00a49b2db2200514168431dc028f0a0b";

    private IMediaWorkDataFactory Works => new MediaWorkDataFactory(Db, Resolve<TimeProvider>());

    private IMediaVersionDataFactory Versions => new MediaVersionDataFactory(Db, Resolve<TimeProvider>());

    private IPhysicalFileDataFactory Files => new PhysicalFileDataFactory(Db, Resolve<TimeProvider>());

    // "Чужі (1986)", TMDb 679, as the reference library reports its theatrical cut
    // (VendorSamples/Jellyfin/movies-page.json).
    public async Task<MediaWorkEntity> Create(IntegrationConnectionEntity source)
    {
        var work = await Works.Create();
        await Works.AddTitle(work.Id, "Чужі", MediaTitleKind.Display, 1986, source.Id, source.Name, SourceItemId);
        await Works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, "679", source.Id, source.Name, SourceItemId);
        var version = await Versions.Create(work.Id, SourceItemId, sourceConnectionId: source.Id, sourceName: source.Name);
        await Files.Create(
            version.Id,
            "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv",
            sourceConnectionId: source.Id,
            sourceName: source.Name);
        return work;
    }
}
