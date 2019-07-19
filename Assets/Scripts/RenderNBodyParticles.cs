using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.Rendering;
using UnityEngine.Rendering.LWRP;

public class RenderNBodyParticles : ScriptableRendererFeature
{
    [System.Serializable]
    public class RenderNBodySettings
    {
        public Material particleMaterial;
    }

    public RenderNBodySettings settings = new RenderNBodySettings();
    public ComputeBuffer particlePositions;
    public RenderParticlesPass particlePass;

    public override void Create()
    {
        particlePositions = new ComputeBuffer(NBody.NumTotal, 16, ComputeBufferType.Default);
        particlePass = new RenderParticlesPass
        {
            particlePositions = particlePositions,
            particleMaterial = settings.particleMaterial,
        };
    }

    public void OnDestroy()
    {
        particlePositions.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!GatherParticlePositionsSystem.particlePositions.IsCreated)
            return;

        renderer.EnqueuePass(particlePass);
    }
}

public class RenderParticlesPass : ScriptableRenderPass
{
    public const string Tag = "RenderParticlesPass";

    public ComputeBuffer particlePositions;
    public Material particleMaterial;

    public RenderParticlesPass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (!GatherParticlePositionsSystem.particlePositions.IsCreated)
            return;

        GatherParticlePositionsSystem.particleJobHandle.Complete();
        int numParticles = GatherParticlePositionsSystem.particlePositions.Length;
        particlePositions.SetData(GatherParticlePositionsSystem.particlePositions);
        GatherParticlePositionsSystem.particlePositions.Dispose();

        var cmd = CommandBufferPool.Get(Tag);

        cmd.SetGlobalBuffer("_vertexPositions", particlePositions);
        cmd.DrawProcedural(new Matrix4x4(),
            particleMaterial,
            -1,
            MeshTopology.Points,
            numParticles);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
