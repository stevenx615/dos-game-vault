using System.IO;
using System.Security.Cryptography;
namespace DOSGameVault;
public static class CoverStorage
{
    public static string FileStem(string name)
    {
        var stem=string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(stem)?"game":stem;
    }
    public static string Copy(string source, string folder, string gameFolderName,string? gameKey=null)
    {
        source=Path.GetFullPath(source); folder=Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        var name=FileStem(gameFolderName);
        using var input=File.OpenRead(source);
        var hash=SHA256.HashData(input);
        var extension=Path.GetExtension(source).ToLowerInvariant();
        for(var suffix=0;;suffix++)
        {
            var destination=Path.Combine(folder,name+(suffix==0?"":$" ({suffix+1})")+extension);
            if(File.Exists(destination))
            {
                if(source.Equals(destination,StringComparison.OrdinalIgnoreCase)) {CoverMatching.Remember(folder,gameKey,destination);return destination;}
                using var existing=File.OpenRead(destination);
                if(SHA256.HashData(existing).SequenceEqual(hash)) {CoverMatching.Remember(folder,gameKey,destination);return destination;}
                continue;
            }
            var created=false;
            try
            {
                input.Position=0;
                using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                created=true;
                input.CopyTo(output);
            }
            catch
            {
                // Remove only the incomplete copy created by this operation.
                if(created && File.Exists(destination)) File.Delete(destination);
                throw;
            }
            CoverMatching.Remember(folder,gameKey,destination);return destination;
        }
    }
}
