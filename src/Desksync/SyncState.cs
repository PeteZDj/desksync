using System.Text.Json;
using System.Text.Json.Nodes;

namespace Desksync;

public sealed record StorageMigration(bool WasBlocked, long PreviousFolderReserve, long PreviousMetadataReserve, long DiskFree, long MetadataFree, DateTimeOffset At);
public sealed record SyncIssue(string Code, string Message, string DeviceId = "", string Path = "");
public sealed record SyncTransfer(string Id, string Name, string Direction, string DeviceId, string DeviceName, long BytesTotal, long BytesDone, string State, DateTimeOffset UpdatedAt)
{
    public int Percent => BytesTotal == 0 ? 0 : (int)Math.Clamp(BytesDone * 100d / BytesTotal, 0, State == "complete" ? 100 : 99);
}
public static class StoragePolicy
{
    public const long FolderReserve = 2_000_000_000, MetadataReserve = 1_000_000_000;
    public static long Required(JsonNode? size, long total)
    {
        var value = double.TryParse(size?["value"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var unit = size?["unit"]?.ToString().ToUpperInvariant() ?? "B";
        var multiplier = unit switch { "%" => total / 100d, "K" or "KB" => 1e3, "M" or "MB" => 1e6, "G" or "GB" => 1e9, "T" or "TB" => 1e12, "KIB" => 1024d, "MIB" => 1048576d, "GIB" => 1073741824d, _ => 1d };
        return (long)Math.Clamp(value * multiplier, 0, long.MaxValue);
    }
    public static bool IsInheritedDefault(JsonNode? size) => size?["unit"]?.ToString() == "%" && double.TryParse(size["value"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value == 1;
}

public sealed partial class Engine
{
    readonly Dictionary<string, SyncTransfer> transfers = [];
    readonly Dictionary<string, (long Sent, long Received, DateTimeOffset At)> networkSamples = [];
    readonly Dictionary<string, JsonNode> pendingCache = [];
    readonly Dictionary<string, long> lastPeerNeed = [];
    DateTimeOffset pendingChecked;
    JsonNode? optionsCache;

    async Task ApplyStoragePolicy()
    {
        optionsCache = await Get("config/options");
        if (Settings.StoragePolicyVersion >= 1) return;
        var folders = await Get("config/folders");
        var folder = folders.AsArray().SingleOrDefault(f => f!["id"]!.ToString() == Settings.FolderId);
        var disk = DiskFor(Settings.Folder);
        var metadata = DiskFor(data);
        var previousFolder = StoragePolicy.Required(folder?["minDiskFree"], disk.TotalSize);
        var previousMetadata = StoragePolicy.Required(optionsCache["minHomeDiskFree"], metadata.TotalSize);
        var changed = false;
        if (StoragePolicy.IsInheritedDefault(optionsCache["minHomeDiskFree"]))
        {
            optionsCache["minHomeDiskFree"] = new JsonObject { ["value"] = 1, ["unit"] = "GB" };
            await Put("config/options", optionsCache); changed = true;
        }
        if (folder != null && StoragePolicy.IsInheritedDefault(folder["minDiskFree"]))
        {
            folder["minDiskFree"] = new JsonObject { ["value"] = 2, ["unit"] = "GB" };
            await Put("config/folders", folders); changed = true;
        }
        Settings = Settings with { StoragePolicyVersion = 1, StorageMigration = changed ? new(disk.AvailableFreeSpace < previousFolder || metadata.AvailableFreeSpace < previousMetadata, previousFolder, previousMetadata, disk.AvailableFreeSpace, metadata.AvailableFreeSpace, DateTimeOffset.UtcNow) : null };
        Save();
        if (changed && folder != null) { using var response = await http.PostAsync("db/scan?folder=" + Uri.EscapeDataString(Settings.FolderId), null); }
    }

    public async Task Rescan()
    {
        using var response = await http.PostAsync("db/scan?folder=" + Uri.EscapeDataString(Settings.FolderId), null);
        if (!response.IsSuccessStatusCode) throw new IOException("The folder could not be scanned. Check the storage and folder errors shown below.");
    }

    void AddActivity(string id, string name, string action, string status = "complete", string detail = "", DateTimeOffset? at = null)
    {
        if (!activity.Any(a => a.Id == id)) activity.Insert(0, new(id, (at ?? DateTimeOffset.UtcNow).ToString("o"), name, action, status, detail));
    }

    async Task ReadEvents()
    {
        if (!activityLoaded)
        {
            activityLoaded = true;
            try { if (File.Exists(Path.Combine(data, "activity.json"))) activity.AddRange(JsonSerializer.Deserialize<List<SyncActivity>>(File.ReadAllText(Path.Combine(data, "activity.json"))) ?? []); }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        var events = await Get("events?since=" + eventCursor + "&timeout=1&limit=300&events=ItemStarted,ItemFinished,LocalChangeDetected,FolderErrors,DeviceConnected,DeviceDisconnected,DownloadProgress,RemoteDownloadProgress");
        foreach (var e in events.AsArray())
        {
            eventCursor = Math.Max(eventCursor, e!["id"]!.GetValue<long>());
            var kind = e["type"]!.ToString(); var body = e["data"]!;
            var at = DateTimeOffset.TryParse(e["time"]?.ToString(), out var parsed) ? parsed : DateTimeOffset.UtcNow;
            if (body["folder"] != null && body["folder"]!.ToString() != Settings.FolderId) continue;
            if (kind == "DownloadProgress")
            {
                foreach (var key in transfers.Where(t => t.Value.Direction == "download").Select(t => t.Key).ToArray()) transfers.Remove(key);
                if (body[Settings.FolderId] is JsonObject progress)
                    foreach (var (name, value) in progress)
                    {
                        var id = "download|" + name;
                        transfers[id] = new(id, name, "download", "", "Your devices", value?["bytesTotal"]?.GetValue<long>() ?? 0, value?["bytesDone"]?.GetValue<long>() ?? 0, "active", at);
                    }
                continue;
            }
            if (kind == "RemoteDownloadProgress")
            {
                var device = body["device"]!.ToString(); var prefix = "upload|" + device + "|";
                foreach (var key in transfers.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) transfers.Remove(key);
                if (body["state"] is JsonObject progress)
                    foreach (var (name, blocks) in progress.Take(30))
                    {
                        var file = await Get("db/file?folder=" + Uri.EscapeDataString(Settings.FolderId) + "&file=" + Uri.EscapeDataString(name));
                        var size = file["global"]?["size"]?.GetValue<long>() ?? 0;
                        var blockSize = file["global"]?["blockSize"]?.GetValue<long>() ?? 0;
                        if(blockSize<=0){var count=file["global"]?["numBlocks"]?.GetValue<long>()??0;blockSize=count>0&&size>0?Math.Max(131072,1L<<(int)Math.Ceiling(Math.Log2((double)size/count))):131072;}
                        var id = prefix + name;
                        transfers[id] = new(id, name, "upload", device, "", size, Math.Min(size, (blocks?.GetValue<long>() ?? 0) * blockSize), "active", at);
                    }
                continue;
            }
            var nameValue = body["item"]?.ToString() ?? body["path"]?.ToString() ?? "Device connection";
            if (kind == "ItemStarted")
            {
                var id = "download|" + nameValue;
                transfers[id] = new(id, nameValue, "download", "", "Your devices", 0, 0, "active", at); continue;
            }
            var error = body["error"]?.ToString() ?? "";
            var action = body["action"]?.ToString() ?? "";
            if (kind == "ItemFinished") transfers.Remove("download|" + nameValue);
            var text = kind == "LocalChangeDetected" ? action.Contains("delete") ? "Deleted locally" : "Changed locally" : kind == "ItemFinished" ? action == "delete" ? "Deletion received" : "Downloaded" : kind == "DeviceConnected" ? "Device connected" : kind == "DeviceDisconnected" ? "Device disconnected" : "Folder needs attention";
            if (kind == "DeviceConnected" || kind == "DeviceDisconnected") nameValue = body["deviceName"]?.ToString() ?? "Device connection";
            AddActivity(e["time"]!.ToString() + "|" + kind + "|" + nameValue, nameValue, text, error.Length > 0 || kind == "FolderErrors" ? "error" : "complete", error, at);
        }
        if (activity.Count > 200) activity.RemoveRange(200, activity.Count - 200);
        var filePath = Path.Combine(data, "activity.json"); File.WriteAllText(filePath + ".tmp", JsonSerializer.Serialize(activity)); File.Move(filePath + ".tmp", filePath, true);
    }

    async Task<object> ReadSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var folder = (await Get("config/folders")).AsArray().SingleOrDefault(f => f!["id"]!.ToString() == Settings.FolderId);
        var connections = (await Get("system/connections"))["connections"]!.AsObject();
        var config = (await Get("config/devices")).AsArray();
        JsonNode? summary = folder == null ? null : await Get("db/status?folder=" + Uri.EscapeDataString(Settings.FolderId));
        var disk = DiskFor(Settings.Folder); var metadata = DiskFor(data);
        optionsCache ??= await Get("config/options");
        var reserve = StoragePolicy.Required(folder?["minDiskFree"], disk.TotalSize);
        var metadataReserve = StoragePolicy.Required(optionsCache["minHomeDiskFree"], metadata.TotalSize);
        var issues = new List<SyncIssue>();
        if (metadata.AvailableFreeSpace < metadataReserve) issues.Add(new("metadata-space", $"Not enough space for the sync index. Free {Math.Ceiling((metadataReserve - metadata.AvailableFreeSpace) / 1e9):0.0} GB or move Desksync to another drive."));
        if (folder != null && disk.AvailableFreeSpace < reserve) issues.Add(new("folder-space", $"Sync is blocked by low disk space. Keep at least {reserve / 1e9:0.0} GB free on this drive."));
        if (summary?["error"]?.ToString() is { Length: > 0 } error) issues.Add(new("folder-error", error));
        if (summary?["watchError"]?.ToString() is { Length: > 0 } watchError) issues.Add(new("watcher-error", watchError));
        if (summary?["pullErrors"]?.GetValue<int>() > 0 || summary?["errors"]?.GetValue<int>() > 0 || summary?["state"]?.ToString() == "error")
        {
            var detail = await Get("folder/errors?folder=" + Uri.EscapeDataString(Settings.FolderId) + "&perpage=20");
            foreach (var item in detail["errors"]?.AsArray() ?? []) issues.Add(new("file-error", item?["error"]?.ToString() ?? "A file could not be synchronized.", "", item?["path"]?.ToString() ?? ""));
            if (issues.Count == 0) issues.Add(new("engine-error", "The sync engine needs attention. Choose Scan now to retry and check folder permissions."));
        }
        try { await ReadEvents(); } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException) { }
        var deviceList = new List<object>(); long uploadNeededBytes = 0, uploadNeededItems = 0; double sentRate = 0, receivedRate = 0;
        var refreshQueue = folder != null && now - pendingChecked > TimeSpan.FromSeconds(10);
        if (refreshQueue) pendingChecked = now;
        var configured = config.Where(d => d!["deviceID"]!.ToString() != DeviceId).ToArray();
        foreach (var entry in configured)
        {
            var id = entry!["deviceID"]!.ToString(); var name = entry["name"]?.ToString() ?? "Computer";
            var c = connections[id]; var connected = c?["connected"]?.GetValue<bool>() == true;
            var sent = c?["outBytesTotal"]?.GetValue<long>() ?? 0; var received = c?["inBytesTotal"]?.GetValue<long>() ?? 0;
            double up = 0, down = 0;
            if (connected && networkSamples.TryGetValue(id, out var previous) && (now - previous.At).TotalSeconds > .2)
            { up = Math.Max(0, sent - previous.Sent) / (now - previous.At).TotalSeconds; down = Math.Max(0, received - previous.Received) / (now - previous.At).TotalSeconds; }
            networkSamples[id] = (sent, received, now); sentRate += up; receivedRate += down;
            JsonNode? completion = null;
            if (folder != null && connected)
            {
                try { completion = await Get("db/completion?device=" + Uri.EscapeDataString(id) + "&folder=" + Uri.EscapeDataString(Settings.FolderId)); }
                catch (HttpRequestException) { issues.Add(new("peer-status", "Unable to read sync completion for " + name + ".", id)); }
            }
            var needBytes = completion?["needBytes"]?.GetValue<long>() ?? 0;
            var needItems = completion?["needItems"]?.GetValue<long>() ?? 0;
            uploadNeededBytes += needBytes; uploadNeededItems += needItems;
            var remoteState = completion?["remoteState"]?.ToString() ?? "unknown";
            if (connected && remoteState == "notSharing") issues.Add(new("not-sharing", name + " is connected but has not accepted this folder. Reconnect its server in Settings.", id));
            if (connected && remoteState == "paused") issues.Add(new("peer-paused", "Sync is paused on " + name + ". Resume it on that computer.", id));
            if (connected && needItems > 0 && refreshQueue)
            {
                try { pendingCache[id] = await Get("db/remoteneed?folder=" + Uri.EscapeDataString(Settings.FolderId) + "&device=" + Uri.EscapeDataString(id) + "&perpage=20"); }
                catch (HttpRequestException) { }
            }
            if (connected && remoteState == "valid" && needItems == 0)
            {
                if (lastPeerNeed.GetValueOrDefault(id) > 0 && pendingCache.TryGetValue(id, out var completed))
                    foreach (var file in completed["files"]?.AsArray() ?? [])
                        AddActivity("upload-complete|" + id + "|" + file?["name"] + "|" + file?["sequence"], file?["name"]?.ToString() ?? "Files", "Uploaded to " + name);
                pendingCache.Remove(id);
            }
            if (connected) lastPeerNeed[id] = needItems;
            var activeNames = new HashSet<string>();
            foreach (var key in transfers.Where(t => t.Value.DeviceId == id).Select(t => t.Key).ToArray())
            {
                var transfer = transfers[key];
                if (!connected || needItems == 0 || now - transfer.UpdatedAt > TimeSpan.FromSeconds(25)) { transfers.Remove(key); continue; }
                transfers[key] = transfer with { DeviceName = name }; activeNames.Add(transfer.Name);
            }
            if (connected && needItems > 0 && pendingCache.TryGetValue(id, out var queue))
                foreach (var file in queue["files"]?.AsArray() ?? [])
                {
                    var fileName = file?["name"]?.ToString() ?? ""; if (fileName.Length == 0 || activeNames.Contains(fileName)) continue;
                    var key = "upload|" + id + "|" + fileName;
                    transfers[key] = new(key, fileName, "upload", id, name, file?["size"]?.GetValue<long>() ?? 0, 0, "queued", now);
                }
            deviceList.Add(new { id, name, connected, type = c?["type"]?.ToString() ?? "", sent, received, uploadRate = up, downloadRate = down, neededBytes = needBytes, neededItems = needItems, completion = completion?["completion"]?.GetValue<double>(), remoteState, remoteSequence = completion?["sequence"]?.GetValue<long>() ?? 0 });
        }
        if (folder != null && summary?["needTotalItems"]?.GetValue<long>() > 0 && refreshQueue)
        {
            var need = await Get("db/need?folder=" + Uri.EscapeDataString(Settings.FolderId) + "&perpage=20");
            foreach (var item in (need["progress"]?.AsArray() ?? []).Concat(need["queued"]?.AsArray() ?? []).Concat(need["rest"]?.AsArray() ?? []))
            {
                var name = item?["name"]?.ToString() ?? ""; var id = "download|" + name;
                if (name.Length > 0 && !transfers.ContainsKey(id)) transfers[id] = new(id, name, "download", "", "Your devices", item?["size"]?.GetValue<long>() ?? 0, 0, "queued", now);
            }
        }
        if ((summary?["needTotalItems"]?.GetValue<long>() ?? 0) == 0)
            foreach (var key in transfers.Where(t => t.Value.Direction == "download").Select(t => t.Key).ToArray()) transfers.Remove(key);
        var active = transfers.Values.Where(t => now - t.UpdatedAt < TimeSpan.FromSeconds(30)).OrderBy(t => t.State == "queued").Take(40).ToArray();
        foreach (var key in transfers.Where(t => now - t.Value.UpdatedAt >= TimeSpan.FromSeconds(30)).Select(t => t.Key).ToArray()) transfers.Remove(key);
        var phase = summary?["state"]?.ToString() ?? "not-configured";
        var paused = folder?["paused"]?.GetValue<bool>() == true;
        var presentation = paused ? "paused" : issues.Count > 0 ? "attention" : !configured.Any(d => connections[d!["deviceID"]!.ToString()]?["connected"]?.GetValue<bool>() == true) ? "offline" : phase.Contains("scan", StringComparison.OrdinalIgnoreCase) ? "scanning" : uploadNeededItems > 0 || (summary?["needTotalItems"]?.GetValue<long>() ?? 0) > 0 || active.Length > 0 ? "syncing" : "up-to-date";
        JsonNode? health = null;
        try { var healthFile = Path.Combine(data, "device-health.json"); if (File.Exists(healthFile)) health = JsonNode.Parse(await File.ReadAllTextAsync(healthFile)); } catch (Exception e) when (e is IOException or JsonException) { }
        var localItems = activity.Take(100).Select(LocalFiles.ActivityPath).Concat(active.Select(t => t.Name)).OfType<string>().Distinct(StringComparer.Ordinal).ToDictionary(name => name, name => LocalFiles.Describe(Settings.Folder, name), StringComparer.Ordinal);
        return new { deviceId = DeviceId, deviceName = Settings.DeviceName, folder = Settings.Folder, folderId = Settings.FolderId, role = Settings.Role, provider = Settings.Provider, setupCompleted = Settings.SetupCompleted, paused, phase, presentation, files = summary?["localFiles"]?.GetValue<long>() ?? 0, bytes = summary?["localBytes"]?.GetValue<long>() ?? 0, globalBytes = summary?["globalBytes"]?.GetValue<long>() ?? 0, neededBytes = summary?["needBytes"]?.GetValue<long>() ?? 0, uploadNeededBytes, uploadNeededItems, needed = summary?["needTotalItems"]?.GetValue<long>() ?? 0, errors = issues.Count, issues, diskTotal = disk.TotalSize, diskFree = disk.AvailableFreeSpace, folderReserveBytes = reserve, metadataFree = metadata.AvailableFreeSpace, metadataReserveBytes = metadataReserve, storageMigration = Settings.StorageMigration, devices = deviceList, transfers = active, uploadRate = sentRate, downloadRate = receivedRate, peerHealth = health, localItems, activity = activity.Take(100).ToArray() };
    }
}

public static class PeerHealth
{
    public static void Merge(JsonNode snapshot, JsonNode? accountHealth)
    {
        var combined = new JsonObject();
        foreach (var source in new[] { snapshot["peerHealth"], accountHealth })
            if (source is JsonObject map) foreach (var (id, value) in map)
                if (value != null && DateTimeOffset.TryParse(value["at"]?.ToString(), out var stamp) && stamp <= DateTimeOffset.UtcNow.AddMinutes(1) && DateTimeOffset.UtcNow - stamp < TimeSpan.FromSeconds(90) && (!DateTimeOffset.TryParse(combined[id]?["at"]?.ToString(), out var existing) || stamp > existing)) combined[id] = value.DeepClone();
        snapshot["peerHealth"] = combined;
        var issues = snapshot["issues"]!.AsArray();
        foreach (var device in snapshot["devices"]!.AsArray().Where(d => d?["connected"]?.GetValue<bool>() == true))
        {
            var id = device!["id"]!.ToString(); var health = combined[id];
            if (health?["issues"] is not JsonArray remoteIssues) continue;
            foreach (var issue in remoteIssues.Take(20))
                issues.Add(new JsonObject { ["Code"] = "remote-" + issue?["code"]?.ToString(), ["Message"] = device["name"]!.ToString() + ": " + issue?["message"]?.ToString(), ["DeviceId"] = id, ["Path"] = "" });
        }
        snapshot["errors"] = issues.Count;
        if (issues.Count > 0 && snapshot["paused"]?.GetValue<bool>() != true) snapshot["presentation"] = "attention";
    }
}
