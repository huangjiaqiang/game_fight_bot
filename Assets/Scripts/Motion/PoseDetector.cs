using System;
using Unity.Sentis;
using UnityEngine;
using TensorInt = Unity.Sentis.Tensor<int>;
using TensorFloat = Unity.Sentis.Tensor<float>;

namespace FightBot.Motion
{
    /// <summary>
    /// MoveNet SinglePose Lightning (TFLite→ONNX) Sentis 2.x 封装, 流水线 GPU 推理 (Unity 2022.3).
    ///
    /// 为什么用流水线: Sentis 2.1 的真·异步 API (ReadbackAndCloneAsync) 返回 Unity6 Awaitable,
    /// 2022.3 没有; ReadbackRequest 的回调签名也变了. 改用 "本帧提交, 下帧取回" 的流水线:
    ///   Step() 先 PeekOutput+DownloadToArray 取回【上一帧】结果 (GPU 已算完, 近非阻塞),
    ///   再 Schedule 提交【本帧】推理 (GPU compute, 非阻塞). 主线程不再被推理长时间占用.
    /// 代价: 1 帧 (~33ms) 延迟, 可忽略. 仅用 Schedule + DownloadToArray (2.1.3 已验证可用).
    ///
    /// 输入: [1,192,192,3] int (RGB 0~255)
    /// 输出: [1,1,17,3] float (y,x,score) 归一化 [0,1]
    /// </summary>
    public class PoseDetector : IDisposable
    {
        public const int InputSize = 192;

        readonly ModelAsset modelAsset;
        Worker worker;

        // 输入像素缓冲 (uint8 以 int 0~255 表示), 每次提交前填好
        readonly int[] inputBuffer = new int[1 * InputSize * InputSize * 3];
        readonly TensorShape inputShape = new TensorShape(1, InputSize, InputSize, 3);
        // 输出缓冲 [1,1,17,3]
        readonly float[] outputBuffer = new float[1 * 1 * Pose.KEYPOINT_COUNT * 3];

        // 上一帧提交的输入 tensor, 取回结果后释放
        TensorInt pendingInput;
        bool hasPending;

        public bool Available { get; private set; }

        public PoseDetector(ModelAsset asset, BackendType backend = BackendType.GPUCompute)
        {
            modelAsset = asset;
            try
            {
                var model = ModelLoader.Load(asset);
                worker = new Worker(model, backend);
                Available = worker != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PoseDetector] 加载模型失败: {e}");
                Available = false;
            }
        }

        /// <summary>
        /// 主线程调用 (每 ~33ms 一次):
        /// 1) 取回上一帧推理结果 (GPU 已算完, DownloadToArray 近非阻塞);
        /// 2) 提交本帧推理 (Schedule 非阻塞).
        /// 返回上一帧的 Pose (1 帧延迟); null 表示无有效姿态/首帧.
        /// </summary>
        public Pose Step(Color32[] pixels, int srcW, int srcH)
        {
            if (!Available || worker == null || pixels == null) return null;

            Pose result = null;
            // 1. drain previous
            if (hasPending)
            {
                try
                {
                    var output = worker.PeekOutput() as TensorFloat;
                    if (output != null)
                    {
                        output.DownloadToArray().CopyTo(outputBuffer, 0);
                        result = ParseOutput();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PoseDetector] 取回异常: {e}");
                }
                if (pendingInput != null) { pendingInput.Dispose(); pendingInput = null; }
                hasPending = false;
            }

            // 2. schedule new (非阻塞 GPU compute)
            try
            {
                FillInput(pixels, srcW, srcH);
                pendingInput = new TensorInt(inputShape, inputBuffer);
                worker.Schedule(pendingInput);
                hasPending = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PoseDetector] 提交异常: {e}");
                if (pendingInput != null) { pendingInput.Dispose(); pendingInput = null; }
                hasPending = false;
            }

            return result;
        }

        void FillInput(Color32[] src, int srcW, int srcH)
        {
            // nearest 缩放 srcW × srcH → 192 × 192, 顺序 [1,H,W,C], 每像素 RGB 三个 uint8 (0~255) 写成 int
            for (int y = 0; y < InputSize; y++)
            {
                int srcY = y * srcH / InputSize;
                for (int x = 0; x < InputSize; x++)
                {
                    int srcX = x * srcW / InputSize;
                    int srcIdx = srcY * srcW + srcX;
                    int dstIdx = (y * InputSize + x) * 3;
                    inputBuffer[dstIdx + 0] = src[srcIdx].r;
                    inputBuffer[dstIdx + 1] = src[srcIdx].g;
                    inputBuffer[dstIdx + 2] = src[srcIdx].b;
                }
            }
        }

        Pose ParseOutput()
        {
            var kps = new KeyPoint[Pose.KEYPOINT_COUNT];
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
            if (pendingInput != null) { pendingInput.Dispose(); pendingInput = null; }
            hasPending = false;
            worker?.Dispose();
            Available = false;
        }
    }
}
