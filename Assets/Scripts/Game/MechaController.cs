using FightBot.Motion;
using UnityEngine;

namespace FightBot.Game
{
    /// <summary>
    /// 机甲主控. 读 MotionPipeline 输出的 BodyIntent + OnJumpExternal 跳跃事件,
    /// 驱动 MechaRig 各关节 transform 与机甲行为.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MechaController : MonoBehaviour
    {
        public MechaRig Rig;
        public MotionPipeline Pipeline;

        [Header("Movement")]
        public float MoveSpeed = 6f;
        public float ForwardSpeed = 4f;
        public float TurnSpeedDeg = 240f;
        public float Gravity = -25f;

        [Header("Jump")]
        public float JumpHeight = 2.5f;

        [Header("Attack")]
        public float AttackRange = 2.5f;
        public float AttackArc = 0.7f;          // cos(45°)
        public float AttackCooldown = 0.4f;
        public int AttackDamage = 25;

        [Header("Hit reaction")]
        public float HitFlashDuration = 0.15f;

        [Header("Rig Tuning (骨骼姿态调参, Play 后在 Inspector 微调)")]
        public float LeanSpineZDeg = 15f;       // 身体侧倾角
        public float LeanSpineXDeg = 10f;       // 前后倾角
        public float ArmRaiseAngle = -80f;      // 举手时上臂 X 角
        public float ArmRestAngle = -20f;       // 放下时上臂 X 角
        public float ArmTwistDeg = 0f;          // 相对 bind 的额外 Z 旋 (bind 自带朝向, 默认 0; 微调用)
        public float SquatDropY = 0.4f;         // 蹲下臀部下沉量
        public float PunchReachForearm = 0.5f;  // 出拳前臂前伸量
        public float PunchReachWeapon = 0.4f;   // 出拳武器前伸量
        public float RestWeaponZ = 0.4f;        // 武器锚点静止 z

        CharacterController cc;
        Vector3 velocity;
        bool airborne;
        float attackCdTimer;
        bool attackingLeft;
        bool attackingRight;
        float attackAnimT;
        float hitFlashT;
        // FBX 骨骼初始 localPosition: 绝对覆盖会破坏骨骼相对位置, 改为相对初始偏移
        bool rigInitDone;
        Vector3 hipsInitLocalPos;
        Vector3 leftForearmInitLocalPos;
        Vector3 rightForearmInitLocalPos;
        Quaternion spineInitRot;
        Quaternion leftArmInitRot;
        Quaternion rightArmInitRot;
        public int MaxHp = 100;
        int currentHp;
        public int CurrentHp => currentHp;

        public System.Action<int, int> OnHpChanged;
        public System.Action<int> OnScoredKill;

        static readonly Color BodyColorIdle = new Color(0.55f, 0.6f, 0.7f);

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            currentHp = MaxHp;
        }

        void OnEnable()
        {
            if (Pipeline != null) Pipeline.OnJumpExternal += OnJumpSignal;
        }

        void OnDisable()
        {
            if (Pipeline != null) Pipeline.OnJumpExternal -= OnJumpSignal;
        }

        void Update()
        {
            if (Pipeline == null) return;

            float dt = Time.deltaTime;

            // ---- 机甲固定位置: 不平移/不转向, 只跟随肢体姿态. 仍保留重力 + 跳跃 ----
            float leanX = Pipeline.BodyIntent.LeanX;
            float leanZ = Pipeline.BodyIntent.LeanZ;
            Vector3 move = Vector3.zero;

            // ---- 重力 + 跳跃 ----
            velocity.y += Gravity * dt;
            if (cc.isGrounded && velocity.y < 0f)
            {
                velocity.y = -2f;
                airborne = false;
            }
            move.y = velocity.y;
            cc.Move(move * dt);

            // ---- 攻击 ----
            attackCdTimer -= dt;
            if (attackAnimT > 0)
            {
                attackAnimT -= dt / 0.25f;
                if (attackAnimT < 0) attackAnimT = 0;
            }
            if (Pipeline.BodyIntent.LeftPunchTriggered && attackCdTimer <= 0)
            {
                attackingLeft = true;
                attackAnimT = 1f;
                attackCdTimer = AttackCooldown;
                TryHitEnemies();
            }
            if (Pipeline.BodyIntent.RightPunchTriggered && attackCdTimer <= 0)
            {
                attackingRight = true;
                attackAnimT = 1f;
                attackCdTimer = AttackCooldown;
                TryHitEnemies();
            }

            ApplyRig(leanX, leanZ, Pipeline.BodyIntent.Squat);

            if (hitFlashT > 0) hitFlashT -= dt;
        }

        public void OnJumpSignal(float intensity)
        {
            if (!airborne && cc.isGrounded)
            {
                airborne = true;
                velocity.y = Mathf.Sqrt(2f * JumpHeight * Mathf.Abs(Gravity));
            }
        }

