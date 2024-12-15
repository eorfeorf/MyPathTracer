using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class MyPathTracerFeature : ScriptableRendererFeature
{
    class MyPathTracerPass : ScriptableRenderPass
    {
        private RayTracingShader _rayTracingShader;
        private RayTracingAccelerationStructure _rayTracingAccelerationStructure;

        public MyPathTracerPass()
        {
            base.profilingSampler = new ProfilingSampler("MyPathTracerPass");
            _rayTracingShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>("Assets/MyPathTracer/Shaders/MyPathTracingShader.raytrace");
        }

        public void Cleanup()
        {
            _rayTracingAccelerationStructure?.Dispose();
        }
        
        
        private class PassData
        {
            public RayTracingShader RayTracingShader;
            public TextureHandle OutputColorTexture;
            public TextureHandle CameraColorTarget;
            public RayTracingAccelerationStructure RayTracingAccelerationStructure;

            public Camera Camera;
        }

        
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            var nativeCommand = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            
            nativeCommand.SetRayTracingShaderPass(data.RayTracingShader, "MyPathTracing");
            
            context.cmd.SetRayTracingAccelerationStructure(data.RayTracingShader, Shader.PropertyToID("_Scene"),
                data.RayTracingAccelerationStructure);
            context.cmd.SetRayTracingTextureParam(data.RayTracingShader, Shader.PropertyToID("_Result"),
                data.OutputColorTexture);
            
            context.cmd.DispatchRays(data.RayTracingShader, "MyRaygenShader", (uint)data.Camera.pixelWidth,
                (uint)data.Camera.pixelHeight, 1, data.Camera);
            
            // 結果をカメラに書き戻す
            nativeCommand.Blit(data.OutputColorTexture, data.CameraColorTarget);
        }

        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "Render Custom Pass";

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            // 現在のカメラで描画されたカラーフレームバッファを取得
            var colorTexture = resourceData.activeColorTexture;
            
            // レイトレ結果を書き出すバッファを作成
            RenderTextureDescriptor rtdesc = cameraData.cameraTargetDescriptor;
            rtdesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            rtdesc.depthStencilFormat = GraphicsFormat.None;
            rtdesc.depthBufferBits = 0;
            rtdesc.enableRandomWrite = true;
            var resultTex = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, rtdesc, "_RayTracedColor", false);
            
            // Acceleration Structureを作成
            if (_rayTracingAccelerationStructure == null)
            {
                var settings = new RayTracingAccelerationStructure.Settings();
                settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
                settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
                settings.layerMask = 255;

                _rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
                
                // ASの構築はここだけ。動的な更新には今は対応しない
                _rayTracingAccelerationStructure.Build();
            }
            
            using (var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData, base.profilingSampler))
            {
                passData.RayTracingShader = _rayTracingShader;
                passData.OutputColorTexture = resultTex;
                passData.CameraColorTarget = colorTexture;
                passData.RayTracingAccelerationStructure = _rayTracingAccelerationStructure;
                passData.Camera = cameraData.camera;
                builder.UseTexture(passData.OutputColorTexture, AccessFlags.Write);
                builder.UseTexture(passData.CameraColorTarget, AccessFlags.ReadWrite);

                // This sets the render target of the pass to the active color texture. Change it to your own render target as needed.
                //builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                // Assigns the ExecutePass function to the render pass delegate. This will be called by the render graph when executing the pass.
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
    }

    MyPathTracerPass m_MyPathTracerPass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_MyPathTracerPass = new MyPathTracerPass();

        // Configures where the render pass should be injected.
        m_MyPathTracerPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera)
        {
            return;
        }
        
        renderer.EnqueuePass(m_MyPathTracerPass);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            m_MyPathTracerPass?.Cleanup();    
        }
    }
}
