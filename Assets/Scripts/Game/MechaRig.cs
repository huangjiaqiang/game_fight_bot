using UnityEngine;

namespace FightBot.Game
{
    /// <summary>
    /// 程序化机甲骨骼. 不依赖外部 FBX, 用 primitive GameObject 组装, 父子层级构成骨骼.
    /// 由 MechaController 在 Update 中驱动各关节 transform.
    /// </summary>
    public class MechaRig : MonoBehaviour
    {
        [Header("Body parts")]
        public Transform Root;
        public Transform Hips;
        public Transform Spine;
        public Transform Chest;
        public Transform Head;

        public Transform LeftShoulder;
        public Transform LeftUpperArm;
        public Transform LeftForearm;
        public Transform LeftHand;
        public Transform LeftWeapon;

        public Transform RightShoulder;
        public Transform RightUpperArm;
        public Transform RightForearm;
        public Transform RightHand;
        public Transform RightWeapon;

        public Transform LeftUpperLeg;
        public Transform LeftLowerLeg;
        public Transform LeftFoot;
        public Transform RightUpperLeg;
        public Transform RightLowerLeg;
        public Transform RightFoot;

        [Header("Renderers (颜色可调)")]
        public Renderer BodyRenderer;
        public Renderer WeaponRenderer;

        /// <summary>挂到 Mecha.prefab 上的 material 主色, 可在运行时切换(受击红色等).</summary>
        // 内置管线 Standard 着色器的主色属性是 _Color (URP 是 _BaseColor, 已弃用 URP)
        static readonly int BaseColorId = Shader.PropertyToID("_Color");

        Color lastBodyColor = new Color(-1f, -1f, -1f, -1f); // 哨兵: 确保首次写入
        public void SetBodyColor(Color c)
        {
            // 热路径: ApplyRig 每帧调用. 非受击时颜色恒定, 短路避免每帧 r.materials 分配 GC.
            if (c == lastBodyColor) return;
            lastBodyColor = c;
            SetRendererColor(BodyRenderer, c);
        }
        public void SetWeaponColor(Color c) => SetRendererColor(WeaponRenderer, c);

        // 遍历 renderer 的所有材质设主色 (FBX SkinnedMeshRenderer 可能多 sub-mesh/材质)
        static void SetRendererColor(Renderer r, Color c)
        {
            if (r == null) return;
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null) mats[i].SetColor(BaseColorId, c);
        }
    }
}
