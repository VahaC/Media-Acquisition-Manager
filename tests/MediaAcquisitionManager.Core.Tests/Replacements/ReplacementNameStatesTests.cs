using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-27 — the names a job took out of their folders and hasn't given back (ADR 0030): one on its way into the
// quarantine, in it, on its way back or out, or deleted from it. A job holding one is Restore's, so Keep both refuses it,
// and a copy holding one can't be dropped. A name still at its old path, back there, or lost is none of them — a job whose
// only video is lost would otherwise be stuck for ever.
public sealed class ReplacementNameStatesTests
{
    [Fact]
    public void Taken_are_the_names_out_of_their_folders()
    {
        //Arrange

        //Act
        var taken = ReplacementNameStates.Taken;

        //Assert
        Assert.Equal(
            [
                ReplacementNameState.Moving, ReplacementNameState.Quarantined, ReplacementNameState.Restoring, ReplacementNameState.Deleting,
                ReplacementNameState.Deleted,
            ],
            taken);
    }

    [Theory]
    [InlineData(ReplacementNameState.Planned, false)]
    [InlineData(ReplacementNameState.Moving, true)]
    [InlineData(ReplacementNameState.Quarantined, true)]
    [InlineData(ReplacementNameState.Stayed, false)]
    [InlineData(ReplacementNameState.Restoring, true)]
    [InlineData(ReplacementNameState.Restored, false)]
    [InlineData(ReplacementNameState.Deleting, true)]
    [InlineData(ReplacementNameState.Deleted, true)]
    [InlineData(ReplacementNameState.Lost, false)]
    public void Taken_holds_a_name_only_while_it_is_out_of_its_folder(ReplacementNameState state, bool taken)
    {
        //Arrange

        //Act
        var holds = ReplacementNameStates.Taken.Contains(state);

        //Assert
        Assert.Equal(taken, holds);
    }

    [Fact]
    public void Taken_answers_for_every_name_state()
    {
        //Arrange
        // Guard the guard: a name state a later ticket adds lands in the theory above, or this fails.
        ReplacementNameState[] known =
        [
            ReplacementNameState.Planned, ReplacementNameState.Moving, ReplacementNameState.Quarantined, ReplacementNameState.Stayed,
            ReplacementNameState.Restoring, ReplacementNameState.Restored, ReplacementNameState.Deleting, ReplacementNameState.Deleted,
            ReplacementNameState.Lost,
        ];

        //Act
        var all = Enum.GetValues<ReplacementNameState>();

        //Assert
        Assert.Equal(known.Order(), all.Order());
    }
}
