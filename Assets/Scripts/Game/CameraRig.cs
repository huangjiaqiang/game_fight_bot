using UnityEngine;

namespace FightBot.Game
{
    /// <summary>第三人称跟随摄像机, 锁定在机甲侧后方.</summary>
    public class CameraRig : MonoBehaviour
    {
        public Transform Target;
        public Vector3 Offset = new Vector3(0f, 5f, -9f);
        public float SmoothTime = 0.12f;
        public float LookAtHeight = 1.5f;

        Camera cam;
        Vector3 vel;

        void Awake() => cam = GetComponent<Camera>();

        void LateUpdate()
        {
            if (Target == null) return;
            Vector3 desired = Target.position + Target.TransformVector(Offset);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref vel, SmoothTime);
            Vector3 lookAt = Target.position + Vector3.up * LookAtHeight;
            transform.rotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
        }
    }
}
