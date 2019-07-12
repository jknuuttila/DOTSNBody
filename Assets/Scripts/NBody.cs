using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.Rendering;

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

public class NBody : MonoBehaviour
{
    [SerializeField] public Mesh mesh;
    [SerializeField] public Material material;
    [SerializeField] public Material particleMaterial;
    [SerializeField] public static Material ParticleMaterial;

    public static readonly int NumMass = 10;
    public static readonly int NumMassless = 500000;
    public static readonly int NumTotal = NumMass + NumMassless;

    static public float2 RandomFloat2(float min, float max)
    {
        return new float2(
            UnityEngine.Random.Range(min, max),
            UnityEngine.Random.Range(min, max));
    }

    public void CreateMassParticle(float2 pos, float mass)
    {
        var em = World.Active.EntityManager;
        Entity e = em.CreateEntity(
            typeof(LocalToWorld),
            typeof(Translation),
            typeof(Position),
            typeof(Velocity),
            typeof(Acceleration),
            typeof(Mass),
            typeof(RenderMesh)
            );
        em.SetComponentData<Position>(e, new Position { pos = pos });
        em.SetComponentData<Mass>(e, new Mass { mass = mass });
        em.SetSharedComponentData<RenderMesh>(e, new RenderMesh
        {
            mesh = mesh,
            material = material
        });
    }

    public void CreateMasslessParticle(float2 pos)
    {
        var em = World.Active.EntityManager;
        Entity e = em.CreateEntity(
            typeof(LocalToWorld),
            typeof(Translation),
            typeof(Position),
            typeof(Velocity),
            typeof(Acceleration)
            );
        em.SetComponentData<Position>(e, new Position { pos = pos });
    }

    public void Start()
    {
        ParticleMaterial = particleMaterial;

        float D = 8;

        for (int i = 0; i < NumMass; ++i)
            CreateMassParticle(RandomFloat2(-D, D), 0.1f);

        for (int i = 0; i < NumMassless; ++i)
            CreateMasslessParticle(RandomFloat2(-D, D));
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
            ComponentType.ReadOnly(typeof(Mass)));
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

#if false
[UpdateBefore(typeof(TransformSystemGroup))]
public class ComputeTranslation : JobComponentSystem
{
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return new TranslationJob().Schedule(this, inputDeps);
    }

    [BurstCompile]
    public struct TranslationJob : IJobForEach<Position, Translation>
    {
        public void Execute(ref Position c0, ref Translation c1)
        {
            c1.Value = new float3(c0.pos.x, 0, c0.pos.y);
        }
    }
}
#endif

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(RenderParticlesSystem))]
public class GatherParticlePositionsSystem : JobComponentSystem
{
    public EntityQuery positionQuery;
    public static NativeArray<float4> particlePositions;
    public static JobHandle particleJobHandle;

    protected override void OnCreate()
    {
        positionQuery = GetEntityQuery(ComponentType.ReadOnly<Position>());
    }

    [BurstCompile]
    public struct GatherPosJob : IJobForEachWithEntity<Position>
    {
        public NativeArray<float4> positions;

        public void Execute(Entity e, int index, [ReadOnly] ref Position p)
        {
            positions[index] = new float4(p.pos.x, 0, p.pos.y, 1);
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        particlePositions = new NativeArray<float4>(positionQuery.CalculateLength(), Allocator.TempJob);
        particleJobHandle = new GatherPosJob { positions = particlePositions }.Schedule(this, inputDeps);
        return particleJobHandle;
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public class RenderParticlesSystem : ComponentSystem
{
    CommandBuffer cmd;
    MaterialPropertyBlock props;
    ComputeBuffer particlePositions;

    protected override void OnCreate()
    {
        props = new MaterialPropertyBlock();
        particlePositions = new ComputeBuffer(NBody.NumTotal, 16, ComputeBufferType.Default);
    }

    protected override void OnDestroy()
    {
        cmd.Dispose();
    }

    protected override void OnUpdate()
    {
        if (cmd is null)
        {
            cmd = new CommandBuffer();
            cmd.name = "RenderParticlesSystem";
            Camera.main.AddCommandBuffer(CameraEvent.AfterForwardOpaque, cmd);
        }

        cmd.Clear();
        props.Clear();

        GatherParticlePositionsSystem.particleJobHandle.Complete();
        particlePositions.SetData(GatherParticlePositionsSystem.particlePositions);

        NBody.ParticleMaterial.SetPass(0);
        NBody.ParticleMaterial.SetBuffer("_vertexPositions", particlePositions);

        cmd.DrawProcedural(new Matrix4x4(),
            NBody.ParticleMaterial,
            -1,
            MeshTopology.Points,
            GatherParticlePositionsSystem.particlePositions.Length);

        GatherParticlePositionsSystem.particlePositions.Dispose();
    }
}

