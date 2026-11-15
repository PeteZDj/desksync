using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Desksync;
public sealed record AccountSession(string AccessToken, DateTimeOffset Expires, JsonNode Profile, JsonNode Server);
public sealed class Account : IDisposable
{
 readonly string file; readonly HttpClient http=new(){BaseAddress=new Uri("https://afrosoftware.com/api/desksync/device/"),Timeout=TimeSpan.FromSeconds(15)};
 public AccountSession? Session {get;private set;}
 public Account(string home){file=Path.Combine(home,"account.dat");try{if(File.Exists(file))Session=JsonSerializer.Deserialize<AccountSession>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser)));}catch(CryptographicException){}catch(JsonException){}}
 void Save(){if(Session==null){if(File.Exists(file))File.Delete(file);return;}File.WriteAllBytes(file,ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Session)),null,DataProtectionScope.CurrentUser));}
 public async Task<JsonNode> Begin(string id,string name,CancellationToken ct){using var r=await http.PostAsJsonAsync("authorize",new{client_id="desksync-desktop",device_id=id,device_name=name},ct);if(!r.IsSuccessStatusCode)throw new IOException("Unable to start sign-in. Check your connection, then try again.");return await r.Content.ReadFromJsonAsync<JsonNode>(ct)??throw new IOException("Sign-in returned no response.");}
 public async Task<bool> Poll(string code,CancellationToken ct){using var r=await http.PostAsJsonAsync("token",new{client_id="desksync-desktop",grant_type="urn:ietf:params:oauth:grant-type:device_code",device_code=code},ct);var d=await r.Content.ReadFromJsonAsync<JsonNode>(ct)??throw new IOException("Sign-in returned no response.");if(!r.IsSuccessStatusCode){var e=d["error"]?.ToString();if(e=="authorization_pending")return false;if(e=="slow_down"){await Task.Delay(5000,ct);return false;}throw new IOException(e=="access_denied"?"You declined this computer's sign-in request.":e=="expired_token"?"Sign-in expired. Choose Sign in to start again.":"The sign-in service is temporarily unavailable.");}Session=new(d["access_token"]!.GetValue<string>(),DateTimeOffset.UtcNow.AddSeconds(d["expires_in"]!.GetValue<int>()),d["profile"]!.DeepClone(),d["server"]!.DeepClone());Save();return true;}
 public async Task Refresh(){if(Session==null)return;using var req=new HttpRequestMessage(HttpMethod.Get,"me");req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Session.AccessToken);using var r=await http.SendAsync(req);if(!r.IsSuccessStatusCode)throw new IOException("Reconnect your Afrosoftware account to refresh server information.");var d=await r.Content.ReadFromJsonAsync<JsonNode>();Session=Session with {Profile=d!["profile"]!.DeepClone(),Server=d["server"]!.DeepClone()};Save();}
 public async Task Disconnect(){if(Session==null)return;using var req=new HttpRequestMessage(HttpMethod.Delete,"session");req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Session.AccessToken);using var r=await http.SendAsync(req);if(!r.IsSuccessStatusCode&&r.StatusCode!=System.Net.HttpStatusCode.Unauthorized)throw new IOException("The server could not remove this computer yet. Sync has been paused; reconnect and try again.");Session=null;Save();}
 public object? Public=>Session==null?null:new{profile=Session.Profile,server=Session.Server,expires=Session.Expires};
 public void Dispose()=>http.Dispose();
}
