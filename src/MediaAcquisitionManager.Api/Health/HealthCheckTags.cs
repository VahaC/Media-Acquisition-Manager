namespace MediaAcquisitionManager.Api.Health;

public static class HealthCheckTags
{
    /// <summary>Checks that must pass before the app can serve requests; <c>/api/health/ready</c>
    /// runs exactly these.</summary>
    public const string Ready = "ready";
}
