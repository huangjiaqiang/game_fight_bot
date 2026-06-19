using UnityEngine;

namespace FightBot.Game
{
    /// <summary>第三人称跟随摄像机: 锁定机甲居中 + 双指捏合缩放.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public Transform Target;
        // 相对机甲的偏移: 较低 + 正后方, 让机甲居中
        public Vector3 Offset = new Vector3(0f, 3.5f, -9f);
        public float SmoothTime = 0.12f;
        public float LookAtHeight = 1.1f;   // 注视机甲中心高度 => 居中

        [Header("双指缩放 (pinch zoom)")]
        public float MinFov = 25f;
        public float MaxFov = 75f;
        public float ZoomSpeed = 0.1f;      // 每像素 pinch delta 对应的 FOV 变化

        Camera cam;
        Vector3 vel;
        float prevPinchDist;
        bool pinchInit;

        void Awake() => cam = GetComponent<Camera>();

        void Update()
        {
            // 双指捏合: 调 FOV (拉远=FOV变大, 拉近=FOV变小)
            if (Input.touchCount == 2)
            {
                var t0 = Input.GetTouch(0);
                var t1 = Input.GetTouch(1);
                float cur = Vector2.Distance(t0.position, t1.position);
                if (!pinchInit || t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
                {
                    prevPinchDist = cur;
                    pinchInit = true;
                }
                else
                {
                    float delta = cur - prevPinchDist;
                    cam.fieldOfView = Mathf.Clamp(cam.fieldOfView - delta * ZoomSpeed, MinFov, MaxFov);
                    prevPinchDist = cur;
                }
            }
            else pinchInit = false;
        }

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
