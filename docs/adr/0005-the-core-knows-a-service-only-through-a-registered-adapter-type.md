# 5. The core knows a service only through a registered adapter type, never by its product

Status: accepted (0.1.0, MAM-7)

## Context

Specification §2 keeps the core independent of any one media server, indexer or download client,
and the acceptance bar of Phase D asks for a second search or client adapter "without changing the
core state machine", with error handling and the UI not depending on qBittorrent-specific fields.
MAM-7 stores the first integrations — Jellyfin, a Torznab feed (Prowlarr serving Toloka, ADR 0004)
and qBittorrent — so each row now has to say which code talks to it, and the core needs contracts
for the libraries, release sources, download clients, file inventory and metadata of later tickets.

The obvious shapes each leave something behind. A product enum in Core puts product names into the
core and makes every new adapter a Core change and a migration. A table per product multiplies the
migrations, the settings page and every query that lists integrations. A JSON settings bag per row
is a passthrough the rules can't check, and a secret inside it can't get a `Has*` flag or its own
encryption (ADR 0007). And an adapter that claims a kind it can't serve yet — a stub that throws
`NotImplementedException` — fails only when someone uses it. Vasyl decided that MAM-7's adapters
implement their registration and a real connection test, and nothing of any kind contract.

## Decision

**An integration stores the id of an adapter type that Infrastructure registers (`jellyfin`,
`torznab`, `qbittorrent`); the core reaches a service only through `IIntegrationAdapter` and the kind
contracts in Core, found by that id, and no Core namespace, type or member is named after a
product.**

