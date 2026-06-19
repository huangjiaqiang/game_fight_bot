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
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void SetBodyColor(Color c)
        {
            if (BodyRenderer != null) BodyRenderer.material.SetColor(BaseColorId, c);
        }

        public void SetWeaponColor(Color c)
        {
            if (WeaponRenderer != null) WeaponRenderer.material.SetColor(BaseColorId, c);
        }
    }
}
