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
        public Texture2D falseColorTexture;
    }

    public RenderNBodySettings settings = new RenderNBodySettings();
    public RenderParticlesPass particlePass;

    public override void Create()
    {
        particlePass = new RenderParticlesPass
        {
            particleMaterial = settings.particleMaterial,
            falseColorTexture = settings.falseColorTexture,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ParticleBuffer.particlePositions is null)
            ScriptableObject.CreateInstance<ParticleBuffer>();

        if (!GatherParticlePositionsSystem.particlePositions.IsCreated)
            return;

        renderer.EnqueuePass(particlePass);
    }
}

public class ParticleBuffer : ScriptableObject
{
    public static ComputeBuffer particlePositions;

    public void OnEnable()
    {
        particlePositions = new ComputeBuffer(NBody.NumTotal, 16, ComputeBufferType.Default);
    }

    public void OnDisable()
    {
        particlePositions.Dispose();
        particlePositions = null;
    }
}

public class RenderParticlesPass : ScriptableRenderPass
{
    public const string Tag = "RenderParticlesPass";

    public Material particleMaterial;
    public Texture2D falseColorTexture;

    public RenderParticlesPass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var particlePositions = ParticleBuffer.particlePositions;

        if (!GatherParticlePositionsSystem.particlePositions.IsCreated || particlePositions is null)
            return;

        GatherParticlePositionsSystem.particleJobHandle.Complete();
        int numParticles = GatherParticlePositionsSystem.particlePositions.Length;
        particlePositions.SetData(GatherParticlePositionsSystem.particlePositions);

        var cmd = CommandBufferPool.Get(Tag);

        cmd.SetGlobalBuffer("_vertexPositions", particlePositions);
        cmd.SetGlobalTexture("_FalseColor", falseColorTexture);

        cmd.DrawProcedural(new Matrix4x4(),
            particleMaterial,
            -1,
            MeshTopology.Points,
            numParticles);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
