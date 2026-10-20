using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using MySql.Data.MySqlClient;
using NLog;

using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterSkills(Character owner)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private enum SkillType : byte
    {
        Skill = 1,
        Buff = 2
    }

    // Tracks the initial skill-loading phase.
    private bool _loadingSkills;
    private readonly List<uint> _removed = [];
    private readonly HashSet<uint> _dwarfJuggernautPassiveIds = [];
    private readonly object _comboFollowUpsLock = new();
    private readonly Dictionary<uint, DateTime> _comboFollowUps = [];
    public Dictionary<uint, Skill> Skills { get; } = [];
    public Dictionary<uint, PassiveBuff> PassiveBuffs { get; } = [];
    private Character Owner { get; } = owner;

    /// <summary>
    /// Try to learn a new Skill.
    /// </summary>
    /// <returns>True when the skill is learned afterwards, including when it was already known.</returns>
    public bool AddSkill(uint skillId) => AddSkill(skillId, consumeLearnItem: true);

    /// <summary>
    /// Checks the normal race, level, ability and skill-point requirements while leaving item
    /// payment to a cast effect that owns the cost in <c>skill_effects</c>.
    /// </summary>
    internal bool CanLearnSkillWithoutLearnItemCost(uint skillId)
    {
        if (Skills.ContainsKey(skillId))
            return false;

        if (HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out _, out _))
            return false;

        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
            return false;

        if (template.CharRaceId > 0)
        {
            if ((int)Owner.Race != template.CharRaceId || Owner.Level < template.AbilityLevel)
                return false;
        }
        else if (template.AbilityId > 0 &&
                 template.AbilityId != Owner.Ability1 &&
                 template.AbilityId != Owner.Ability2 &&
                 template.AbilityId != Owner.Ability3)
        {
            return false;
        }

        var points = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level);
        points -= GetUsedSkillPoints(AbilityType.General);
        return template.SkillPoints <= points;
    }

    /// <summary>
    /// Learns a skill whose item requirement has already been validated and consumed by its skill effect.
    /// </summary>
    internal bool AddSkillWithoutLearnItemCost(uint skillId) => AddSkill(skillId, consumeLearnItem: false);

    private bool AddSkill(uint skillId, bool consumeLearnItem)
    {
        // Successors are selected through CSActivateHeirSkill and remain separate from the learned
        // base-skill table. Accepting one here would persist it as an ordinary learned skill.
        if (HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out _, out _))
        {
            Logger.Warn("LearnSkill reject {0}: {1} is a Heir successor", Owner.Name, skillId);
            return false;
        }

        // Check if what we want to learn is part of an active skill tree (or not part of one)
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
        {
            Logger.Warn("LearnSkill reject {0}: skill={1} has no template", Owner.Name, skillId);
            return false;
        }

        // Race skills live outside the three selected skill trees. Their own race and player-level
        // requirements are stored on the skill row; treating AbilityId as a normal tree requirement
        // silently rejects Dwarf/Warborn transformation skills for characters without that tree.
        if (template.CharRaceId > 0)
        {
            if ((int)Owner.Race != template.CharRaceId)
            {
                Logger.Warn("LearnSkill reject {0}: skill={1} requires race={2}, character race={3}",
                    Owner.Name, skillId, template.CharRaceId, (int)Owner.Race);
                return false;
            }

            if (Owner.Level < template.AbilityLevel)
            {
                Logger.Info("LearnSkill reject {0}: skill={1} requires level={2}, character level={3}",
                    Owner.Name, skillId, template.AbilityLevel, Owner.Level);
                return false;
            }
        }
        else if (template.AbilityId > 0 &&
            template.AbilityId != Owner.Ability1 &&
            template.AbilityId != Owner.Ability2 &&
            template.AbilityId != Owner.Ability3)
        {
            Logger.Info("LearnSkill reject {0}: skill={1} requires ability={2}; active abilities are {3}, {4}, {5}",
                Owner.Name, skillId, template.AbilityId, Owner.Ability1, Owner.Ability2, Owner.Ability3);
            return false;
        }

        // Get total skill points for the player's level
        var points = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level);

        // Deduct the amount of skill points already used
        points -= GetUsedSkillPoints(AbilityType.General);

        // Check if we have enough remaining to learn this Skill
        if (template.SkillPoints > points)
        {
            Logger.Info(
                "LearnSkill reject {0}: skill={1} cost={2} remaining={3} (levelBudget={4})",
                Owner.Name, skillId, template.SkillPoints, points,
                ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level));
            return false;
        }

        // The database owns item requirements for skills. Most skills have no learn item, but
        // racial transformation skills can require one (for example, Dwarf Summon Juggernaut).
        // Validate the pair before consuming anything so malformed rows cannot partially learn.
        if (Skills.TryGetValue(skillId, out var skill))
        {
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
            Logger.Info("LearnSkill accepted {0}: skill={1} was already learned", Owner.Name, skillId);
            return true;
        }

        if (consumeLearnItem &&
            ((template.SkillLearnItemId == 0) != (template.SkillLearnItemAmount == 0) ||
             template.SkillLearnItemAmount < 0))
        {
            Logger.Warn("LearnSkill reject {0}: skill={1} has invalid learn-item requirement item={2} amount={3}",
                Owner.Name, skillId, template.SkillLearnItemId, template.SkillLearnItemAmount);
            return false;
        }

        if (consumeLearnItem && template.SkillLearnItemId > 0)
        {
            if (!Owner.Inventory.CheckItems(SlotType.Inventory, template.SkillLearnItemId, template.SkillLearnItemAmount))
            {
                Logger.Info("LearnSkill reject {0}: skill={1} requires item={2} amount={3}",
                    Owner.Name, skillId, template.SkillLearnItemId, template.SkillLearnItemAmount);
                return false;
            }

            var consumed = Owner.Inventory.Bag.ConsumeItem(
                ItemTaskType.SkillReagents, template.SkillLearnItemId, template.SkillLearnItemAmount, null);
            if (consumed != template.SkillLearnItemAmount)
            {
                Logger.Warn("LearnSkill reject {0}: skill={1} consumed {2}/{3} learn items (item={4})",
                    Owner.Name, skillId, consumed, template.SkillLearnItemAmount, template.SkillLearnItemId);
                return false;
            }
        }

        AddSkill(template, 1, true);

        if (consumeLearnItem)
            Logger.Info("LearnSkill accepted {0}: skill={1} race={2} level={3}",
                Owner.Name, skillId, (int)Owner.Race, Owner.Level);
        return true;
    }

    /// <summary>
    /// Adds a Skill and optionally sends a SCSkillLearnedPacket
    /// </summary>
    /// <param name="template"></param>
    /// <param name="level"></param>
    /// <param name="packet"></param>
    public void AddSkill(SkillTemplate template, byte level, bool packet)
    {
        var skill = new Skill
        {
            Id = template.Id,
            Template = template,
            Level = SkillRankRules.ForUnit(template, Owner)
        };
        Skills.Add(skill.Id, skill);

        if (packet)
            Owner.SendPacket(new SCSkillLearnedPacket(skill));

        // Check for newly unlocked passives after learning a skill.
        if (!_loadingSkills)
            GrantUnlockedTreePassives(notify: packet);
    }

    /// <summary>
    /// Try to learn a Passive Skill.
    /// </summary>
    /// <param name="buffId"></param>
    /// <param name="notify">
    /// When true, broadcasts <c>SCBuffLearned</c> (client chat "Learned …"). Use false when
    /// restoring a skillsaver snapshot; the client rebuilds passives from AllInfo / AbilitySetUpdated.
    /// </param>
    public void AddBuff(uint buffId, bool notify = true)
    {
        // Check if what we want to learn is part of an active skill tree (or not part of one)
        var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
        // buffId comes straight off CSLearnBuffPacket, so an unknown one has to be refused rather than
        // dereferenced: passive_buffs 51, 268, 274 and 289 name a buff that is not in the table.
        if (template == null)
            return;

        // passive_buffs.level gates the learn: the 11 rows at 40-60 (the Predator/Trooper chains and two
        // level-60 general passives) were learnable at level 1, because CSLearnBuffPacket carries nothing
        // but the id and nothing else looked at the column.
        if (!PassiveBuffLevelRules.CanLearn(template, Owner.Level))
            return;

        if (template.AbilityId > 0 &&
           template.AbilityId != Owner.Ability1 &&
           template.AbilityId != Owner.Ability2 &&
           template.AbilityId != Owner.Ability3)
            return;

        // Get total skill points for the player's level
        var points = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level);

        // Deduct the amount of skill points already used
        points -= GetUsedSkillPoints(AbilityType.General);

        // Check whether enough points remain (passive_buffs.skill_points; often 0 for early passives).
        if (template.SkillPoints > points)
            return;

        // Check if there are enough points already invested in this tree to allow learning this Passive
        if (GetUsedSkillPoints(template.AbilityId) < template.ReqPoints)
            return;

        // Check if we already learned it
        if (PassiveBuffs.ContainsKey(buffId))
            return;

        // Add Passive Buff
        var buff = new PassiveBuff { Id = buffId, Template = template };
        PassiveBuffs.Add(buff.Id, buff);
        if (notify)
            Owner.BroadcastPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id), true);
        buff.Apply(Owner);
    }

    /// <summary>
    /// Re-evaluates the passives a character holds after their level or ability level moved: every learned
    /// passive is rebuilt at its new ability level, and the <c>passive_buffs</c> rows marked
    /// <c>active='t'</c> that the character has just become old enough for are learned.
    /// </summary>
    /// <remarks>
    /// <c>Character.ApplyLevelUpBenefits</c> used to refresh HP/MP and nothing else, so a passive's
    /// bonuses stayed at the ability level they were first applied with (the <c>Buff.AbLevel</c> default
    /// of 1) for the whole life of the character. A rebuild is a remove plus an apply because the bonus
    /// Values are snapshotted in <c>BuffTemplate.Start</c>. Neither half sends packets for a passive:
    /// <c>Start</c> and <c>Dispel</c> broadcast only for non-passives, so the client is not disturbed.
    /// </remarks>
    public void ReevaluatePassivesOnLevelUp()
    {
        foreach (var passive in PassiveBuffs.Values.ToList())
        {
            if (passive?.Template == null)
                continue;
            passive.Remove(Owner);
            passive.Apply(Owner);
        }

        foreach (var passiveId in SkillManager.Instance.GetAutoGrantablePassiveBuffIds(Owner.Level, PassiveBuffs.Keys))
            AddBuff(passiveId, notify: false);
        // Check ability-branch passives after the character levels up.
        GrantUnlockedTreePassives(notify: false);
        GrantDwarfRacialSkills(notify: true);
    }

    /// <summary>
    /// Grants the Dwarf's transformation cancel skill unconditionally, then grants Juggernaut
    /// auto-learn skills and passives at their character-level gates once Summon Juggernaut is learned.
    /// </summary>
    internal void GrantDwarfRacialSkills(bool notify)
    {
        if ((int)Owner.Race != DwarfTransformationLearnRules.DwarfRaceId)
            return;

        LearnDwarfRaceSkill(
            DwarfTransformationLearnRules.CancelTransformationSkillId,
            notify,
            ignoreLevelRequirement: true);

        var skillManager = SkillManager.Instance;
        var juggernautPassives = skillManager.GetLevelUnlockedPassiveBuffs(
            DwarfTransformationLearnRules.JuggernautAbilityId,
            int.MaxValue);
        foreach (var template in juggernautPassives)
            _dwarfJuggernautPassiveIds.Add(template.Id);

        // The form skills and passives are only useful once the character has learned the form.
        if (!Skills.ContainsKey(DwarfTransformationLearnRules.SummonJuggernautSkillId))
            return;

        foreach (var template in skillManager.GetAutoLearnRaceSkills(
                     DwarfTransformationLearnRules.DwarfRaceId,
                     DwarfTransformationLearnRules.JuggernautAbilityId,
                     Owner.Level))
        {
            LearnDwarfRaceSkill(template.Id, notify);
        }

        foreach (var template in juggernautPassives.Where(template => template.Level <= Owner.Level))
        {
            if (PassiveBuffs.ContainsKey(template.Id))
                continue;

            // These race passives are inactive in content because they are not meant to be purchased
            // with ordinary skill points. Their level gates are still authoritative.
            var passive = new PassiveBuff(template);
            PassiveBuffs.Add(passive.Id, passive);
            if (notify)
                Owner.BroadcastPacket(new SCBuffLearnedPacket(Owner.ObjId, passive.Id), true);
            passive.Apply(Owner);
            Logger.Info("Dwarf Juggernaut passive auto-learned for {0}: passive={1} level={2}",
                Owner.Name, passive.Id, Owner.Level);
        }
    }

    private void LearnDwarfRaceSkill(uint skillId, bool notify, bool ignoreLevelRequirement = false)
    {
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null ||
            template.CharRaceId != DwarfTransformationLearnRules.DwarfRaceId ||
            (!ignoreLevelRequirement && template.AbilityLevel > Owner.Level))
            return;

        var rank = SkillRankRules.ForUnit(template, Owner);
        if (Skills.TryGetValue(skillId, out var learned))
        {
            if (learned.Level == rank)
                return;

            learned.Level = rank;
            if (notify)
                Owner.SendPacket(new SCSkillLearnedPacket(learned));
            Logger.Info("Dwarf race skill rank updated for {0}: skill={1} rank={2}",
                Owner.Name, skillId, rank);
            return;
        }

        AddSkill(template, 1, notify);
        Logger.Info("Dwarf race skill auto-learned for {0}: skill={1} level={2}",
            Owner.Name, skillId, Owner.Level);
    }

    // Automatically grant passives unlocked by reaching
    // the required number of points in an ability branch.
    private void GrantUnlockedTreePassives(bool notify = true)
    {
        foreach (var passiveId in SkillManager.Instance
            .GetPointUnlockedPassiveBuffIds(Owner.Level, PassiveBuffs.Keys))
        {
            AddBuff(passiveId, notify);
        }
    }
    /// <summary>
    /// Re-send every learned skill/passive to this character. Needed after <c>SCAbilitySwapped</c>
    /// when the client wiped trees and there is no <c>SCAbilitySetUpdated(Changed)</c> path to
    /// restore them from a saved set (e.g. NPC <see cref="CharacterAbilities.Swap"/>).
    /// Each <c>SCSkillLearned</c> raises the <c>SKILL_LEARNED</c> chat message; do not use this on skillsaver activation.
    /// Temporary grants are re-sent too: the client has just wiped the same list they were added to.
    /// </summary>
    public void ResendLearnedToOwner()
    {
        foreach (var skill in Skills.Values)
            if (!ReplacedSkillIds.Contains(skill.Id))
                Owner.SendPacket(new SCSkillLearnedPacket(skill));
        // Temporary grants are deliberately not re-sent here: they are never announced as learned in
        // the first place (see AddTemporarySkill), and the client rebuilds them from the buffs it is sent.
        foreach (var buff in PassiveBuffs.Values)
            Owner.SendPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id));
    }

    #region buff-granted skills

    /// <summary>
    /// Skills the character holds only while a buff grants them (<c>buff_skills</c>,
    /// <c>buff_mount_skills</c>, and the replacement side of <c>buff_swap_skills</c>).
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Skills"/>, which is the saved skill list.
    /// <see cref="Save"/> writes exactly its members and <see cref="Load"/> reads them back, so keeping
    /// temporary grants out of it is what stops a buff-granted skill from becoming a learned one on the
    /// next logout, and stops it from eating skill points while it is up. They are also kept out of what
    /// the client is told, because the client's own skill-point counter charges every id it is handed as
    /// learned; see <see cref="LiveSkillIds"/> and <see cref="AddTemporarySkill"/>.
    /// </remarks>
    public Dictionary<uint, Skill> TemporarySkills { get; } = [];

    /// <summary>
    /// Learned skills that an active <c>buff_swap_skills</c> row has removed from the bar. The skill stays learned;
    /// only the list the client is shown drops it, and it comes back when the swap ends.
    /// </summary>
    public HashSet<uint> ReplacedSkillIds { get; } = [];

    // Which active buff grants what, so that two buffs granting the same skill ("one ends, the other
    // still holds it") are reconciled together instead of one of them revoking the other's grant.
    private readonly Dictionary<Buff, BuffGrantSet> _buffGrantHolders = [];
    private readonly Dictionary<uint, PassiveBuff> _grantedPassives = [];

    /// <summary>
    /// Registers what <paramref name="buff"/> grants and reconciles the character's temporary set
    /// against every other active grant. Called from <c>BuffTemplate.Start</c>, which also runs on a
    /// refresh and on stack growth, so this has to be idempotent: the buff's entry is replaced, not
    /// added to.
    /// </summary>
    public void ApplyBuffGrants(Buff buff, BuffGrantSet grants)
    {
        if (buff == null || grants == null || grants.IsEmpty)
            return;

        _buffGrantHolders[buff] = grants;
        ReconcileBuffGrants();
    }

    /// <summary>
    /// Drops what <paramref name="buff"/> granted and reconciles the rest. Called from
    /// <c>BuffTemplate.Dispel</c> is called for every way a buff can end: dispel, timeout, removal, or death.
    /// It is also called twice on the <c>Buffs.RemoveBuff</c> path, which is why an unknown buff is simply ignored.
    /// </summary>
    public void RevokeBuffGrants(Buff buff)
    {
        if (buff == null || !_buffGrantHolders.Remove(buff))
            return;

        ReconcileBuffGrants();
    }

    /// <summary>
    /// Skill ids the client is shown as learned for this character, minus the entries a live swap has
    /// replaced. This is the list <c>SCUnitState</c> carries.
    /// </summary>
    /// <remarks>
    /// Learned skills only. A buff-granted skill must stay out of it: the client derives those from
    /// <c>buff_skills</c> / <c>buff_swap_skills</c> as soon as it is sent the buff (its own compact DB
    /// carries 2,316 + 71 + 849 rows of them), and every id that enters this list is one the client
    /// treats as learned — which it then charges <c>skills.skill_points</c> for. The glider buffs are
    /// the loudest case: 3530 alone hands out seven skills at one point each, so listing them turned a
    /// spent skill-point pool negative while the character was gliding.
    /// </remarks>
    public IReadOnlyList<uint> LiveSkillIds()
    {
        var ids = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var skill in Skills.Values)
            if (!ReplacedSkillIds.Contains(skill.Id) && seen.Add(skill.Id))
                ids.Add(skill.Id);
        return ids;
    }

    /// <summary>
    /// Whether the character holds <paramref name="skillId"/> — learned, buff-granted, the
    /// currently selected Heir successor, or a Combo follow-up of any of those roots.
    /// ZoneAuthority keys off this; without the Combo branch, hold-to-cycle skills (Triple Slash,
    /// Quake heir, Gods' Whip, Whirlwind Slash, …) only land the first hit and the rest are refused unlearned.
    /// </summary>
    public bool HasSkill(uint skillId)
    {
        if (Skills.ContainsKey(skillId) || TemporarySkills.ContainsKey(skillId) || IsActiveHeirSuccessor(skillId))
            return true;

        var roots = Skills.Keys
            .Concat(TemporarySkills.Keys)
            .Concat(Owner.HeirSkills?.ActiveSuccessorSkillIds() ?? []);
        return SkillComboRules.IsHeldOrComboFollowUp(skillId, roots, SkillComboRules.NextFollowUp);
    }

    /// <summary>Opens a data-defined window in which a combo-linked follow-up may be cast.</summary>
    public void OpenComboFollowUp(uint skillId, int windowMilliseconds)
    {
        if (skillId == 0 || windowMilliseconds <= 0)
            return;

        lock (_comboFollowUpsLock)
            _comboFollowUps[skillId] = DateTime.UtcNow.AddMilliseconds(windowMilliseconds);
    }

    /// <summary>Whether <paramref name="skillId"/> is an unexpired combo follow-up.</summary>
    public bool HasActiveComboFollowUp(uint skillId)
    {
        var now = DateTime.UtcNow;
        lock (_comboFollowUpsLock)
        {
            foreach (var expired in _comboFollowUps.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
                _comboFollowUps.Remove(expired);
            return _comboFollowUps.TryGetValue(skillId, out var expiresAt) && expiresAt > now;
        }
    }

    /// <summary>Ends the previous combo window when a new cast starts.</summary>
    public void ClearComboFollowUps()
    {
        lock (_comboFollowUpsLock)
            _comboFollowUps.Clear();
    }

    private void ReconcileBuffGrants()
    {
        var holders = _buffGrantHolders.Values.ToList();

        var added = BuffGrantRules.AddedSkills([.. TemporarySkills.Keys], holders);
        foreach (var skillId in added)
            AddTemporarySkill(skillId);

        var released = BuffGrantRules.ReleasedSkills([.. TemporarySkills.Keys], holders);
        foreach (var skillId in released)
            TemporarySkills.Remove(skillId);

        var replaced = BuffGrantRules.ReplacedOrigins(holders);
        ReplacedSkillIds.Clear();
        foreach (var skillId in replaced)
            ReplacedSkillIds.Add(skillId);

        ReconcileGrantedPassives(holders);

        if (added.Count > 0 || released.Count > 0 || replaced.Count > 0)
            Logger.Info("BuffGrant {0}: +[{1}] -[{2}] replaced=[{3}]",
                Owner.Name,
                string.Join(",", added),
                string.Join(",", released),
                string.Join(",", replaced));
    }

    private void AddTemporarySkill(uint skillId)
    {
        // Already learned: there is nothing temporary to add, and nothing to take away afterwards.
        if (Skills.ContainsKey(skillId))
            return;

        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
            return;

        // buff_skills carries no level, so a grant sits at level 1 for as long as it lasts.
        var skill = new Skill { Id = template.Id, Template = template, Level = 1 };
        TemporarySkills.TryAdd(skillId, skill);

        // No SCSkillLearned here. That packet is the client's "this character learned skill X" and it
        // charges skills.skill_points for it against the character's pool — the server never sends
        // skill points of its own (SCCharacterState writes additionalSkillPoint 0 and
        // SCUpdateAdditionalSkillPoint is only ever sent empty), so the client computes
        // levels.skill_points(level) minus the cost of everything it believes was learned. Announcing a
        // grant that way is what drained the pool: the gliding buffs alone hand out 17657 + 13435 +
        // 13440 + 14236 + 19331 + 19167 + 24113, one point each, and a character whose points were
        // already spent went negative for as long as the glider was out. SCBuffCreated already carries
        // the buff, and the client has the buff_skills / buff_swap_skills tables locally to build the
        // bar entries from it — so nothing is lost by staying quiet here.
    }

    private void ReconcileGrantedPassives(List<BuffGrantSet> holders)
    {
        // A passive the character already learned is left to the learned instance: PassiveBuff.Remove
        // removes the buff family, which would take the learned one down with the grant.
        var wanted = BuffGrantRules.HeldPassives(holders)
            .Where(passiveBuffId => !PassiveBuffs.ContainsKey(passiveBuffId))
            .ToList();

        foreach (var passiveBuffId in wanted.Where(id => !_grantedPassives.ContainsKey(id)).ToList())
        {
            var template = SkillManager.Instance.GetPassiveBuffTemplate(passiveBuffId);
            if (template == null)
                continue;

            // The same application path a learned passive uses (CharacterSkills.AddBuff), only kept out
            // of PassiveBuffs so that it is neither saved nor charged skill points.
            var passive = new PassiveBuff { Id = passiveBuffId, Template = template };
            _grantedPassives[passiveBuffId] = passive;
            passive.Apply(Owner);
            Logger.Info("BuffGrant apply {0}: passive={1}", Owner.Name, passiveBuffId);
        }

        foreach (var passiveBuffId in _grantedPassives.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            if (_grantedPassives.Remove(passiveBuffId, out var passive))
            {
                passive.Remove(Owner);
                Logger.Info("BuffGrant revoke {0}: passive={1}", Owner.Name, passiveBuffId);
            }
        }
    }

    #endregion

    /// <summary>
    /// Resets all skills from a specific ability Skill Tree
    /// </summary>
    /// <param name="abilityId"></param>
    /// <param name="notifyClient">
    /// When false, only mutates server state. Used before <c>SCAbilitySwapped</c> so a following
    /// <c>SCSkillsReset</c> does not cancel the client's learn-ability banner queue.
    /// </param>
    public void Reset(AbilityType abilityId, bool notifyClient = true)
    {
        // TODO: with price...
        foreach (var skill in new List<Skill>(Skills.Values))
        {
            if (skill.Template.AbilityId != abilityId || skill.Template.CharRaceId > 0)
                continue;
            Skills.Remove(skill.Id);
            _removed.Add(skill.Id);
        }

        foreach (var buff in new List<PassiveBuff>(PassiveBuffs.Values))
        {
            if (buff.Template.AbilityId != abilityId || _dwarfJuggernautPassiveIds.Contains(buff.Id))
                continue;
            buff.Remove(Owner);
            PassiveBuffs.Remove(buff.Id);
            _removed.Add(buff.Id);
        }

        Owner.HeirSkills?.RemoveByAbility(abilityId, notifyClient: notifyClient);
        if (notifyClient)
        {
            Owner.BroadcastPacket(new SCSkillsResetPacket(Owner.ObjId, abilityId), true);

            // Racial skills share an AbilityId with their form's content tree but are not members of
            // the character's resettable ability tree. Restore them after the client processes reset.
            if ((int)Owner.Race == DwarfTransformationLearnRules.DwarfRaceId)
            {
                foreach (var skill in Skills.Values.Where(skill => skill.Template.CharRaceId > 0))
                    Owner.SendPacket(new SCSkillLearnedPacket(skill));
                foreach (var passive in PassiveBuffs.Values.Where(passive =>
                             _dwarfJuggernautPassiveIds.Contains(passive.Id)))
                    Owner.SendPacket(new SCBuffLearnedPacket(Owner.ObjId, passive.Id));
            }
        }
    }

    /// <summary>
    /// Get skill points invested in total or for a specific tree
    /// </summary>
    /// <param name="ability">Ability whose Skill Tree to check. Use AbilityType.General if you want the total for all learned skills</param>
    /// <returns>Number of skill points invested</returns>
    private int GetUsedSkillPoints(AbilityType ability)
{
    var points = 0;

    foreach (var skill in Skills.Values)
        if (ability == AbilityType.General || skill.Template.AbilityId == ability)
            points += skill.Template.SkillPoints;

    return points;
}

    /// <summary>
    /// A successor is castable only when the character selected that exact content-defined Heir
    /// replacement. Matching only ability and ability-level allowed unrelated skills to bypass the
    /// authoritative selection map.
    /// </summary>
    public bool IsActiveHeirSuccessor(uint skillId) => Owner.HeirSkills?.IsActiveSuccessor(skillId) == true;

    #region database
    public void Load(MySqlConnection connection)
    {
        // Begin the initial skill load.
        _loadingSkills = true;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM skills WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var type = Enum.Parse<SkillType>(reader.GetString("type"), true);
                    switch (type)
                    {
                        case SkillType.Skill:
                            var skill = new Skill
                            {
                                Id = reader.GetUInt32("id"),
                                Level = reader.GetByte("level")
                            };
                            // Persisted skills have already paid their learn-item cost. Restore them
                            // without consuming the item again on every login.
                            AddSkill(skill.Id, consumeLearnItem: false);
                            break;
                        case SkillType.Buff:
                            var buffId = reader.GetUInt32("id");
                            // A saved passive whose template is gone is skipped rather than dereferenced:
                            // the loader already dropped those rows, and PassiveBuff.Apply would have
                            // thrown on the null template.
                            var passiveTemplate = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
                            if (passiveTemplate == null)
                                break;
                            var buff = new PassiveBuff { Id = buffId, Template = passiveTemplate };
                            PassiveBuffs.Add(buff.Id, buff);
                            buff.Apply(Owner);
                            break;
                    }
                }
            }
        }

        foreach (var skill in Skills.Values)
            if (skill != null)
                skill.Template = SkillManager.Instance.GetSkillTemplate(skill.Id);

        // Initial load is complete; grant any newly unlocked passives.
        _loadingSkills = false;
        GrantDwarfRacialSkills(notify: false);
        GrantUnlockedTreePassives(notify: false);
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removed.Count > 0)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "DELETE FROM skills WHERE owner = @owner AND id IN(" + string.Join(",", _removed) + ")";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            command.ExecuteNonQuery();
            _removed.Clear();
        }

        foreach (var skill in Skills.Values)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES (@id, @level, @type, @owner)";
            command.Parameters.AddWithValue("@id", skill.Id);
            command.Parameters.AddWithValue("@level", skill.Level);
            command.Parameters.AddWithValue("@type", (byte)SkillType.Skill);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.ExecuteNonQuery();
        }

        foreach (var buff in PassiveBuffs.Values)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES(@id,@level,@type,@owner)";
            command.Parameters.AddWithValue("@id", buff.Id);
            command.Parameters.AddWithValue("@level", 1);
            command.Parameters.AddWithValue("@type", (byte)SkillType.Buff);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.ExecuteNonQuery();
        }
    }

    #endregion
}
