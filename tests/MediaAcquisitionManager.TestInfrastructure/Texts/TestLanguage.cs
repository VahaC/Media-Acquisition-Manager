namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — which pass of the suite this process runs. <c>run-tests.sh</c> runs the whole .NET suite twice:
/// once as it always ran, and once with <c>MAM_TEST_LANGUAGE=pseudo</c>, where every request asks for the
/// pseudo-language (<see cref="PseudoTag"/>) and the server writes each of its sentences in English wrapped in
/// <c>⟦…⟧</c>. The English asserts stay as they are; the harness checks the wrapping and strips it, so a sentence that
/// reached the reader without the renderer fails the test that read it.
/// </summary>
public static class TestLanguage
{
    /// <summary>The environment variable that picks the pass.</summary>
    public const string VariableName = "MAM_TEST_LANGUAGE";

    /// <summary>Its value for the pseudo-language pass.</summary>
    public const string PseudoValue = "pseudo";

    /// <summary>The <c>Accept-Language</c> tag of the pseudo-language (the server reads its primary subtag, <c>qps</c>).</summary>
    public const string PseudoTag = "qps-ploc";

    /// <summary>Whether this process runs the pseudo-language pass. Read once: the factory's JSON options and every host
    /// of the process must agree.</summary>
    public static bool IsPseudo { get; } =
        string.Equals(Environment.GetEnvironmentVariable(VariableName), PseudoValue, StringComparison.OrdinalIgnoreCase);
}
