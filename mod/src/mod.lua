-- 仅启用灵火精华地点的原生地图传送，不更改解锁条件和存档
local expected = "1269579|^/game38/branches/release_1.0|2026-10-16T12:58:37.402390Z"
assert(game.version == expected, "游戏版本与验证版本不同，停止应用")
local registries = game.assets.get_resources_by_type("keen::MapMarkerRegistryResource")
assert(#registries == 1, "地图标记注册表数量不符")
local registry = registries[1]
assert(tostring(registry.guid) == "d165e819-f603-42a3-8f7d-827e9e8cb97a", "地图标记注册表不符")
assert(#registry.data.mapMarkers.values == 49, "地图标记条目数量不符")
local count = 0
for _, marker in ipairs(registry.data.mapMarkers.values) do
    if marker.markerId.value == 2068618774 then
        assert(marker.sortingCategory == "FlameRelated", "地点分类不符")
        assert(tostring(marker.icon.image) == "1e84528f-3bfa-45a1-a58e-899e8d1513cd", "灵火精华图标不符")
        assert(not marker.isCustomMarkerType, "目标不是原生地图标记")
        assert(not marker.isFastTravelDestination, "原始传送状态不符")
        marker.isFastTravelDestination = true
        count = count + 1
    end
end
assert(count == 1, "未能唯一识别灵火精华地点")
if loader.features.export then
    local raw = buffer.create()
    raw:write_resource("keen::MapMarkerRegistryResource", registry.data)
    io.export("registry-patched.bin", raw)
    io.export("patch-result.txt", "标记类型：2068618774\n修改字段：isFastTravelDestination\n修改数量：1\n解锁条件：未修改\n")
end
print("灵火神殿传送：已设置 1 个标记类型，未改动解锁条件")
