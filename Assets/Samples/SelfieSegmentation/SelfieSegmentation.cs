﻿
using UnityEngine;

namespace TensorFlowLite
{

    /// <summary>
    /// https://ai.google.dev/edge/mediapipe/solutions/vision/image_segmenter
    /// https://storage.googleapis.com/mediapipe-assets/Model%20Card%20MediaPipe%20Selfie%20Segmentation.pdf
    /// </summary>
    public sealed class SelfieSegmentation : BaseVisionTask
    {
        [System.Serializable]
        public class Options
        {
            [FilePopup("*.tflite")]
            public string modelFile = string.Empty;
            public AspectMode aspectMode = AspectMode.Fit;
            public TfLiteDelegateType delegateType = TfLiteDelegateType.GPU;
            public ComputeShader compute = null;

            [Range(0.1f, 4f)]
            public float sigmaColor = 1f;


            public void UpdateParameter()
            {
                compute.SetFloat("sigmaColor", sigmaColor);
            }
        }

        private float[,,] output0; // height, width, 1

        private readonly ComputeShader compute;
        private readonly Options options;
        private ComputeBuffer labelBuffer;
        private RenderTexture labelTex;
        private RenderTexture maskTex;

        private readonly int kLabelToTex;
        private readonly int kBilateralFilter;
        private static readonly int kLabelBuffer = Shader.PropertyToID("LabelBuffer");
        private static readonly int kInputTexture = Shader.PropertyToID("InputTexture");
        private static readonly int kOutputTexture = Shader.PropertyToID("OutputTexture");

        public SelfieSegmentation(Options options)
        {
            var interpreterOptions = new InterpreterOptions();
            interpreterOptions.AutoAddDelegate(options.delegateType, typeof(float));
            Load(FileUtil.LoadFile(options.modelFile), interpreterOptions);

            this.options = options;
            AspectMode = options.aspectMode;

            int[] odim0 = interpreter.GetOutputTensorInfo(0).shape;

            Debug.Assert(odim0[1] == height);
            Debug.Assert(odim0[2] == width);

            output0 = new float[odim0[1], odim0[2], odim0[3]];

            compute = options.compute;
            compute.SetInt("Width", width);
            compute.SetInt("Height", height);

            compute.SetFloat("sigmaColor", options.sigmaColor);
            compute.SetFloat("sigmaTexel", Mathf.Max(1f / width, 1f / height));
            compute.SetInt("step", 1);
            compute.SetInt("radius", 1);

            labelBuffer = new ComputeBuffer(height * width, sizeof(float));

            labelTex = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            labelTex.enableRandomWrite = true;
            labelTex.Create();

            maskTex = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            maskTex.enableRandomWrite = true;
            maskTex.Create();

            kLabelToTex = compute.FindKernel("LabelToTex");
            kBilateralFilter = compute.FindKernel("BilateralFilter");
        }

        public override void Dispose()
        {
            output0 = null;
            if (labelTex != null)
            {
                labelTex.Release();
                Object.Destroy(labelTex);
                labelTex = null;
            }
            if (maskTex != null)
            {
                maskTex.Release();
                Object.Destroy(maskTex);
                maskTex = null;
            }

            labelBuffer?.Release();
            labelBuffer = null;

            base.Dispose();
        }

        protected override void PostProcess()
        {
            interpreter.GetOutputTensorData(0, output0);
        }

        public RenderTexture GetResultTexture()
        {
            // Label to Texture
            labelBuffer.SetData(output0);
            compute.SetBuffer(kLabelToTex, kLabelBuffer, labelBuffer);
            compute.SetTexture(kLabelToTex, kOutputTexture, labelTex);
            compute.Dispatch(kLabelToTex, width / 8, height / 8, 1);
            // return labelTex;

            // Bilateral Filter
            options.UpdateParameter();
            compute.SetTexture(kBilateralFilter, kInputTexture, labelTex);
            compute.SetTexture(kBilateralFilter, kOutputTexture, maskTex);
            compute.Dispatch(kBilateralFilter, width / 8, height / 8, 1);
            return maskTex;
        }
    }
}
