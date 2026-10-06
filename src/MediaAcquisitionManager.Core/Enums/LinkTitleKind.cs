namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-34 — which title of a TMDb candidate came closest to a library work's titles.</summary>
public enum LinkTitleKind
{
    /// <summary>Its title in the catalog's language.</summary>
    Title = 1,

    /// <summary>Its title in the work's original language.</summary>
    OriginalTitle = 2,

    /// <summary>One of the other titles the catalog knows it by (another country's, another release's).</summary>
    AlternativeTitle = 3,
}
