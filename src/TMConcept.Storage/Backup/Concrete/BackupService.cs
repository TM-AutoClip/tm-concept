using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;

namespace TMConcept.Storage.Backup;

/// <summary>
/// Backups as one file (<c>.tmcbak</c>): <see cref="SealedBox"/> (Argon2id + AES-256-GCM, the backup password) with the magic word
/// <c>TMCBAK03</c> around [header length (4, LE) | header JSON (<see cref="BackupHeader"/>) | database key (32) | encrypted database copy |
/// sample image files]. What goes in is chosen (<see cref="BackupContents"/>): API keys only when chosen (allowed because the whole file is
/// password-encrypted), profiles with their model choices and their sample image files, concepts with the trash, the video list and the
/// costs, settings. The copy is made with SQLite's online backup into a file encrypted with the same key, so no plain data is written to
/// disk at any moment; what was not chosen, the stored backup key and the backup bookkeeping are deleted from the copy before it is sealed.
/// Videos are never in a backup. The backup password is required at the first start; the key derived from it is kept only in the encrypted
/// database (never in a backup file), so daily backups need no password. Opening a backup always needs the password; changing it needs the
/// current one. A forgotten backup password cannot be recovered. A restore is possible only while there is no database yet (first start),
/// says what the file holds before anything is replaced, and brings back exactly that. Only this format and a database of this schema
/// version are restored (no backward compatibility, 7 Nov 2026): a file of format 02 (0.1.1–0.1.5) or a database of another version
/// (0.1.6 and older wrote no schema version) is refused with <see cref="BackupRefusedException"/> before anything changes.
/// </summary>
public sealed class BackupService(IAppPaths paths, DatabaseKey key, Vault vault, ConceptDatabase database, KdfParameters kdf,
    TimeProvider clock, ILogger<BackupService> logger)
{
    private const string KeySetting = "backup-key";
    private const string LastSetting = "backup-last";
    private const string ManualLastSetting = "backup-last-manual";
    private const string ContentsSetting = "backup-contents";

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TMCBAK03");

    // Every backup file starts with "TMCBAK" and its two-digit format.
    private static readonly byte[] MagicFamily = Encoding.ASCII.GetBytes("TMCBAK");

    /// <summary>Whether a backup password was set.</summary>
    public async Task<bool> HasPasswordAsync(CancellationToken cancellationToken) => await LoadKeyAsync(cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Sets the backup password (first start, restore); later backups open with it. A change goes through <see cref="ChangePasswordAsync"/>.</summary>
    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken)
    {
        if (!BackupPasswordPolicy.IsAcceptable(password))
        {
            throw new ArgumentException($"The backup password must be at least {BackupPasswordPolicy.MinimumLength} characters.", nameof(password));
        }

        var derived = SealedBox.Derive(password, kdf);
        var record = new BackupKeyRecord
        {
            Salt = Convert.ToBase64String(derived.Salt),
            MemoryKiB = derived.Kdf.MemoryKiB,
            Iterations = derived.Kdf.Iterations,
            Parallelism = derived.Kdf.Parallelism,
            Key = Convert.ToBase64String(derived.Key),
        };
        CryptographicOperations.ZeroMemory(derived.Key);
        await WriteSettingAsync(KeySetting, JsonSerializer.Serialize(record, StorageJsonContext.Default.BackupKeyRecord), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Backup password set");
    }

    /// <summary>Whether <paramref name="password"/> is the current backup password (false when none is set).</summary>
    public async Task<bool> VerifyPasswordAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (await LoadKeyAsync(cancellationToken).ConfigureAwait(false) is not { } stored)
        {
            return false;
        }

        var candidate = SealedBox.Derive(password, stored.Salt, stored.Kdf);
        try
        {
            return CryptographicOperations.FixedTimeEquals(candidate, stored.Key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
            CryptographicOperations.ZeroMemory(stored.Key);
        }
    }

    /// <summary>
    /// Changes the backup password: refused (false, logged as a warning without any password) when <paramref name="current"/> is not the
    /// current one, so a person at an unlocked computer cannot take the backups over. Later backups open with the new password, older files
    /// with the old one.
    /// </summary>
    public async Task<bool> ChangePasswordAsync(string current, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!await VerifyPasswordAsync(current, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Backup password change refused: the current password was wrong");
            return false;
        }

        await SetPasswordAsync(password, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Backup password changed");
        return true;
    }

    /// <summary>
    /// When the last automatic backup (the daily one, with what automatic backups hold) was written; <c>null</c> before the first. Only this
    /// one decides whether the daily backup is due, so a partial "Back up now" never stands in for it.
    /// </summary>
    public async Task<DateTimeOffset?> LastBackupAsync(CancellationToken cancellationToken) => await TimeSettingAsync(LastSetting, cancellationToken).ConfigureAwait(false);

    /// <summary>When the last backup of any kind (daily, Back up now, before a change) was written; <c>null</c> before the first (shown on the page).</summary>
    public async Task<DateTimeOffset?> LastAnyBackupAsync(CancellationToken cancellationToken)
    {
        var daily = await TimeSettingAsync(LastSetting, cancellationToken).ConfigureAwait(false);
        var other = await TimeSettingAsync(ManualLastSetting, cancellationToken).ConfigureAwait(false);
        return daily is null ? other : other is null ? daily : daily > other ? daily : other;
    }

    /// <summary>What automatic backups hold (<see cref="BackupContents.Automatic"/> until the user saves a choice).</summary>
    public async Task<BackupContents> AutomaticContentsAsync(CancellationToken cancellationToken)
    {
        var row = await SettingAsync(ContentsSetting, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return BackupContents.Automatic;
        }

        try
        {
            return JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.BackupContents) is { IsValid: true } saved ? saved : BackupContents.Automatic;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The saved contents of automatic backups are damaged; the default (everything except the keys) is used");
            return BackupContents.Automatic;
        }
    }

    /// <summary>Saves what automatic backups hold.</summary>
    public async Task SaveAutomaticContentsAsync(BackupContents contents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (!contents.IsValid)
        {
            throw new ArgumentException("Nothing is chosen, or concepts without their profiles.", nameof(contents));
        }

        await WriteSettingAsync(ContentsSetting, JsonSerializer.Serialize(contents, StorageJsonContext.Default.BackupContents), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Setting changed: automatic backups hold {Contents}", contents);
    }

    /// <summary>Writes a backup of the open database with <paramref name="contents"/> to <paramref name="target"/>.</summary>
    public async Task CreateAsync(string target, BackupContents contents, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentNullException.ThrowIfNull(contents);
        if (!contents.IsValid)
        {
            throw new ArgumentException("Nothing is chosen, or concepts without their profiles.", nameof(contents));
        }

        var backupKey = await LoadKeyAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No backup password was set.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var copy = TemporaryFile();
        var dek = key.Copy();
        byte[]? plain = null;
        try
        {
            var passphrase = key.Passphrase();
            using (var source = new SqliteConnection(ConceptDatabase.ConnectionString(paths.DatabaseFile, passphrase)))
            using (var destination = new SqliteConnection(ConceptDatabase.ConnectionString(copy, passphrase)))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            var header = new BackupHeader { CreatedAtUtc = clock.GetUtcNow(), Contents = contents };
            var images = new List<byte[]>();
            await using (var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(copy, passphrase), cancellationToken).ConfigureAwait(false))
            {
                await ScrubAsync(db, contents, cancellationToken).ConfigureAwait(false);
                header.KeyCount = await db.ApiKeys.CountAsync(cancellationToken).ConfigureAwait(false);
                header.ProfileCount = await db.Profiles.CountAsync(cancellationToken).ConfigureAwait(false);
                header.ConceptCount = await db.Concepts.CountAsync(cancellationToken).ConfigureAwait(false);
                header.VideoCount = await db.Videos.CountAsync(cancellationToken).ConfigureAwait(false);
                foreach (var image in await db.ProfileImages.ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await ReadImageAsync(image.FileName, cancellationToken).ConfigureAwait(false) is { } bytes)
                    {
                        images.Add(bytes);
                        header.Images.Add(new BackupImage { Path = image.FileName, Length = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
                    }
                }
            }

            SqliteConnection.ClearAllPools();
            var databaseBytes = await File.ReadAllBytesAsync(copy, cancellationToken).ConfigureAwait(false);
            header.DatabaseLength = databaseBytes.Length;
            var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, StorageJsonContext.Default.BackupHeader);
            plain = new byte[4 + headerBytes.Length + dek.Length + databaseBytes.Length + images.Sum(i => i.Length)];
            BinaryPrimitives.WriteInt32LittleEndian(plain, headerBytes.Length);
            var at = 4;
            foreach (var part in new[] { headerBytes, dek, databaseBytes }.Concat(images))
            {
                part.CopyTo(plain, at);
                at += part.Length;
            }

            CryptographicOperations.ZeroMemory(databaseBytes);
            var sealedData = SealedBox.Seal(Magic, backupKey, plain);
            var temporary = target + ".tmp";
            await File.WriteAllBytesAsync(temporary, sealedData, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
            logger.LogInformation("Backup written ({Kilobytes} KB; holds {Contents}; {Images} sample images)", sealedData.Length / 1024, contents, header.Images.Count);
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }

            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(backupKey.Key);
            Delete(copy);
        }
    }

    /// <summary>
    /// Writes a backup into the backup folder now: with <paramref name="contents"/> (Back up now: its own files and its own last
    /// <see cref="BackupRules.Keep"/>, never counted as the daily backup), or what automatic backups hold when null (the daily kind).
    /// </summary>
    public async Task<string> CreateInFolderAsync(BackupContents? contents, CancellationToken cancellationToken) => contents is null
        ? await CreateNamedAsync(BackupRules.Prefix, await AutomaticContentsAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false)
        : await CreateNamedAsync(BackupRules.ManualPrefix, contents, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The extra backup before a large change (a profile deleted, <see cref="BackupRules.BulkChange"/> or more concepts deleted for good):
    /// taken before the change, so the data can come back: always the profiles and the concepts (what such a change removes), the API keys
    /// and settings as automatic backups hold them. Kept apart from the daily ones (the last
    /// <see cref="BackupRules.Keep"/> of each), so a busy day does not push the older days out. <c>null</c> when no backup password is set (logged).
    /// </summary>
    public async Task<string?> CreateBeforeChangeAsync(string reason, CancellationToken cancellationToken)
    {
        if (!await HasPasswordAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("No backup before the change ({Reason}): no backup password is set", reason);
            return null;
        }

        var automatic = await AutomaticContentsAsync(cancellationToken).ConfigureAwait(false);
        var contents = new BackupContents { Keys = automatic.Keys, Settings = automatic.Settings, Profiles = true, Concepts = true };
        var file = await CreateNamedAsync(BackupRules.ChangePrefix, contents, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Backup before a change written ({Reason})", reason);
        return file;
    }

    /// <summary>
    /// The daily backup: written when the last one is not from today (local date). Returns the file, or <c>null</c> when none was due or no
    /// password is set (the reason is logged).
    /// </summary>
    public async Task<string?> RunDailyAsync(CancellationToken cancellationToken)
    {
        if (!await HasPasswordAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Daily backup skipped: no backup password is set");
            return null;
        }

        var today = clock.GetLocalNow().Date;
        if (await LastBackupAsync(cancellationToken).ConfigureAwait(false) is { } last && TimeZoneInfo.ConvertTime(last, clock.LocalTimeZone).Date >= today)
        {
            return null;
        }

        return await CreateInFolderAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The backups in the backup folder, newest first.</summary>
    public IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(paths.BackupDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(paths.BackupDirectory, "*" + BackupRules.Extension)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupFile(f.FullName, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), f.Length))
            .ToList();
    }

    /// <summary>What a backup holds, without restoring anything (shown before a restore). <see cref="SealedBoxException"/> as for a restore.</summary>
    public async Task<RestoreReport> InspectAsync(string source, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        var payload = await OpenAsync(source, password, cancellationToken).ConfigureAwait(false);
        var copy = TemporaryFile();
        try
        {
            // A backup of another version is refused here already, before the page offers to restore it.
            await File.WriteAllBytesAsync(copy, payload.Database.ToArray(), cancellationToken).ConfigureAwait(false);
            await VerifyAsync(copy, payload.Key, cancellationToken).ConfigureAwait(false);
            return Report(payload.Header);
        }
        finally
        {
            payload.Clear();
            Delete(copy);
        }
    }

    /// <summary>
    /// Restores a backup into this computer's data folder: exactly what it holds (the database copy, and the sample image files of its
    /// profiles into the library folder). Allowed only before a database exists (first start). The key is sealed again with
    /// <paramref name="protector"/> (this computer's keyring or a master password). <see cref="SealedBoxException"/> for a wrong password or
    /// a damaged file — the two are deliberately not told apart.
    /// </summary>
    public async Task<RestoreReport> RestoreAsync(string source, string password, IKeyProtector protector, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(protector);
        if (vault.State != VaultState.NotCreated)
        {
            throw new InvalidOperationException("A backup can be restored only at the first start, before any data exists.");
        }

        var payload = await OpenAsync(source, password, cancellationToken).ConfigureAwait(false);
        var copy = TemporaryFile();
        try
        {
            await File.WriteAllBytesAsync(copy, payload.Database.ToArray(), cancellationToken).ConfigureAwait(false);
            await VerifyAsync(copy, payload.Key, cancellationToken).ConfigureAwait(false);
            if (File.Exists(paths.DatabaseFile))
            {
                // A database file without a key file cannot be opened by anyone; it is moved aside, never deleted.
                File.Move(paths.DatabaseFile, paths.DatabaseFile + ".orphan-" + clock.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            }

            await vault.InstallAsync(copy, payload.Key, protector, cancellationToken).ConfigureAwait(false);
            var images = await RestoreImagesAsync(payload, cancellationToken).ConfigureAwait(false);

            // The backup carries no backup key: it is derived again from the password just entered, so daily backups go on without asking.
            await SetPasswordAsync(password, cancellationToken).ConfigureAwait(false);
            var report = await ReportAsync(payload.Header, images, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Restore finished: {Keys} API keys, {Profiles} profiles, {Images} sample images, {Concepts} concepts, {Videos} videos (holds {Contents})",
                report.Keys, report.Profiles, report.Images, report.Concepts, report.Videos, report.Contents);
            return report;
        }
        finally
        {
            payload.Clear();
            Delete(copy);
        }
    }

    /// <summary>
    /// Removes from the copy what was not chosen, and always the stored backup key and the backup bookkeeping, in one transaction. Secure
    /// delete (SQLite3 Multiple Ciphers is built with it; checked whenever a file comes in) overwrites the removed rows and the pages they
    /// free with zeros, so the sealed copy holds nothing of them (DeletedDataTests). The copy is not compacted: VACUUM is SQL with no EF Core
    /// counterpart, so the zeroed pages stay in it (open decision, release notes 0.1.7). The schema version row stays (a restore reads it).
    /// </summary>
    private static async Task ScrubAsync(ConceptDbContext db, BackupContents contents, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string[] bookkeeping = [KeySetting, LastSetting, ManualLastSetting];
        await db.Settings.Where(s => bookkeeping.Contains(s.Key)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (!contents.Keys)
        {
            await db.ApiKeys.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!contents.Concepts || !contents.Profiles)
        {
            await db.QueueItems.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.VideoCosts.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Videos.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Embeddings.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Concepts.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.HistoryDigests.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Runs.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!contents.Profiles)
        {
            await db.ProfileImages.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ProfileStepModels.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Profiles.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!contents.Settings)
        {
            await db.Settings.Where(s => s.Key != ConceptSchema.VersionSetting).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> ReadImageAsync(string stored, CancellationToken cancellationToken)
    {
        try
        {
            var file = Core.Paths.LibraryPaths.Resolve(paths.LibraryDirectory, stored);
            if (File.Exists(file))
            {
                return await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            }

            logger.LogWarning("A sample image file is missing; the backup holds its record without the file");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "A sample image could not be read for the backup; it holds its record without the file");
        }

        return null;
    }

    /// <summary>Writes the image files back; a different file already at a path is moved aside (never overwritten or deleted).</summary>
    private async Task<int> RestoreImagesAsync(BackupPayload payload, CancellationToken cancellationToken)
    {
        var written = 0;
        var at = 0;
        foreach (var image in payload.Header.Images)
        {
            var bytes = payload.Images.Slice(at, checked((int)image.Length)).ToArray();
            at += bytes.Length;
            try
            {
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), image.Sha256, StringComparison.Ordinal))
                {
                    logger.LogWarning("A sample image in the backup does not match its checksum; it is not restored");
                    continue;
                }

                var target = Core.Paths.LibraryPaths.Resolve(paths.LibraryDirectory, image.Path);
                if (File.Exists(target))
                {
                    var existing = await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false);
                    if (string.Equals(Convert.ToHexStringLower(SHA256.HashData(existing)), image.Sha256, StringComparison.Ordinal))
                    {
                        written++;
                        continue;
                    }

                    File.Move(target, target + ".before-restore-" + clock.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);
                written++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "A sample image of the backup could not be written back");
            }
        }

        return written;
    }

    private static async Task<BackupPayload> OpenAsync(string source, string password, CancellationToken cancellationToken)
    {
        var sealedData = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
        if (sealedData.Length >= Magic.Length && sealedData.AsSpan(0, MagicFamily.Length).SequenceEqual(MagicFamily)
            && !sealedData.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            // Another format of this app (02 = 0.1.1–0.1.5): told apart from a damaged file, and not read.
            throw new BackupRefusedException(
                $"This backup has format {Encoding.ASCII.GetString(sealedData, MagicFamily.Length, Magic.Length - MagicFamily.Length)} of another TM Concept version; only backups of this version can be restored. Nothing was restored.");
        }

        var plain = SealedBox.Open(Magic, password, sealedData);
        try
        {
            var length = plain.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(plain) : -1;
            if (length <= 0 || 4L + length + 32 > plain.Length)
            {
                throw new SealedBoxException("The backup content is damaged.");
            }

            var header = JsonSerializer.Deserialize(plain.AsSpan(4, length), StorageJsonContext.Default.BackupHeader)
                ?? throw new SealedBoxException("The backup content is damaged.");
            var keyAt = 4 + length;
            var databaseAt = keyAt + 32;
            var imagesAt = databaseAt + header.DatabaseLength;
            if (header.DatabaseLength <= 0 || imagesAt + header.Images.Sum(i => i.Length) != plain.Length || header.Images.Any(i => i.Length < 0))
            {
                throw new SealedBoxException("The backup content is damaged.");
            }

            return new BackupPayload(header, plain, keyAt, databaseAt, checked((int)imagesAt));
        }
        catch (JsonException ex)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new SealedBoxException("The backup content is damaged.", ex);
        }
        catch (SealedBoxException)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw;
        }
    }

    private async Task<DerivedKey?> LoadKeyAsync(CancellationToken cancellationToken)
    {
        var row = await SettingAsync(KeySetting, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var record = JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.BackupKeyRecord)
            ?? throw new InvalidDataException("The stored backup key is damaged.");
        return new DerivedKey(Convert.FromBase64String(record.Salt), new KdfParameters(record.MemoryKiB, record.Iterations, record.Parallelism),
            Convert.FromBase64String(record.Key));
    }

    private async Task<string> CreateNamedAsync(string prefix, BackupContents contents, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var target = Path.Combine(paths.BackupDirectory, prefix + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + BackupRules.Extension);
        await CreateAsync(target, contents, cancellationToken).ConfigureAwait(false);
        Prune(prefix);
        await WriteSettingAsync(prefix == BackupRules.Prefix ? LastSetting : ManualLastSetting, now.UtcTicks.ToString(CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);
        return target;
    }

    private async Task<DateTimeOffset?> TimeSettingAsync(string name, CancellationToken cancellationToken)
    {
        var row = await SettingAsync(name, cancellationToken).ConfigureAwait(false);
        return row is not null && long.TryParse(row.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;
    }

    private async Task WriteSettingAsync(string name, string value, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.UpsertAsync(new SettingRow { Key = name, Value = value }, s => s.Key == name, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SettingRow?> SettingAsync(string name, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Settings.FirstOrDefaultAsync(s => s.Key == name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Keeps the newest <see cref="BackupRules.Keep"/> files of one kind (daily, Back up now, or before a change).</summary>
    private void Prune(string prefix)
    {
        // Each kind is its prefix followed by the date (a digit), so "tm-concept-" never takes the manual or change files in.
        bool Kind(string name) => name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsAsciiDigit(name[prefix.Length]);
        foreach (var old in List().Where(f => Kind(System.IO.Path.GetFileName(f.Path))).Skip(BackupRules.Keep))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Old backup could not be deleted");
            }
        }
    }

    /// <summary>
    /// Opens the temporary copy and checks it against this version's schema before anything is installed: a backup of another version
    /// (older or newer) is refused here and nothing in the data folder changes.
    /// </summary>
    private async Task VerifyAsync(string file, byte[] dek, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await ConceptDatabase.OpenAsync(ConceptDatabase.ConnectionString(file, Convert.ToHexString(dek)), cancellationToken)
                .ConfigureAwait(false);
            await db.Settings.CountAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ConceptDatabase.EnsureSchemaAsync(db, cancellationToken).ConfigureAwait(false);
            }
            catch (DatabaseVersionException ex)
            {
                logger.LogError(ex, "Restore refused: the backup was made by another version (schema {Found})", ex.Found);
                throw new BackupRefusedException("This backup was made by another version of TM Concept; only backups of this version can be restored. "
                    + ex.Message + " Nothing was restored.", ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or SqliteException or DbUpdateException)
            {
                logger.LogError(ex, "Restore refused: the backup's database does not have this version's schema");
                throw new BackupRefusedException(ex is InvalidOperationException
                    ? ex.Message
                    : "This backup's database does not have this version's schema; nothing was restored.", ex);
            }
        }
        catch (SqliteException ex)
        {
            throw new SealedBoxException("The database inside the backup does not open; the backup is damaged.", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static RestoreReport Report(BackupHeader header) => new(header.CreatedAtUtc, header.Contents, header.KeyCount,
        header.ProfileCount, header.Images.Count, header.ConceptCount, header.VideoCount);

    private async Task<RestoreReport> ReportAsync(BackupHeader header, int images, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new RestoreReport(header.CreatedAtUtc, header.Contents,
            await db.ApiKeys.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Profiles.CountAsync(cancellationToken).ConfigureAwait(false),
            images,
            await db.Concepts.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Videos.CountAsync(cancellationToken).ConfigureAwait(false));
    }

    private string TemporaryFile()
    {
        Directory.CreateDirectory(paths.DataDirectory);
        return System.IO.Path.Combine(paths.DataDirectory, "backup-" + Guid.NewGuid().ToString("N") + ".tmp");
    }

    private void Delete(string file)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(file);
        }
        catch (IOException ex)
        {
            // The temporary copy is encrypted; it is only left behind when Windows still holds the file. Logged, not hidden.
            logger.LogWarning(ex, "Temporary backup file could not be deleted");
        }
    }
}
