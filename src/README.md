# 桌面助手模块

使用 .NET Framework WinForms，入口为 Program，保留单实例控制

- Views/MainForm：自动连接、真实定位进度、取消、重试和运行中传送开关
- Services/RuntimeTravelService：版本校验、完整注册表搜索、同进程缓存、单字节写入及恢复
- Services/RuntimeTravelService.TravelMarkers：多类地图标记 ID、传送字段偏移和状态匹配
- Services/RuntimeTravelService.Zoom：地图缩放资源定位、最远档位切换及恢复
- Services/RuntimeTravelService.ShroudTravel：瘴气快速传送限制切换及恢复
- Services/TravelService：原版资源校验和存档备份，历史磁盘补丁方法不再由主界面调用

主界面只通过 RuntimeTravelService 修改内存，备份仍要求游戏正常退出
进度由服务报告实际扫描量，界面只负责展示；传送与缩放缓存均按版本、PID、启动时间和完整资源校验
首次发现已启用状态不视为本助手持有的改动，用户可明确点击关闭恢复
新游戏进程不沿用旧地址、不自动启用；原存档不用于行为测试

主窗体为「雾锁王国修改器」，固定尺寸并始终保留进度区域
第一行：备份存档、校验状态；第二行：启用/关闭传送、开启更小缩放
第三行：允许瘴气中传送/恢复瘴气限制
传送按钮调用 Toggle(true/false)，切换灵火神殿、宝石锻炉、远古尖塔、地点和特殊探索点
缩放按钮调用 ToggleZoom(true/false)，将地图相机从原生四档扩展为五档
原生档位为 16/8/4/2，开启后第五档为 0.45
关闭时先恢复四档限制，再把第五档恢复为原生 0.7
瘴气传送按钮只切换快速传送禁用判断的通用分支，下落和水中限制保持不变
冷扫描同时定位传送注册表和地图界面资源，两者分别缓存并在写入前后校验完整内容
