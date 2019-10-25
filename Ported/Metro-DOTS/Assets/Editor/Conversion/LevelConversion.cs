﻿using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class LevelConversion : GameObjectConversionSystem
{
    protected override void OnUpdate()
    {
        // EntityManager -> In CONVERSION world: will not be saved, stored or serialized
        // DstEntityManager -> In destination world: what will be serialized into the subscene

        Entities.ForEach<Metro>(Convert);
    }

    void Convert(Entity entity, Metro metroComponent)
    {
        Debug.Log("Start Metro Conversion");

        metroComponent.SetupMetroLines();

        SetupPlatforms(entity, metroComponent);
        SetupCommuters(entity, metroComponent);
        SetupTrains(metroComponent);

        Object.DestroyImmediate(Metro.GetRoot);
    }

    void SetupTrains(Metro metro)
    {
        Debug.Log("SetupTrains");

        var prefab = GetPrimaryEntity(metro.prefab_trainCarriage);
        var globalTrainIdx = 0;
        foreach (var metroLine in metro.metroLines)
        {
            var stationData = BuildStationsData(metroLine);
            for (var trainIndex = 0; trainIndex < metroLine.maxTrains; trainIndex++)
            {
                var trainSpawnerEntity = CreateAdditionalEntity(metro.transform);
                var trainLineRef = BuildBezierPath(metroLine);

                var numberOfCarriages = metroLine.carriagesPerTrain;

                var trainLine = new BezierCurve { line = trainLineRef };
                var spacing =  trainIndex * (1f / metroLine.maxTrains);
                globalTrainIdx++;

                var currentPlatform = GetCurrentPlatformId(stationData, spacing);
                var spawnTrain = new SpawnTrain(prefab, trainLine, numberOfCarriages, globalTrainIdx, metroLine.carriageLength_onRail, spacing, currentPlatform);

                DstEntityManager.AddComponentData(trainSpawnerEntity, spawnTrain);
                var buffer = DstEntityManager.AddBuffer<StationData>(trainSpawnerEntity);
                buffer.CopyFrom(stationData);
            }
        }
    }

    static StationData[] BuildStationsData(MetroLine metroLine)
    {
        var stationData = new StationData[metroLine.platforms.Count];
        for (var pl = 0; pl < metroLine.platforms.Count; pl++)
        {
            var platform = metroLine.platforms[pl];
            var startT = platform.point_platform_START.distanceAlongPath / metroLine.bezierPath.GetPathDistance();
            var endT = platform.point_platform_END.distanceAlongPath / metroLine.bezierPath.GetPathDistance();
            stationData[pl] = new StationData { start = startT, end = endT, platformId = platform.platformIndex };
        }

        return stationData;
    }

    public int GetCurrentPlatformId(StationData[] stations, float t)
    {
        if (t < stations[0].start)
            return stations[0].platformId;

        var previousStation = stations[0];
        for (var i = 1; i < stations.Length; i++)
        {
            var station = stations[i];
            if (t > previousStation.end && t < station.end)
            {
                return station.platformId;
            }
            previousStation = station;
        }

        return stations[stations.Length - 1].platformId;
    }

    BlobAssetReference<Curve> BuildBezierPath(MetroLine metroLine)
    {
        using (var builder = new BlobBuilder(Allocator.Temp))
        {
            ref var curveRoot = ref builder.ConstructRoot<Curve>();
            var lineBuilder = builder.Allocate(ref curveRoot.points, metroLine.bezierPath.points.Count);

            for (var pt = 0; pt < metroLine.bezierPath.points.Count; pt++)
            {
                var bezierPt = metroLine.bezierPath.points[pt];
                lineBuilder[pt] = new BezierPt(bezierPt);
            }

            var assetRef = builder.CreateBlobAssetReference<Curve>(Allocator.Persistent);
            assetRef.Value.distance = metroLine.bezierPath.GetPathDistance();
            return assetRef;
        }
    }

    void SetupCommuters(Entity entity, Metro metro)
    {
        Debug.Log("SetupCommuters");

        var lookup = PathLookupHelper.CreatePathLookup(Pathfinding.GetAllPlatformsInScene());
        DstEntityManager.AddComponentData(entity, new SpawnCommuters{ numberOfCommuters = metro.maxCommuters, prefab = GetPrimaryEntity(metro.prefab_commuter)});
        DstEntityManager.AddComponentData(entity, new PathLookup { value = lookup });
    }

    void SetupPlatforms(Entity entity, Metro metro)
    {
        Debug.Log("SetupPlatforms");

        var platforms = Pathfinding.GetAllPlatformsInScene().OrderBy(i => i.platformIndex).ToList();
        using (var blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            ref var builderRoot = ref blobBuilder.ConstructRoot<PlatformsTrBlob>();
            var translationBuilder = blobBuilder.Allocate(ref builderRoot.translations, platforms.Count);
            var rotationsBuilder = blobBuilder.Allocate(ref builderRoot.rotations, platforms.Count);

            for (var i = 0; i < platforms.Count; i++)
            {
                var p = platforms[i].transform;
                translationBuilder[i] = p.position;
                rotationsBuilder[i] = p.rotation;
            }

            var blob = blobBuilder.CreateBlobAssetReference<PlatformsTrBlob>(Allocator.Persistent);
            DstEntityManager.AddComponentData(entity, new PlatformTransforms{ value = blob });
            DstEntityManager.AddComponentData(entity, new SpawnPlatforms{ platformPrefab = GetPrimaryEntity(metro.prefab_platform)});
        }
    }
}