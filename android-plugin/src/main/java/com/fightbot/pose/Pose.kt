package com.fightbot.pose

/**
 * MoveNet 17 关键点. 归一化 [0,1], x 向右 y 向下.
 * 插件只透传 x/y/score; Unity 侧 Pose.cs 负责派生量.
 */
data class KeyPoint(val x: Float, val y: Float, val score: Float)

class Pose(val keypoints: List<KeyPoint>) {
    companion object { const val COUNT = 17 }
}