**A type id is part of the on-disk contract.** It matches `^[a-z][a-z0-9-]{1,31}$`, is compared byte
for byte and is stored on every integration of the type. Renaming one orphans those rows: they stay
listed with `adapterDisplayName: null`, editing or testing them answers 409 ("This version of the app
can't connect to …, so this integration can only be removed."), and removing them works.

**The registry is a Strategy.** `IntegrationAdapterRegistry` indexes every registered
`IIntegrationAdapter` by its descriptor's type id and throws, naming the offender, when it is built
with two adapters of one id, a malformed id or a type without a display name. A kind contract
(`IMediaLibraryProvider`, `IReleaseSearchProvider`, `IDownloadClient`, `IMetadataResolver`) is a
further interface of the same adapter class; `Find<T>(typeId)` returns the adapter only when it
implements `T`, so a type can't be used for a kind it doesn't serve, and there is no second
registration to forget. `IFileInventory` is not one: it is the app's own look at its local mounts,
not a connection, so it isn't found by a type id and answers its own `FileInventoryStatus`. The
descriptor says what the type takes: a kind, three credential slots (user name, password, API key),
each `NotUsed`, `Optional` or `Required`, and whether the service reports file paths, which decides
whether path mappings apply (ADR 0003).

**One outcome for every call.** A call to an integration ends in one `IntegrationCallStatus`
(`Succeeded`, `Unreachable`, `TimedOut`, `AuthenticationFailed`, `Refused`, `ServiceError`,
`UnexpectedResponse`, `NotFound`) and one readable sentence, and never throws on an expected
failure; only the caller's own cancellation propagates. The page colours a result by its status,
never by a product's field.

**A connection test answers 200 whatever the service said.** The request asks "does it connect?",
and "no, because the key was refused" is its answer, not a failure of this API: 200
`{ status, message }` for every outcome. 409 stays for what the app itself can't do: a stored secret
it can't read, or a type this build no longer registers.

**Only the test, for now.** Each later ticket adds its kind to the same class — MAM-8 the library to
`JellyfinAdapter`, MAM-10 the search to `TorznabAdapter`, MAM-11 to MAM-14 the client to
`QBittorrentAdapter` — with a convention test that every adapter whose descriptor names a kind
implements that kind's contract. That test can't exist in MAM-7, where the decision makes it fail.

| Rejected option | Why not |
| --- | --- |
| A product enum in Core (`IntegrationType.Jellyfin`) | Product names in the core, and a Core change and a migration for every adapter |
| A table or entity per product | A migration and a settings screen per product; lists and provenance would join across them |
| A JSON settings bag per row | A passthrough the rules can't check; a secret inside gets no `Has*` flag and no encryption of its own |
| Stub kind implementations | Claims a capability that fails at run time; the decision was a real test and nothing more |
| A second registration per kind contract | One more line to forget per adapter; `Find<T>` asks the adapter that is already there |
| 502 for a failed connection test | Reports the service's "no" as the app's failure; the dialog needs the sentence either way |

`CoreIsVendorNeutralTests.No_core_type_member_or_namespace_is_named_after_a_product` refuses a
product name in Core (doc comments may name products).
`IntegrationsInfrastructureExtensionsTests.The_registry_lists_jellyfin_torznab_and_qbittorrent_once_each_with_their_descriptors`
pins the three types and what each takes, and
`IntegrationsConnectionTestHttpTests.Test_is_200_with_the_adapters_status_and_message` the 200 for
every status. `IntegrationsUpdateHttpTests.Update_of_an_integration_of_an_unregistered_type_is_409_with_the_remove_only_sentence`
and `IntegrationsDeleteHttpTests.Delete_of_an_integration_of_an_unregistered_type_is_204` pin the
orphaned row.

## Consequences

A Phase D Transmission or SABnzbd adapter is one class that implements `IIntegrationAdapter` and its
kind contract, one registration and one named HTTP client; the settings page, the rules, the
encryption and the registry need nothing. The three credential slots cover every product the
specification and the roadmap name (a key for Jellyfin, Emby, Plex, Prowlarr, Jackett and SABnzbd; a
user name and password for qBittorrent, Transmission and Deluge); a product that needs a fourth kind
of credential would need a new column.

The kind contracts carry only their Phase A members. MAM-8 to MAM-14 add members as they implement
them, and amend this record when a decision changes.

## Anti-consequence

This keeps product names out of the core's code, not out of its knowledge: doc comments name the
products a contract was shaped against, and the Torznab adapter speaks Torznab, not Prowlarr. It
doesn't make a type id free to rename.

## Implementation (added 0.1.0, MAM-8)

**Jellyfin serves its kind.** `JellyfinAdapter` implements `IMediaLibraryProvider` (one item can
hold several versions), so `Find<IMediaLibraryProvider>("jellyfin")` returns it while `torznab` and
`qbittorrent` still return nothing. The library sync asks the registry by the stored type, and a
media library integration whose type can't read a library is refused with 409 ("This version of the
app can't read a library from “…”."), never by a product name. Kind calls get the test's
guarantees: `HttpIntegrationAdapter.CallAsync<T>` (the Template Method, extended) maps transport
failures and timeouts to the same statuses and sentences and masks the call's secrets, from one
source of sentences shared with the test.

**The version gate is the connection test; the contract has no member for it.** An adapter refuses
a server version it can't read in `TestConnectionAsync`, with the new status `UnsupportedVersion`
(the list above gains it), and a sync runs that test before asking for anything else: the owner's
**Test** button and a sync answer with the same sentence, and the test still answers 200. Jellyfin's
minimum is 10.9, Vasyl's choice. Only 12.1.0 was sampled live; 10.9 to 10.11 are accepted from
reading Jellyfin's source (a user-less `/Items` with an API key, and the `MediaBrowser Token`
header, work since 10.9). An older version gets "This is Jellyfin 10.8.13; the app reads libraries
from Jellyfin 10.9 or later. Update Jellyfin, then test again.", and one without a leading
`major.minor` the sentence that it can't be compared with 10.9: refused, not read through answers
nobody has seen.

**The convention test exists, for the kinds implemented so far.**
`AdapterKindContractTests.Every_media_library_adapter_implements_the_media_library_contract`
(Infrastructure.Tests) takes the registry the app builds and checks that every type whose
descriptor names `MediaLibrary` implements `IMediaLibraryProvider`, and that it found at least one.
Release search joins it with MAM-10 and download clients with MAM-11, when their contracts are
implemented; their adapters still only test a connection.

`JellyfinAdapterTests.TestConnection_of_jellyfin_before_10_9_is_an_unsupported_version`,
`JellyfinAdapterTests.TestConnection_of_a_version_it_can_not_compare_is_an_unsupported_version` and
`LibrarySyncFailureHttpTests.Sync_of_a_jellyfin_older_than_10_9_fails_before_asking_for_anything_else`
pin the gate, `IntegrationsConnectionTestHttpTests.Test_is_200_with_the_adapters_status_and_message`
its 200, and `IntegrationsInfrastructureExtensionsTests.The_registry_finds_jellyfin_and_nothing_else_as_a_media_library`
and `LibrarySyncStartHttpTests.Start_of_a_media_library_whose_type_cant_read_a_library_is_409_with_its_sentence`
the registry's answer.
