package com.fightbot.pose

import android.content.Context
import android.graphics.Bitmap
import org.tensorflow.lite.Interpreter
import org.tensorflow.lite.gpu.CompatibilityList
import org.tensorflow.lite.gpu.GpuDelegate
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * MoveNet SinglePose Lightning (TFLite). 移植自 game_jumping_in_muddle_paddle.
 * 输入 [1,192,192,3] uint8, 输出 [1,1,17,3] float (y,x,score) 归一化.
 * 模型在 assets/models/movenet_singlepose_lightning.tflite.
 */
class PoseDetector(context: Context) {

    private var gpuDelegate: GpuDelegate? = null
    private var interpreter: Interpreter? = null

    val available: Boolean

    private val inputBuffer: ByteBuffer = ByteBuffer
        .allocateDirect(INPUT_SIZE * INPUT_SIZE * 3)
        .order(ByteOrder.nativeOrder())

    private val outputArray: Array<Array<Array<FloatArray>>> =
        Array(1) { Array(1) { Array(17) { FloatArray(3) } } }

    init {
        val modelBuffer = try { loadModelFile(context, MODEL_PATH) } catch (e: Throwable) { null }
        if (modelBuffer == null) {
            available = false
        } else {
            val options = Interpreter.Options().setNumThreads(4)
            try {
                if (CompatibilityList().isDelegateSupportedOnThisDevice) {
                    gpuDelegate = GpuDelegate()
                    options.addDelegate(gpuDelegate)
                }
            } catch (e: Throwable) { /* GPU 不可用, CPU 回退 */ }
            interpreter = try { Interpreter(modelBuffer, options) } catch (e: Throwable) { null }
            available = interpreter != null
        }
    }

    fun detect(bitmap: Bitmap): Pose? {
        val interp = interpreter ?: return null
        val scaled = if (bitmap.width == INPUT_SIZE && bitmap.height == INPUT_SIZE) bitmap
                     else Bitmap.createScaledBitmap(bitmap, INPUT_SIZE, INPUT_SIZE, true)
        fillInput(scaled)
        inputBuffer.rewind()
        interp.run(inputBuffer, outputArray)
        return parseOutput()
    }

    private fun fillInput(bitmap: Bitmap) {
        inputBuffer.rewind()
        val w = INPUT_SIZE
        val h = INPUT_SIZE
        val pixels = IntArray(w * h)
        bitmap.getPixels(pixels, 0, w, 0, 0, w, h)
        for (pixel in pixels) {
            inputBuffer.put(((pixel shr 16) and 0xFF).toByte())
            inputBuffer.put(((pixel shr 8) and 0xFF).toByte())
            inputBuffer.put((pixel and 0xFF).toByte())
        }
    }

    private fun parseOutput(): Pose {
        val kps = ArrayList<KeyPoint>(17)
        for (j in 0 until 17) {
            val y = outputArray[0][0][j][0]
            val x = outputArray[0][0][j][1]
            val score = outputArray[0][0][j][2]
            kps.add(KeyPoint(x, y, score))
        }
        return Pose(kps)
    }

    fun close() {
        interpreter?.close()
        gpuDelegate?.close()
    }

    private fun loadModelFile(context: Context, path: String): ByteBuffer {
        val fd = context.assets.openFd(path)
        val inputStream = fd.createInputStream()
        val data = ByteArray(fd.length.toInt())
        inputStream.read(data)
        inputStream.close()
        fd.close()
        val buf = ByteBuffer.allocateDirect(data.size).order(ByteOrder.nativeOrder())
        buf.put(data)
        return buf
    }

    companion object {
        private const val MODEL_PATH = "models/movenet_singlepose_lightning.tflite"
        const val INPUT_SIZE = 192
    }
}
