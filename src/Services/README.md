# 运行时服务

主界面通过 RuntimeTravelService 的各功能分文件访问游戏进程，TravelService 负责版本核验和存档备份

昼夜功能位于 RuntimeTravelService.DayNight.cs，只沿 Build 26082781 已核实的模块指针链读取当前世界，不参与传送和缩放的冷扫描
每次操作重新核对对象身份、世界加载状态和时间配置，只写时间偏移与更新序号，等待游戏自身输出目标时段后报告成功
白天和黑夜属于一次性调整，关闭助手不回退时间，不加入启用全部
字段依据及验收结果见 reports/daynight-verification.md
