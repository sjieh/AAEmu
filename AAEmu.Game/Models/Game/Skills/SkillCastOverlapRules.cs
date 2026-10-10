namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Hold-to-repeat and plot-only combo hits (Flamebolt 10752 + 24894/24895).
/// The client starts each hit. Instant follow-ups skip the 150 ms anti-spam
/// and must not cancel the in-flight plot.
/// </summary>
public static class SkillCastOverlapRules
{
    /// <summary>
    /// Instant plot hits with a tiny custom GCD (Flamebolt 24894/24895 are 0 ms / 10 ms).
    /// </summary>
    public static bool IsInstantComboHit(int castingTime, int customGcd) =>
        castingTime <= 0 && customGcd > 0 && customGcd <= 50;

    /// <summary>
    /// Combo hits skip the shared SkillLastUsed anti-spam. They still honor the
    /// shared GCD (first hit 1000 ms, follow-ups 10 ms). They must not write
    /// SkillLastUsed, or the next parent press is blocked.
    /// </summary>
    public static bool BypassesSharedCastGate(int castingTime, int customGcd) =>
        IsInstantComboHit(castingTime, customGcd);

    /// <summary>
    /// Each hit arms its own <c>custom_gcd</c>. A skill with <c>weapon_gcd_id</c> also arms its
    /// weapon-based GCD, even if <c>default_gcd</c> is not set. Skills with only <c>default_gcd</c>
    /// still arm the server default: 29,054 skills carry custom_gcd 0 with default_gcd set.
    /// </summary>
    public static bool ArmsSharedGlobalCooldown(
        int castingTime,
        int customGcd,
        bool defaultGcd,
        bool hasWeaponGcd = false) =>
        customGcd > 0 || hasWeaponGcd || defaultGcd;

    /// <summary>
    /// A new plot cancels a busy one, except combo hits and a repeat of the same skill
    /// (hold / GCD next). Those must finish their own Fired/Damaged.
    /// Fishing hold swap still goes through <paramref name="sportFishWouldCancel"/>.
    /// </summary>
    public static bool ShouldCancelPreviousPlot(
        bool previousWasBusy,
        bool incomingIsInstantCombo,
        bool incomingSameSkill,
        bool sportFishWouldCancel,
        bool previousIsComboFollowUpOfIncoming = false)
    {
        if (incomingIsInstantCombo || incomingSameSkill || previousIsComboFollowUpOfIncoming)
            return false;
        return sportFishWouldCancel;
    }
}
