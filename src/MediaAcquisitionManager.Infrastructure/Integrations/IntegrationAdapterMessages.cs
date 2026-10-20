using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations;

/// <summary>
/// MAM-47 (ADR 0042) — the sentences several integrations share word for word, where only a name differs: one key each in
/// <c>integrations.json</c>, made here once, so every adapter and every Api caller names the same key and a translator
/// writes the sentence once (design §1.5: a sentence several areas produce lives with its shared producer). Internal to
/// Infrastructure and visible to the Api, like the adapters themselves.
/// </summary>
/// <remarks>
/// The version gates are not here: Jellyfin "reads libraries from" a version, qBittorrent "works with" one and
/// Transmission is "needed" in one, so each is its adapter's own sentence (one key per wording). A sentence a static
/// reader of an adapter words names the service by the constant its descriptor is built from, so the name is the same
/// in both.
/// </remarks>
internal static class IntegrationAdapterMessages
{
    /// <summary>
    /// A connection test that reached the service and read its version — Jellyfin's (when it names no server),
    /// qBittorrent's and Transmission's words are the same but for the name: "Connected to Transmission 4.0.6."
    /// </summary>
    /// <param name="service">The service's own name (<see cref="IntegrationAdapterDescriptor.DisplayName"/>).</param>
    /// <param name="version">The version as the service reported it, cleaned where it was read.</param>
    public static Message Connected(string service, VendorText version) =>
        Message.Of("integrations.adapter.connected", ("service", service), ("version", version));

    /// <summary>
    /// A download client refused the user name or password the app signed in with — qBittorrent's and Transmission's
    /// words are the same but for the name: "qBittorrent refused the user name or password."
    /// </summary>
    /// <param name="service">The service's own name (<see cref="IntegrationAdapterDescriptor.DisplayName"/>).</param>
    public static Message CredentialsRefused(string service) =>
        Message.Of("integrations.adapter.credentials-refused", ("service", service));

    /// <summary>
    /// A download client's answer to a question about a torrent's files isn't a list of files — qBittorrent's and
    /// Transmission's words are the same but for the name: "The service at this address answered, but not with
    /// Transmission's files."
    /// </summary>
    /// <param name="service">The service's own name (<see cref="IntegrationAdapterDescriptor.DisplayName"/>).</param>
    public static Message NotFiles(string service) =>
        Message.Of("integrations.adapter.not-files", ("service", service));

    /// <summary>
    /// A download client's answer to a question about its torrents isn't a list of torrents — qBittorrent's and
    /// Transmission's words are the same but for the name: "The service at this address answered, but not with
    /// qBittorrent's torrents."
    /// </summary>
    /// <param name="service">The service's own name (<see cref="IntegrationAdapterDescriptor.DisplayName"/>).</param>
    public static Message NotTorrents(string service) =>
        Message.Of("integrations.adapter.not-torrents", ("service", service));

    /// <summary>
    /// A download client's answer to a new torrent is none it gives — qBittorrent's and Transmission's words are the same
    /// but for the name: "The service at this address answered, but not the way Transmission answers a new torrent."
    /// </summary>
    /// <param name="service">The service's own name (<see cref="IntegrationAdapterDescriptor.DisplayName"/>).</param>
    public static Message NotAnAddAnswer(string service) =>
        Message.Of("integrations.adapter.not-an-add-answer", ("service", service));

    /// <summary>
    /// A download client no longer has the torrent a job follows — qBittorrent's and Transmission's words are the same
    /// but for the name: "Transmission doesn't have this torrent any more."
    /// </summary>
    /// <param name="service">The service's own name (<see cref="IntegrationAdapterDescriptor.DisplayName"/>).</param>
    public static Message TorrentGone(string service) =>
        Message.Of("integrations.adapter.torrent-gone", ("service", service));

    /// <summary>
    /// MAM-24 — a download client's list of every torrent with its files outgrew what the app reads
    /// (<see cref="IntegrationsInfrastructureExtensions.MaximumResponseBytes"/>): "qBittorrent's list of every torrent with
    /// its files is larger than the app reads (4 MiB), …". MAM-47 — the limit is a size the template writes, so the
    /// unit follows the reader's language (pinned by <c>EnglishFormatPinTests</c>).
    /// </summary>
    /// <param name="service">The service's own name (<see cref="IntegrationAdapterDescriptor.DisplayName"/>).</param>
    public static Message HoldingsTooLarge(string service) =>
        Message.Of(
            "integrations.adapter.holdings-too-large",
            ("service", service),
            ("limit", new SizeValue(IntegrationsInfrastructureExtensions.MaximumResponseBytes)));

    /// <summary>
    /// An adapter threw out of a call the Api made, so what the integration answered is unknown — the destination
    /// check, the release sources and categories, the download sender and poller and the replacement's holdings say it
    /// alike: "The app couldn't read what “Living room” answered."
    /// </summary>
    /// <param name="integrationName">The integration's name as the owner saved it; the template quotes it.</param>
    public static Message UnreadableAnswer(string integrationName) =>
        Message.Of("integrations.adapter.unreadable-answer", ("name", integrationName));
}
