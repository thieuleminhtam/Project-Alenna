using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// Difficulty / stat knobs. Edit them on the GameManager object in the Inspector (outside Play mode to keep them),
    /// or live from the Esc pause menu while playing.
    /// </summary>
    [System.Serializable]
    public class Tuning
    {
        [Header("Boss — Hộ Vệ Rễ Đen")]
        [Tooltip("Máu tối đa của boss")] public int bossHp = 1100;
        [Tooltip("Thanh thể lực (bị phá khi parry/chém)")] public float bossStamina = 360f;
        [Tooltip("Tốc độ hồi thể lực mỗi giây (sau 2.5 s không bị đánh)")] public float bossStaminaRegen = 6f;
        [Range(0.3f, 0.95f), Tooltip("Dưới tỉ lệ máu này → phase 2")] public float phase2At = 0.7f;
        [Range(0.02f, 0.5f), Tooltip("Dưới tỉ lệ máu này → phase 3")] public float phase3At = 0.1f;
        [Range(0.5f, 2f), Tooltip("Tốc độ ra đòn (vung, gồng, chạy)")] public float bossSpeed = 1.15f;
        [Range(0.3f, 3f), Tooltip("Tần suất ra đòn: càng cao càng ít nghỉ")] public float bossAggression = 1.5f;

        [Header("Xác sống")]
        public int zombieHp = 70;
        public float zombieStamina = 100f;

        [Header("Người chơi")]
        [Range(1, 10)] public int hearts = 3;
        [Range(0, 10)] public int flasks = 2;
        [Tooltip("Thể lực tối đa — chỉ mất khi đỡ đòn (parry trễ)")] public float maxStamina = 60f;
        public float blockCost = 22f;
        // (renamed fields so the wider defaults take effect even on an existing scene)
        [Range(0.04f, 0.35f), Tooltip("Cửa sổ parry chuẩn (giây)")] public float perfectParryWindow = 0.17f;
        [Range(0.04f, 0.5f), Tooltip("Thời gian bất tử khi lộn (giây)")] public float rollInvuln = 0.24f;
        [Range(0f, 150f), Tooltip("Sức bền quái mất khi parry chuẩn")] public float parryStaminaDamage = 45f;
        [Range(0.1f, 1.5f), Tooltip("Hệ số sức bền quái mất khi chém trúng")] public float hitStaminaScale = 0.6f;

        static readonly Tuning fallback = new Tuning();
        public static Tuning I { get { return GameManager.I != null && GameManager.I.tuning != null ? GameManager.I.tuning : fallback; } }
    }
}
