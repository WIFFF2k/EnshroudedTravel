using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace EnshroudedTravel {
public static class LocalConfiguration {
    public static TravelService CreateService(string root) {
        // 从不参与版本控制的本机配置读取目录，缺失时明确提示
        string path=Path.Combine(root,"config.local.json");
        if(!File.Exists(path))throw new IOException("CONFIG_MISSING：请复制 config.example.json 为 config.local.json 并填写本机目录");
        Dictionary<string,string> settings;
        try {settings=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(path));}
        catch(ArgumentException) {throw new IOException("CONFIG_INVALID：config.local.json 格式错误，请按示例填写");}
        string game,save;
        if(settings==null || !settings.TryGetValue("gameDirectory",out game) || !settings.TryGetValue("saveDirectory",out save)
            || string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(save))
            throw new IOException("CONFIG_REQUIRED：必须填写游戏目录与存档目录");
        if(!Path.IsPathRooted(game) || !Path.IsPathRooted(save))throw new IOException("CONFIG_PATH：游戏目录与存档目录必须是绝对路径");
        if(!File.Exists(Path.Combine(game,"enshrouded.exe")))throw new IOException("CONFIG_GAME：游戏目录中找不到 enshrouded.exe");
        if(!Directory.Exists(save))throw new IOException("CONFIG_SAVE：配置的存档目录不存在");
        return new TravelService(root,game,save);
    }
}
}
