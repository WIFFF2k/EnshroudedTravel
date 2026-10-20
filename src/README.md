# 桌面助手模块

使用 .NET Framework WinForms，入口为 Program，保留单实例控制

- Views/MainForm：自动连接、真实定位进度、取消、重试和运行中传送开关
- Services/RuntimeTravelService：版本校验、完整注册表搜索、同进程缓存、单字节写入及恢复
- Services/TravelService：原版资源校验和存档备份，历史磁盘补丁方法不再由主界面调用

主界面只通过 RuntimeTravelService 修改内存，备份仍要求游戏正常退出
进度由服务报告实际扫描量，界面只负责展示；缓存按版本、PID、启动时间和完整注册表校验
首次发现已启用状态不视为本助手持有的改动，用户可明确点击关闭恢复
新游戏进程不沿用旧地址、不自动启用；原存档不用于行为测试
