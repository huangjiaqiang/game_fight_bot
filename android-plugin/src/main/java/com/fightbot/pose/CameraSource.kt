package com.fightbot.pose

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.ImageFormat
import android.graphics.Matrix
import android.graphics.Rect
import android.graphics.YuvImage
import android.util.Size
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.core.content.ContextCompat
import androidx.lifecycle.LifecycleOwner
import java.io.ByteArrayOutputStream
import java.util.concurrent.Executors

/**
 * CameraX 前置摄像头 + ImageAnalysis. 移植自 game_jumping_in_muddle_paddle.
 * 每帧转成水平镜像后的 Bitmap, 回调在 analysisExecutor 线程.
 */
class CameraSource(
    private val context: Context,
    private val lifecycleOwner: LifecycleOwner,
    private val onFrame: (Bitmap, Long) -> Unit
) {
    private val analysisExecutor = Executors.newSingleThreadExecutor { r ->
        Thread(r, "FightBotCameraAnalysis").apply { isDaemon = true }
    }
    private var cameraProvider: ProcessCameraProvider? = null
    @Volatile private var bound = false

    @SuppressLint("UnsafeOptInUsageError")
    fun start() {
        val future = ProcessCameraProvider.getInstance(context)
        future.addListener({
            val provider = future.get()
            cameraProvider = provider
            val analysis = ImageAnalysis.Builder()
                .setTargetResolution(Size(640, 480))
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
            analysis.setAnalyzer(analysisExecutor) { image -> processImage(image) }
            try {
                provider.unbindAll()
                provider.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_FRONT_CAMERA, analysis)
                bound = true
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }, ContextCompat.getMainExecutor(context))
    }

    fun stop() {
        cameraProvider?.unbindAll()
        bound = false
    }

    @SuppressLint("UnsafeOptInUsageError")
    private fun processImage(image: ImageProxy) {
        val ts = System.currentTimeMillis()
        val bitmap = imageToBitmap(image)
        image.close()
        if (bitmap != null) {
            val mirrored = mirrorHorizontal(bitmap)
            onFrame(mirrored, ts)
        }
    }

    private fun mirrorHorizontal(src: Bitmap): Bitmap {
        val m = Matrix().apply { preScale(-1f, 1f) }
        return Bitmap.createBitmap(src, 0, 0, src.width, src.height, m, true)
    }

    @SuppressLint("UnsafeOptInUsageError")
    private fun imageToBitmap(image: ImageProxy): Bitmap? {
        if (image.format != ImageFormat.YUV_420_888) return null
        val nv21 = yuv420ToNv21(image)
        val yuv = YuvImage(nv21, ImageFormat.NV21, image.width, image.height, null)
        val out = ByteArrayOutputStream()
        yuv.compressToJpeg(Rect(0, 0, image.width, image.height), 85, out)
        val bytes = out.toByteArray()
        return BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
    }

    @SuppressLint("UnsafeOptInUsageError")
    private fun yuv420ToNv21(image: ImageProxy): ByteArray {
        val w = image.width
        val h = image.height
        val ySize = w * h
        val nv21 = ByteArray(ySize + ySize / 2)

        val yPlane = image.planes[0]
        val uPlane = image.planes[1]
        val vPlane = image.planes[2]
        val yRowStride = yPlane.rowStride
        val uRowStride = uPlane.rowStride
        val vRowStride = vPlane.rowStride
        val yPixelStride = yPlane.pixelStride
        val uPixelStride = uPlane.pixelStride
        val vPixelStride = vPlane.pixelStride

        if (yRowStride == w && yPixelStride == 1) {
            yPlane.buffer.get(nv21, 0, ySize)
        } else {
            val buf = yPlane.buffer
            var pos = 0
            for (row in 0 until h) {
                buf.position(row * yRowStride)
                for (col in 0 until w) {
                    nv21[pos++] = buf.get(row * yRowStride + col * yPixelStride)
                }
            }
        }

        val uBuf = uPlane.buffer
        val vBuf = vPlane.buffer
        val uvHeight = h / 2
        val uvWidth = w / 2
        var uvPos = ySize
        for (row in 0 until uvHeight) {
            for (col in 0 until uvWidth) {
                val vIdx = row * vRowStride + col * vPixelStride
                val uIdx = row * uRowStride + col * uPixelStride
                nv21[uvPos++] = vBuf.get(vIdx)
                nv21[uvPos++] = uBuf.get(uIdx)
            }
        }
        return nv21
    }
}
