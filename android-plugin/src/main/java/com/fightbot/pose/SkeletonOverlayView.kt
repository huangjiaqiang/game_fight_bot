package com.fightbot.pose

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.PixelFormat
import android.graphics.RectF
import android.view.SurfaceHolder
import android.view.SurfaceView

/**
 * 右上角画中画: 摄像头帧 + 17 关键点骨骼. 移植自 game_jumping_in_muddle_paddle 的
 * GameEngine.drawCameraPip / drawSingleSkeleton.
 *
 * SurfaceView + 自渲染线程 (lockHardwareCanvas), 读 PosePlugin 的 latestFrame + latestPose.
 * 显示帧 == 推理帧 (PosePlugin 里推理用的同一张镜像 bitmap), 故骨骼与人体天然对齐.
 */
class SkeletonOverlayView(context: Context) : SurfaceView(context), SurfaceHolder.Callback {

    private var renderThread: Thread? = null
    @Volatile private var rendering = false

    private val bgPaint = Paint(Paint.FILTER_BITMAP_FLAG)
    private val borderPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE; strokeWidth = 3f; style = Paint.Style.STROKE
    }
    private val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(255, 128, 51); strokeWidth = 6f; style = Paint.Style.STROKE; strokeCap = Paint.Cap.ROUND
    }
    private val kpPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }

    // MoveNet 17 关键点标准骨架连接
    private val bones = arrayOf(
        intArrayOf(0, 1), intArrayOf(0, 2), intArrayOf(1, 3), intArrayOf(2, 4),
        intArrayOf(0, 5), intArrayOf(0, 6),
        intArrayOf(5, 7), intArrayOf(7, 9), intArrayOf(6, 8), intArrayOf(8, 10),
        intArrayOf(5, 6), intArrayOf(5, 11), intArrayOf(6, 12), intArrayOf(11, 12),
        intArrayOf(11, 13), intArrayOf(13, 15), intArrayOf(12, 14), intArrayOf(14, 16)
    )

    init {
        holder.addCallback(this)
        setZOrderOnTop(true)            // 让骨骼小窗 surface 在 Unity 之上
        holder.setFormat(PixelFormat.TRANSLUCENT)
        isClickable = false
        isFocusable = false
    }

    override fun surfaceCreated(holder: SurfaceHolder) {
        rendering = true
        renderThread = Thread {
            while (rendering) {
                drawOnce(holder)
                try { Thread.sleep(16) } catch (_: InterruptedException) { break }
            }
        }.apply { isDaemon = true; name = "FightBotSkeleton"; start() }
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, w: Int, h: Int) {}
    override fun surfaceDestroyed(holder: SurfaceHolder) {
        rendering = false
        try { renderThread?.join(120) } catch (_: InterruptedException) {}
    }

    private fun drawOnce(holder: SurfaceHolder) {
        val canvas = holder.lockHardwareCanvas() ?: return
        try {
            val vw = canvas.width.toFloat()
            val vh = canvas.height.toFloat()
            canvas.drawColor(Color.argb(210, 0, 0, 0))  // 半透明黑底

            val frame = PosePlugin.latestFrame
            val pose = PosePlugin.getLatestPose()

            // centerCrop (与参考一致): dx/dy/dW/dH 复用于骨骼映射 => 对齐
            var dx = 0f; var dy = 0f; var dW = vw; var dH = vh
            if (frame != null) {
                val s = maxOf(vw / frame.width, vh / frame.height)
                dW = frame.width * s; dH = frame.height * s
                dx = (vw - dW) / 2f; dy = (vh - dH) / 2f
                canvas.drawBitmap(frame, null, RectF(dx, dy, dx + dW, dy + dH), bgPaint)
            }
            if (pose != null && pose.size == 51) drawSkeleton(canvas, pose, dx, dy, dW, dH)

            canvas.drawRect(0f, 0f, vw, vh, borderPaint)  // 白色描边
        } finally {
            holder.unlockCanvasAndPost(canvas)
        }
    }

    private fun drawSkeleton(canvas: Canvas, pose: FloatArray, dx: Float, dy: Float, dW: Float, dH: Float) {
        // pose 布局: [x,y,score]*17
        fun px(i: Int) = dx + pose[i * 3] * dW
        fun py(i: Int) = dy + pose[i * 3 + 1] * dH
        fun vis(i: Int) = pose[i * 3 + 2] > 0.3f

        for (b in bones) {
            if (vis(b[0]) && vis(b[1]))
                canvas.drawLine(px(b[0]), py(b[0]), px(b[1]), py(b[1]), linePaint)
        }
        for (i in 0 until 17) {
            val sc = pose[i * 3 + 2]
            if (sc <= 0.3f) continue
            kpPaint.color = when {
                i == 11 || i == 12 -> Color.rgb(255, 217, 0)   // 髋部高亮
                sc > 0.6f -> Color.rgb(80, 255, 120)           // 高置信绿
                else -> Color.rgb(255, 180, 80)                // 低置信橙
            }
            val r = if (i == 11 || i == 12) dW * 0.022f else dW * 0.014f
            canvas.drawCircle(px(i), py(i), r, kpPaint)
        }
    }
}
