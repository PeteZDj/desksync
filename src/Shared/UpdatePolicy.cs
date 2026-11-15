using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
namespace Desksync;
public sealed record ReleaseManifest(string Version,string Url,string Sha256,long Bytes,string Channel);
public static class UpdatePolicy
{
 public static string PublicKey {get{using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Desksync.UpdatePublicKey.pem")!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}}
 public static ReleaseManifest Verify(byte[] manifest,byte[] signature,string? publicKey=null)
 {
  if(manifest.Length>16384||signature.Length>1024)throw new IOException("Update metadata is too large.");using var rsa=RSA.Create();rsa.ImportFromPem(publicKey??PublicKey);if(!rsa.VerifyData(manifest,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new IOException("The update signature could not be verified. No update was installed.");
  var release=JsonSerializer.Deserialize<ReleaseManifest>(manifest,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new IOException("Invalid update metadata.");
  if(!Version.TryParse(release.Version,out _)||!Uri.TryCreate(release.Url,UriKind.Absolute,out var url)||url.Scheme!="https"||!((url.Host=="afrosoftware.com"&&url.AbsolutePath.StartsWith("/downloads/desksync/",StringComparison.Ordinal))||(url.Host=="github.com"&&url.AbsolutePath.StartsWith("/PeteZDj/desksync/releases/download/",StringComparison.OrdinalIgnoreCase)))||!System.Text.RegularExpressions.Regex.IsMatch(release.Sha256??"","^[a-f0-9]{64}$")||release.Bytes<=0||release.Bytes>500_000_000||release.Channel!="preview")throw new IOException("The update metadata is invalid or unsupported.");return release;
 }
 public static void VerifyArchive(string path,ReleaseManifest release){var info=new FileInfo(path);if(info.Length!=release.Bytes)throw new IOException("The update download is incomplete.");using var stream=File.OpenRead(path);if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("The update checksum did not match. No update was installed.");}
 public static string SafeTarget(string root,string relative){var full=Path.GetFullPath(Path.Combine(root,relative));if(Path.IsPathRooted(relative)||!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+'\\',StringComparison.OrdinalIgnoreCase)||relative.Contains(':'))throw new IOException("The update archive contains an unsafe path.");return full;}
}
