using System;
using System.Collections.Generic;
using System.IO;

namespace EnshroudedTravel {
public sealed partial class RuntimeTravelService {
    private const int MarkerValuesOffset = 732;
    private const int MarkerCount = 49;
    private const int MarkerSize = 80;
    private const int MarkerIdOffset = 16;
    private const int MarkerFastTravelOffset = 24;
    private static readonly uint[] TravelMarkerIds = {
        2068618774, // 灵火精华
        4099254117, // 宝石锻炉
        2708501182, // 远古尖塔
        2704970124, // 特殊敌营
        2671522318, // 火盆
        1359276241, // 农场
        3050313574, // 村庄
        1755200041, // 矿点
        1789853740, // 幸存者营地
        3675766579  // 洞穴
    };

    private static int[] BuildTravelOffsets(byte[] template) {
        // 按标记 ID 解析传送字段偏移，避免依赖条目顺序
        var offsets=new List<int>();
        foreach(uint markerId in TravelMarkerIds) {
            int found=0,foundOffset=0;
            for(int i=0;i<MarkerCount;i++) {
                int record=MarkerValuesOffset+i*MarkerSize;
                if(BitConverter.ToUInt32(template,record+MarkerIdOffset)==markerId) {
                    foundOffset=record+MarkerFastTravelOffset;
                    found++;
                }
            }
            if(found!=1)throw new IOException("TRAVEL_TEMPLATE_INVALID：传送标记模板不唯一");
            if(!offsets.Contains(foundOffset))offsets.Add(foundOffset);
        }
        offsets.Sort();
        return offsets.ToArray();
    }
    private bool MatchesTravelState(byte[] data,bool enabled) {
        // 只允许目标标记的传送字段进入指定状态
        if(data.Length!=original.Length)return false;
        for(int i=0;i<data.Length;i++) {
            bool target=Array.IndexOf(travelOffsets,i)>=0;
            byte expected=target?(enabled?(byte)1:original[i]):original[i];
            if(data[i]!=expected)return false;
        }
        return true;
    }
    private bool MatchesKnownTravelState(byte[] data) {
        // 定位和缓存复核同时接受原生状态与启用状态
        return MatchesTravelState(data,false) || MatchesTravelState(data,true);
    }
}
}
