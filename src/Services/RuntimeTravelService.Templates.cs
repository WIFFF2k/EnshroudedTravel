using System.IO;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private static byte[] LoadBundledTemplate(string name,string expectedHash) {
        // 从程序内置资源加载并完整校验模板，用户无需准备外部文件
        using(var stream=typeof(RuntimeTravelService).Assembly.GetManifestResourceStream("EnshroudedTravel.Templates."+name)) {
            if(stream==null)throw new IOException("APP_RESOURCE_MISSING：程序文件不完整，请重新获取仓库中的 EnshroudedTravel.exe");
            if(TravelService.Hash(stream)!=expectedHash)
                throw new IOException("APP_RESOURCE_INVALID：程序内置资源校验失败，请重新获取仓库中的 EnshroudedTravel.exe");
            stream.Position=0;
            using(var output=new MemoryStream()) {
                stream.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
}
