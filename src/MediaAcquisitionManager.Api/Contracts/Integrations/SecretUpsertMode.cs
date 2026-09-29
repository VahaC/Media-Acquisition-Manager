namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What a save or a draft test does with one stored secret of an integration.</summary>
/// <remarks>MAM-7 — secrets never come back from the API, so a form can't send the stored value
/// back unchanged; it says what to do with it instead.</remarks>
public enum SecretUpsertMode
{
    /// <summary>Leave the stored secret as it is (on a new integration: store none).</summary>
    Keep = 1,

    /// <summary>Store the value that comes with it.</summary>
    Set = 2,

    /// <summary>Remove the stored secret.</summary>
    Clear = 3,
}
