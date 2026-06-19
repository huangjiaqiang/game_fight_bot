package com.fightbot.pose

import android.app.Activity
import android.content.Context
import android.graphics.Bitmap
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.view.Gravity
import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import java.util.concurrent.atomic.AtomicReference

/**
 * Unity 入口 (AndroidJavaClass("com.fightbot.pose.PosePlugin")).
 *
 * 启动 CameraX + TFLite 推理 (后台线程) + 右上角骨骼小窗 (SkeletonOverlayView, 叠在 Unity 上层).
 * 把最新 17 关键点存进 AtomicReference, Unity 每帧 getLatestPose() 轮询.
 *
 * 注意: Unity 跑在独立线程, 而 LifecycleRegistry/CameraX/UI 操作要求 Android 主线程,
 * 故 lifecycle/camera/View 增删用 Handler(Looper.getMainLooper()).post 切到主线程.
 */
object PosePlugin {
    private const val TAG = "FightBotPose"
    private const val KPT = 17

    private var cameraSource: CameraSource? = null
    private var poseDetector: PoseDetector? = null
    private var lifecycleOwner: PluginLifecycleOwner? = null
    private var overlayView: SkeletonOverlayView? = null
    private var hostActivity: Activity? = null

    private val latestPose = AtomicReference<FloatArray>(FloatArray(0))
    @Volatile var latestFrame: Bitmap? = null
        private set

    @Volatile private var running = false
    @Volatile private var fpsWindowStartMs = 0L
    @Volatile private var fpsWindowCount = 0L
    @Volatile private var currentFps = 0f

    private val mainHandler = Handler(Looper.getMainLooper())

    fun start(activity: Activity) {
        if (running) { Log.w(TAG, "already running"); return }
        try {
            hostActivity = activity
            val ctx: Context = activity.applicationContext
            val detector = PoseDetector(ctx)
            if (!detector.available) { Log.e(TAG, "PoseDetector 不可用 (模型/GPU?)"); return }
            poseDetector = detector

            val owner = PluginLifecycleOwner()
            lifecycleOwner = owner

            cameraSource = CameraSource(ctx, owner) { bmp, ts ->
                latestFrame = bmp
                val pose = try { detector.detect(bmp) } catch (t: Throwable) { Log.e(TAG, "detect", t); null }
                if (pose != null) {
                    val arr = FloatArray(KPT * 3)
                    for (i in 0 until KPT) {
                        val kp = pose.keypoints[i]
                        arr[i * 3] = kp.x
                        arr[i * 3 + 1] = kp.y
                        arr[i * 3 + 2] = kp.score
                    }
                    latestPose.set(arr)
                    if (fpsWindowStartMs == 0L) { fpsWindowStartMs = ts; fpsWindowCount = 0 }
                    fpsWindowCount++
                    val dt = ts - fpsWindowStartMs
                    if (dt >= 1000L) {
                        currentFps = fpsWindowCount * 1000f / dt
                        fpsWindowStartMs = ts
                        fpsWindowCount = 0
                    }
                }
            }

            // lifecycle/camera/骨骼View 都必须在 Android 主线程
            mainHandler.post {
                try {
                    owner.start()
                    cameraSource?.start()
                    addOverlay(activity)
                } catch (t: Throwable) { Log.e(TAG, "main-thread start failed", t) }
            }
            running = true
            Log.i(TAG, "started")
        } catch (e: Throwable) {
            Log.e(TAG, "start failed", e)
        }
    }

    fun stop() {
        val cs = cameraSource; val owner = lifecycleOwner; val det = poseDetector; val act = hostActivity
        cameraSource = null; lifecycleOwner = null; poseDetector = null; hostActivity = null
        latestPose.set(FloatArray(0)); latestFrame = null; running = false
        mainHandler.post {
            removeOverlay(act)
            try { cs?.stop() } catch (_: Throwable) {}
            try { owner?.stop() } catch (_: Throwable) {}
            try { det?.close() } catch (_: Throwable) {}
        }
        Log.i(TAG, "stopped")
    }

    private fun addOverlay(activity: Activity) {
        if (overlayView != null) return
        val content = activity.findViewById<ViewGroup>(android.R.id.content) ?: run {
            Log.e(TAG, "contentView 不存在"); return
        }
        val dm = activity.resources.displayMetrics
        val pipW = (dm.widthPixels * 0.24f).toInt()
        val pipH = (pipW * 0.75f).toInt()
        val margin = (minOf(dm.widthPixels, dm.heightPixels) * 0.03f).toInt()
        val view = SkeletonOverlayView(activity)
        val lp = FrameLayout.LayoutParams(pipW, pipH).apply {
            gravity = Gravity.TOP or Gravity.END
            topMargin = margin
            marginEnd = margin
        }
        content.addView(view, lp)
        overlayView = view
        Log.i(TAG, "overlay added ${pipW}x${pipH}")
    }

    private fun removeOverlay(activity: Activity?) {
        val v = overlayView ?: return
        try {
            val act = activity ?: hostActivity
            val content = act?.findViewById<ViewGroup>(android.R.id.content)
            content?.removeView(v)
        } catch (_: Throwable) {}
        overlayView = null
    }

    fun getLatestPose(): FloatArray = latestPose.get()
    fun getInferenceFps(): Float = currentFps
    fun isRunning(): Boolean = running
}

class PluginLifecycleOwner : LifecycleOwner {
    private val registry = LifecycleRegistry(this)
    override val lifecycle: Lifecycle get() = registry
    fun start() {
        registry.handleLifecycleEvent(Lifecycle.Event.ON_CREATE)
        registry.handleLifecycleEvent(Lifecycle.Event.ON_START)
    }
    fun stop() {
        registry.handleLifecycleEvent(Lifecycle.Event.ON_STOP)
        registry.handleLifecycleEvent(Lifecycle.Event.ON_DESTROY)
    }
}