        void ApplyRig(float leanX, float leanZ, bool squat)
        {
            if (Rig == null) return;
            CacheRigInit();

            // 脊椎侧倾/前后倾: bind rot 基础上叠加增量 (绝对覆盖会丢 bind 朝向)
            if (Rig.Spine != null)
                Rig.Spine.localRotation = spineInitRot * Quaternion.Euler(leanZ * LeanSpineXDeg, 0f, -leanX * LeanSpineZDeg);

            // 蹲: 相对初始臀部位置下沉 (绝对覆盖会把骨骼从父节点拔出导致错位)
            float squatY = squat ? -SquatDropY : 0f;
            if (Rig.Hips != null)
                Rig.Hips.localPosition = hipsInitLocalPos + new Vector3(0f, squatY, 0f);

            // 手臂: bind rot 基础上叠加抬手增量 (举手/放下); 右侧 Z 镜像
            float leftArm = Pipeline.BodyIntent.LeftArmRaised ? ArmRaiseAngle : ArmRestAngle;
            float rightArm = Pipeline.BodyIntent.RightArmRaised ? ArmRaiseAngle : ArmRestAngle;
            if (Rig.LeftUpperArm != null)
                Rig.LeftUpperArm.localRotation = leftArmInitRot * Quaternion.Euler(leftArm, 0f, ArmTwistDeg);
            if (Rig.RightUpperArm != null)
                Rig.RightUpperArm.localRotation = rightArmInitRot * Quaternion.Euler(rightArm, 0f, -ArmTwistDeg);

            // 出拳: 前臂/武器沿 z 前伸. 前臂相对初始位姿偏移 (骨骼); 武器锚点绝对 (锚点初始已知)
            float punch = attackAnimT > 0 ? Mathf.Sin(attackAnimT * Mathf.PI) : 0f;
            if (attackAnimT <= 0) { attackingLeft = false; attackingRight = false; }

            if (Rig.LeftForearm != null)
                Rig.LeftForearm.localPosition = leftForearmInitLocalPos + new Vector3(0f, 0f, (attackingLeft ? punch * PunchReachForearm : 0f));
            if (Rig.RightForearm != null)
                Rig.RightForearm.localPosition = rightForearmInitLocalPos + new Vector3(0f, 0f, (attackingRight ? punch * PunchReachForearm : 0f));
            if (Rig.LeftWeapon != null)
                Rig.LeftWeapon.localPosition = new Vector3(0f, 0f, RestWeaponZ + (attackingLeft ? punch * PunchReachWeapon : 0f));
            if (Rig.RightWeapon != null)
                Rig.RightWeapon.localPosition = new Vector3(0f, 0f, RestWeaponZ + (attackingRight ? punch * PunchReachWeapon : 0f));

            if (hitFlashT > 0)
                Rig.SetBodyColor(Color.Lerp(BodyColorIdle, Color.red, hitFlashT / HitFlashDuration));
            else
                Rig.SetBodyColor(BodyColorIdle);
        }

        // 缓存骨骼初始位姿 (position + rotation), 供蹲下/出拳/抬手做相对偏移.
        // 任一字段未映射 (null) 时 rigInitDone 保持 false, 下帧重试 (#5).
        void CacheRigInit()
        {
            if (rigInitDone || Rig == null) return;
            bool allCached = true;
            if (Rig.Spine != null) spineInitRot = Rig.Spine.localRotation; else allCached = false;
            if (Rig.Hips != null) hipsInitLocalPos = Rig.Hips.localPosition; else allCached = false;
            if (Rig.LeftUpperArm != null) leftArmInitRot = Rig.LeftUpperArm.localRotation; else allCached = false;
            if (Rig.RightUpperArm != null) rightArmInitRot = Rig.RightUpperArm.localRotation; else allCached = false;
            if (Rig.LeftForearm != null) leftForearmInitLocalPos = Rig.LeftForearm.localPosition; else allCached = false;
            if (Rig.RightForearm != null) rightForearmInitLocalPos = Rig.RightForearm.localPosition; else allCached = false;
            rigInitDone = allCached;
        }

        void TryHitEnemies()
        {
            Collider[] hits = Physics.OverlapSphere(
                transform.position + transform.forward * 1f,
                AttackRange, LayerMask.GetMask("Enemy"));
            foreach (var h in hits)
            {
                Vector3 toEnemy = (h.transform.position - transform.position).normalized;
                if (Vector3.Dot(toEnemy, transform.forward) >= AttackArc)
                {
                    var enemy = h.GetComponent<EnemyDummy>();
                    if (enemy != null && enemy.IsAlive)
                    {
                        bool killed = enemy.TakeDamage(AttackDamage);
                        if (killed) OnScoredKill?.Invoke(AttackDamage);
                    }
                }
            }
        }

        public void TakeDamage(int dmg)
        {
            if (Pipeline != null && Pipeline.BodyIntent.Squat) dmg = Mathf.FloorToInt(dmg * 0.5f);
            currentHp = Mathf.Max(0, currentHp - dmg);
            hitFlashT = HitFlashDuration;
            OnHpChanged?.Invoke(currentHp, MaxHp);
        }
    }
}
