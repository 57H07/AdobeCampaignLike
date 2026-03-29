# Deployment Note: AddBodyPathAndChecksumToTemplates Migration

**Migration name:** `20260329165000_AddBodyPathAndChecksumToTemplates`
**Applied in:** US-003 (Database schema migration for BodyPath)
**Date:** 2026-03-29

## Summary

This migration replaces the `HtmlBody`/`DocxBody` column on the `Templates` and `TemplateHistory`
tables with a file-based storage model. Template bodies are now stored on disk; the database holds
only the relative path and an integrity checksum.

### Schema changes

| Table            | Column removed | Columns added                                     |
|------------------|---------------|---------------------------------------------------|
| `Templates`      | `HtmlBody`    | `BodyPath` NVARCHAR(500) NOT NULL                 |
|                  |               | `BodyChecksum` NVARCHAR(64) NULL                  |
|                  |               | `RowVersion` ROWVERSION NOT NULL (concurrency)    |
| `TemplateHistory`| `HtmlBody`    | `BodyPath` NVARCHAR(500) NOT NULL                 |
|                  |               | `BodyChecksum` NVARCHAR(64) NULL                  |

`BodyPath` is a relative path from the configured storage root (e.g.
`templates/00000000-0000-0000-0001-000000000001/v1.html`). It never includes the server root or
an absolute OS path.

`BodyChecksum` is a SHA-256 hex digest of the file content (lowercase, 64 characters). It is
computed at upload time and verified at dispatch time.

## Rollback policy — NO DOWN MIGRATION

**This migration has no automated rollback.**

The `HtmlBody` column is dropped by the `Up` method. The `Down` method recreates the column with
an empty default value, which would result in data loss for any rows that existed before the
migration ran. For this reason, rolling back this migration is **not supported** in production.

**If a rollback is required:**

1. Take the database offline.
2. Restore the most recent pre-migration backup.
3. Redeploy the previous application version.
4. Verify data integrity before bringing the service back online.

Ensure a full database backup is taken and verified **before** running this migration in any
environment where data exists.

## Applying the migration

```bash
dotnet ef database update --project src/CampaignEngine.Infrastructure \
                          --startup-project src/CampaignEngine.Infrastructure
```

The migration applies cleanly on an empty database. On an existing database it requires that
`Templates` and `TemplateHistory` have no rows (or that the operator has accepted the data loss
of `HtmlBody` content and pre-backed-up file bodies separately).

## BodyChecksum format

- Algorithm: SHA-256
- Encoding: lowercase hexadecimal string
- Length: exactly 64 characters
- Example: `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`
  (SHA-256 of empty string)

The checksum column is nullable. A `NULL` checksum means the file has not yet been verified or
was created before integrity checking was enabled.
