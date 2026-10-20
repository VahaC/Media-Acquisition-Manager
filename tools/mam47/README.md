# MAM-47 tools

`copy_production_db.py` copies production's database to the owner's PC, so the startup conversion of the stored English
sentences (ADR 0042) can be tried on real data before the release.

```
python D:\Sources\Media-Acquisition-Manager\tools\mam47\copy_production_db.py
python D:\Sources\Media-Acquisition-Manager\tools\mam47\copy_production_db.py --delete
```

- **Read-only on the media host.** One ssh call to `root@192.168.1.195` (ssh asks for the password itself). It copies
  `mam.db` and `mam.db-wal` of the container's `/data` mount until a copy is taken while neither changed, streams them
  back and removes its temporary folder. It never reads `.secrets` and never opens the database on the host (SQLite run
  as root there could leave a root-owned `-shm` the app can't write).
- **On this PC** the copy is checked with `PRAGMA quick_check` and saved as one file at
  `%USERPROFILE%\mam47-production-copy\mam.db`, outside the repository.
- **It holds password and token hashes** and sealed integration keys (without the key that opens them). Delete it with
  `--delete` once the conversion run is done.

Python 3.10+, standard library only; it reuses the ssh runner of `tools/mam34/common.py`.

## Converting the copy

`ProductionCopyConversionTests` runs the startup converter on the copy; it is skipped unless
`MAM47_PRODUCTION_DB_COPY` names the folder above. It copies `mam.db` into a temporary file of its own first, so the
copy itself is never converted, and writes its report to `MAM47_CONVERSION_REPORT`, or to
`mam47-conversion-report.md` beside the copy when that is unset.

```
$env:MAM47_PRODUCTION_DB_COPY="$env:USERPROFILE\mam47-production-copy"
dotnet test tests/MediaAcquisitionManager.Api.Tests -c Release --filter "FullyQualifiedName~ProductionCopyConversionTests"
```

The report quotes production's own words (paths, titles, services' answers): keep it out of the repository, and delete
it with the copy (`--delete` removes only the files the script wrote).
