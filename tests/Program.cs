using Desksync;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

var root = Path.Combine(Path.GetTempPath(), "desksync-qa-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Engine? a = null, b = null;
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
async Task WaitFor(Func<bool> condition, string name, int seconds = 45)
{
    var until = DateTime.UtcNow.AddSeconds(seconds);
    while (!condition() && DateTime.UtcNow < until) await Task.Delay(200);
    Check(condition(), name);
}
async Task Prepare(string data, int gui, int sync)
{
    var home = Path.Combine(data, "engine"); Directory.CreateDirectory(home);
    var start = Engine.StartInfo("generate", "--home", home, "--no-port-probing"); start.RedirectStandardOutput = true; start.RedirectStandardError = true;
    using var p = Process.Start(start)!; var output = p.StandardOutput.ReadToEndAsync(); var errors = p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); await Task.WhenAll(output, errors);
    if (p.ExitCode != 0) throw new Exception("Test engine generation failed");
    var path = Path.Combine(home, "config.xml"); var xml = XDocument.Load(path); var options = xml.Root!.Element("options")!;
    xml.Root.Element("gui")!.Element("address")!.Value = "127.0.0.1:" + gui;
    options.Elements("listenAddress").Remove(); options.Add(new XElement("listenAddress", "tcp://127.0.0.1:" + sync));
    foreach (var field in new[] { "globalAnnounceEnabled", "localAnnounceEnabled", "relaysEnabled", "natEnabled", "startBrowser" }) options.Element(field)!.Value = "false";
    xml.Save(path);
}
async Task Direct(Engine engine, string device, int port)
{
    var xml = XDocument.Load(Path.Combine(engine.Home, "engine", "config.xml"));
    using var http = new HttpClient(); http.DefaultRequestHeaders.Add("X-API-Key", xml.Root!.Element("gui")!.Element("apikey")!.Value);
    var url = "http://" + xml.Root!.Element("gui")!.Element("address")!.Value + "/rest/config/devices/" + device;
    var settings = await http.GetFromJsonAsync<JsonNode>(url); settings!["addresses"] = new JsonArray("tcp://127.0.0.1:" + port);
    using var response = await http.PutAsJsonAsync(url, settings); response.EnsureSuccessStatusCode();
}
try
{
    using(var rsa=System.Security.Cryptography.RSA.Create(2048))
    {
        var metadata=System.Text.Encoding.UTF8.GetBytes("{\"version\":\"0.2.1\",\"url\":\"https://afrosoftware.com/downloads/desksync/test.zip\",\"sha256\":\""+new string('a',64)+"\",\"bytes\":100,\"channel\":\"preview\"}");
        var signature=rsa.SignData(metadata,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pss);
        Check(UpdatePolicy.Verify(metadata,signature,rsa.ExportSubjectPublicKeyInfoPem()).Version=="0.2.1","Signed release metadata is accepted");metadata[12]^=1;var rejected=false;try{UpdatePolicy.Verify(metadata,signature,rsa.ExportSubjectPublicKeyInfoPem());}catch(IOException){rejected=true;}Check(rejected,"Tampered update metadata is rejected");
        var unsafePath=false;try{UpdatePolicy.SafeTarget(root,@"..\escape.exe");}catch(IOException){unsafePath=true;}Check(unsafePath,"Update path traversal is rejected");
    }
    foreach(var blocked in new[]{@"C:\",@"C:\Windows",@"\\server\share",Path.Combine(root,"private")}){var rejected=false;try{Engine.ValidateFolder(blocked,Path.Combine(root,"private"));}catch(ArgumentException){rejected=true;}Check(rejected,"Unsafe sync folder is rejected");}
    foreach (var invalid in new[] { "https://evil.test/", "desksync://pair?v=2", "desksync://pair?v=1&device=bad&folder=..", "file:///C:/Windows" })
    {
        var rejected = false; try { Pairing.Parse(invalid); } catch (ArgumentException) { rejected = true; } Check(rejected, "Reject invalid pairing input");
    }
    var homeA = Path.Combine(root, "a"); var homeB = Path.Combine(root, "b");
    await Prepare(homeA, 55391, 55201); await Prepare(homeB, 55392, 55202);
    a = new Engine(homeA, Path.Combine(root, "files-a"), 55391); b = new Engine(homeB, Path.Combine(root, "files-b"), 55392);
    await a.Initialize(); await b.Initialize();
    Check((await a.Get("config/folders")).AsArray().Count == 1, "Exactly one selected folder");
    Check((await a.Get("config/devices")).AsArray().Count == 1, "No remote trust by default");
    Check(Pairing.Parse(a.Code.Encode()) == a.Code, "Pairing code round trip");
    var selfRejected = false; try { await a.Pair(a.Code, false); } catch (ArgumentException) { selfRejected = true; } Check(selfRejected, "Reject self pairing");
    await b.Pair(a.Code, true); Check(b.Settings.FolderId == a.Settings.FolderId, "Client joins server folder identity");
    await a.Pair(b.Code, false); await a.Pair(b.Code, false);
    Check((await a.Get("config/devices")).AsArray().Count == 2, "Repeated pairing does not duplicate trust");
    await Direct(a, b.DeviceId, 55202); await Direct(b, a.DeviceId, 55201);
    File.WriteAllText(Path.Combine(a.Settings.Folder, "from-server.txt"), "server version");
    await WaitFor(() => File.Exists(Path.Combine(b.Settings.Folder, "from-server.txt")), "Server to desktop transfer");
    File.WriteAllText(Path.Combine(b.Settings.Folder, "from-desktop.txt"), "desktop version");
    await WaitFor(() => File.Exists(Path.Combine(a.Settings.Folder, "from-desktop.txt")), "Desktop to server transfer");
    Check(File.ReadAllText(Path.Combine(a.Settings.Folder, "from-desktop.txt")) == "desktop version", "Transferred content matches");
    await a.Pause(true); Check((await a.Get("config/folders/" + a.Settings.FolderId))["paused"]!.GetValue<bool>(), "Pause persists in engine"); await a.Pause(false);
    await b.Shutdown(); b.Dispose(); b = null; await Task.Delay(1000);
    File.WriteAllText(Path.Combine(root, "files-b", "offline.txt"), "offline edit");
    b = new Engine(homeB, guiPort: 55392); await b.Initialize();
    await WaitFor(() => File.Exists(Path.Combine(a.Settings.Folder, "offline.txt")), "Offline desktop edit transfers after restart");
    File.Delete(Path.Combine(a.Settings.Folder, "from-server.txt"));
    await WaitFor(() => !File.Exists(Path.Combine(b.Settings.Folder, "from-server.txt")), "Deletion synchronizes");
    Check(Directory.Exists(Path.Combine(b.Settings.Folder, ".stversions")) && Directory.EnumerateFiles(Path.Combine(b.Settings.Folder, ".stversions"), "*", SearchOption.AllDirectories).Any(), "Remote deletion retains a version");
    var snapshot=System.Text.Json.JsonSerializer.SerializeToNode(await a.Snapshot())!;Check(snapshot["files"]!.GetValue<long>()>0,"Dashboard reports real file counts");Check(snapshot["diskTotal"]!.GetValue<long>()>0,"Dashboard reports real disk capacity");
    var oldFolder=a.Settings.Folder;var newFolder=Path.Combine(root,"relocated");await a.SetFolder(newFolder);Check(File.Exists(Path.Combine(newFolder,"from-desktop.txt")),"Folder relocation copies existing files");Check(File.Exists(Path.Combine(oldFolder,"from-desktop.txt")),"Folder relocation keeps original files");
    await a.Forget(b.DeviceId);
    Check((await a.Get("config/devices")).AsArray().Count == 1, "Device revocation removes trust");
    Check((await a.Get("config/folders")).AsArray().All(f => f!["devices"]!.AsArray().All(d => d!["deviceID"]!.GetValue<string>() != b.DeviceId)), "Revocation removes folder grants");
    Console.WriteLine($"{checks} checks passed, including real bidirectional transfers between two isolated loopback engines.");
}
finally
{
    foreach (var engine in new[] { a, b }) if (engine != null) { try { await engine.Shutdown(); } catch { } engine.Dispose(); }
    // Preserve the isolated temporary directory on failure for diagnosis. It never contains production files.
    Console.WriteLine("Isolated test directory: " + root);
}
