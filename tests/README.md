# 验证程序

## 运行中版本

- RuntimeVerify.cs：驱动真实主窗体和生产服务，在 backup 游戏进程中启用、关闭、启用后关闭窗口；检查注册表、原资源和原存档哈希
- ProgressVerify.cs：只读取当前进程，不修改游戏内存；暂时移走定位缓存，检查真实扫描进度和 PID、取消、恢复缓存后重新校验

结果位于 reports/tests-runtime.txt 与 reports/tests-progress.txt，截图位于 reports/ui-runtime.png、reports/ui-location-progress.png 与 reports/ui-location-ready.png
ProgressVerify 必须在已有有效缓存时运行，测试结束将缓存放回
RuntimeVerify 涉及真实进程写入，只能在用户确认的 backup 角色及世界中运行

## 历史磁盘版本

Verify.cs 和 reports/tests-running.txt 保留早期磁盘补丁验证记录
其中「游戏运行时禁用传送按钮」不再适用于当前界面，不能直接用来验收运行中版本

## 尚未完成

游戏重启后的端到端重连、跨重启固定路径定位、不同地点落点覆盖、自动核实角色世界身份
