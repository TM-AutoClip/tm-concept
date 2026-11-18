# The database and its upgrade steps

For contributors. TM Concept keeps its data in one encrypted SQLite file (SQLite3 Multiple Ciphers) through EF Core. No SQL is written by
hand anywhere (the architecture test `NoHandWrittenSqlTests` fails on it): EF Core writes every statement, including the schema.

## How the schema changes between versions

Since 0.1.17 the database records its schema version: the upgrade steps it went through, in EF Core's `__EFMigrationsHistory` table.

- **One step per app version that changes the database.** A step is an EF Core migration named after the version (`Version0117`,
  `Version0118`, …) in `src/TMConcept.Storage/Database/Concrete/Migrations/`. It is written by EF Core's tooling, never by hand, and it
  knows only the schema of the step before it.
- **A chain, not a jump.** A database from several versions ago goes through every step it lacks, oldest first. Nobody writes "from 0.1.17
  straight to 0.1.20".
- **`Version0117` is the baseline**: the whole schema of 0.1.17. Databases made by 0.1.16 or earlier recorded nothing and are not opened
  (the unlock window offers to set them aside); 0.1.16 also had a `queue_items.SettingsJson` column that 0.1.17 removed.
- **A released step is never edited.** A mistake in a released step is fixed by the next step.

What happens at the start (`SchemaUpgrader`, called by `Vault.OpenAsync`, `Vault.CreateAsync` and the restore check):

1. A new, empty file gets every step at once.
2. An existing file with steps to go: a copy of it is kept in `<data>/backups/before-upgrade/` (encrypted with the same key; the newest 5
   stay), the steps run on a working copy next to it (`concept.db.upgrading`), each in its own transaction, and only when all of them went
   through does the working copy replace the file. A failed step leaves the file exactly as it was (`DatabaseVersionException`,
   `UpgradeFailed`, with the step's name); the next start tries again.
3. A file with tables but no record (`Unrecorded`) or with a step this version does not know (`Newer`, made by a newer version) is not
   changed.
4. A restored backup goes through the same chain (on its temporary copy, before anything is installed).

## Adding the step of a new version

1. Change the mapping in `src/TMConcept.Storage/Database/Concrete/ConceptDbContext.cs` (and the entity classes in Core).
2. Run once for the version, with its number:

   ```
   tools/database-step.ps1 -Name Version0118      # Windows
   tools/database-step.sh Version0118             # Linux
   ```

   The script installs EF Core's `dotnet-ef` tool and the design package into a temporary folder (nothing of them stays in the repository),
   runs `dotnet ef migrations add`, renames the files to `Version0118.cs` / `Version0118.Designer.cs` (file name = type name), moves the
   model snapshot next to the steps, and writes the compiled model again (`tools/compiled-model.ps1`).
3. Read the generated `Up` method. EF Core rebuilds SQLite tables where SQLite cannot alter them; check that data is kept. When the change
   needs data moved (for example a column split in two), add it inside the step with EF Core's migration operations, still no SQL text.
   When the rows already there need another value than the new column's default (and the generated step must stay as the tool wrote it),
   add a data step to `EfSchemaSteps.AfterStepAsync`: EF Core LINQ (`ExecuteUpdateAsync`), run right after its step on the same working
   copy. `Version0118` has one: the topic suggestions of 0.1.17 become `Picked` (0.1.17 blocked them all and never recorded which ones got
   a concept), the column's default `Shown` is only for new rows. `Version0119` (the templates table `video_styles`, one JSON document per
   template, and the profile's `ChannelPhoto` column) needs none: no row gets a template, and an empty photo is the right value.
   `Version0120` (the documents: `documents`, `document_chunks`, `document_chunk_vectors`, and the concepts' `Sources` column) needs none
   either: no document exists before it, and an older concept has no sources (empty).
   `Version0125Costs` (the runs' `ConceptId` column, indexed: an AI edit names its concept) needs none: an older edit names no concept
   (empty), and its cost is never guessed. Steps of one version from parallel work are named after their subject (`Version0125Costs`,
   `Version0125Documents`); each is generated on its own, the second one after merging the first.
4. Build and run the tests: `SchemaUpgradeTests.TheAppsStepsMatchItsMappingAndANewFileRecordsThem` fails while the mapping and the steps
   differ (EF Core would also refuse to upgrade with pending model changes), and `CompiledModelTests` fails while the compiled model is old.
5. Add a test that upgrades a file of the previous step with real data (see `SchemaUpgradeTests` for the pattern) when the step moves data.
6. Say it in the release notes: "the database is upgraded at the first start; a copy is kept in backups/before-upgrade".

Two versions that change the database in a row need two steps; a version that does not change it adds none.

## Rules that stay

- No hand-written SQL, also not in steps or tests.
- Every database file that comes in goes through the chain (first start, unlock, restore).
- The chain is tested with fake steps (`FakeSchemaSteps`): several versions at once, a failed step leaves the file unchanged, a copy is kept
  before an upgrade, a newer or an unrecorded file is refused.
