using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Salyra.Upload;

public static class Disk
{
    public static string Safe(string id)
    {
        if (!Regex.IsMatch(id, "\\A[a-zA-Z0-9_-]{1,100}\\z"))
            throw new UploadException(400, "ID", "Invalid upload identifier");
        return id;
    }

    public static async Task Atomic(string path, JsonNode value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, value.ToJsonString(), token);
            await using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Write))
            {
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public static async Task<JsonObject> Measure(string path, int index, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, token);
        return new()
        {
            ["index"] = index,
            ["size"] = stream.Length,
            ["sha256"] = Convert.ToHexString(hash).ToLowerInvariant(),
            ["reference"] = new JsonObject { ["version"] = 1, ["index"] = index },
        };
    }
}

public sealed class DiskSessions(string directory) : ISessionStore
{
    [DllImport("libc", SetLastError = true)]
    static extern int flock(int descriptor, int operation);

    static void Acquire(FileStream stream)
    {
        if (OperatingSystem.IsWindows())
        {
            stream.Lock(0, 1);
            return;
        }
        if (flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 6) != 0)
            throw new IOException("Session is locked");
    }

    static void Release(FileStream stream)
    {
        if (OperatingSystem.IsWindows())
            stream.Unlock(0, 1);
        else
            flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 8);
    }

    static readonly SemaphoreSlim[] Stripes = Enumerable
        .Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    sealed class Lease(FileStream stream, SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                Release(stream);
            }
            finally
            {
                stream.Dispose();
                semaphore.Release();
            }
            return ValueTask.CompletedTask;
        }
    }

    public async Task<IAsyncDisposable> Lock(string id, CancellationToken token)
    {
        Disk.Safe(id);
        Directory.CreateDirectory(directory);
        var semaphore = Stripes[
            (
                StringComparer.Ordinal.GetHashCode(Path.GetFullPath(Path.Combine(directory, id)))
                & int.MaxValue
            ) % Stripes.Length
        ];
        await semaphore.WaitAsync(token);
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                Path.Combine(directory, id + ".lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            );
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    Acquire(stream);
                    return new Lease(stream, semaphore);
                }
                catch (IOException)
                {
                    await Task.Delay(10, token);
                }
            }
        }
        catch
        {
            stream?.Dispose();
            semaphore.Release();
            throw;
        }
    }

    public async Task<JsonObject?> Get(string id, CancellationToken token)
    {
        string path = Path.Combine(directory, Disk.Safe(id) + ".json");
        return File.Exists(path)
            ? JsonNode.Parse(await File.ReadAllTextAsync(path, token))!.AsObject()
            : null;
    }

    public Task Save(JsonObject session, CancellationToken token) =>
        Disk.Atomic(
            Path.Combine(directory, Disk.Safe(session["id"]!.GetValue<string>()) + ".json"),
            session,
            token
        );
}

public sealed class DiskStorage(string directory) : IStorage
{
    string Parts(JsonObject session) =>
        Path.Combine(directory, "parts", Disk.Safe(session["id"]!.GetValue<string>()));

    string Result(JsonObject session) =>
        Path.Combine(directory, "files", Disk.Safe(session["id"]!.GetValue<string>()));

    public Task<JsonNode?> Begin(JsonObject session, UploadContext context)
    {
        Directory.CreateDirectory(Parts(session));
        return Task.FromResult<JsonNode?>(
            new JsonObject { ["version"] = 1, ["id"] = session["id"]!.DeepClone() }
        );
    }

