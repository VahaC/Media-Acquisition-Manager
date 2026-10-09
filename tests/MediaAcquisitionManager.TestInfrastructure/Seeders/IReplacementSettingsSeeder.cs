using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>
/// The owner's replacement gates for a test (MAM-24, ADR 0033), planted without the Media folders page — so a POSIX
/// folder such as <c>/media/movies</c> can be ticked on Windows too, as <see cref="IAllowedRootSeeder"/> allows
/// <c>/media</c>.
/// </summary>
public interface IReplacementSettingsSeeder
{
    /// <summary>Switches replacement on and ticks <paramref name="folders"/>, in the order given.</summary>
    Task<IReadOnlyList<ReplacementFolderEntity>> SwitchOn(params string[] folders);

    /// <summary>Ticks <paramref name="folders"/>, in the order given, and leaves the switch as it is (off unless it was
    /// switched on).</summary>
    Task<IReadOnlyList<ReplacementFolderEntity>> Tick(params string[] folders);
}
