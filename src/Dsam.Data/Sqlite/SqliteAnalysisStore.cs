using Dsam.Core.Analysis;
using Dsam.Core.Disassembly;
using Microsoft.Data.Sqlite;

namespace Dsam.Data.Sqlite;

public sealed class SqliteAnalysisStore : IAnalysisStore
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteAnalysisStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = builder.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? ".");

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            """, cancellationToken);

        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS labels (
                address TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                kind TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS comments (
                address TEXT NOT NULL,
                kind TEXT NOT NULL,
                text TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY (address, kind)
            );

            CREATE TABLE IF NOT EXISTS functions (
                entry_address TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                start_address TEXT NOT NULL,
                end_address TEXT NOT NULL,
                status TEXT NOT NULL,
                prototype TEXT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS basic_blocks (
                function_entry_address TEXT NOT NULL,
                start_address TEXT NOT NULL,
                end_address TEXT NOT NULL,
                PRIMARY KEY (function_entry_address, start_address),
                FOREIGN KEY (function_entry_address) REFERENCES functions(entry_address) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS instructions (
                address TEXT PRIMARY KEY,
                file_offset INTEGER NOT NULL,
                size INTEGER NOT NULL,
                mnemonic TEXT NOT NULL,
                text TEXT NOT NULL,
                bytes BLOB NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS xrefs (
                from_address TEXT NOT NULL,
                to_address TEXT NOT NULL,
                kind TEXT NOT NULL,
                operand_index INTEGER NOT NULL,
                PRIMARY KEY (from_address, to_address, kind, operand_index)
            );

            CREATE INDEX IF NOT EXISTS ix_xrefs_to_address ON xrefs(to_address);
            CREATE INDEX IF NOT EXISTS ix_instructions_file_offset ON instructions(file_offset);
            """, cancellationToken);

        await UpsertMetadataAsync(connection, "schema_version", "1", cancellationToken);
        await UpsertMetadataAsync(connection, "created_by", "Dsam", cancellationToken);
    }

    public async Task UpsertLabelAsync(Label label, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO labels(address, name, kind, updated_utc)
            VALUES ($address, $name, $kind, $updatedUtc)
            ON CONFLICT(address) DO UPDATE SET
                name = excluded.name,
                kind = excluded.kind,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$address", AddressStorage.Format(label.Address));
        command.Parameters.AddWithValue("$name", label.Name);
        command.Parameters.AddWithValue("$kind", label.Kind.ToString());
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertCommentAsync(Comment comment, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO comments(address, kind, text, updated_utc)
            VALUES ($address, $kind, $text, $updatedUtc)
            ON CONFLICT(address, kind) DO UPDATE SET
                text = excluded.text,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$address", AddressStorage.Format(comment.Address));
        command.Parameters.AddWithValue("$kind", comment.Kind.ToString());
        command.Parameters.AddWithValue("$text", comment.Text);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertFunctionAsync(FunctionAnalysis function, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO functions(entry_address, name, start_address, end_address, status, prototype, updated_utc)
                VALUES ($entry, $name, $start, $end, $status, $prototype, $updatedUtc)
                ON CONFLICT(entry_address) DO UPDATE SET
                    name = excluded.name,
                    start_address = excluded.start_address,
                    end_address = excluded.end_address,
                    status = excluded.status,
                    prototype = excluded.prototype,
                    updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$entry", AddressStorage.Format(function.EntryAddress));
            command.Parameters.AddWithValue("$name", function.Name);
            command.Parameters.AddWithValue("$start", AddressStorage.Format(function.StartAddress));
            command.Parameters.AddWithValue("$end", AddressStorage.Format(function.EndAddress));
            command.Parameters.AddWithValue("$status", function.Status.ToString());
            command.Parameters.AddWithValue("$prototype", (object?)function.Prototype ?? DBNull.Value);
            command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (function.BasicBlocks is not null)
        {
            await using (var deleteBlocks = connection.CreateCommand())
            {
                deleteBlocks.Transaction = (SqliteTransaction)transaction;
                deleteBlocks.CommandText = "DELETE FROM basic_blocks WHERE function_entry_address = $entry;";
                deleteBlocks.Parameters.AddWithValue("$entry", AddressStorage.Format(function.EntryAddress));
                await deleteBlocks.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var block in function.BasicBlocks)
            {
                await using var insertBlock = connection.CreateCommand();
                insertBlock.Transaction = (SqliteTransaction)transaction;
                insertBlock.CommandText = """
                    INSERT INTO basic_blocks(function_entry_address, start_address, end_address)
                    VALUES ($entry, $start, $end);
                    """;
                insertBlock.Parameters.AddWithValue("$entry", AddressStorage.Format(function.EntryAddress));
                insertBlock.Parameters.AddWithValue("$start", AddressStorage.Format(block.StartAddress));
                insertBlock.Parameters.AddWithValue("$end", AddressStorage.Format(block.EndAddress));
                await insertBlock.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveInstructionsAsync(IEnumerable<DecodedInstruction> instructions, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var instruction in instructions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO instructions(address, file_offset, size, mnemonic, text, bytes, updated_utc)
                    VALUES ($address, $fileOffset, $size, $mnemonic, $text, $bytes, $updatedUtc)
                    ON CONFLICT(address) DO UPDATE SET
                        file_offset = excluded.file_offset,
                        size = excluded.size,
                        mnemonic = excluded.mnemonic,
                        text = excluded.text,
                        bytes = excluded.bytes,
                        updated_utc = excluded.updated_utc;
                    """;
                command.Parameters.AddWithValue("$address", AddressStorage.Format(instruction.Address));
                command.Parameters.AddWithValue("$fileOffset", instruction.FileOffset);
                command.Parameters.AddWithValue("$size", instruction.Length);
                command.Parameters.AddWithValue("$mnemonic", instruction.Mnemonic);
                command.Parameters.AddWithValue("$text", instruction.Text);
                command.Parameters.Add("$bytes", SqliteType.Blob).Value = instruction.Bytes;
                command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteXrefs = connection.CreateCommand())
            {
                deleteXrefs.Transaction = (SqliteTransaction)transaction;
                deleteXrefs.CommandText = "DELETE FROM xrefs WHERE from_address = $from;";
                deleteXrefs.Parameters.AddWithValue("$from", AddressStorage.Format(instruction.Address));
                await deleteXrefs.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var xref in instruction.Xrefs)
            {
                await using var insertXref = connection.CreateCommand();
                insertXref.Transaction = (SqliteTransaction)transaction;
                insertXref.CommandText = """
                    INSERT OR IGNORE INTO xrefs(from_address, to_address, kind, operand_index)
                    VALUES ($from, $to, $kind, $operandIndex);
                    """;
                insertXref.Parameters.AddWithValue("$from", AddressStorage.Format(xref.FromAddress));
                insertXref.Parameters.AddWithValue("$to", AddressStorage.Format(xref.ToAddress));
                insertXref.Parameters.AddWithValue("$kind", xref.Kind.ToString());
                insertXref.Parameters.AddWithValue("$operandIndex", xref.OperandIndex);
                await insertXref.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Xref>> GetXrefsToAsync(ulong address, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT from_address, to_address, kind, operand_index
            FROM xrefs
            WHERE to_address = $to
            ORDER BY from_address;
            """;
        command.Parameters.AddWithValue("$to", AddressStorage.Format(address));

        var results = new List<Xref>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new Xref(
                AddressStorage.Parse(reader.GetString(0)),
                AddressStorage.Parse(reader.GetString(1)),
                Enum.Parse<XrefKind>(reader.GetString(2)),
                reader.GetInt32(3)));
        }

        return results;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertMetadataAsync(
        SqliteConnection connection,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO metadata(key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
