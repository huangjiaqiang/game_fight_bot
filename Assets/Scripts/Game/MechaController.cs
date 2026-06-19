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

        CharacterController cc;
        Vector3 velocity;
        bool airborne;
        float attackCdTimer;
        bool attackingLeft;
        bool attackingRight;
        float attackAnimT;
        float hitFlashT;
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

            // ---- 移动 ----
            Vector3 move = Vector3.zero;
            float leanX = Pipeline.BodyIntent.LeanX;
            float leanZ = Pipeline.BodyIntent.LeanZ;
            move.x = leanX * MoveSpeed;
            move.z = leanZ * ForwardSpeed;

            if (new Vector2(move.x, move.z).sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(
                    new Vector3(move.x, 0f, move.z).normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target, TurnSpeedDeg * dt);
            }

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

            Vector3 spineEuler = Rig.Spine.localEulerAngles;
            spineEuler.z = -leanX * 15f;
            spineEuler.x = leanZ * 10f;
            Rig.Spine.localRotation = Quaternion.Euler(spineEuler);

            float squatY = squat ? -0.4f : 0f;
            Rig.Hips.localPosition = new Vector3(0f, squatY, 0f);

            float leftArm = Pipeline.BodyIntent.LeftArmRaised ? -80f : -20f;
            float rightArm = Pipeline.BodyIntent.RightArmRaised ? -80f : -20f;
            Rig.LeftUpperArm.localRotation = Quaternion.Euler(leftArm, 0f, 90f);
            Rig.RightUpperArm.localRotation = Quaternion.Euler(rightArm, 0f, -90f);

            if (attackAnimT > 0)
            {
                float punch = Mathf.Sin(attackAnimT * Mathf.PI);
                if (attackingLeft)
                {
                    Rig.LeftForearm.localPosition = new Vector3(0f, 0f, 0.3f + punch * 0.5f);
                    Rig.LeftWeapon.localPosition = new Vector3(0f, 0f, 0.4f + punch * 0.4f);
                }
                if (attackingRight)
                {
                    Rig.RightForearm.localPosition = new Vector3(0f, 0f, 0.3f + punch * 0.5f);
                    Rig.RightWeapon.localPosition = new Vector3(0f, 0f, 0.4f + punch * 0.4f);
                }
            }
            else
            {
                attackingLeft = false;
                attackingRight = false;
                Rig.LeftForearm.localPosition = new Vector3(0f, 0f, 0.3f);
                Rig.RightForearm.localPosition = new Vector3(0f, 0f, 0.3f);
                Rig.LeftWeapon.localPosition = new Vector3(0f, 0f, 0.4f);
                Rig.RightWeapon.localPosition = new Vector3(0f, 0f, 0.4f);
            }

            if (hitFlashT > 0)
                Rig.SetBodyColor(Color.Lerp(BodyColorIdle, Color.red, hitFlashT / HitFlashDuration));
            else
                Rig.SetBodyColor(BodyColorIdle);
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
