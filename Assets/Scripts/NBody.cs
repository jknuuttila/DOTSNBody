using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;

public struct Position : IComponentData
{
    public float2 pos;
}

public struct Velocity : IComponentData
{
    public float2 vel;
}

public struct Acceleration : IComponentData
{
    public float2 acc;
}

public struct Mass : IComponentData
{
    public float mass;
}

public struct HasMass : IComponentData {}

public class NBody : MonoBehaviour
{
    [SerializeField] public Mesh mesh;
    [SerializeField] public Material material;

    public static readonly int NumMass = 10;
    public static readonly int NumMassless = 500000;
    public static readonly int NumTotal = NumMass + NumMassless;
    public static readonly int NumParticlesPerJob = 10000;

    private EntityArchetype masslessArchetype;
    private EntityArchetype massArchetype;

    static public float2 RandomFloat2(float min, float max)
    {
        return new float2(
            UnityEngine.Random.Range(min, max),
            UnityEngine.Random.Range(min, max));
    }

    public void CreateMassParticle(float2 pos, float mass)
    {
        var em = World.Active.EntityManager;
        Entity e = em.CreateEntity(massArchetype);
        em.SetComponentData<Position>(e, new Position { pos = pos });
        em.SetComponentData<Mass>(e, new Mass { mass = mass });
    }

    public void CreateMasslessParticle(float2 pos)
    {
        var em = World.Active.EntityManager;
        Entity e = em.CreateEntity(masslessArchetype);
    }

    struct CreateMasslessJob : IJobParallelFor
    {
        public EntityArchetype archetype;
        public EntityCommandBuffer.Concurrent ecb;
        public Unity.Mathematics.Random random;
        public bool randomInit;

        public float posMin;
        public float posMax;

        [NativeSetThreadIndex]
        public int threadIndex;

        public void Execute(int index)
        {
            if (randomInit)
            {
                random = new Unity.Mathematics.Random((uint)threadIndex);
                randomInit = false;
            }

            Entity e = ecb.CreateEntity(index, archetype);
            float2 pos = random.NextFloat2(posMin, posMax);
            ecb.SetComponent<Position>(index, e, new Position { pos = pos });
        }
    }

    public void Start()
    {
        masslessArchetype = World.Active.EntityManager.CreateArchetype(
            typeof(Translation),
            typeof(Position),
            typeof(Velocity),
            typeof(Acceleration),
            typeof(Mass)
            );

        massArchetype = World.Active.EntityManager.CreateArchetype(
            typeof(Translation),
            typeof(Position),
            typeof(Velocity),
            typeof(Acceleration),
            typeof(Mass),
            typeof(HasMass)
            );

        float D = 8;

        for (int i = 0; i < NumMass; ++i)
            CreateMassParticle(RandomFloat2(-D, D), 0.15f);

        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        var createMassless = new CreateMasslessJob
        {
            archetype = masslessArchetype,
            ecb = ecb.ToConcurrent(),
            posMin = -D,
            posMax = D,
            randomInit = true,
            threadIndex = 0, // patched by job system
        }.Schedule(NumMassless, NumParticlesPerJob);
        createMassless.Complete();

        ecb.Playback(World.Active.EntityManager);
        ecb.Dispose();
    }
}

[UpdateBefore(typeof(Gravity))]
public class ZeroAcceleration : JobComponentSystem
{
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return new ZeroAccelerationJob().Schedule(this, inputDeps);
    }

    [BurstCompile]
    public struct ZeroAccelerationJob : IJobForEach<Acceleration>
    {
        public void Execute(ref Acceleration a)
        {
            a.acc = 0;
        }
    }
}

[UpdateBefore(typeof(Movement))]
public class Gravity : JobComponentSystem
{
    public static readonly float G = 1;

    public EntityQuery m_GravitySources;

    protected override void OnCreate()
    {
        m_GravitySources = GetEntityQuery(
            ComponentType.ReadOnly(typeof(Position)),
            ComponentType.ReadOnly(typeof(Mass)),
            ComponentType.ReadOnly(typeof(HasMass)));
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var position = m_GravitySources.ToComponentDataArray<Position>(Allocator.TempJob, out var positionJob);
        var mass = m_GravitySources.ToComponentDataArray<Mass>(Allocator.TempJob, out var massJob);

        return new GravityJob { mass = mass, position = position }.Schedule(this, JobHandle.CombineDependencies(inputDeps, positionJob, massJob));
    }

    [BurstCompile]
    public struct GravityJob : IJobForEach<Position, Acceleration>
    {
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<Position> position;
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<Mass> mass;
        
        public void Execute([ReadOnly] ref Position pos, ref Acceleration a)
        {
            int len = position.Length;

            float2 p0 = pos.pos;

            for (int i = 0; i < len; ++i)
            {
                float2 p1 = position[i].pos;

                if (math.all(p0 == p1))
                    continue;

                float m = mass[i].mass;

                float2 d = p1 - p0;
                float r2 = math.lengthsq(d);
                d *= math.rsqrt(r2);

                a.acc += G * m * d;
            }
        }
    }
}

public class Movement : JobComponentSystem
{
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return new MovementJob { dt = Time.deltaTime }.Schedule(this, inputDeps);
    }

    [BurstCompile]
    public struct MovementJob : IJobForEach<Position, Velocity, Acceleration>
    {
        public float dt;

        public void Execute(ref Position p, ref Velocity v, [ReadOnly] ref Acceleration a)
        {
            v.vel += a.acc * dt;
            p.pos += v.vel * dt;
        }
    }
}

[UpdateAfter(typeof(Movement))]
public class GatherParticlePositionsSystem : JobComponentSystem
{
    public EntityQuery positionQuery;
    public EntityQuery positionMassQuery;
    public static NativeArray<float4> particlePositions;
    public static JobHandle particleJobHandle;

    protected override void OnCreate()
    {
        positionQuery = GetEntityQuery(
            ComponentType.ReadOnly<Position>(),
            ComponentType.ReadOnly<Velocity>(),
            ComponentType.ReadOnly<Mass>());
    }

    [BurstCompile]
    public struct GatherPosJob : IJobForEachWithEntity<Position, Velocity, Mass>
    {
        public NativeArray<float4> positions;

        public void Execute(Entity e, int index,
            [ReadOnly] ref Position p,
            [ReadOnly] ref Velocity v,
            [ReadOnly] ref Mass m)
        {
            positions[index] = new float4(p.pos.x, p.pos.y, math.length(v.vel), m.mass);
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        DestroyPositions();

        particlePositions = new NativeArray<float4>(
            positionQuery.CalculateLength(),
            Allocator.TempJob,
            NativeArrayOptions.UninitializedMemory);

        particleJobHandle = new GatherPosJob
        {
            positions = particlePositions,
        }.Schedule(this, inputDeps);

        return particleJobHandle;
    }

    protected override void OnDestroy()
    {
        DestroyPositions();
    }

    private void DestroyPositions()
    {
        if (particlePositions.IsCreated)
            particlePositions.Dispose();
    }
}

