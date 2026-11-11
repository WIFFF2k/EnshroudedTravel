using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace EnshroudedTravel {
public sealed class TravelService {
    public readonly string Root;
    public readonly string Game;
    public readonly string Save;
    public const string ExeHash = "5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77";
    private const string KfcOriginalHash = "2093a7cc0859ba2f878fc252a65c36b39605d7e1106e091c63809f6dea460bf5";
    private const string ResourcesOriginalHash = "2f54eb9e4677f129c4d527aa9b163d89d0aecee70f2b5b728d7bacaa239d91a7";
    public readonly string[] Names = { "enshrouded.kfc", "enshrouded.kfc_resources" };
    public string LastBackup = "尚未执行本次备份";
    public Action<string> Log = delegate {};
    private string ResourceBackup { get { return Path.Combine(Root, "backups", "resources-26430625"); } }
    private string Journal { get { return Path.Combine(Root, "operation.pending"); } }

    public TravelService(string root, string game, string save) {
        // 固定本次配置的游戏目录和存档目录
        Root = Path.GetFullPath(root); Game = Path.GetFullPath(game); Save = Path.GetFullPath(save);
    }
    public static string Hash(Stream stream) {
        // 按完整文件内容计算哈希
        stream.Position = 0;
        using(var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    public static string HashFile(string path) {
        // 只读取目标文件，不修改文件内容
        using(var f = File.OpenRead(path)) return Hash(f);
    }
    public Process[] GameProcesses() {
        // 所有同名游戏实例均会阻止资源切换
        return Process.GetProcessesByName("enshrouded");
    }
    public string Connection() {
        // 显示真实进程状态，不把进程发现宣称为功能验收
        var ps = GameProcesses();
        if(ps.Length == 0) return "未连接 · 等待游戏启动";
        try {
            var match = ps.Where(p => string.Equals(p.MainModule.FileName, Path.Combine(Game, "enshrouded.exe"), StringComparison.OrdinalIgnoreCase)).ToArray();
            return match.Length == 1 ? "已连接 · PID " + match[0].Id + " · 仅监测" : "发现其他游戏实例 · 操作已锁定";
        } catch(System.ComponentModel.Win32Exception) { return "发现游戏 · 无法读取进程路径"; }
        catch(InvalidOperationException) { return "游戏正在退出 · 等待下次检测"; }
        finally { foreach(var p in ps) p.Dispose(); }
    }
    public void RequireClosed() {
        // 游戏运行时禁止改写资源或创建不一致的备份
        var ps = GameProcesses();
        bool running = ps.Length != 0;
        foreach(var p in ps) p.Dispose();
        if(running) throw new InvalidOperationException("GAME_RUNNING：请先正常退出游戏，再切换传送或备份");
    }
    public string State() {
        // 当前主界面只允许运行中内存修改，磁盘资源必须保持适配版本原版
        if(!File.Exists(Path.Combine(Game, "enshrouded.exe"))) return "游戏目录不存在";
        if(HashFile(Path.Combine(Game, "enshrouded.exe")) != ExeHash) return "版本不支持";
        if(HashFile(Path.Combine(Game,"enshrouded.kfc"))!=KfcOriginalHash
            || HashFile(Path.Combine(Game,"enshrouded.kfc_resources"))!=ResourcesOriginalHash)
            return "资源不同 · 已停止操作";
        return "已关闭";
    }
    public string BackupSaves() {
        // 复制全部存档后逐个比对源和目标，不覆盖已有备份
        RequireClosed();
        if(!Directory.Exists(Save)) throw new IOException("SAVE_MISSING：找不到存档目录");
        var files = Directory.GetFiles(Save, "*", SearchOption.AllDirectories);
        if(files.Length == 0) throw new IOException("SAVE_EMPTY：存档目录为空");
        string output = Path.Combine(Root, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(output);
        var manifest = new List<string>();
        foreach(string file in files) {
            string rel = file.Substring(Save.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
            string to = Path.Combine(output, "1203620", rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            string before = HashFile(file);
            File.Copy(file, to, false);
            if(before != HashFile(to) || before != HashFile(file)) throw new IOException("SAVE_CHANGED：备份期间存档发生变化，请重试");
            manifest.Add(before + "  " + rel);
        }
        if(!files.OrderBy(x=>x).SequenceEqual(Directory.GetFiles(Save, "*", SearchOption.AllDirectories).OrderBy(x=>x))) throw new IOException("SAVE_CHANGED：备份期间文件列表变化");
        RequireClosed();
        foreach(string file in files) {
            string rel=file.Substring(Save.TrimEnd(Path.DirectorySeparatorChar).Length+1);
            if(HashFile(file)!=HashFile(Path.Combine(output,"1203620",rel))) throw new IOException("SAVE_CHANGED：备份完成时源文件已变化，请重试");
        }
        File.WriteAllLines(Path.Combine(output, "SHA256.txt"), manifest, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "verified.txt"), "已校验文件数：" + files.Length + "\r\n来源：" + Save, new UTF8Encoding(false));
        LastBackup = output; Log("存档备份通过：" + files.Length + " 个文件\r\n" + output);
        return output;
    }
    public void Toggle(bool enable) {
        // 两个文件先同时独占打开，校验备份后才开始落盘
        RequireClosed();
        string state = State();
        if(state == "版本不支持" || state == "资源不同 · 已停止操作" || state == "游戏目录不存在") throw new IOException("VERSION_MISMATCH：游戏版本或资源不同，未修改任何内容");
        if(state == "恢复待处理" && enable) throw new IOException("RECOVERY_REQUIRED：先点击关闭并恢复");
        if(enable && state.StartsWith("已启用")) return;
        if(!enable && state == "已关闭") return;
        BackupSaves();
        Directory.CreateDirectory(ResourceBackup);
        var streams = new List<FileStream>();
        var patches = new List<Patch>();
        FileStream exeLock=null;
        bool began = false;
        try {
            exeLock=new FileStream(Path.Combine(Game,"enshrouded.exe"),FileMode.Open,FileAccess.Read,FileShare.None);
            if(Hash(exeLock)!=ExeHash) throw new IOException("VERSION_MISMATCH：游戏版本已经变化");
            foreach(string name in Names) {
                patches.Add(new Patch(Path.Combine(Root, "payload", name + ".patch")));
                streams.Add(new FileStream(Path.Combine(Game,name), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            }
            RequireClosed();
            for(int i=0;i<Names.Length;i++) {
                string backup = Path.Combine(ResourceBackup, Names[i]);
                string current = Hash(streams[i]);
                if(current != patches[i].Original && current != patches[i].Patched) throw new IOException("RESOURCE_CHANGED：资源在操作前已变化，不自动覆盖未知内容");
                if(!File.Exists(backup)) {
                    if(current != patches[i].Original) throw new IOException("BACKUP_MISSING：缺少可验证的原始资源备份");
                    streams[i].Position=0;
                    using(var copy = new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)) streams[i].CopyTo(copy);
                }
                if(HashFile(backup) != patches[i].Original) throw new IOException("BACKUP_INVALID：原始资源备份校验失败");
            }
            File.WriteAllText(Journal, Game + "\r\n" + (enable?"启用":"恢复"), new UTF8Encoding(false));
            began=true;
            for(int i=0;i<Names.Length;i++) {
                if(enable) patches[i].Apply(streams[i]); else Restore(streams[i],Path.Combine(ResourceBackup,Names[i]));
                string expected = enable ? patches[i].Patched : patches[i].Original;
                if(Hash(streams[i]) != expected) throw new IOException("VERIFY_FAILED：写入后校验失败");
            }
            File.Delete(Journal); began=false;
            Log(enable ? "已启用灵火神殿地图传送；解锁条件与存档未修改" : "原始资源已恢复并通过完整哈希校验");
        } catch {
            if(began) {
                for(int i=0;i<streams.Count;i++) Restore(streams[i],Path.Combine(ResourceBackup,Names[i]));
                for(int i=0;i<streams.Count;i++) if(Hash(streams[i]) != patches[i].Original) throw new IOException("RECOVERY_FAILED：自动恢复校验失败，请保留备份并停止启动游戏");
                File.Delete(Journal);
            }
            throw;
        } finally { foreach(var f in streams) f.Dispose(); foreach(var p in patches) p.Dispose(); if(exeLock!=null)exeLock.Dispose(); }
    }
    private static void Restore(FileStream target,string backup) {
        // 从已验证原始资源恢复，不涉及用户存档
        target.Position=0;
        using(var source=File.OpenRead(backup)) { target.SetLength(source.Length); source.CopyTo(target); }
        target.Flush(true);
    }
}
public sealed class Patch : IDisposable {
    private readonly BinaryReader reader;
    public readonly string Original, Patched;
    private readonly long oldLength,newLength;
    private readonly int count;
    public Patch(string path) {
        // 校验补丁格式并读取精确版本指纹
        reader = new BinaryReader(File.OpenRead(path));
        if(Encoding.ASCII.GetString(reader.ReadBytes(4)) != "ETP1") throw new IOException("PATCH_INVALID：补丁格式错误");
        Original=BitConverter.ToString(reader.ReadBytes(32)).Replace("-","").ToLowerInvariant();
        Patched=BitConverter.ToString(reader.ReadBytes(32)).Replace("-","").ToLowerInvariant();
        oldLength=reader.ReadInt64(); newLength=reader.ReadInt64(); count=reader.ReadInt32();
    }
    public void Apply(FileStream target) {
        // 只写已生成的差异区间，随后由服务再次验证完整资源
        if(target.Length != oldLength || TravelService.Hash(target) != Original) throw new IOException("PATCH_SOURCE：补丁源文件不匹配");
        target.SetLength(newLength);
        for(int i=0;i<count;i++) {
            long offset=reader.ReadInt64(); int size=reader.ReadInt32();
            if(offset<0 || size<0 || offset>newLength-size) throw new IOException("PATCH_RANGE：补丁区间越界");
            byte[] bytes=reader.ReadBytes(size);
            if(bytes.Length != size) throw new IOException("PATCH_TRUNCATED：补丁不完整");
            target.Position=offset; target.Write(bytes,0,bytes.Length);
        }
        target.Flush(true);
    }
    public void Dispose() {
        // 释放补丁文件句柄
        reader.Dispose();
    }
}
}
