using System;
using Unity.Sentis;
using UnityEngine;
using TensorInt = Unity.Sentis.Tensor<int>;

namespace FightBot.Motion
{
    /// <summary>
    /// MoveNet SinglePose Lightning (TFLite) Sentis 封装.
    /// 直接复用 peppapig_jump 的 movenet_singlepose_lightning.tflite.
    ///
    /// 输入: [1, 192, 192, 3] uint8 (RGB 0~255)
    /// 输出: [1, 1, 17, 3] float32 (y, x, score), 归一化到 [0,1]
    /// </summary>
    public class PoseDetector : IDisposable
    {
        public const int InputSize = 192;

        readonly ModelAsset modelAsset;
        Worker worker;
        TensorInt inputTensor;
        readonly float[] outputBuffer = new float[1 * 1 * Pose.KEYPOINT_COUNT * 3];

        /// <summary>推理是否可用(模型加载成功 + worker 已建)</summary>
        public bool Available { get; private set; }

        public PoseDetector(ModelAsset asset, BackendType backend = BackendType.GPUCompute)
        {
            modelAsset = asset;
            try
            {
                var model = ModelLoader.Load(asset);
                worker = model.CreateWorker(backend);
                inputTensor = new TensorInt(new TensorShape(1, InputSize, InputSize, 3));
                Available = worker != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PoseDetector] 加载模型失败: {e}");
                Available = false;
            }
        }

        /// <summary>
        /// 在 [pixels] (尺寸 srcW × srcH, 假定已水平镜像) 上做推理.
        /// 返回单人 Pose (关键点归一化到 [0,1]); 若推理失败返回 null.
        /// </summary>
        public Pose Detect(Color32[] pixels, int srcW, int srcH)
        {
            if (!Available || worker == null || pixels == null) return null;

            try
            {
                FillInput(pixels, srcW, srcH);
                inputTensor.MakeReadable();
                worker.Execute(inputTensor);

                var output = worker.PeekOutput() as TensorFloat;
                if (output == null) return null;
                output.MakeReadable();
                output.ToReadOnlyArray().CopyTo(outputBuffer, 0);

                return ParseOutput();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PoseDetector] 推理异常: {e}");
                return null;
            }
        }

        void FillInput(Color32[] src, int srcW, int srcH)
        {
            var data = inputTensor.Download();
            // 1) 双线性缩放 srcW × srcH → 192 × 192, 写入 inputTensor.data 顺序 [1, H, W, C] uint8
            // 简单 nearest + 双线性混合: 实测 192×192 输入下 nearest 也够用, 这里用 area-avg
            for (int y = 0; y < InputSize; y++)
            {
                int srcY = y * srcH / InputSize;
                for (int x = 0; x < InputSize; x++)
                {
                    int srcX = x * srcW / InputSize;
                    int srcIdx = srcY * srcW + srcX;
                    int dstIdx = (y * InputSize + x) * 3;
                    data[dstIdx + 0] = src[srcIdx].r;
                    data[dstIdx + 1] = src[srcIdx].g;
                    data[dstIdx + 2] = src[srcIdx].b;
                }
            }
            inputTensor.Upload(data);
        }

        Pose ParseOutput()
        {
            var kps = new KeyPoint[Pose.KEYPOINT_COUNT];
            // outputBuffer 顺序 [1, 1, 17, 3]: 每个关键点连续 (y, x, score)
            for (int j = 0; j < Pose.KEYPOINT_COUNT; j++)
            {
                int baseIdx = j * 3;
                float y = outputBuffer[baseIdx + 0];
                float x = outputBuffer[baseIdx + 1];
                float s = outputBuffer[baseIdx + 2];
                kps[j] = new KeyPoint(x, y, s);
            }
            return new Pose(kps);
        }

        public void Dispose()
        {
            worker?.Dispose();
            inputTensor?.Dispose();
            Available = false;
        }
    }
}