    public async Task<JsonObject> WritePart(
        JsonObject session,
        JsonObject part,
        Stream body,
        UploadContext context
    )
    {
        int index = part["index"]!.GetValue<int>();
        long expected = part["size"]!.GetValue<long>();
        string checksum = part["sha256"]!.GetValue<string>(),
            path = Path.Combine(Parts(session), index.ToString());
        if (File.Exists(path))
        {
            var existing = await Disk.Measure(path, index, context.Cancellation);
            if (
                existing["size"]!.GetValue<long>() != expected
                || existing["sha256"]!.GetValue<string>() != checksum
            )
                throw new UploadException(409, "PART_CONFLICT", "Chunk contains different data");
        }
        string temp = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            byte[] buffer = new byte[65536];
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                int count;
                while ((count = await body.ReadAsync(buffer, context.Cancellation)) > 0)
                {
                    size += count;
                    if (size > expected)
                        throw new UploadException(413, "PART_SIZE", "Chunk exceeds expected size");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), context.Cancellation);
                }
                if (
                    size != expected
                    || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != checksum
                )
                    throw new UploadException(422, "CHECKSUM", "Chunk checksum does not match");
                output.Flush(true);
            }
            File.Move(temp, path, true);
            var saved = part.DeepClone().AsObject();
            saved["reference"] = new JsonObject { ["version"] = 1, ["index"] = index };
            await Disk.Atomic(path + ".receipt.json", saved, context.Cancellation);
            return saved;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public async Task<List<JsonObject>> Probe(JsonObject session, UploadContext context)
    {
        var parts = new List<JsonObject>();
        string directory = Parts(session);
        if (!Directory.Exists(directory))
            return parts;
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            if (!int.TryParse(Path.GetFileName(path), out int index))
                continue;
            string receipt = path + ".receipt.json";
            JsonObject saved;
            if (File.Exists(receipt))
                saved = JsonNode
                    .Parse(await File.ReadAllTextAsync(receipt, context.Cancellation))!
                    .AsObject();
            else
            {
                saved = await Disk.Measure(path, index, context.Cancellation);
                await Disk.Atomic(receipt, saved, context.Cancellation);
            }
            if (saved["size"]!.GetValue<long>() != new FileInfo(path).Length)
                throw new UploadException(500, "STORAGE_CHECKPOINT", "Stored chunk changed");
            parts.Add(saved);
        }
        return parts.OrderBy(p => p["index"]!.GetValue<int>()).ToList();
    }

    public async Task<JsonNode?> InspectResult(JsonObject session, UploadContext context)
    {
        string path = Result(session);
        if (!File.Exists(path))
            return null;
        var saved = await Disk.Measure(path, 0, context.Cancellation);
        if (saved["size"]!.GetValue<long>() != session["descriptor"]!["size"]!.GetValue<long>())
            throw new UploadException(500, "RESULT_SIZE", "Completed file size does not match");
        return new JsonObject
        {
            ["id"] = session["id"]!.DeepClone(),
            ["size"] = saved["size"]!.DeepClone(),
            ["sha256"] = saved["sha256"]!.DeepClone(),
        };
    }

    public async Task<JsonNode?> Finish(
        JsonObject session,
        List<JsonObject> parts,
        UploadContext context
    )
    {
        var existing = await InspectResult(session, context);
        if (existing is not null)
            return existing;
        string path = Result(session);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            byte[] buffer = new byte[65536];
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                foreach (var part in parts)
                {
                    using var partHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long size = 0;
                    await using var input = File.OpenRead(
                        Path.Combine(Parts(session), part["index"]!.GetValue<int>().ToString())
                    );
                    int count;
                    while ((count = await input.ReadAsync(buffer, context.Cancellation)) > 0)
                    {
                        hash.AppendData(buffer, 0, count);
                        partHash.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), context.Cancellation);
                        size += count;
                    }
                    if (
                        size != part["size"]!.GetValue<long>()
                        || Convert.ToHexString(partHash.GetHashAndReset()).ToLowerInvariant()
                            != part["sha256"]!.GetValue<string>()
                    )
                        throw new UploadException(422, "CHECKSUM", "Saved chunk changed");
                    total += size;
                }
                if (total != session["descriptor"]!["size"]!.GetValue<long>())
                    throw new UploadException(422, "SIZE", "Assembled size does not match");
                output.Flush(true);
            }
            File.Move(temp, path, true);
            Directory.Delete(Parts(session), true);
            return new JsonObject
            {
                ["id"] = session["id"]!.DeepClone(),
                ["size"] = total,
                ["sha256"] = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            };
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public Task Abort(JsonObject session, UploadContext context)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        if (Directory.Exists(Parts(session)))
            Directory.Delete(Parts(session), true);
        return Task.CompletedTask;
    }
}
