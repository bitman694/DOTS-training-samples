﻿using System;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Systems {
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UpdateSpeedSystem))]
    public class UpdateApproachSpeedSystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            return Entities.ForEach((Entity entity, ref CarSpeedComponent speed, in InIntersectionComponent inIntersection, in OnSplineComponent onSpline) =>
            {
                float approachSpeed = 1f;
                if (inIntersection.Value)
                    approachSpeed = .7f;
                else
                {
                    var queue = TrackSplines.GetQueue(onSpline.Spline, onSpline.Direction, onSpline.Side);

                    if (queue[0].Entity != entity)
                    {
                        // someone's ahead of us - don't clip through them
                        int index = -1;
                        for (int i = 0; i < queue.Count; i++)
                        {
                            if (queue[i].Entity == entity)
                            {
                                index = i;
                                break;
                            }
                        }
                        float queueSize = TrackSplines.carQueueSize[onSpline.Spline];
                        float maxT = queue[index - 1].SplineTimer - queueSize;
                        speed.SplineTimer = math.min(speed.SplineTimer, maxT);
                        approachSpeed = (maxT - speed.SplineTimer) * 5f;
                    }
                    else
                    {
                        // we're "first in line" in our lane, but we still might need
                        // to slow down if our next intersection is occupied
                        var s = onSpline.Spline;
                        var target = onSpline.Direction == 1 ? TrackSplines.endIntersection[s] : TrackSplines.startIntersection[s];
                        if (Intersections.Occupied[target][(onSpline.Side + 1) / 2])
                            approachSpeed = (1f - speed.SplineTimer) * .8f + .2f;
                    }
                }
                speed.NormalizedSpeed = math.min(speed.NormalizedSpeed, approachSpeed);
            }).WithoutBurst().WithName("UpdateApproachSpeed").Schedule(inputDeps);
        }
    }
}