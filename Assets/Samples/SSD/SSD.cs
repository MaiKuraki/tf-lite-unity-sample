﻿using System.IO;
using UnityEngine;

namespace TensorFlowLite
{
    public class SSD : System.IDisposable
    {
        public struct Result
        {
            public int classID;
            public float score;
            public Rect rect;
        }

        const int WIDTH = 300;
        const int HEIGHT = 300;
        const int CHANNELS = 3; // RGB

        Interpreter interpreter;
        RenderTexture resizeTexture;
        Material resizeMat;
        Texture2D fetchTexture;


        // https://www.tensorflow.org/lite/models/object_detection/overview
        sbyte[,,] inputs = new sbyte[HEIGHT, WIDTH, CHANNELS];

        float[,] outputs0 = new float[10, 4]; // [top, left, bottom, right] * 10
        float[] outputs1 = new float[10]; // Classes
        float[] outputs2 = new float[10]; // Scores
        Result[] results = new Result[10];

        public SSD(string modelPath)
        {
            interpreter = new Interpreter(File.ReadAllBytes(modelPath), 2);
            interpreter.ResizeInputTensor(0, new int[] { 1, HEIGHT, WIDTH, CHANNELS });
            interpreter.AllocateTensors();

        }

        public void Dispose()
        {
            interpreter?.Dispose();

            if (resizeTexture != null)
            {
                Object.Destroy(resizeTexture);
                Object.Destroy(resizeMat);
            }
            if (fetchTexture != null)
            {
                Object.Destroy(fetchTexture);
            }
        }

        public void Invoke(Texture inputTex)
        {
            RenderTexture tex = ResizeTexture(inputTex);
            TextureToTensor(tex, inputs);

            interpreter.SetInputTensorData(0, inputs);
            interpreter.Invoke();
            interpreter.GetOutputTensorData(0, outputs0);
            interpreter.GetOutputTensorData(1, outputs1);
            interpreter.GetOutputTensorData(2, outputs2);
        }

        RenderTexture ResizeTexture(Texture texture)
        {
            if (resizeTexture == null)
            {
                resizeTexture = new RenderTexture(WIDTH, HEIGHT, 0, RenderTextureFormat.ARGB32);
                resizeMat = new Material(Shader.Find("Hidden/YFlip"));

                resizeMat.SetInt("_FlipX", Application.isMobilePlatform ? 1 : 0);
                resizeMat.SetInt("_FlipY", 1);
            }
            Graphics.Blit(texture, resizeTexture, resizeMat, 0);
            return resizeTexture;
        }

        void TextureToTensor(RenderTexture texture, sbyte[,,] inputs)
        {
            if (fetchTexture == null)
            {
                fetchTexture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, 0, false);
            }

            var prevRT = RenderTexture.active;
            RenderTexture.active = texture;

            fetchTexture.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
            fetchTexture.Apply();

            RenderTexture.active = prevRT;

            var pixels = fetchTexture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                int y = i / WIDTH;
                int x = i % WIDTH;
                inputs[y, x, 0] = unchecked((sbyte)pixels[i].r);
                inputs[y, x, 1] = unchecked((sbyte)pixels[i].g);
                inputs[y, x, 2] = unchecked((sbyte)pixels[i].b);
            }
        }

        public Result[] GetResults()
        {
            for (int i = 0; i < 10; i++)
            {
                // Invert Y to adapt Unity UI space
                float top = 1f - outputs0[i, 0];
                float left = outputs0[i, 1];
                float bottom = 1f - outputs0[i, 2];
                float right = outputs0[i, 3];

                results[i] = new Result()
                {
                    classID = (int)outputs1[i],
                    score = outputs2[i],
                    rect = new Rect(left, top, right - left, top - bottom),
                };
            }
            return results;
        }
    }
}