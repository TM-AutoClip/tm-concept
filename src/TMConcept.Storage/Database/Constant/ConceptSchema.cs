namespace TMConcept.Storage.Database;

/// <summary>
/// The schema of the database file: exactly what EF Core 10 created for 0.1.0–0.1.2 (EnsureCreated), so files of those versions open
/// unchanged. Created once, when the file has no table yet; no migrations before the first public data.
/// </summary>
internal static class ConceptSchema
{
    /// <summary>Every table and index.</summary>
    public const string Create = """
        CREATE TABLE "api_keys" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_api_keys" PRIMARY KEY,
            "Provider" TEXT NOT NULL,
            "Label" TEXT NOT NULL,
            "Secret" TEXT NOT NULL,
            "CreatedAtUtc" INTEGER NOT NULL,
            "LastOkAtUtc" INTEGER NULL
        );
        CREATE TABLE "model_overrides" (
            "Provider" TEXT NOT NULL,
            "ModelId" TEXT NOT NULL,
            "InputUsd" TEXT NOT NULL,
            "OutputUsd" TEXT NOT NULL,
            "CacheReadUsd" TEXT NOT NULL,
            "Note" TEXT NOT NULL,
            "UpdatedAtUtc" INTEGER NOT NULL,
            CONSTRAINT "PK_model_overrides" PRIMARY KEY ("Provider", "ModelId")
        );
        CREATE TABLE "profiles" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_profiles" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "ContentLanguage" TEXT NOT NULL,
            "Format" INTEGER NOT NULL,
            "TargetSeconds" INTEGER NOT NULL,
            "ConceptText" TEXT NOT NULL,
            "TemplateId" TEXT NULL,
            "SpeechEngine" TEXT NOT NULL,
            "SpeechVoice" TEXT NOT NULL,
            "SpeechSpeed" REAL NOT NULL,
            "ConceptCounter" INTEGER NOT NULL,
            "CreatedAtUtc" INTEGER NOT NULL,
            "UpdatedAtUtc" INTEGER NOT NULL
        );
        CREATE TABLE "runs" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_runs" PRIMARY KEY,
            "ProfileId" TEXT NULL,
            "Kind" INTEGER NOT NULL,
            "Provider" TEXT NOT NULL,
            "Model" TEXT NOT NULL,
            "KeyLabel" TEXT NOT NULL,
            "Requested" INTEGER NOT NULL,
            "Accepted" INTEGER NOT NULL,
            "RejectedDuplicates" INTEGER NOT NULL,
            "ExtraPrompt" TEXT NOT NULL,
            "EstimateMinUsd" TEXT NOT NULL,
            "EstimateMaxUsd" TEXT NOT NULL,
            "ActualUsd" TEXT NOT NULL,
            "CostExact" INTEGER NOT NULL,
            "InputTokens" INTEGER NOT NULL,
            "CachedTokens" INTEGER NOT NULL,
            "CacheWriteTokens" INTEGER NOT NULL,
            "OutputTokens" INTEGER NOT NULL,
            "StartedAtUtc" INTEGER NOT NULL,
            "FinishedAtUtc" INTEGER NULL,
            "Status" INTEGER NOT NULL,
            "Error" TEXT NOT NULL
        );
        CREATE TABLE "settings" (
            "Key" TEXT NOT NULL CONSTRAINT "PK_settings" PRIMARY KEY,
            "Value" TEXT NOT NULL
        );
        CREATE TABLE "step_models" (
            "Step" TEXT NOT NULL CONSTRAINT "PK_step_models" PRIMARY KEY,
            "Provider" TEXT NOT NULL,
            "Model" TEXT NOT NULL,
            "KeyId" TEXT NULL,
            "OptionsJson" TEXT NOT NULL
        );
        CREATE TABLE "concepts" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_concepts" PRIMARY KEY,
            "ProfileId" TEXT NOT NULL,
            "Number" INTEGER NOT NULL,
            "Status" INTEGER NOT NULL,
            "Title" TEXT NOT NULL,
            "Summary" TEXT NOT NULL,
            "Content" TEXT NOT NULL,
            "Language" TEXT NOT NULL,
            "RunId" TEXT NULL,
            "SimilarToId" TEXT NULL,
            "Similarity" REAL NULL,
            "DuplicateNote" TEXT NOT NULL,
            "CreatedAtUtc" INTEGER NOT NULL,
            "DecidedAtUtc" INTEGER NULL,
            CONSTRAINT "FK_concepts_profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "profiles" ("Id") ON DELETE CASCADE
        );
        CREATE TABLE "history_digests" (
            "ProfileId" TEXT NOT NULL CONSTRAINT "PK_history_digests" PRIMARY KEY,
            "Version" INTEGER NOT NULL,
            "Text" TEXT NOT NULL,
            "CoversUntilNumber" INTEGER NOT NULL,
            "UpdatedAtUtc" INTEGER NOT NULL,
            CONSTRAINT "FK_history_digests_profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "profiles" ("Id") ON DELETE CASCADE
        );
        CREATE TABLE "profile_images" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_profile_images" PRIMARY KEY,
            "ProfileId" TEXT NOT NULL,
            "FileName" TEXT NOT NULL,
            "Caption" TEXT NOT NULL,
            "Width" INTEGER NOT NULL,
            "Height" INTEGER NOT NULL,
            "Sha256" TEXT NOT NULL,
            "AddedAtUtc" INTEGER NOT NULL,
            CONSTRAINT "FK_profile_images_profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "profiles" ("Id") ON DELETE CASCADE
        );
        CREATE TABLE "concept_embeddings" (
            "ConceptId" TEXT NOT NULL CONSTRAINT "PK_concept_embeddings" PRIMARY KEY,
            "ProfileId" TEXT NOT NULL,
            "Model" TEXT NOT NULL,
            "Dimensions" INTEGER NOT NULL,
            "Vector" BLOB NOT NULL,
            CONSTRAINT "FK_concept_embeddings_concepts_ConceptId" FOREIGN KEY ("ConceptId") REFERENCES "concepts" ("Id") ON DELETE CASCADE
        );
        CREATE TABLE "queue_items" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_queue_items" PRIMARY KEY,
            "ProfileId" TEXT NOT NULL,
            "ConceptId" TEXT NOT NULL,
            "Position" INTEGER NOT NULL,
            "SettingsJson" TEXT NOT NULL,
            "AddedAtUtc" INTEGER NOT NULL,
            CONSTRAINT "FK_queue_items_concepts_ConceptId" FOREIGN KEY ("ConceptId") REFERENCES "concepts" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_queue_items_profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "profiles" ("Id") ON DELETE CASCADE
        );
        CREATE TABLE "videos" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_videos" PRIMARY KEY,
            "ConceptId" TEXT NULL,
            "ProfileId" TEXT NOT NULL,
            "Title" TEXT NOT NULL,
            "Folder" TEXT NOT NULL,
            "FileName" TEXT NOT NULL,
            "ThumbnailName" TEXT NOT NULL,
            "DurationMs" INTEGER NOT NULL,
            "Width" INTEGER NOT NULL,
            "Height" INTEGER NOT NULL,
            "Fps" INTEGER NOT NULL,
            "SizeBytes" INTEGER NOT NULL,
            "Engine" TEXT NOT NULL,
            "EngineVersion" TEXT NOT NULL,
            "CodeModel" TEXT NOT NULL,
            "ConceptModel" TEXT NOT NULL,
            "SpeechModel" TEXT NOT NULL,
            "ImageModel" TEXT NOT NULL,
            "SettingsJson" TEXT NOT NULL,
            "PromptVersion" TEXT NOT NULL,
            "TotalUsd" TEXT NOT NULL,
            "CostExact" INTEGER NOT NULL,
            "ElapsedMs" INTEGER NOT NULL,
            "Warnings" TEXT NOT NULL,
            "CreatedAtUtc" INTEGER NOT NULL,
            CONSTRAINT "FK_videos_concepts_ConceptId" FOREIGN KEY ("ConceptId") REFERENCES "concepts" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_videos_profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "profiles" ("Id") ON DELETE CASCADE
        );
        CREATE TABLE "video_costs" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_video_costs" PRIMARY KEY,
            "VideoId" TEXT NOT NULL,
            "Step" TEXT NOT NULL,
            "RunId" TEXT NULL,
            "Usd" TEXT NOT NULL,
            "Exact" INTEGER NOT NULL,
            CONSTRAINT "FK_video_costs_videos_VideoId" FOREIGN KEY ("VideoId") REFERENCES "videos" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX "IX_api_keys_Provider" ON "api_keys" ("Provider");
        CREATE INDEX "IX_concept_embeddings_ProfileId_Model" ON "concept_embeddings" ("ProfileId", "Model");
        CREATE UNIQUE INDEX "IX_concepts_ProfileId_Number" ON "concepts" ("ProfileId", "Number");
        CREATE INDEX "IX_concepts_ProfileId_Status" ON "concepts" ("ProfileId", "Status");
        CREATE INDEX "IX_profile_images_ProfileId" ON "profile_images" ("ProfileId");
        CREATE INDEX "IX_queue_items_ConceptId" ON "queue_items" ("ConceptId");
        CREATE INDEX "IX_queue_items_ProfileId_Position" ON "queue_items" ("ProfileId", "Position");
        CREATE INDEX "IX_runs_StartedAtUtc" ON "runs" ("StartedAtUtc");
        CREATE INDEX "IX_video_costs_VideoId" ON "video_costs" ("VideoId");
        CREATE INDEX "IX_videos_ConceptId" ON "videos" ("ConceptId");
        CREATE INDEX "IX_videos_ProfileId_CreatedAtUtc" ON "videos" ("ProfileId", "CreatedAtUtc");
        """;
}
