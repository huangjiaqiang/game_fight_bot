using UnityEngine;

namespace FightBot.Game
{
    /// <summary>测试用静态敌人方块, 受击掉血死亡后倒下, 用于验证机甲攻击命中.</summary>
    public class EnemyDummy : MonoBehaviour
    {
        public int MaxHp = 100;
        public int CurrentHp { get; private set; }
        public bool IsAlive { get; private set; } = true;

        Renderer rend;
        Color baseColor;
        float deathT = -1f;

        void Awake()
        {
            rend = GetComponentInChildren<Renderer>();
            if (rend != null) baseColor = rend.material.color;
            CurrentHp = MaxHp;
        }

        void Update()
        {
            if (deathT >= 0)
            {
                deathT += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.Euler(90f, transform.eulerAngles.y, 0f),
                    Mathf.Clamp01(deathT * 4f));
                if (deathT > 2f) gameObject.SetActive(false);
            }
        }

        /// <returns>true if this hit killed the enemy</returns>
        public bool TakeDamage(int dmg)
        {
            if (!IsAlive) return false;
            CurrentHp = Mathf.Max(0, CurrentHp - dmg);
            if (rend != null) rend.material.color = Color.Lerp(baseColor, Color.red, 0.5f);
            if (CurrentHp <= 0)
            {
                IsAlive = false;
                deathT = 0f;
                return true;
            }
            return false;
        }
    }
}
